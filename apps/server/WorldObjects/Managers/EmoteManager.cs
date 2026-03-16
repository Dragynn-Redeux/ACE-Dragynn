using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using ACE.Common;
using ACE.Common.Extensions;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.DatLoader;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Factories.Enum;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using MarketBroker = ACE.Server.Market.MarketBroker;
using Serilog;

namespace ACE.Server.WorldObjects.Managers;

public class EmoteManager
{
    private readonly ILogger _log = Log.ForContext<EmoteManager>();

    public WorldObject WorldObject => _proxy ?? _worldObject;

    private WorldObject _worldObject;
    private WorldObject _proxy;

    /// <summary>
    /// Returns TRUE if this WorldObject is currently busy processing other emotes
    /// </summary>
    public bool IsBusy { get; set; }
    public int Nested { get; set; }

    public bool Debug = false;

    public EmoteManager(WorldObject worldObject)
    {
        _worldObject = worldObject;
    }

    /// <summary>
    /// Executes an emote
    /// </summary>
    /// <param name="emoteSet">The parent set of this emote</param>
    /// <param name="emote">The emote to execute</param>
    /// <param name="targetObject">A target object, usually player</param>
    /// <param name="actionChain">Only used for passing to further sets</param>
    public float ExecuteEmote(PropertiesEmote emoteSet, PropertiesEmoteAction emote, WorldObject targetObject = null)
    {
        var player = targetObject as Player;
        var creature = WorldObject as Creature;
        var targetCreature = targetObject as Creature;

        var delay = 0.0f;
        var emoteType = (EmoteType)emote.Type;

        //if (Debug)
        //Console.WriteLine($"{WorldObject.Name}.ExecuteEmote({emoteType})");

        var text = emote.Message;

        switch ((EmoteType)emote.Type)
        {
            case EmoteType.Act:
                // short for 'acting' text
                var message = Replace(text, WorldObject, targetObject, emoteSet.Quest);
                WorldObject.EnqueueBroadcast(new GameMessageSystemChat(message, ChatMessageType.Broadcast), 30.0f);
                break;

            case EmoteType.Activate:

                if (WorldObject.ActivationTarget > 0)
                {
                    // ActOnUse delay?
                    var activationTarget = WorldObject.CurrentLandblock?.GetObject(WorldObject.ActivationTarget);
                    activationTarget?.OnActivate(player ?? WorldObject);
                }
                else if (WorldObject.GeneratorId.HasValue && WorldObject.GeneratorId > 0) // Fallback to linked generator
                {
                    var linkedGenerator = WorldObject.CurrentLandblock?.GetObject(WorldObject.GeneratorId ?? 0);
                    linkedGenerator?.OnActivate(WorldObject);
                }
                break;

            case EmoteType.AddCharacterTitle:

                // emoteAction.Stat == null for all EmoteType.AddCharacterTitle entries in current db?
                if (player != null && emote.Amount != 0)
                {
                    player.AddTitle((CharacterTitle)emote.Amount);
                }

                break;

            case EmoteType.AddContract:

                // Contracts werent in emote table for 16py, guessing that Stat was used to hold id for contract.
                if (player != null && emote.Stat.HasValue && emote.Stat.Value > 0)
                {
                    player.ContractManager.Add(emote.Stat.Value);
                }

                break;

            case EmoteType.AdminSpam:

                text = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);

                PlayerManager.BroadcastToChannelFromEmote(Channel.Admin, text);
                break;

            case EmoteType.AssignCapstoneDungeon:
                if (player != null)
                {
                    Landblock.AssignCapstoneDungeon(player, emote.Message);
                }

                break;

            case EmoteType.AwardLevelProportionalSkillXP:

                var min = emote.Min64 ?? emote.Min ?? 0;
                var max = emote.Max64 ?? emote.Max ?? 0;

                if (player != null)
                {
                    player.GrantLevelProportionalSkillXP((Skill)emote.Stat, emote.Percent ?? 0, min, max);
                }

                break;

            case EmoteType.AwardLevelProportionalXP:

                min = emote.Min64 ?? emote.Min ?? 0;
                max = emote.Max64 ?? emote.Max ?? 0;

                if (player != null)
                {
                    player.GrantLevelProportionalXp(emote.Percent ?? 0, min, max, player.Level ?? 1);
                }

                break;

            case EmoteType.AwardSkillRanks:
                if (player != null && emote.Stat != null)
                {
                    var stat = emote.Stat.Value;
                    var amount = emote.Amount ?? 1;

                    if ((Skill)stat is Skill.PortalMagic)
                    {
                        player.GrantSkillRanks((Skill)stat, amount);
                    }
                    else
                    {
                        for (var i = 0; i < (emote.Amount ?? 1); i++)
                        {
                            player.HandleActionRaiseSkill((Skill)stat, 0, true);

                        }
                    }
                }

                break;
            case EmoteType.AwardLuminance:

                if (player != null)
                {
                    player.EarnLuminance(emote.Amount64 ?? emote.HeroXP64 ?? 0, XpType.Quest, ShareType.None);
                }

                break;

            case EmoteType.AwardNoContribSkillXP:

                if (player != null)
                {
                    if (emote.Stat == null)
                    {
                        _log.Error(
                            "EmoteType.AwardNoContribSkillXP - emote.Stat is null. Should have a reference to a skill id. {Player} was not awarded xp.",
                            player.Name
                        );
                        break;
                    }

                    if (emote.Stat == null)
                    {
                        _log.Warning(
                            "EmoteType.AwardNoContribSkillXP - emote.Amount (difficulty) is null. Defaulting to '0'. Xp awarded to {Player} may be incorrect.",
                            player.Name
                        );
                    }

                    var playerSkill = (Skill)emote.Stat;
                    var skill = player.GetCreatureSkill(playerSkill);
                    var difficulty = emote.Amount ?? 0;

                    Player.TryAwardCraftingXp(player, skill, playerSkill, difficulty);
                }
                break;

            case EmoteType.AwardNoShareXP:

                if (player != null)
                {
                    player.EarnXP(emote.Amount64 ?? emote.Amount ?? 0, XpType.Quest, player.Level, ShareType.None);
                }

                break;

            case EmoteType.AwardSkillPoints:

                if (player != null)
                {
                    player.AwardSkillPoints((Skill)emote.Stat, (uint)emote.Amount);
                }

                break;

            case EmoteType.AwardSkillXP:

                if (player != null)
                {
                    if (delay < 1)
                    {
                        delay += 1; // because of how AwardSkillXP grants and then raises the skill, ensure delay is at least 1 to allow for processing correctly
                    }

                    player.AwardSkillXP((Skill)emote.Stat, (uint)emote.Amount, true);
                }
                break;

            case EmoteType.AwardTrainingCredits:

                if (player != null)
                {
                    player.AddSkillCredits(emote.Amount ?? 0);
                }

                break;

            case EmoteType.AwardXP:

                if (player != null)
                {
                    var amt = emote.Amount64 ?? emote.Amount ?? 0;
                    if (amt > 0)
                    {
                        player.EarnXP(amt, XpType.Quest, player.Level, ShareType.All);
                    }
                    else if (amt < 0)
                    {
                        player.SpendXP(-amt);
                    }
                }
                break;

            case EmoteType.BLog:

                text = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);

                _log.Information(
                    $"0x{WorldObject.Guid}:{WorldObject.Name}({WorldObject.WeenieClassId}).EmoteManager.BLog - {text}"
                );
                break;

            case EmoteType.CapstoneCacheReward:

                if (player != null)
                {
                    AwardCapstoneItems(player, emote.Amount ?? 2);
                    AwardCapstoneTradeNotes(player, emote.Amount ?? 2);
                }

                break;

            case EmoteType.CastSpell:

                if (WorldObject != null)
                {
                    var spell = new Spell((uint)emote.SpellId);
                    var damageMultiplier = emote.Percent ?? 1.0;
                    var tryResist = emote.Message != "noresist";

                    if (spell.NotFound)
                    {
                        _log.Error(
                            $"{WorldObject.Name} ({WorldObject.Guid}) EmoteManager.CastSpell - unknown spell {emote.SpellId}"
                        );
                        break;
                    }

                    creature.CheckForHumanPreCast(spell);

                    var spellTarget = GetSpellTarget(spell, targetObject);

                    var preCastTime = creature.PreCastMotion(spellTarget);

                    delay = preCastTime + creature.GetPostCastTime(spell);

                    var castChain = new ActionChain();
                    castChain.AddDelaySeconds(preCastTime);
                    castChain.AddAction(
                        creature,
                        () =>
                        {
                            creature.TryCastSpell_WithRedirects(
                                spell,
                                spellTarget,
                                creature,
                                null,
                                false,
                                false,
                                tryResist,
                                damageMultiplier);

                            creature.PostCastMotion();
                        }
                    );
                    castChain.EnqueueChain();
                }
                break;

            case EmoteType.CastSpellInstant:

                if (WorldObject != null)
                {
                    var spell = new Spell((uint)emote.SpellId);
                    var damageMultiplier = emote.Percent ?? 1.0;
                    var tryResist = emote.Message != "noresist";

                    if (!spell.NotFound)
                    {
                        var spellTarget = GetSpellTarget(spell, targetObject);

                        WorldObject.TryCastSpell_WithRedirects(
                            spell,
                            spellTarget,
                            WorldObject,
                            null,
                            false,
                            false,
                            tryResist,
                            damageMultiplier
                        );
                    }
                }
                break;

            case EmoteType.CloseMe:

                // animation delay?
                if (WorldObject is Container container)
                {
                    container.Close(null);
                }
                else if (WorldObject is Door closeDoor)
                {
                    closeDoor.Close();
                }

                break;

            case EmoteType.CreateSigilTrinket:

                if (player != null && WorldObject != null)
                {
                    // Prefer explicit PropertyInt values on the source WorldObject when present,
                    // otherwise fall back to emote fields or defaults.
                    var trinketTier = WorldObject.GetProperty(PropertyInt.Tier) ?? emote.WealthRating ?? 2;

                    // emote.Stat (optional) -> SigilTrinketType
                    var trinketType = emote.Stat.HasValue ? (SigilTrinketType)emote.Stat.Value : SigilTrinketType.Scarab;

                    // Prefer PropertyInt-backed values when available (works for SigilTrinket or any weenie with those properties, e.g. gems)
                    var forcedEffectId = WorldObject.GetProperty(PropertyInt.SigilTrinketEffectId) ?? null;
                    //var forcedWieldSkillRng = WorldObject.GetProperty(PropertyInt.SigilTrinketSkill)
                    //                            ?? (emote.Shade.HasValue ? (int?)Convert.ToInt32(Math.Round(emote.Shade.Value)) : null);

                    // allow emote to override created WCID, otherwise use source object's weenie class id as fallback
                    var forcedWcid = emote.WeenieClassId ?? null;

                    // Read optional allowed specialized skills from the source object's PropertyString.
                    // Stored format: comma-separated enum ints or names (matches SigilTrinket.AllowedSpecializedSkills getter).
                    List<Skill> allowedSpecializedSkills = null;
                    var rawAllowed = WorldObject.GetProperty(PropertyString.SigilTrinketAllowedSpecializedSkills);
                    if (!string.IsNullOrWhiteSpace(rawAllowed))
                    {
                        var parts = rawAllowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        var tmp = new List<Skill>(parts.Length);
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out var v))
                            {
                                if (Enum.IsDefined(typeof(Skill), v))
                                {
                                    tmp.Add((Skill)v);
                                }
                            }
                            else
                            {
                                if (Enum.TryParse<Skill>(p, true, out var sk))
                                {
                                    tmp.Add(sk);
                                }
                            }
                        }

                        if (tmp.Count > 0)
                        {
                            allowedSpecializedSkills = tmp;
                        }
                    }

                    var profile = new ACE.Server.Factories.Entity.TreasureDeathExtended
                    {
                        Tier = trinketTier,
                        LootQualityMod = 0,
                        ItemChance = 100,
                        ItemMinAmount = 1,
                        ItemMaxAmount = 1,
                        MagicItemChance = 100,
                        MagicItemMinAmount = 1,
                        MagicItemMaxAmount = 1,
                        MundaneItemChance = 100,
                        MundaneItemMinAmount = 1,
                        MundaneItemMaxAmount = 1,
                        UnknownChances = 21
                    };

                    var trinket = LootGenerationFactory.CreateSigilTrinket(
                        profile,
                        trinketType,
                        true,
                        forcedEffectId,
                        forcedWcid,
                        allowedSpecializedSkills
                    );

                    if (trinket != null)
                    {
                        player.TryCreateForGive(WorldObject, trinket);
                    }
                }
                break;

            case EmoteType.CreateTreasure:

                if (player != null)
                {
                    var treasureTier = emote.WealthRating ?? 1;

                    var treasureType = (TreasureItemCategory?)emote.TreasureType ?? TreasureItemCategory.Undef;

                    var treasureClass = (TreasureItemType_Orig?)emote.TreasureClass ?? TreasureItemType_Orig.Undef;

                    // Create a dummy treasure profile for passing emote values
                    var profile = new ACE.Server.Factories.Entity.TreasureDeathExtended
                    {
                        Tier = treasureTier,
                        ForceTreasureItemType = treasureClass,
                        //TreasureType = (uint)treasureType,
                        LootQualityMod = 0,
                        ItemChance = 100,
                        ItemMinAmount = 1,
                        ItemMaxAmount = 1,
                        //ItemTreasureTypeSelectionChances = (int)treasureClass,
                        MagicItemChance = 100,
                        MagicItemMinAmount = 1,
                        MagicItemMaxAmount = 1,
                        //MagicItemTreasureTypeSelectionChances = (int)treasureClass,
                        MundaneItemChance = 100,
                        MundaneItemMinAmount = 1,
                        MundaneItemMaxAmount = 1,
                        //MundaneItemTypeSelectionChances = (int)treasureClass,
                        UnknownChances = 21
                    };

                    var treasure = LootGenerationFactory.CreateRandomLootObjects_New(profile, treasureType);
                    if (treasure != null)
                    {
                        player.TryCreateForGive(WorldObject, treasure);
                    }
                }
                break;

            /* decrements a PropertyInt stat by some amount */
            case EmoteType.DecrementIntStat:

                // only used by 1 emote in 16PY - check for lower bounds?
                if (targetObject != null && emote.Stat != null)
                {
                    var intProperty = (PropertyInt)emote.Stat;
                    var current = targetObject.GetProperty(intProperty) ?? 0;
                    current -= emote.Amount ?? 1;
                    targetObject.SetProperty(intProperty, current);

                    if (player != null)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessagePrivateUpdatePropertyInt(player, intProperty, current)
                        );
                    }
                }
                break;

            case EmoteType.DecrementMyQuest:
            case EmoteType.DecrementQuest:

                var questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    questTarget.QuestManager.Decrement(emote.Message, emote.Amount ?? 1);
                }

                break;

            case EmoteType.DeleteSelf:

                if (WorldObject is Creature)
                {
                    WorldObject.DeleteObject();
                    break;
                }

                if (player != null)
                {
                    var wo = player.FindObject(
                        WorldObject.Guid.Full,
                        Player.SearchLocations.Everywhere,
                        out _,
                        out _,
                        out _
                    );

                    if (wo != null)
                    {
                        var woStackSize = wo.StackSize ?? 1;

                        if (woStackSize > 1)
                        {
                            if (!player.TryConsumeFromInventoryWithNetworking(wo, 1))
                            {
                                WorldObject.EmoteManager?._log.Warning(
                                    "[EMOTE] DeleteSelf: failed to consume 1x from stack 0x{Guid:X8}:{Name} for player {Player}",
                                    wo.Guid.Full,
                                    wo.Name,
                                    player.Name
                                );
                            }
                        }
                        else
                        {
                            if (!player.TryConsumeFromInventoryWithNetworking(wo))
                            {
                                WorldObject.EmoteManager?._log.Warning(
                                    "[EMOTE] DeleteSelf: failed to consume 0x{Guid:X8}:{Name} for player {Player}",
                                    wo.Guid.Full,
                                    wo.Name,
                                    player.Name
                                );
                            }
                        }

                        break;
                    }

                    WorldObject.EmoteManager?._log.Warning(
                        "[EMOTE] DeleteSelf: WorldObject 0x{Guid:X8}:{Name} not found in possessions of {Player}; skipping inventory delete.",
                        WorldObject.Guid.Full,
                        WorldObject.Name,
                        player.Name
                    );

                    break;
                }

                WorldObject.DeleteObject();
                break;

            case EmoteType.DirectBroadcast:

                text = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);

                if (player != null)
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(text, ChatMessageType.Broadcast));
                }

                break;

            case EmoteType.Enlightenment:

                if (player != null)
                {
                    Enlightenment.HandleEnlightenment(WorldObject, player);
                }

                break;

            case EmoteType.EraseMyQuest:
            case EmoteType.EraseQuest:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    questTarget.QuestManager.Erase(emote.Message);
                }

                break;

            case EmoteType.EraseFellowQuest:

                if (player != null)
                {
                    if (player.Fellowship != null)
                    {
                        player.Fellowship.QuestManager.Erase(emote.Message);
                    }
                }

                break;

            case EmoteType.FellowBroadcast:

                if (player != null)
                {
                    var fellowship = player.Fellowship;

                    if (fellowship != null)
                    {
                        text = Replace(emote.Message, WorldObject, player, emoteSet.Quest);

                        fellowship.BroadcastToFellow(text);
                    }
                }
                break;

            case EmoteType.Generate:

                if (WorldObject.IsGenerator)
                {
                    WorldObject.Generator_Generate();
                }

                break;

            case EmoteType.Give:

                var success = false;

                var stackSize = emote.StackSize ?? 1;

                if (player != null && emote.WeenieClassId != null)
                {

                    var weenieClassId = emote.WeenieClassId;

                    // Trophies
                    if (WorldObject is Creature { RefusalItem.Item1: { Value: not null, TrophyQuality: not null } } creatureObject)
                    {
                        stackSize = creatureObject.RefusalItem.Item1.Value ?? 1;

                        // For Trophy Smith, allow wcid to iterate to provide a scaled reward based on trophy quality
                        var trophyQualityIteration = 0u;
                        if (creatureObject is { WeenieClassId: 3932 })
                        {
                            trophyQualityIteration = (uint)((creatureObject.RefusalItem.Item1.TrophyQuality - 1) ?? 0);
                            weenieClassId += trophyQualityIteration;
                        }
                    }

                    // Sigil Trinkets
                    if (WorldObject is Creature { RefusalItem.Item1: { WeenieType: WeenieType.SigilTrinket} } creatureObject2)
                    {
                        stackSize = (creatureObject2.RefusalItem.Item1.Value / 10) ?? 1;
                    }

                    var motionChain = new ActionChain();

                    if (!WorldObject.DontTurnOrMoveWhenGiving && creature != null)
                    {
                        delay = creature.Rotate(targetCreature);
                        motionChain.AddDelaySeconds(delay);
                    }
                    motionChain.AddAction(
                        WorldObject,
                        () =>
                            player.GiveFromEmote(
                                WorldObject,
                                weenieClassId ?? 0,
                                stackSize > 0 ? stackSize : 1,
                                emote.Palette ?? 0,
                                emote.Shade ?? 0
                            )
                    );
                    motionChain.EnqueueChain();
                }

                break;

            /* redirects to the GotoSet category for this action */
            case EmoteType.Goto:

                // TODO: revisit if nested chains need to back-propagate timers
                var gotoSet = GetEmoteSet(EmoteCategory.GotoSet, emote.Message);
                ExecuteEmoteSet(gotoSet, targetObject, true);
                break;

            /* increments a PropertyInt stat by some amount */
            case EmoteType.IncrementIntStat:

                if (targetObject != null && emote.Stat != null)
                {
                    var intProperty = (PropertyInt)emote.Stat;
                    var current = targetObject.GetProperty(intProperty) ?? 0;
                    current += emote.Amount ?? 1;
                    targetObject.SetProperty(intProperty, current);

                    if (player != null)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessagePrivateUpdatePropertyInt(player, intProperty, current)
                        );
                    }
                }
                break;

            case EmoteType.IncrementMyQuest:
            case EmoteType.IncrementQuest:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    questTarget.QuestManager.Increment(emote.Message, emote.Amount ?? 1);
                }

                break;

            case EmoteType.InflictVitaePenalty:
                if (player != null)
                {
                    player.InflictVitaePenalty(emote.Amount ?? 5);
                }

                break;

            case EmoteType.InqAttributeStat:

                if (targetCreature != null)
                {
                    var attr = targetCreature.Attributes[(PropertyAttribute)emote.Stat];

                    if (attr == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success =
                            attr != null
                            && attr.Current >= (emote.Min ?? int.MinValue)
                            && attr.Current <= (emote.Max ?? int.MaxValue);

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqBoolStat:

                if (targetObject != null)
                {
                    var stat = targetObject.GetProperty((PropertyBool)emote.Stat);

                    if (stat == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success = stat ?? false;

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqContractsFull:

                ExecuteEmoteSet(
                    player != null && player.ContractManager.IsFull
                        ? EmoteCategory.TestSuccess
                        : EmoteCategory.TestFailure,
                    emote.Message,
                    targetObject,
                    true
                );
                break;

            case EmoteType.InqEvent:

                var started = EventManager.IsEventStarted(emote.Message, WorldObject, targetObject);
                ExecuteEmoteSet(
                    started ? EmoteCategory.EventSuccess : EmoteCategory.EventFailure,
                    emote.Message,
                    targetObject,
                    true
                );
                break;

            case EmoteType.InqFellowNum:

                // unused in PY16 - ensure # of fellows between min-max?
                var result = HasValidTestNoFellow(emote.Message)
                    ? EmoteCategory.TestNoFellow
                    : EmoteCategory.NumFellowsFailure;

                if (player?.Fellowship != null)
                {
                    var fellows = player.Fellowship.GetFellowshipMembers();

                    if (fellows.Count < (emote.Min ?? int.MinValue) || fellows.Count > (emote.Max ?? int.MaxValue))
                    {
                        result = EmoteCategory.NumFellowsFailure;
                    }
                    else
                    {
                        result = EmoteCategory.NumFellowsSuccess;
                    }
                }
                ExecuteEmoteSet(result, emote.Message, targetObject, true);
                break;

            case EmoteType.InqFellowQuest:

                if (player != null)
                {
                    if (player.Fellowship != null)
                    {
                        var hasQuest = player.Fellowship.QuestManager.HasQuest(emote.Message);
                        var canSolve = player.Fellowship.QuestManager.CanSolve(emote.Message);

                        // verify: QuestSuccess = player has quest, and their last completed time + quest minDelta <= currentTime
                        success = hasQuest && !canSolve;

                        ExecuteEmoteSet(
                            success ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                    else
                    {
                        ExecuteEmoteSet(EmoteCategory.QuestNoFellow, emote.Message, targetObject, true);
                    }
                }
                break;

            case EmoteType.InqFellowQuestSolves:

                if (player != null)
                {
                    if (player.Fellowship != null)
                    {
                        var questSolves = player.Fellowship.QuestManager.HasQuestSolves(
                            emote.Message,
                            emote.Min,
                            emote.Max
                        );

                        ExecuteEmoteSet(
                            questSolves ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqFloatStat:

                if (targetObject != null)
                {
                    var stat = targetObject.GetProperty((PropertyFloat)emote.Stat);

                    if (stat == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        stat ??= 0.0f;
                        success =
                            stat >= (emote.MinDbl ?? double.MinValue) && stat <= (emote.MaxDbl ?? double.MaxValue);
                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqInt64Stat:

                if (targetObject != null)
                {
                    var stat = targetObject.GetProperty((PropertyInt64)emote.Stat);

                    if (stat == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        stat ??= 0;
                        success = stat >= (emote.Min64 ?? long.MinValue) && stat <= (emote.Max64 ?? long.MaxValue);
                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqIntStat:

                if (targetObject != null)
                {
                    var stat = targetObject.GetProperty((PropertyInt)emote.Stat);

                    if (stat == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        stat ??= 0;
                        success = stat >= (emote.Min ?? int.MinValue) && stat <= (emote.Max ?? int.MaxValue);
                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqNumCharacterTitles:

                if (player != null)
                {
                    var numTitles = player.NumCharacterTitles;
                    success =
                        numTitles != null
                        && numTitles >= (emote.Min ?? int.MinValue)
                        && numTitles <= (emote.Max ?? int.MaxValue);
                    ExecuteEmoteSet(
                        success ? EmoteCategory.NumCharacterTitlesSuccess : EmoteCategory.NumCharacterTitlesFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }
                break;

            case EmoteType.InqOwnsItems:

                if (player != null)
                {
                    var numRequired = emote.StackSize ?? 1;

                    var items = player.GetInventoryItemsOfWCID(emote.WeenieClassId ?? 0);
                    items.AddRange(player.GetEquippedObjectsOfWCID(emote.WeenieClassId ?? 0));
                    var numItems = items.Sum(i => i.StackSize ?? 1);

                    uint? refusalItemGuidId = null;
                    if (creature?.RefusalItem.Item2 != null)
                    {
                        refusalItemGuidId = creature.RefusalItem.Item2;

                        success = false;

                        foreach (var item in items)
                        {
                            if (item.Guid.Full == refusalItemGuidId)
                            {
                                success = numItems >= numRequired;
                                break;
                            }
                        }
                    }
                    else
                    {
                        success = numItems >= numRequired;
                    }

                    ExecuteEmoteSet(
                        success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }
                break;

            case EmoteType.InqPackSpace:

                if (player != null)
                {
                    var numRequired = emote.Amount ?? 1;

                    success = false;
                    if (numRequired > 10000) // Since emote was not in 16py and we have just the two fields to go on, I will assume you could "mask" the value to pick between free Item Capacity space or free Container Capacity space
                    {
                        var freeSpace = player.GetFreeContainerSlots();

                        success = freeSpace >= (numRequired - 10000);
                    }
                    else
                    {
                        var freeSpace = player.GetFreeInventorySlots(false); // assuming this was only for main pack. makes things easier at this point.

                        success = freeSpace >= numRequired;
                    }

                    ExecuteEmoteSet(
                        success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }
                break;

            case EmoteType.InqMyQuest:
            case EmoteType.InqQuest:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    // Do normal Emote Checks for the quest and solves
                    var hasQuest = questTarget.QuestManager.HasQuest(emote.Message);
                    var canSolve = questTarget.QuestManager.CanSolve(emote.Message);

                    success = hasQuest && !canSolve;

                    ExecuteEmoteSet(
                        success ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }

                break;

            case EmoteType.InqMyQuestBitsOff:
            case EmoteType.InqQuestBitsOff:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    var hasNoQuestBits = questTarget.QuestManager.HasNoQuestBits(emote.Message, emote.Amount ?? 0);

                    ExecuteEmoteSet(
                        hasNoQuestBits ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }

                break;

            case EmoteType.InqMyQuestBitsOn:
            case EmoteType.InqQuestBitsOn:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    var hasQuestBits = questTarget.QuestManager.HasQuestBits(emote.Message, emote.Amount ?? 0);

                    ExecuteEmoteSet(
                        hasQuestBits ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }

                break;

            case EmoteType.InqMyQuestSolves:
            case EmoteType.InqQuestSolves:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    var questSolves = questTarget.QuestManager.HasQuestSolves(emote.Message, emote.Min, emote.Max);

                    ExecuteEmoteSet(
                        questSolves ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                        emote.Message,
                        targetObject,
                        true
                    );
                }
                break;

            case EmoteType.InqRawAttributeStat:

                if (targetCreature != null)
                {
                    var attr = targetCreature.Attributes[(PropertyAttribute)emote.Stat];

                    if (attr == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success =
                            attr != null
                            && attr.Base >= (emote.Min ?? int.MinValue)
                            && attr.Base <= (emote.Max ?? int.MaxValue);

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqRawSecondaryAttributeStat:

                if (targetCreature != null)
                {
                    var vital = targetCreature.Vitals[(PropertyAttribute2nd)emote.Stat];

                    if (vital == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success =
                            vital != null
                            && vital.Base >= (emote.Min ?? int.MinValue)
                            && vital.Base <= (emote.Max ?? int.MaxValue);

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqRawSkillStat:

                if (targetCreature != null)
                {
                    var skill = targetCreature.GetCreatureSkill((Skill)emote.Stat);

                    if (skill == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success =
                            skill != null
                            && skill.Base >= (emote.Min ?? int.MinValue)
                            && skill.Base <= (emote.Max ?? int.MaxValue);

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqSecondaryAttributeStat:

                if (targetCreature != null)
                {
                    var vital = targetCreature.Vitals[(PropertyAttribute2nd)emote.Stat];

                    if (vital == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success =
                            vital != null
                            && vital.Current >= (emote.Min ?? int.MinValue)
                            && vital.Current <= (emote.Max ?? int.MaxValue);

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqSkillSpecialized:

                if (targetCreature != null)
                {
                    var skill = targetCreature.GetCreatureSkill((Skill)emote.Stat);

                    if (skill == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success = skill != null && skill.AdvancementClass == SkillAdvancementClass.Specialized;

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqSkillStat:

                if (targetCreature != null)
                {
                    var skill = targetCreature.GetCreatureSkill((Skill)emote.Stat);

                    if (skill == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success =
                            skill != null
                            && skill.Current >= (emote.Min ?? int.MinValue)
                            && skill.Current <= (emote.Max ?? int.MaxValue);

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqSkillTrained:

                if (targetCreature != null)
                {
                    var skill = targetCreature.GetCreatureSkill((Skill)emote.Stat);

                    if (skill == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success = skill != null && skill.AdvancementClass >= SkillAdvancementClass.Trained;

                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqStringStat:

                if (targetObject != null)
                {
                    var stringStat = targetObject.GetProperty((PropertyString)emote.Stat);

                    if (stringStat == null && HasValidTestNoQuality(emote.Message))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.Message, targetObject, true);
                    }
                    else
                    {
                        success = stringStat != null && stringStat.Equals(emote.TestString);
                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }
                break;

            case EmoteType.InqYesNo:

                if (player != null)
                {
                    var emoteMessage = emote.TestString;
                    var questName = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);

                    if (WorldObject is Creature { RefusalItem.Item1: not null } creatureObject)
                    {
                        emoteMessage = emoteMessage.Replace("(TrophyName)", creatureObject.RefusalItem.Item1.Name);
                        emoteMessage = emoteMessage.Replace("(TrophyValue)", creatureObject.RefusalItem.Item1.Value.ToString());
                        emoteMessage = emoteMessage.Replace("(SigilValue)", (creatureObject.RefusalItem.Item1.Value / 10).ToString());
                    }
                    var confirmationText = Replace(emoteMessage, WorldObject, targetObject, emoteSet.Quest);
                    if (
                        !player.ConfirmationManager.EnqueueSend(
                            new Confirmation_YesNo(WorldObject.Guid, player.Guid, questName),
                            confirmationText
                        )
                    )
                    {
                        ExecuteEmoteSet(EmoteCategory.TestFailure, emote.Message, player);
                    }
                }
                break;

            case EmoteType.Invalid:
                break;

            case EmoteType.KillSelf:

                if (creature != null)
                {
                    creature.Smite(creature);
                }

                break;

            case EmoteType.LocalBroadcast:

                message = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);
                var range = emote.Amount ?? WorldObject.LocalBroadcastRange;
                WorldObject.EnqueueBroadcast(
                    new GameMessageSystemChat(message, ChatMessageType.Broadcast),
                    (float)range
                );
                break;

            case EmoteType.LocalSignal:

                if (WorldObject != null && WorldObject.CurrentLandblock != null)
                {
                    var crossLb = WorldObject.GetProperty(PropertyBool.SignalCrossLB) ?? false;

                     if (crossLb)
                    {
                        WorldObject.CurrentLandblock.EmitSignalWithAdjacents(WorldObject, emote.Message);
                    }
                    else
                    {
                        WorldObject.CurrentLandblock.EmitSignal(WorldObject, emote.Message);
                    }
                }

                break;


            case EmoteType.LockFellow:

                if (player != null && player.Fellowship != null)
                {
                    player.HandleActionFellowshipChangeLock(true, emoteSet.Quest);
                }

                break;

            /* plays an animation on the target object (usually the player) */
            case EmoteType.ForceMotion:

                var motionCommand = MotionCommandHelper.GetMotion(emote.Motion.Value);
                var motion = new Motion(targetObject, motionCommand, emote.Extent);
                if (player != null)
                {
                    player.ExecuteMotion(motion);
                }
                else
                {
                    targetObject.EnqueueBroadcastMotion(motion);
                }
                break;

            /* plays an animation on the source object */
            case EmoteType.Motion:

                var debugMotion = false;

                if (Debug)
                {
                    Console.Write($".{(MotionCommand)emote.Motion}");
                }

                // If the landblock is dormant, there are no players in range
                if (WorldObject.CurrentLandblock?.IsDormant ?? false)
                {
                    break;
                }

                // are there players within emote range?
                if (!WorldObject.PlayersInRange(ClientMaxAnimRange))
                {
                    break;
                }

                if (WorldObject.PhysicsObj != null && WorldObject.PhysicsObj.IsMovingTo())
                {
                    break;
                }

                if (WorldObject == null || WorldObject.CurrentMotionState == null)
                {
                    break;
                }

                // TODO: REFACTOR ME
                if (emoteSet.Category != EmoteCategory.Vendor && emoteSet.Style != null)
                {
                    var startingMotion = new Motion((MotionStance)emoteSet.Style, (MotionCommand)emoteSet.Substyle);
                    motion = new Motion((MotionStance)emoteSet.Style, (MotionCommand)emote.Motion, emote.Extent);

                    if (WorldObject.CurrentMotionState.Stance != startingMotion.Stance)
                    {
                        if (WorldObject.CurrentMotionState.Stance == MotionStance.Invalid)
                        {
                            if (debugMotion)
                            {
                                Console.WriteLine(
                                    $"{WorldObject.Name} running starting motion {(MotionStance)emoteSet.Style}, {(MotionCommand)emoteSet.Substyle}"
                                );
                            }

                            delay = WorldObject.ExecuteMotion(startingMotion);
                        }
                    }
                    else
                    {
                        if (
                            WorldObject.CurrentMotionState.MotionState.ForwardCommand
                                == startingMotion.MotionState.ForwardCommand
                            && startingMotion.Stance == MotionStance.NonCombat
                        ) // enforce non-combat here?
                        {
                            if (debugMotion)
                            {
                                Console.WriteLine(
                                    $"{WorldObject.Name} running motion {(MotionStance)emoteSet.Style}, {(MotionCommand)emote.Motion}"
                                );
                            }

                            float? maxRange = ClientMaxAnimRange;
                            if (MotionQueue.Contains((MotionCommand)emote.Motion))
                            {
                                maxRange = null;
                            }

                            var motionTable = DatManager.PortalDat.ReadFromDat<DatLoader.FileTypes.MotionTable>(
                                WorldObject.MotionTableId
                            );
                            var animLength = motionTable.GetAnimationLength(
                                WorldObject.CurrentMotionState.Stance,
                                (MotionCommand)emote.Motion,
                                MotionCommand.Ready
                            );

                            delay = WorldObject.ExecuteMotion(motion, true, maxRange);

                            var motionChain = new ActionChain();
                            motionChain.AddDelaySeconds(animLength);
                            motionChain.AddAction(
                                WorldObject,
                                () =>
                                {
                                    // FIXME: better cycle handling
                                    var cmd = WorldObject.CurrentMotionState.MotionState.ForwardCommand;
                                    if (
                                        cmd != MotionCommand.Dead
                                        && cmd != MotionCommand.Sleeping
                                        && cmd != MotionCommand.Sitting
                                        && !cmd.ToString().EndsWith("State")
                                    )
                                    {
                                        if (debugMotion)
                                        {
                                            Console.WriteLine(
                                                $"{WorldObject.Name} running starting motion again {(MotionStance)emoteSet.Style}, {(MotionCommand)emoteSet.Substyle}"
                                            );
                                        }

                                        WorldObject.ExecuteMotion(startingMotion);
                                    }
                                }
                            );
                            motionChain.EnqueueChain();

                            if (debugMotion)
                            {
                                Console.WriteLine(
                                    $"{WorldObject.Name} appending time to existing chain: " + animLength
                                );
                            }
                        }
                    }
                }
                else
                {
                    // vendor / other motions
                    var startingMotion = new Motion(MotionStance.NonCombat, MotionCommand.Ready);
                    var motionTable = DatManager.PortalDat.ReadFromDat<DatLoader.FileTypes.MotionTable>(
                        WorldObject.MotionTableId
                    );
                    var animLength = motionTable.GetAnimationLength(
                        WorldObject.CurrentMotionState.Stance,
                        (MotionCommand)emote.Motion,
                        MotionCommand.Ready
                    );

                    motion = new Motion(MotionStance.NonCombat, (MotionCommand)emote.Motion, emote.Extent);

                    if (debugMotion)
                    {
                        Console.WriteLine(
                            $"{WorldObject.Name} running motion (block 2) {MotionStance.NonCombat}, {(MotionCommand)(emote.Motion ?? 0)}"
                        );
                    }

                    delay = WorldObject.ExecuteMotion(motion);

                    var motionChain = new ActionChain();
                    motionChain.AddDelaySeconds(animLength);
                    motionChain.AddAction(WorldObject, () => WorldObject.ExecuteMotion(startingMotion, false));

                    motionChain.EnqueueChain();
                }

                break;

            /* move to position relative to home */
            case EmoteType.Move:

                if (creature != null)
                {
                    // If the landblock is dormant, there are no players in range
                    if (WorldObject.CurrentLandblock?.IsDormant ?? false)
                    {
                        break;
                    }

                    // are there players within emote range?
                    if (!WorldObject.PlayersInRange(ClientMaxAnimRange))
                    {
                        break;
                    }

                    var newPos = new Position(creature.Home);
                    newPos.Pos += new Vector3(emote.OriginX ?? 0, emote.OriginY ?? 0, emote.OriginZ ?? 0); // uses relative position

                    // ensure valid quaternion - all 0s for example can lock up physics engine
                    if (
                        emote.AnglesX != null
                        && emote.AnglesY != null
                        && emote.AnglesZ != null
                        && emote.AnglesW != null
                        && (emote.AnglesX != 0 || emote.AnglesY != 0 || emote.AnglesZ != 0 || emote.AnglesW != 0)
                    )
                    {
                        // also relative, or absolute?
                        newPos.Rotation *= new Quaternion(
                            emote.AnglesX.Value,
                            emote.AnglesY.Value,
                            emote.AnglesZ.Value,
                            emote.AnglesW.Value
                        );
                    }

                    if (Debug)
                    {
                        Console.WriteLine(newPos.ToLOCString());
                    }

                    // get new cell
                    newPos.LandblockId = new LandblockId(PositionExtensions.GetCell(newPos));

                    // TODO: handle delay for this?
                    creature.MoveTo(newPos, creature.GetRunRate(), true, null, emote.Extent);
                }
                break;

            case EmoteType.MoveHome:

                // TODO: call MoveToManager on server, handle delay for this?
                if (creature != null && creature.Home != null)
                {
                    // are we already at home origin?
                    if (creature.Location.Pos.Equals(creature.Home.Pos))
                    {
                        // just turnto if required?
                        if (Debug)
                        {
                            Console.Write($" - already at home origin, checking rotation");
                        }

                        if (!creature.Location.Rotation.Equals(creature.Home.Rotation))
                        {
                            if (Debug)
                            {
                                Console.Write($" - turning to");
                            }

                            delay = creature.TurnTo(creature.Home);
                        }
                        else if (Debug)
                        {
                            Console.Write($" - already at home rotation, doing nothing");
                        }
                    }
                    else
                    {
                        if (Debug)
                        {
                            Console.Write($" - {creature.Home.ToLOCString()}");
                        }

                        // how to get delay with this, callback required?
                        creature.MoveTo(creature.Home, creature.GetRunRate(), true, null, emote.Extent);
                    }
                }
                break;

            case EmoteType.MoveToPos:

                if (creature != null)
                {
                    var currentPos = creature.Location;

                    var newPos = new Position();
                    newPos.LandblockId = new LandblockId(emote.ObjCellId ?? currentPos.LandblockId.Raw);

                    newPos.Pos = new Vector3(
                        emote.OriginX ?? currentPos.Pos.X,
                        emote.OriginY ?? currentPos.Pos.Y,
                        emote.OriginZ ?? currentPos.Pos.Z
                    );

                    if (
                        emote.AnglesX == null
                        || emote.AnglesY == null
                        || emote.AnglesZ == null
                        || emote.AnglesW == null
                    )
                    {
                        newPos.Rotation = new Quaternion(
                            currentPos.Rotation.X,
                            currentPos.Rotation.Y,
                            currentPos.Rotation.Z,
                            currentPos.Rotation.W
                        );
                    }
                    else
                    {
                        newPos.Rotation = new Quaternion(
                            emote.AnglesX ?? 0,
                            emote.AnglesY ?? 0,
                            emote.AnglesZ ?? 0,
                            emote.AnglesW ?? 1
                        );
                    }

                    //if (emote.ObjCellId != null)
                    //newPos.LandblockId = new LandblockId(emote.ObjCellId.Value);

                    newPos.LandblockId = new LandblockId(PositionExtensions.GetCell(newPos));

                    var walkRunThreshold = emote.Amount;
                    var runSpeed = emote.Shade ?? creature.GetRunRate();

                    // TODO: handle delay for this?
                    creature.MoveTo(newPos, runSpeed, true, walkRunThreshold, emote.Extent);
                }
                break;

            case EmoteType.OpenMe:

                if (WorldObject is Container openContainer)
                {
                    openContainer.Open(null);
                }
                else if (WorldObject is Door openDoor)
                {
                    openDoor.Open();
                }

                break;

            case EmoteType.PetCastSpellOnOwner:

                if (creature is Pet passivePet && passivePet.P_PetOwner != null)
                {
                    var spell = new Spell((uint)emote.SpellId);
                    passivePet.TryCastSpell(spell, passivePet.P_PetOwner);
                }
                break;

            case EmoteType.PhysScript:

                if (player != null && emote.Message == "player")
                {
                    player.PlayParticleEffect((PlayScript)emote.PScript, player.Guid, emote.Extent);
                }
                else
                {
                    WorldObject.PlayParticleEffect((PlayScript)emote.PScript, WorldObject.Guid, emote.Extent);
                }

                break;

            case EmoteType.PopUp:

                if (player != null)
                {
                    var popupMessage = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);
                    player.Session.Network.EnqueueSend(new GameEventPopupString(player.Session, popupMessage));
                }

                break;

            case EmoteType.RelieveVitaePenalty:

                if (player != null)
                {
                    player.RelieveVitaePenalty(emote.Amount);
                }

                break;

            case EmoteType.RemoveContract:

                if (player != null && emote.Stat.HasValue && emote.Stat.Value > 0)
                {
                    player.HandleActionAbandonContract((uint)emote.Stat);
                }

                break;

            case EmoteType.RemoveVitaePenalty:

                if (player != null)
                {
                    player.EnchantmentManager.RemoveVitae();
                }

                break;

            case EmoteType.ResetAttributeXp:

                if (emote.Stat is null || player is null)
                {
                    _log.Error("emote.Stat or Player is null for {ResetGem}.", WorldObject.Name);
                    break;
                }

                var propertyAttribute = (PropertyAttribute)emote.Stat;

                if ((int)propertyAttribute > 6)
                {
                    _log.Error("emote.Stat is out of range for {ResetGem}. Should be 1-6.", WorldObject.Name);
                    break;
                }

                player.RefundXP(player.Attributes[propertyAttribute].ExperienceSpent);
                player.Attributes[propertyAttribute].Ranks = 0;
                player.Attributes[propertyAttribute].ExperienceSpent = 0;
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, player.Attributes[propertyAttribute]));

                player.SendMessage($"You successfully reset your {propertyAttribute} attribute to its innate state. All experience spent on the attribute has been restored.");

                break;

            case EmoteType.ResetHomePosition:

                if (WorldObject.Location != null)
                {
                    WorldObject.Home = new Position(WorldObject.Location);
                }

                break;

            case EmoteType.ResetVitalXp:

                if (emote.Stat is null || player is null)
                {
                    _log.Error("emote.Stat or Player is null for {ResetGem}.", WorldObject.Name);
                    break;
                }

                var propertyVital = (PropertyAttribute2nd)emote.Stat;

                if ((int)propertyVital > 6)
                {
                    _log.Error("emote.Stat is out of range for {ResetGem}. Should be 1, 3, or 5.", WorldObject.Name);
                    break;
                }

                player.RefundXP(player.Vitals[propertyVital].ExperienceSpent);
                player.Vitals[propertyVital].Ranks = 0;
                player.Vitals[propertyVital].ExperienceSpent = 0;
                player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, player.Vitals[propertyVital]));

                player.SendMessage($"You successfully reset your {propertyVital.ToString().Remove(0,3)} attribute to its innate state. All experience spent on the attribute has been restored.");

                break;

            case EmoteType.Say:

                if (Debug)
                {
                    Console.Write($" - {emote.Message}");
                }

                message = Replace(emote.Message, WorldObject, targetObject, emoteSet.Quest);

                var name = WorldObject.CreatureType == CreatureType.Olthoi ? WorldObject.Name + "&" : WorldObject.Name;

                range = emote.Amount ?? WorldObject.LocalBroadcastRange;

                if (emote.Extent > 0)
                {
                    WorldObject.EnqueueBroadcast(
                        new GameMessageHearRangedSpeech(
                            message,
                            name,
                            WorldObject.Guid.Full,
                            emote.Extent,
                            ChatMessageType.Emote
                        ),
                        (float)range
                    );
                }
                else
                {
                    WorldObject.EnqueueBroadcast(
                        new GameMessageHearSpeech(message, name, WorldObject.Guid.Full, ChatMessageType.Emote),
                        range
                    );
                }

                break;

            case EmoteType.SetAltRacialSkills:
                break;

            case EmoteType.SetBoolStat:

                if (player != null)
                {
                    player.UpdateProperty(player, (PropertyBool)emote.Stat, emote.Amount == 0 ? false : true);
                    player.EnqueueBroadcast(
                        false,
                        new GameMessagePublicUpdatePropertyBool(
                            player,
                            (PropertyBool)emote.Stat,
                            emote.Amount == 0 ? false : true
                        )
                    );
                }
                break;

            case EmoteType.SetEyePalette:
                //if (creature != null)
                //    creature.EyesPaletteDID = (uint)emote.Display;
                break;

            case EmoteType.SetEyeTexture:
                //if (creature != null)
                //    creature.EyesTextureDID = (uint)emote.Display;
                break;

            case EmoteType.SetFloatStat:

                if (player != null)
                {
                    player.UpdateProperty(player, (PropertyFloat)emote.Stat, emote.Percent);
                    player.EnqueueBroadcast(
                        false,
                        new GameMessagePublicUpdatePropertyFloat(
                            player,
                            (PropertyFloat)emote.Stat,
                            Convert.ToDouble(emote.Percent)
                        )
                    );
                }
                break;

            case EmoteType.SetHeadObject:
                //if (creature != null)
                //    creature.HeadObjectDID = (uint)emote.Display;
                break;

            case EmoteType.SetHeadPalette:
                break;

            case EmoteType.SetInt64Stat:

                if (player != null)
                {
                    player.UpdateProperty(player, (PropertyInt64)emote.Stat, emote.Amount64);
                    player.EnqueueBroadcast(
                        false,
                        new GameMessagePublicUpdatePropertyInt64(
                            player,
                            (PropertyInt64)emote.Stat,
                            Convert.ToInt64(emote.Amount64)
                        )
                    );
                }
                break;

            case EmoteType.SetIntStat:

                if (player != null)
                {
                    player.UpdateProperty(player, (PropertyInt)emote.Stat, emote.Amount);
                    player.EnqueueBroadcast(
                        false,
                        new GameMessagePublicUpdatePropertyInt(
                            player,
                            (PropertyInt)emote.Stat,
                            Convert.ToInt32(emote.Amount)
                        )
                    );
                }
                break;

            case EmoteType.SetLBEnviron:
                {
                    if (creature != null)
                    {
                        var environChange = EnvironChangeType.Clear;

                        if (emote.Amount != null)
                        {
                            if (Enum.IsDefined(typeof(EnvironChangeType), emote.Amount))
                            {
                                environChange = (EnvironChangeType)emote.Amount;
                            }
                            else
                            {
                                environChange = EnvironChangeType.Clear;
                            }
                        }

                        creature.CurrentLandblock?.DoEnvironChange(environChange);
                    }
                }

                break;

            case EmoteType.SetMouthPalette:
                break;

            case EmoteType.SetMouthTexture:
                //if (creature != null)
                //    creature.MouthTextureDID = (uint)emote.Display;
                break;

            case EmoteType.SetNosePalette:
                break;

            case EmoteType.SetNoseTexture:
                //if (creature != null)
                //    creature.NoseTextureDID = (uint)emote.Display;
                break;
            case EmoteType.SetMyIntStat:

                if (creature != null)
                {
                    creature.UpdateProperty(creature, (PropertyInt)emote.Stat, emote.Amount);
                }
                break;
            case EmoteType.SetMyFloatStat:

                if (creature != null)
                {
                    creature.UpdateProperty(creature, (PropertyFloat)emote.Stat, emote.Percent);
                }
                break;
            case EmoteType.SetMyBoolStat:

                if (creature != null)
                {
                    creature.UpdateProperty(creature, (PropertyBool)emote.Stat, emote.Amount == 0 ? false : true);
                }
                break;

            case EmoteType.SetMyQuestBitsOff:
            case EmoteType.SetQuestBitsOff:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null && emote.Message != null && emote.Amount != null)
                {
                    questTarget.QuestManager.SetQuestBits(emote.Message, (int)emote.Amount, false);
                }

                break;

            case EmoteType.SetMyQuestBitsOn:
            case EmoteType.SetQuestBitsOn:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null && emote.Message != null && emote.Amount != null)
                {
                    questTarget.QuestManager.SetQuestBits(emote.Message, (int)emote.Amount);
                }

                break;

            case EmoteType.SetMyQuestCompletions:
            case EmoteType.SetQuestCompletions:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null && emote.Amount != null)
                {
                    questTarget.QuestManager.SetQuestCompletions(emote.Message, (int)emote.Amount);
                }

                break;

            case EmoteType.SetSanctuaryPosition:

                if (player != null)
                {
                    player.SetPosition(
                        PositionType.Sanctuary,
                        new Position(
                            emote.ObjCellId.Value,
                            emote.OriginX.Value,
                            emote.OriginY.Value,
                            emote.OriginZ.Value,
                            emote.AnglesX.Value,
                            emote.AnglesY.Value,
                            emote.AnglesZ.Value,
                            emote.AnglesW.Value
                        )
                    );
                }

                break;

            case EmoteType.Sound:

                WorldObject.EnqueueBroadcast(new GameMessageSound(WorldObject.Guid, (Sound)emote.Sound, 1.0f));
                break;

            case EmoteType.SpendLuminance:

                if (player != null)
                {
                    player.SpendLuminance(emote.Amount64 ?? emote.HeroXP64 ?? 0);
                }

                break;

            case EmoteType.StampFellowQuest:

                if (player != null)
                {
                    if (player.Fellowship != null)
                    {
                        var questName = emote.Message;

                        player.Fellowship.QuestManager.Stamp(emote.Message);
                    }
                }
                break;

            case EmoteType.StampMyQuest:
            case EmoteType.StampQuest:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    var questName = emote.Message;

                    if (questName.EndsWith("@#kt", StringComparison.Ordinal))
                    {
                        _log.Warning(
                            $"0x{WorldObject.Guid}:{WorldObject.Name} ({WorldObject.WeenieClassId}).EmoteManager.ExecuteEmote: EmoteType.StampQuest({questName}) is a depreciated kill task method."
                        );
                    }

                    if (questName.StartsWith("ACCOUNT_") && questTarget is Player questPlayer)
                    {
                        var questNameTrimmed = QuestManager.GetQuestName(questName);
                        var characters = DatabaseManager.Shard.BaseDatabase.GetCharacters(questPlayer.Account.AccountId, true);

                        foreach (var character in characters)
                        {
                            if (character.IsDeleted)
                            {
                                continue;
                            }

                            var quest = character.GetOrCreateQuest(questNameTrimmed, questPlayer.CharacterDatabaseLock, out var questRegistryWasCreated);

                            if (questRegistryWasCreated)
                            {
                                quest.LastTimeCompleted = (uint)Time.GetUnixTime();
                                quest.NumTimesCompleted = 1; // initial add / first solve

                                quest.CharacterId = character.Id;

                                if (Debug)
                                {
                                    Console.WriteLine($"{character.Name}.QuestManager.Update({quest}): added quest");
                                }

                                questPlayer.CharacterChangesDetected = true;
                                questPlayer.ContractManager.NotifyOfQuestUpdate(quest.QuestName);
                            }
                            else
                            {
                                if (questPlayer.QuestManager.IsMaxSolves(questName))
                                {
                                    continue;
                                }

                                // update existing quest
                                quest.LastTimeCompleted = (uint)Time.GetUnixTime();
                                quest.NumTimesCompleted++;

                                questPlayer.CharacterChangesDetected = true;
                                questPlayer.ContractManager.NotifyOfQuestUpdate(quest.QuestName);
                            }
                        }
                    }

                    questTarget.QuestManager.Stamp(emote.Message);

                    if (QuestManager.CapstoneCompletionQuests.Contains(emote.Message))
                    {
                        var capstoneDifficulty = Math.Round(questTarget.CurrentLandblock.LandblockLootQualityMod * 100);
                        var fellowshipSize = 1;

                        if (questTarget is Player { Fellowship: not null } playerInFellowship)
                        {
                            fellowshipSize = playerInFellowship.Fellowship.GetFellowshipMembers().Count;
                        }

                        questTarget.QuestManager.Stamp(emote.Message+"_size:"+fellowshipSize+"_diff:"+capstoneDifficulty+"%");
                    }
                }
                break;

            case EmoteType.StartBarber:

                if (player != null)
                {
                    player.StartBarber();
                }

                break;

            case EmoteType.StartEvent:

                EventManager.StartEvent(emote.Message, WorldObject, targetObject);
                break;

            case EmoteType.StopEvent:

                EventManager.StopEvent(emote.Message, WorldObject, targetObject);
                break;

            case EmoteType.TakeItems:

                if (player != null)
                {
                    var weenieItemToTake = emote.WeenieClassId ?? 0;
                    var amountToTake = emote.StackSize ?? 1;

                    if (weenieItemToTake == 0)
                    {
                        _log.Warning(
                            $"EmoteManager.Execute: 0x{WorldObject.Guid} {WorldObject.Name} ({WorldObject.WeenieClassId}) EmoteType.TakeItems has invalid emote.WeenieClassId: {weenieItemToTake}"
                        );
                        break;
                    }

                    if (amountToTake < -1 || amountToTake == 0)
                    {
                        _log.Warning(
                            $"EmoteManager.Execute: 0x{WorldObject.Guid} {WorldObject.Name} ({WorldObject.WeenieClassId}) EmoteType.TakeItems has invalid emote.StackSize: {amountToTake}"
                        );
                        break;
                    }

                    // If a guid was stored during a Refuse emote, make sure that specific item is taken
                    uint? refusalItemGuidId = null;

                    if (creature?.RefusalItem.Item2 != null)
                    {
                        refusalItemGuidId = creature.RefusalItem.Item2;

                        creature.RefusalItem.Item1 = null;
                        creature.RefusalItem.Item2 = null;
                    }

                    if ((player.GetNumInventoryItemsOfWCID(weenieItemToTake) > 0 && player.TryConsumeFromInventoryWithNetworking(weenieItemToTake, amountToTake == -1 ? int.MaxValue : amountToTake, refusalItemGuidId))
                        || (player.GetNumEquippedObjectsOfWCID(weenieItemToTake) > 0 && player.TryConsumeFromEquippedObjectsWithNetworking(weenieItemToTake, amountToTake == -1 ? int.MaxValue : amountToTake))
                    )
                    {
                        var itemTaken = DatabaseManager.World.GetCachedWeenie(weenieItemToTake);
                        if (itemTaken != null)
                        {
                            var amount = amountToTake == -1 ? "all" : amountToTake.ToString();

                            if (!WorldObject.TakeItemsSilently.HasValue || WorldObject.TakeItemsSilently == false)
                            {
                                var msg = $"You hand over {amount} of your {itemTaken.GetPluralName()}.";
                                player.Session.Network.EnqueueSend(
                                    new GameMessageSystemChat(msg, ChatMessageType.Broadcast)
                                );
                            }
                        }
                    }
                }
                break;

            case EmoteType.TeachSpell:

                if (player != null)
                {
                    player.LearnSpellWithNetworking((uint)emote.SpellId, false);
                }

                break;

            case EmoteType.TeleportSelf:

                //if (WorldObject is Player)
                //(WorldObject as Player).Teleport(emote.Position);
                break;

            case EmoteType.TeleportTarget:

                if (player != null)
                {
                    if (emote.OriginX.HasValue
                        && emote.OriginY.HasValue
                        && emote.OriginZ.HasValue
                        && emote.AnglesX.HasValue
                        && emote.AnglesY.HasValue
                        && emote.AnglesZ.HasValue
                        && emote.AnglesW.HasValue
                    )
                    {
                        switch (emote.ObjCellId)
                        {
                            // if ObjCellId is null, teleport to position within current cell
                            case null:
                                var destination = new Position(
                                    WorldObject.Location.Cell,
                                    emote.OriginX.Value,
                                    emote.OriginY.Value,
                                    emote.OriginZ.Value,
                                    emote.AnglesX.Value,
                                    emote.AnglesY.Value,
                                    emote.AnglesZ.Value,
                                    emote.AnglesW.Value
                                );

                                WorldObject.AdjustDungeon(destination);
                                WorldManager.ThreadSafeTeleport(player, destination);
                                break;
                            case > 0:
                            {
                                destination = new Position(
                                    emote.ObjCellId.Value,
                                    emote.OriginX.Value,
                                    emote.OriginY.Value,
                                    emote.OriginZ.Value,
                                    emote.AnglesX.Value,
                                    emote.AnglesY.Value,
                                    emote.AnglesZ.Value,
                                    emote.AnglesW.Value
                                );

                                WorldObject.AdjustDungeon(destination);
                                WorldManager.ThreadSafeTeleport(player, destination);
                                break;
                            }
                            // position is relative to WorldObject's current location
                            default:
                            {
                                var relativeDestination = new Position(WorldObject.Location);
                                relativeDestination.Pos += new Vector3(
                                    emote.OriginX.Value,
                                    emote.OriginY.Value,
                                    emote.OriginZ.Value
                                );
                                relativeDestination.Rotation = new Quaternion(
                                    emote.AnglesX.Value,
                                    emote.AnglesY.Value,
                                    emote.AnglesZ.Value,
                                    emote.AnglesW.Value
                                );
                                relativeDestination.LandblockId = new LandblockId(relativeDestination.GetCell());

                                WorldObject.AdjustDungeon(relativeDestination);
                                WorldManager.ThreadSafeTeleport(player, relativeDestination);
                                break;
                            }
                        }
                    }
                }
                break;

            case EmoteType.Tell:

                if (player != null)
                {
                    message = Replace(emote.Message, WorldObject, player, emoteSet.Quest);
                    player.Session.Network.EnqueueSend(
                        new GameEventTell(WorldObject, message, player, ChatMessageType.Tell)
                    );
                }
                break;

            case EmoteType.TellFellow:

                if (player != null)
                {
                    var fellowship = player.Fellowship;
                    if (fellowship != null)
                    {
                        text = Replace(emote.Message, WorldObject, player, emoteSet.Quest);

                        fellowship.TellFellow(WorldObject, text);
                    }
                }
                break;

            case EmoteType.TextDirect:

                if (player != null)
                {
                    message = Replace(emote.Message, WorldObject, player, emoteSet.Quest);
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Broadcast));
                }
                break;

            case EmoteType.Turn:

                if (creature != null)
                {
                    // turn to heading
                    var rotation = new Quaternion(
                        emote.AnglesX ?? 0,
                        emote.AnglesY ?? 0,
                        emote.AnglesZ ?? 0,
                        emote.AnglesW ?? 1
                    );
                    var newPos = new Position(creature.Location);
                    newPos.Rotation = rotation;

                    var rotateTime = creature.TurnTo(newPos);
                    delay = rotateTime;
                }
                break;

            case EmoteType.TurnToTarget:

                if (creature != null && targetCreature != null)
                {
                    delay = creature.Rotate(targetCreature);
                }

                break;

            case EmoteType.UntrainSkill:

                if (player != null)
                {
                    player.ResetSkill((Skill)emote.Stat);
                }

                break;

            case EmoteType.UpdateFellowQuest:

                if (player != null)
                {
                    if (player.Fellowship != null)
                    {
                        var questName = emote.Message;

                        var hasQuest = player.Fellowship.QuestManager.HasQuest(questName);

                        if (!hasQuest)
                        {
                            // add new quest
                            player.Fellowship.QuestManager.Update(questName);
                            hasQuest = player.Fellowship.QuestManager.HasQuest(questName);
                            ExecuteEmoteSet(
                                hasQuest ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                                emote.Message,
                                targetObject,
                                true
                            );
                        }
                        else
                        {
                            // update existing quest
                            var canSolve = player.Fellowship.QuestManager.CanSolve(questName);
                            if (canSolve)
                            {
                                player.Fellowship.QuestManager.Stamp(questName);
                            }

                            ExecuteEmoteSet(
                                canSolve ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                                emote.Message,
                                targetObject,
                                true
                            );
                        }
                    }
                    else
                    {
                        ExecuteEmoteSet(EmoteCategory.QuestNoFellow, emote.Message, targetObject, true);
                    }
                }
                break;

            case EmoteType.UpdateMyQuest:
            case EmoteType.UpdateQuest:

                questTarget = GetQuestTarget((EmoteType)emote.Type, targetCreature, creature);

                if (questTarget != null)
                {
                    var questName = emote.Message;

                    var hasQuest = questTarget.QuestManager.HasQuest(questName);

                    if (!hasQuest)
                    {
                        // add new quest
                        questTarget.QuestManager.Update(questName);
                        hasQuest = questTarget.QuestManager.HasQuest(questName);
                        ExecuteEmoteSet(
                            hasQuest ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                    else
                    {
                        // update existing quest
                        var canSolve = questTarget.QuestManager.CanSolve(questName);
                        if (canSolve)
                        {
                            questTarget.QuestManager.Stamp(questName);
                        }

                        ExecuteEmoteSet(
                            canSolve ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure,
                            emote.Message,
                            targetObject,
                            true
                        );
                    }
                }

                break;

            case EmoteType.WorldBroadcast:

                message = Replace(text, WorldObject, targetObject, emoteSet.Quest);

                PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast), message, "World");

                PlayerManager.LogBroadcastChat(Channel.AllBroadcast, WorldObject, message);

                break;

            case EmoteType.TrainSkill:

                if (player != null)
                {
                    var trainedSuccessfully = player.TrainSkill((Skill)emote.Stat, 0);

                    if (trainedSuccessfully)
                    {
                        var updateSkill = new GameMessagePrivateUpdateSkill(
                            player,
                            player.GetCreatureSkill((Skill)emote.Stat)
                        );

                        var msg = new GameMessageSystemChat(
                            $"{((NewSkillNames)emote.Stat).ToSentence()} trained.",
                            ChatMessageType.Advancement
                        );

                        player.Session.Network.EnqueueSend(updateSkill, msg);
                    }
                    else
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"Failed to train {((NewSkillNames)emote.Stat).ToSentence()}!",
                                ChatMessageType.Advancement
                            )
                        );
                    }
                }
                break;

            case EmoteType.BroadcastSpellStacks:

                if (player != null)
                {
                    player.GetAllSpellStacks();
                }
                break;

            case EmoteType.RemoveEnchantment:

                if (player != null)
                {
                    var emoteSpellId = Convert.ToUInt32(emote.SpellId);

                    if (emoteSpellId != 0)
                    {
                        var spellToRemove = new Spell(emoteSpellId);
                        var enchantments = player.EnchantmentManager.GetEnchantments(spellToRemove.Category);
                        player.EnchantmentManager.Dispel(enchantments);
                    }
                }
                break;

            case EmoteType.StampQuestForAllFellows:
                var targetPlayer = targetCreature as Player;

                // If emote is triggered by a player-created hotspot, reference the hotspot's player
                if (targetObject is Hotspot {P_HotspotOwner: not null} targetHotspot)
                {
                    targetPlayer = targetHotspot.P_HotspotOwner;
                }

                if (targetPlayer is null)
                {
                    _log.Error("ExecuteEmote({EmoteSet}, {Emote}, {TargetObject}) - StampQuestForAllFellows - targetPlayer is null.", emoteSet, emote, targetObject);
                    break;
                }

                // Stamp quest for killer, regardless of if in a fellowship
                questTarget = GetQuestTarget((EmoteType)emote.Type, targetPlayer, creature);

                if (questTarget != null)
                {
                    var questName = emote.Message;

                    if (questName.EndsWith("@#kt", StringComparison.Ordinal))
                    {
                        _log.Warning(
                            $"0x{WorldObject.Guid}:{WorldObject.Name} ({WorldObject.WeenieClassId}).EmoteManager.ExecuteEmote: EmoteType.StampQuest({questName}) is a depreciated kill task method."
                        );
                    }

                    questTarget.QuestManager.Stamp(emote.Message);
                }

                // If killer is in a fellowship, also stamp the quest for all fellows
                if (targetPlayer != null && targetPlayer.Fellowship != null)
                {
                    foreach (var fellow in targetPlayer.Fellowship.GetFellowshipMembers().Values)
                    {
                        if (targetPlayer == fellow)
                        {
                            continue;
                        }

                        questTarget = GetQuestTarget((EmoteType)emote.Type, fellow, creature);

                        if (questTarget != null)
                        {
                            var questName = emote.Message;

                            if (questName.EndsWith("@#kt", StringComparison.Ordinal))
                            {
                                _log.Warning(
                                    $"0x{WorldObject.Guid}:{WorldObject.Name} ({WorldObject.WeenieClassId}).EmoteManager.ExecuteEmote: EmoteType.StampQuest({questName}) is a depreciated kill task method."
                                );
                            }

                            questTarget.QuestManager.Stamp(emote.Message);
                        }
                    }
                }

                break;

            case EmoteType.AdjustServerPropertyLong:

                var adjustPropertyString = emote.Message;
                var adjustmentAmount = Convert.ToInt64(emote.Amount);
                var currentValue = PropertyManager.GetLong(adjustPropertyString).Item;

                if (adjustPropertyString != null)
                {
                    var newValue = currentValue + adjustmentAmount;

                    if (newValue >= 0)
                    {
                        PropertyManager.ModifyLong(adjustPropertyString, newValue);
                    }
                }

                break;

            // -----------------------------------------------------------------------------
            // EmoteType.InqServerPropertyLong  (content author guide)
            //
            // Add an emote_action on the triggering emote (e.g., Use=7 or ReceiveLocalSignal=37):
            //   type        = 10016 (InqServerPropertyLong)
            //   message     = <PROPERTY_KEY>               // exact shard key name
            //   min / max   = optional inclusive bounds    // NULL = unbounded on that side
            //   test_String = optional NOQ key             // ONLY if you want the NOQ branch
            //
            // Then add target emotes on the same object with quest = <PROPERTY_KEY>:
            //   21 (TestSuccess): fires when value ∈ [min..max]  → put StartEvent, etc.
            //   22 (TestFailure): fires when value ∉ [min..max]  → put StopEvent, etc.
            //   23 (TestNoQuality): fires only if test_String is set AND HasValidTestNoQuality
            // -----------------------------------------------------------------------------
            case EmoteType.InqServerPropertyLong:
            {
                var key = emote.Message;
                if (key != null)
                {
                    // Get the shard property (long)
                    var propertyValue = PropertyManager.GetLong(key).Item;

                    // Optional NOQ path (explicit opt-in via test_String)
                    if (!string.IsNullOrWhiteSpace(emote.TestString) &&
                        HasValidTestNoQuality(emote.TestString))
                    {
                        ExecuteEmoteSet(EmoteCategory.TestNoQuality, emote.TestString!, targetObject, true);
                    }
                    else
                    {
                        // Range path (default) – use long sentinels; assign to OUTER 'success'
                        var lowerBound = emote.Min.HasValue ? (long)emote.Min.Value : long.MinValue;
                        var upperBound = emote.Max.HasValue ? (long)emote.Max.Value : long.MaxValue;

                        success = (propertyValue >= lowerBound) && (propertyValue <= upperBound);

                        // quest filter = key (content must set quest on 21/22 to <PROPERTY_KEY>)
                        ExecuteEmoteSet(
                            success ? EmoteCategory.TestSuccess : EmoteCategory.TestFailure,
                            key,
                            targetObject,
                            true
                        );
                    }
                }
                break;
            }



            default:
                _log.Debug(
                    "EmoteManager.Execute - Encountered Unhandled EmoteType {EmoteType} for {WorldObjectName} ({WorldObjectWeenieClassId})",
                    (EmoteType)emote.Type,
                    WorldObject.Name,
                    WorldObject.WeenieClassId
                );
                break;
        }

        return delay;
    }

    /// <summary>
    /// Selects an emote set based on category, and optional: quest, vendor, rng
    /// </summary>
    public PropertiesEmote GetEmoteSet(
        EmoteCategory category,
        string questName = null,
        VendorType? vendorType = null,
        uint? wcid = null,
        bool useRNG = true
    )
    {
        //if (Debug) Console.WriteLine($"{WorldObject.Name}.EmoteManager.GetEmoteSet({category}, {questName}, {vendorType}, {wcid}, {useRNG})");

        if (_worldObject.Biota.PropertiesEmote == null)
        {
            return null;
        }

        // always pull emoteSet from _worldObject
        var emoteSet = _worldObject.Biota.PropertiesEmote.Where(e => e.Category == category);

        // optional criteria
        if ((category == EmoteCategory.HearChat || category == EmoteCategory.ReceiveTalkDirect) && questName != null)
        {
            emoteSet = emoteSet.Where(e =>
                e.Quest != null && e.Quest.Equals(questName, StringComparison.OrdinalIgnoreCase) || e.Quest == null
            );
        }
        else if (questName != null)
        {
            emoteSet = emoteSet.Where(e =>
                e.Quest != null && e.Quest.Equals(questName, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (vendorType != null)
        {
            emoteSet = emoteSet.Where(e => e.VendorType != null && e.VendorType.Value == vendorType);
        }

        if (wcid != null)
        {
            emoteSet = emoteSet.Where(e => e.WeenieClassId == wcid.Value);
        }

        if (category == EmoteCategory.HeartBeat)
        {
            WorldObject.GetCurrentMotionState(out var currentStance, out var currentMotion);

            emoteSet = emoteSet.Where(e => e.Style == null || e.Style == currentStance);
            emoteSet = emoteSet.Where(e => e.Substyle == null || e.Substyle == currentMotion);
        }

        if (category == EmoteCategory.WoundedTaunt)
        {
            if (_worldObject is Creature creature)
            {
                emoteSet = emoteSet.Where(e =>
                    creature.Health.Percent >= e.MinHealth && creature.Health.Percent <= e.MaxHealth
                );
            }
        }

        if (useRNG)
        {
            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
            emoteSet = emoteSet.Where(e => e.Probability > rng).OrderBy(e => e.Probability);
            //emoteSet = emoteSet.Where(e => e.Probability >= rng);
        }

        return emoteSet.FirstOrDefault();
    }

    /// <summary>
    /// Convenience wrapper between GetEmoteSet and ExecututeEmoteSet
    /// </summary>
    public void ExecuteEmoteSet(
        EmoteCategory category,
        string quest = null,
        WorldObject targetObject = null,
        bool nested = false
    )
    {
        //if (Debug) Console.WriteLine($"{WorldObject.Name}.EmoteManager.ExecuteEmoteSet({category}, {quest}, {targetObject}, {nested})");

        var emoteSet = GetEmoteSet(category, quest);

        if (emoteSet == null)
        {
            return;
        }

        // TODO: revisit if nested chains need to propagate timers
        ExecuteEmoteSet(emoteSet, targetObject, nested);
    }

    /// <summary>
    /// Executes a set of emotes to run with delays
    /// </summary>
    /// <param name="emoteSet">A list of emotes to execute</param>
    /// <param name="targetObject">An optional target, usually player</param>
    /// <param name="actionChain">For adding delays between emotes</param>
    public bool ExecuteEmoteSet(PropertiesEmote emoteSet, WorldObject targetObject = null, bool nested = false)
    {
        //if (Debug) Console.WriteLine($"{WorldObject.Name}.EmoteManager.ExecuteEmoteSet({emoteSet}, {targetObject}, {nested})");

        // detect busy state
        // TODO: maybe eventually we should consider having categories that can be queued?
        // there are some categories that shouldn't be queued, like heartbeats...
        if (IsBusy && !nested)
        {
            return false;
        }

        // start action chain
        Nested++;
        Enqueue(emoteSet, targetObject);

        return true;
    }

    public void Enqueue(PropertiesEmote emoteSet, WorldObject targetObject, int emoteIdx = 0, float delay = 0.0f)
    {
        //if (Debug) Console.WriteLine($"{WorldObject.Name}.EmoteManager.Enqueue({emoteSet}, {targetObject}, {emoteIdx}, {delay})");

        if (emoteSet == null)
        {
            Nested--;
            return;
        }

        IsBusy = true;

        // Ensure the action collection is present and the requested index is valid.
        // Protect against ArgumentOutOfRangeException observed in production.
        if (emoteSet.PropertiesEmoteAction == null)
        {
            _log.Error(
                "Enqueue - emoteSet.PropertiesEmoteAction is null. Aborting. WorldObject=0x{Guid} {Name} ({WeenieClassId}), EmoteSet={Category}:{Quest}",
                WorldObject.Guid,
                WorldObject.Name,
                WorldObject.WeenieClassId,
                emoteSet.Category,
                emoteSet.Quest
            );

            Nested--;

            if (Nested == 0)
            {
                IsBusy = false;
            }

            return;
        }

        // Try to obtain an IList for O(1) Count and index access; fall back to materializing the sequence.
        var actionsList = emoteSet.PropertiesEmoteAction as IList<PropertiesEmoteAction> ?? emoteSet.PropertiesEmoteAction.ToList();

        if (actionsList.Count == 0)
        {
            _log.Warning(
                "Enqueue - emoteSet.PropertiesEmoteAction is empty. Nothing to enqueue. WorldObject=0x{Guid} {Name} ({WeenieClassId}), EmoteSet={Category}:{Quest}",
                WorldObject.Guid,
                WorldObject.Name,
                WorldObject.WeenieClassId,
                emoteSet.Category,
                emoteSet.Quest
            );

            Nested--;

            if (Nested == 0)
            {
                IsBusy = false;
            }

            return;
        }

        if (emoteIdx < 0 || emoteIdx >= actionsList.Count)
        {
            _log.Error(
                "Enqueue - emoteIdx out of range. Requested {RequestedIndex} but valid range is 0..{MaxIndex}. Aborting to prevent crash. WorldObject=0x{Guid} {Name} ({WeenieClassId}), EmoteSet={Category}:{Quest}",
                emoteIdx,
                actionsList.Count - 1,
                WorldObject.Guid,
                WorldObject.Name,
                WorldObject.WeenieClassId,
                emoteSet.Category,
                emoteSet.Quest
            );

            // include a minimal diagnostic snippet
            try
            {
                var actionSummaries = string.Join(", ", actionsList.Select(a => ((EmoteType)a.Type).ToString() + (string.IsNullOrEmpty(a.Message) ? "" : $":{a.Message}")));
                _log.Debug("Enqueue - emote actions: {Actions}", actionSummaries);
            }
            catch
            {
                // ignore diagnostics failures
            }

            Nested--;

            if (Nested == 0)
            {
                IsBusy = false;
            }

            return;
        }

        var emote = actionsList[emoteIdx];

        if (
            Nested > 75
            && !string.IsNullOrEmpty(emoteSet.Quest)
            && emoteSet.Quest == emote.Message
            && EmoteIsBranchingType(emote)
        )
        {
            var emoteStack = $"{emoteSet.Category}: {emoteSet.Quest}\n";
            foreach (var e in emoteSet.PropertiesEmoteAction)
            {
                emoteStack +=
                    $"       - {(EmoteType)emote.Type}{(string.IsNullOrEmpty(emote.Message) ? "" : $": {emote.Message}")}\n";
            }

            _log.Error(
                $"[EMOTE] {WorldObject.Name}.EmoteManager.Enqueue(): Nested > 75, possible Infinite loop detected and aborted on 0x{WorldObject.Guid}:{WorldObject.WeenieClassId}\n-> {emoteStack}"
            );

            Nested--;

            if (Nested == 0)
            {
                IsBusy = false;
            }

            return;
        }

        if (delay + emote.Delay > 0)
        {
            var actionChain = new ActionChain();

            if (Debug)
            {
                actionChain.AddAction(WorldObject, () => Console.Write($"{emote.Delay} - "));
            }

            // delay = post-delay from actual time of previous emote
            // emote.Delay = pre-delay for current emote
            actionChain.AddDelaySeconds(delay + emote.Delay);

            actionChain.AddAction(WorldObject, () => DoEnqueue(emoteSet, targetObject, emoteIdx, emote));
            actionChain.EnqueueChain();
        }
        else
        {
            DoEnqueue(emoteSet, targetObject, emoteIdx, emote);
        }
    }

    /// <summary>
    /// This should only be called by Enqueue
    /// </summary>
    private void DoEnqueue(
        PropertiesEmote emoteSet,
        WorldObject targetObject,
        int emoteIdx,
        PropertiesEmoteAction emote
    )
    {
        if (Debug)
        {
            Console.Write($"{(EmoteType)emote.Type}");
        }

        //if (!string.IsNullOrEmpty(emoteSet.Quest) && emoteSet.Quest == emote.Message && EmoteIsBranchingType(emote))
        //{
        //    _log.Error($"[EMOTE] {WorldObject.Name}.EmoteManager.DoEnqueue(): Infinite loop detected on 0x{WorldObject.Guid}:{WorldObject.WeenieClassId}\n-> {emoteSet.Category}: {emoteSet.Quest} to {(EmoteType)emote.Type}: {emote.Message}");

        //    Nested--;

        //    if (Nested == 0)
        //        IsBusy = false;

        //    return;
        //}

        var nextDelay = ExecuteEmote(emoteSet, emote, targetObject);

        if (Debug)
        {
            Console.WriteLine($" - {nextDelay}");
        }

        if (emoteIdx < emoteSet.PropertiesEmoteAction.Count - 1)
        {
            Enqueue(emoteSet, targetObject, emoteIdx + 1, nextDelay);
        }
        else
        {
            if (nextDelay > 0)
            {
                var delayChain = new ActionChain();
                delayChain.AddDelaySeconds(nextDelay);
                delayChain.AddAction(
                    WorldObject,
                    () =>
                    {
                        Nested--;

                        if (Nested == 0)
                        {
                            IsBusy = false;
                        }
                    }
                );
                delayChain.EnqueueChain();
            }
            else
            {
                Nested--;

                if (Nested == 0)
                {
                    IsBusy = false;
                }
            }
        }
    }

    private bool EmoteIsBranchingType(PropertiesEmoteAction emote)
    {
        if (emote == null)
        {
            return false;
        }

        var emoteType = (EmoteType)emote.Type;

        switch (emoteType)
        {
            case EmoteType.UpdateQuest:
            case EmoteType.InqQuest:
            case EmoteType.InqQuestSolves:
            case EmoteType.InqBoolStat:
            case EmoteType.InqIntStat:
            case EmoteType.InqFloatStat:
            case EmoteType.InqStringStat:
            case EmoteType.InqAttributeStat:
            case EmoteType.InqRawAttributeStat:
            case EmoteType.InqSecondaryAttributeStat:
            case EmoteType.InqRawSecondaryAttributeStat:
            case EmoteType.InqSkillStat:
            case EmoteType.InqRawSkillStat:
            case EmoteType.InqSkillTrained:
            case EmoteType.InqSkillSpecialized:
            case EmoteType.InqEvent:
            case EmoteType.InqFellowQuest:
            case EmoteType.InqFellowNum:
            case EmoteType.UpdateFellowQuest:
            case EmoteType.Goto:
            case EmoteType.InqNumCharacterTitles:
            case EmoteType.InqYesNo:
            case EmoteType.InqOwnsItems:
            case EmoteType.UpdateMyQuest:
            case EmoteType.InqMyQuest:
            case EmoteType.InqMyQuestSolves:
            case EmoteType.InqPackSpace:
            case EmoteType.InqQuestBitsOn:
            case EmoteType.InqQuestBitsOff:
            case EmoteType.InqMyQuestBitsOn:
            case EmoteType.InqMyQuestBitsOff:
            case EmoteType.InqInt64Stat:
            case EmoteType.InqContractsFull:
                return true;
            default:
                return false;
        }
    }

    public bool HasValidTestNoQuality(string testName) => GetEmoteSet(EmoteCategory.TestNoQuality, testName) != null;

    public bool HasValidTestNoFellow(string testName) => GetEmoteSet(EmoteCategory.TestNoFellow, testName) != null;

    /// <summary>
    /// The maximum animation range of the client
    /// Motions broadcast outside of this range will be automatically queued by client
    /// </summary>
    public static float ClientMaxAnimRange = 96.0f; // verify: same indoors?

    /// <summary>
    /// The client automatically queues animations that are broadcast outside of 96.0f range
    /// Normally we exclude these emotes from being broadcast outside this range,
    /// but for certain emotes (like monsters going to sleep) we want to always broadcast / enqueue
    /// </summary>
    public static HashSet<MotionCommand> MotionQueue = new HashSet<MotionCommand>() { MotionCommand.Sleeping };

    public void DoVendorEmote(VendorType vendorType, WorldObject target)
    {
        var vendorSet = GetEmoteSet(EmoteCategory.Vendor, null, vendorType);
        var heartbeatSet = GetEmoteSet(EmoteCategory.Vendor, null, VendorType.Heartbeat);

        ExecuteEmoteSet(vendorSet, target);
        ExecuteEmoteSet(heartbeatSet, target, true);
    }

    public IEnumerable<PropertiesEmote> Emotes(EmoteCategory emoteCategory)
    {
        return WorldObject.Biota.PropertiesEmote.Where(x => x.Category == emoteCategory);
    }

    public string Replace(string message, WorldObject source, WorldObject target, string quest)
    {
        var result = message;

        if (result == null)
        {
            _log.Warning(
                $"[EMOTE] {WorldObject.Name}.EmoteManager.Replace(message, {source.Name}:0x{source.Guid}:{source.WeenieClassId}, {target.Name}:0x{target.Guid}:{target.WeenieClassId}, {quest}): message was null!"
            );
            return "";
        }

        var sourceName = source != null ? source.Name : "";
        var targetName = target != null ? target.Name : "";

        result = result.Replace("%n", sourceName);
        result = result.Replace("%mn", sourceName);
        result = result.Replace("%s", targetName);
        result = result.Replace("%tn", targetName);

        var sourceLevel = source != null ? $"{source.Level ?? 0}" : "";
        var targetLevel = target != null ? $"{target.Level ?? 0}" : "";
        result = result.Replace("%ml", sourceLevel);
        result = result.Replace("%tl", targetLevel);

        //var sourceTemplate = source != null ? source.GetProperty(PropertyString.Title) : "";
        //var targetTemplate = source != null ? target.GetProperty(PropertyString.Title) : "";
        var sourceTemplate = source != null ? source.GetProperty(PropertyString.Template) : "";
        var targetTemplate = target != null ? target.GetProperty(PropertyString.Template) : "";
        result = result.Replace("%mt", sourceTemplate);
        result = result.Replace("%tt", targetTemplate);

        var sourceHeritage = source != null ? source.HeritageGroupName : "";
        var targetHeritage = target != null ? target.HeritageGroupName : "";
        result = result.Replace("%mh", sourceHeritage);
        result = result.Replace("%th", targetHeritage);

        //result = result.Replace("%mf", $"{source.GetProperty(PropertyString.Fellowship)}");
        //result = result.Replace("%tf", $"{target.GetProperty(PropertyString.Fellowship)}");

        //result = result.Replace("%l", $"{???}"); // level?
        //result = result.Replace("%pk", $"{???}"); // pk status?
        //result = result.Replace("%a", $"{???}"); // allegiance?
        //result = result.Replace("%p", $"{???}"); // patron?

        // Find quest in standard or LSD custom usage for %tqt and %CDtime
        var embeddedQuestName = result.Contains("@") ? message.Split("@")[0] : null;
        var questName = !string.IsNullOrWhiteSpace(embeddedQuestName) ? embeddedQuestName : quest;

        // LSD custom tqt usage
        result = result.Replace(
            $"{questName}@%tqt",
            "You may complete this quest again in %tqt.",
            StringComparison.OrdinalIgnoreCase
        );

        // LSD custom CDtime variable
        if (result.Contains("%CDtime"))
        {
            result = result.Replace($"{questName}@", "", StringComparison.OrdinalIgnoreCase);
        }

        if (target is Player targetPlayer)
        {
            result = result.Replace(
                "%tqt",
                !string.IsNullOrWhiteSpace(quest)
                    ? targetPlayer.QuestManager.GetNextSolveTime(questName).GetFriendlyString()
                    : ""
            );

            result = result.Replace(
                "%CDtime",
                !string.IsNullOrWhiteSpace(quest)
                    ? targetPlayer.QuestManager.GetNextSolveTime(questName).GetFriendlyString()
                    : ""
            );

            result = result.Replace(
                "%tf",
                $"{(targetPlayer.Fellowship != null ? targetPlayer.Fellowship.FellowshipName : "")}"
            );

            result = result.Replace(
                "%fqt",
                !string.IsNullOrWhiteSpace(quest) && targetPlayer.Fellowship != null
                    ? targetPlayer.Fellowship.QuestManager.GetNextSolveTime(questName).GetFriendlyString()
                    : ""
            );

            result = result.Replace(
                "%tqm",
                !string.IsNullOrWhiteSpace(quest) ? targetPlayer.QuestManager.GetMaxSolves(questName).ToString() : ""
            );

            result = result.Replace(
                "%tqc",
                !string.IsNullOrWhiteSpace(quest)
                    ? targetPlayer.QuestManager.GetCurrentSolves(questName).ToString()
                    : ""
            );
        }

        if (source is Creature sourceCreature)
        {
            result = result.Replace(
                "%mqt",
                !string.IsNullOrWhiteSpace(quest)
                    ? sourceCreature.QuestManager.GetNextSolveTime(questName).GetFriendlyString()
                    : ""
            );

            result = result.Replace(
                "%mxqt",
                !string.IsNullOrWhiteSpace(quest)
                    ? sourceCreature.QuestManager.GetNextSolveTime(questName).GetFriendlyLongString()
                    : ""
            );

            //result = result.Replace("%CDtime", !string.IsNullOrWhiteSpace(quest) ? sourceCreature.QuestManager.GetNextSolveTime(questName).GetFriendlyString() : "");

            result = result.Replace(
                "%mqc",
                !string.IsNullOrWhiteSpace(quest)
                    ? sourceCreature.QuestManager.GetCurrentSolves(questName).ToString()
                    : ""
            );
        }

        var olthoiNorthCampSouthSupplyLevel = PropertyManager.GetLong("olthoi_north_camp_south_supply_level").Item;
        var olthoiNorthCampSouthSupplyPercentile = $"{Math.Round(olthoiNorthCampSouthSupplyLevel / 10.0f, 1)}";

        result = result.Replace("%onss", olthoiNorthCampSouthSupplyPercentile);

        var olthoiNorthCampNorthSupplyLevel = PropertyManager.GetLong("olthoi_north_camp_north_supply_level").Item;
        var olthoiNorthCampNorthSupplyPercentile = $"{Math.Round(olthoiNorthCampNorthSupplyLevel / 10.0f, 1)}";

        result = result.Replace("%onns", olthoiNorthCampNorthSupplyPercentile);

        var olthoiNorthCampWestSupplyLevel = PropertyManager.GetLong("olthoi_north_camp_west_supply_level").Item;
        var olthoiNorthCampWestSupplyPercentile = $"{Math.Round(olthoiNorthCampWestSupplyLevel / 10.0f, 1)}";

        result = result.Replace("%onws", olthoiNorthCampWestSupplyPercentile);

        var fragmentStabilityPhaseOneLevel = PropertyManager.GetLong("fragment_stability_phase_one").Item;
        var fragmentStabilityPhaseOnePercentile = $"{Math.Round(fragmentStabilityPhaseOneLevel / 150.0f, 1)}";

        result = result.Replace("%fspo", fragmentStabilityPhaseOnePercentile);

        return result;
    }

    /// <summary>
    /// Returns the creature target for quest emotes
    /// </summary>
    public static Creature GetQuestTarget(EmoteType emote, Creature target, Creature self)
    {
        switch (emote)
        {
            // MyQuest always targets self
            case EmoteType.DecrementMyQuest:
            case EmoteType.EraseMyQuest:
            case EmoteType.IncrementMyQuest:
            case EmoteType.InqMyQuest:
            case EmoteType.InqMyQuestBitsOff:
            case EmoteType.InqMyQuestBitsOn:
            case EmoteType.InqMyQuestSolves:
            case EmoteType.SetMyQuestBitsOff:
            case EmoteType.SetMyQuestBitsOn:
            case EmoteType.SetMyQuestCompletions:
            case EmoteType.StampMyQuest:
            case EmoteType.UpdateMyQuest:

                return self;

            default:

                return target ?? self;
        }
    }

    private WorldObject GetSpellTarget(Spell spell, WorldObject target)
    {
        var targetSelf = spell.Flags.HasFlag(SpellFlags.SelfTargeted);
        var untargeted = spell.NonComponentTargetType == ItemType.None;

        var spellTarget = target;
        if (untargeted)
        {
            spellTarget = null;
        }
        else if (targetSelf)
        {
            spellTarget = WorldObject;
        }

        return spellTarget;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HeartBeat()
    {
        // player didn't do idle emotes in retail?
        if (WorldObject is Player)
        {
            return;
        }

        if (WorldObject is Creature creature && creature.IsAwake)
        {
            // Patrol mobs are always "awake" so they can think/move.
            // Still allow heartbeat emotes while patrolling, but only when not in combat.
            if (!(creature.HasPatrol && creature.AttackTarget == null))
            {
                return;
            }
        }

        ExecuteEmoteSet(EmoteCategory.HeartBeat);
    }


    public void OnUse(Creature activator)
    {
        // Don't let player 'Use' interrupt combat AI.
        if (WorldObject is Creature usedCreature && usedCreature.AttackTarget != null)
        {
            return;
        }

        if (WorldObject is Creature creature && creature.HasPatrol && creature.AttackTarget == null)
        {
            creature.CancelMoveToForEmote();
            creature.PatrolResetDestination();
        }

        ExecuteEmoteSet(EmoteCategory.Use, null, activator);

        if (activator is Player player && MarketBroker.IsMarketBroker(WorldObject))
        {
            MarketBroker.SendHelp(player);
        }
    }



    public void OnPortal(Creature activator)
    {
        IsBusy = false;

        ExecuteEmoteSet(EmoteCategory.Portal, null, activator);
    }

    public void OnActivation(Creature activator)
    {
        ExecuteEmoteSet(EmoteCategory.Activation, null, activator);
    }

    public void OnGeneration()
    {
        ExecuteEmoteSet(EmoteCategory.Generation, null, null);
    }

    public void OnWield(Creature wielder)
    {
        ExecuteEmoteSet(EmoteCategory.Wield, null, wielder);
    }

    public void OnUnwield(Creature wielder)
    {
        ExecuteEmoteSet(EmoteCategory.UnWield, null, wielder);
    }

    public void OnPickup(Creature initiator)
    {
        ExecuteEmoteSet(EmoteCategory.PickUp, null, initiator);
    }

    public void OnDrop(Creature dropper)
    {
        ExecuteEmoteSet(EmoteCategory.Drop, null, dropper);
    }

    /// <summary>
    /// Called when an idle mob becomes alerted by a player
    /// and initially wakes up
    /// </summary>
    public void OnWakeUp(Creature target)
    {
        ExecuteEmoteSet(EmoteCategory.Scream, null, target);
    }

    /// <summary>
    /// Called when a monster switches targets
    /// </summary>
    public void OnNewEnemy(WorldObject newEnemy)
    {
        ExecuteEmoteSet(EmoteCategory.NewEnemy, null, newEnemy);
    }

    /// <summary>
    /// Called when a monster completes an attack
    /// </summary>
    public void OnAttack(WorldObject target)
    {
        ExecuteEmoteSet(EmoteCategory.Taunt, null, target);
    }

    public void OnDamage(Creature attacker)
    {
        ExecuteEmoteSet(EmoteCategory.WoundedTaunt, null, attacker);
    }

    public void OnReceiveCritical(Creature attacker)
    {
        ExecuteEmoteSet(EmoteCategory.ReceiveCritical, null, attacker);
    }

    public void OnResistSpell(Creature attacker)
    {
        ExecuteEmoteSet(EmoteCategory.ResistSpell, null, attacker);
    }

    public void OnDeath(DamageHistoryInfo lastDamagerInfo)
    {
        IsBusy = false;

        var lastDamager = lastDamagerInfo?.TryGetPetOwnerOrAttacker();

        ExecuteEmoteSet(EmoteCategory.Death, null, lastDamager);
    }

    /// <summary>
    /// Called when a monster kills a player
    /// </summary>
    public void OnKill(Player player)
    {
        ExecuteEmoteSet(EmoteCategory.KillTaunt, null, player);
    }

    /// <summary>
    /// Called when player interacts with item that has a Quest string
    /// </summary>
    public void OnQuest(Creature initiator)
    {
        var questName = WorldObject.Quest;

        var hasQuest = initiator.QuestManager.HasQuest(questName);

        if (!hasQuest)
        {
            // add new quest
            initiator.QuestManager.Update(questName);
            hasQuest = initiator.QuestManager.HasQuest(questName);
            ExecuteEmoteSet(hasQuest ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure, questName, initiator);
        }
        else
        {
            // update existing quest
            var canSolve = initiator.QuestManager.CanSolve(questName);
            if (canSolve)
            {
                initiator.QuestManager.Stamp(questName);
            }

            ExecuteEmoteSet(canSolve ? EmoteCategory.QuestSuccess : EmoteCategory.QuestFailure, questName, initiator);
        }
    }

    /// <summary>
    /// Called when this NPC receives a direct text message from a player
    /// </summary>
    public void OnTalkDirect(Player player, string message)
    {
        ExecuteEmoteSet(EmoteCategory.ReceiveTalkDirect, message, player);

        if (MarketBroker.IsMarketBroker(WorldObject))
        {
            MarketBroker.HandleTalkDirect(player, WorldObject, message);
        }
    }

    /// <summary>
    /// Called when this NPC receives a local signal from a player
    /// </summary>
    public void OnLocalSignal(WorldObject emitter, string message)
    {
        ExecuteEmoteSet(EmoteCategory.ReceiveLocalSignal, message, emitter);
    }

    /// <summary>
    /// Called when monster exceeds the maximum distance from home position
    /// </summary>
    public void OnHomeSick(WorldObject attackTarget)
    {
        ExecuteEmoteSet(EmoteCategory.Homesick, null, attackTarget);
    }

    /// <summary>
    /// Called when this NPC hears local chat from a player
    /// </summary>
    public void OnHearChat(Player player, string message)
    {
        ExecuteEmoteSet(EmoteCategory.HearChat, message, player);
    }

    //public bool HasAntennas => WorldObject.Biota.BiotaPropertiesEmote.Count(x => x.Category == (int)EmoteCategory.ReceiveLocalSignal) > 0;

    /// <summary>
    /// Call this function when WorldObject is being used via a proxy object, e.g.: Hooker on a Hook
    /// </summary>
    public void SetProxy(WorldObject worldObject)
    {
        _proxy = worldObject;
    }

    /// <summary>
    /// Called when this object is removed from the proxy object (Hooker is picked up from Hook)
    /// </summary>
    public void ClearProxy()
    {
        _proxy = null;
    }

    /// <summary>
    /// Trade note awards for completing a capstone dungeon.<br /><br />
    /// Type Odds: 40% = I, 30% = V, 20% = X, 9% = L, 1% = C<br />
    /// Capstone Completions and Dungeon Mods raise the minimum roll.<br />
    /// </summary>
    private void AwardCapstoneTradeNotes(Player player, int amount)
    {
        var capstoneModifier = GetCapstoneModifier(player.CurrentLandblock);
        var capstonesCompleted = QuestManager.GetCapstonesCompleted(player);
        var minimumRoll = (capstonesCompleted * 5) + (capstoneModifier * 100);

        for (var i = 0; i < amount; i++)
        {
            var tradeNote = 2621u; // I note
            switch (ThreadSafeRandom.Next((int)minimumRoll, 100))
            {
                case <= 25:
                    break;
                case <= 50:
                    tradeNote = 2622u; // V note
                    break;
                case <= 90:
                    tradeNote = 2623u; // X note
                    break;
                case <= 99:
                    tradeNote = 2624u; // L note
                    break;
                default:
                    tradeNote = 2625u; // C Note
                    break;
            }

            player.GiveFromEmote(WorldObject, tradeNote);
        }
    }

    /// <summary>
    /// Item awards for completing a capstone dungeon.<br /><br />
    /// Amount Odds:  40% = 1, 30% = 2, 20% = 3, 9% = 4, 1% = 5<br />
    /// Capstone Completions and Dungeon Mods raise the minimum roll.<br />
    /// </summary>
    private void AwardCapstoneItems(Player player, int numRewards)
    {
        var itemPool = new List<(uint,int)>
        {
            (1054000,1), // Pearl of Transference
            (1054000,1), // Pearl of Transference
            (1054000,1), // Pearl of Transference
            (1054000,1), // Pearl of Transference
            (1054000,1), // Pearl of Transference
            (1054005,10), // Pearl of Spell Purging
            (1054005,10), // Pearl of Spell Purging
            (1054005,10), // Pearl of Spell Purging
            (1054005,10), // Pearl of Spell Purging
            (1054005,10), // Pearl of Spell Purging
            (1054004,1),  // Upgrade Kit
            (1054004,1),  // Upgrade Kit
            (1054004,1),  // Upgrade Kit
            (1054004,1),  // Upgrade Kit
            (1054004,1),  // Upgrade Kit
            (1054002,1), // Sanguine Crystal
            (1054003,1), // Scourging Stone
            (1053972,1) // Tailoring Kit
        };

        var capstoneModifier = GetCapstoneModifier(player.CurrentLandblock);
        var capstonesCompleted = QuestManager.GetCapstonesCompleted(player);
        var minimumRoll = (capstonesCompleted * 2) + (capstoneModifier * 100);

        var amount = 1;
        switch (ThreadSafeRandom.Next((int)minimumRoll, 100))
        {
            case <= 40:
                break;
            case <= 70:
                amount = 2;
                break;
            case <= 90:
                amount = 3;
                break;
            case <= 99:
                amount = 4;
                break;
            default:
                amount = 5;
                break;
        }

        for (var i = 0; i < numRewards; i++)
        {
            var randomIndex = ThreadSafeRandom.Next(0, itemPool.Count - 1);

            var randomItem = itemPool[randomIndex];

            var amountToGive = amount * randomItem.Item2;

            // max 1 amount for Sanguine/Scouring/Tailor
            if (randomItem.Item1 is 1054002 or 1054003 or 1054972)
            {
                amountToGive = 1;
            }

            player.GiveFromEmote(WorldObject, randomItem.Item1, amountToGive);
        }
    }

    private static double GetCapstoneModifier(Landblock landblock)
    {
        return landblock?.LandblockLootQualityMod ?? 1.0;
    }
}
