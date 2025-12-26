using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ItemType = ACE.Entity.Enum.ItemType;
using MotionCommand = ACE.Entity.Enum.MotionCommand;
using SpellId = ACE.Entity.Enum.SpellId;

namespace ACE.Server.WorldObjects;

public class SpellTransference : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public SpellTransference(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public SpellTransference(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    public static void BroadcastSpellTransfer(
        Player player,
        string spellName,
        WorldObject target,
        double chance = 1.0f,
        bool success = true
    )
    {
        // send local broadcast
        if (success)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} successfully transfers {spellName} to the {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                ),
                8f,
                ChatMessageType.Craft
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} fails to transfer {spellName} to the {target.NameWithMaterial}. The target is destroyed.",
                    ChatMessageType.Craft
                ),
                8f,
                ChatMessageType.Craft
            );
        }
    }

    public static void BroadcastSpellExtraction(
        Player player,
        string spellName,
        WorldObject target,
        double chance,
        bool success
    )
    {
        // send local broadcast
        if (success)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} successfully extracts {spellName} from the {target.NameWithMaterial}. The target is destroyed.",
                    ChatMessageType.Craft
                ),
                8f,
                ChatMessageType.Craft
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} fails to extract {spellName} from the {target.NameWithMaterial}. The target is destroyed.",
                    ChatMessageType.Craft
                ),
                8f,
                ChatMessageType.Craft
            );
        }
    }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        if (source.SpellExtracted == null)
        {
            var pearlStackSize = source.StackSize ?? 1;
            var targetWorkmanship = Math.Clamp((target.ItemWorkmanship ?? 1), 1, 10);
            var amountToAdd = targetWorkmanship * targetWorkmanship;
            var consumed = amountToAdd > 1 ? $"and consuming {amountToAdd} pearls" : "";

            if (player.IsBusy)
            {
                player.SendUseDoneEvent(WeenieError.YoureTooBusy);
                return;
            }

            if (target.WeenieType == source.WeenieType)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            if (!RecipeManager.VerifyUse(player, source, target, true))
            {
                if (!confirmed)
                {
                    player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                }
                else
                {
                    player.SendTransientError(
                        "Either you or one of the items involved does not pass the requirements for this craft interaction."
                    );
                }

                return;
            }

            if (target.Workmanship == null || target.Tier == null)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            if (target.Retained == true)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {target.NameWithMaterial} is retained and cannot be dismantled for a contained spell.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (pearlStackSize < amountToAdd)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You require {amountToAdd} Pearls of Spell Transference to extract a spell from {target.NameWithMaterial}.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            var spellCount = 0;
            var allSpells = target.Biota.GetKnownSpellsIds(target.BiotaDatabaseLock);

            if (target.ProcSpell != null && target.ProcSpell != 0)
            {
                allSpells.Add((int)target.ProcSpell);
            }

            var spells = new List<int>();

            foreach (var spellId in allSpells)
            {
                var spell = new Spell(spellId);
                spells.Add(spellId);
            }

            spellCount = spells.Count;
            if (spellCount == 0)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {target.NameWithMaterial} does not have any valid spells to extract.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }
            // if not confirmed yet, we select a spell from the item and assign the ID to the pearl's SpellToExtract property.
            // confirmation runs this method a second time after hitting yes
            // with multi-spell items, repeated "No" confirmations will cycle through the item's spells
            if (!confirmed)
            {
                if (target.RemainingConfirmations == null)
                {
                    target.RemainingConfirmations = spellCount;
                }

                var spellToExtractRoll = (target.RemainingConfirmations ?? 1) - 1;
                var spellToExtractId = spells[spellToExtractRoll];

                source.SpellToExtract = (uint?)spellToExtractId;

                target.RemainingConfirmations--;
                if (target.RemainingConfirmations == 0)
                {
                    target.RemainingConfirmations = null;
                }
            }

            if (source.SpellToExtract == null)
            {
                _log.Error("UseObjectOnTarget() - {Source}.SpellToExtract is null", source);
                return;
            }

            var chosenSpell = new Spell((uint)source.SpellToExtract);
            var chance = 100;

            if (!confirmed)
            {
                if (
                    !player.ConfirmationManager.EnqueueSend(
                        new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                        $"Extract {chosenSpell.Name} from {target.NameWithMaterial}, destroying it {consumed} in the process?" +
                        $"\n\nIf this item contains more than one spell, selecting 'No' will cycle through the remaining spells.\n\n"
                    )
                )
                {
                    player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
                }

                if (PropertyManager.GetBool("craft_exact_msg").Item)
                {
                    var exactMsg = $"You have a 100% chance of extracting a spell from {target.NameWithMaterial}.";

                    player.Session.Network.EnqueueSend(new GameMessageSystemChat(exactMsg, ChatMessageType.Craft));
                }

                return;
            }

            var actionChain = new ActionChain();

            var animTime = 0.0f;

            player.IsBusy = true;

            if (player.CombatMode != CombatMode.NonCombat)
            {
                var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
                actionChain.AddDelaySeconds(stanceTime);

                animTime += stanceTime;
            }

            animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

            actionChain.AddAction(
                player,
                () =>
                {
                    if (!RecipeManager.VerifyUse(player, source, target, true))
                    {
                        player.SendTransientError(
                            "Either you or one of the items involved does not pass the requirements for this craft interaction."
                        );
                        return;
                    }
                    var pearl = WorldObjectFactory.CreateNewWorldObject(1054001);
                    var success = true;
                    var spellName = "";
                    if (success)
                    {
                        var spell = new Spell((int)source.SpellToExtract);
                        spellName = spell.Name;

                        pearl.SpellExtracted = source.SpellToExtract;

                        var itemType = "";
                        if (target.ItemType == ItemType.Jewelry)
                        {
                            itemType = "a piece of jewelry";
                        }
                        if (target.ItemType == ItemType.Armor)
                        {
                            itemType = "a piece of armor";
                        }
                        if (target.ItemType == ItemType.MissileWeapon || target.ItemType == ItemType.MeleeWeapon)
                        {
                            itemType = "a missile or melee weapon";
                        }
                        if (target.ItemType == ItemType.Caster)
                        {
                            itemType = "a magic caster";
                        }
                        if (target.ItemType == ItemType.Clothing)
                        {
                            itemType = "a piece of clothing";
                        }

                        player.TryConsumeFromInventoryWithNetworking(source, amountToAdd);

                        pearl.Tier = target.Tier;
                        var wieldReq = LootGenerationFactory.GetWieldDifficultyPerTier(pearl.Tier ?? 1);
                        pearl.LongDesc =
                            $"This pearl contains the spell {spell.Name}." +
                            $"\n\nIt may only be applied to {itemType} with a Wield Requirement of {wieldReq} or greater." +
                            $"\n\nAdding this spell will increase Spellcraft and Arcane Lore of the target item, and will bind it to your character." +
                            $"\n\nIf the spell is an on-hit weapon proc, it will add a Life or War Magic skill wield requirement as well.";
                        pearl.TinkerLog = $"{target.ItemType}";
                        pearl.UiEffects = ACE.Entity.Enum.UiEffects.BoostMana;

                        if (MiniSpellIcons.TryGetValue((SpellId)spell.Id, out var icon))
                        {
                            pearl.IconOverlayId = icon;
                        }

                        player.EnqueueBroadcast(new GameMessageUpdateObject(source));
                        player.PlayParticleEffect(PlayScript.EnchantUpBlue, player.Guid);

                        player.TryConsumeFromInventoryWithNetworking(target, amountToAdd);

                        player.TryCreateInInventoryWithNetworking(pearl);
                    }

                    BroadcastSpellExtraction(player, spellName, target, chance, success);
                }
            );

            player.EnqueueMotion(actionChain, MotionCommand.Ready);

            actionChain.AddAction(
                player,
                () =>
                {
                    player.SendUseDoneEvent();
                    player.IsBusy = false;
                }
            );

            actionChain.EnqueueChain();

            player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
        }
        // handle Transference
        else
        {
            if (player.IsBusy)
            {
                player.SendUseDoneEvent(WeenieError.YoureTooBusy);
                return;
            }

            if (!RecipeManager.VerifyUse(player, source, target, true))
            {
                if (!confirmed)
                {
                    player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                }
                else
                {
                    player.SendTransientError(
                        "Either you or one of the items involved does not pass the requirements for this craft interaction."
                    );
                }

                return;
            }

            if (target.Workmanship == null || target.Tier == null)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            if (target.Tier < source.Tier)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {target.NameWithMaterial} is not powerful enough to contain the spell.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            var validType = false;

            if (Enum.TryParse<ItemType>(source.TinkerLog, out var itemType))
            {
                if (itemType == ItemType.MissileWeapon || itemType == ItemType.MeleeWeapon)
                {
                    if (target.ItemType != ItemType.MissileWeapon && target.ItemType != ItemType.MeleeWeapon)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"The {source.Name} cannot be used on an item of that type.",
                                ChatMessageType.Craft
                            )
                        );
                        player.SendUseDoneEvent();
                        return;
                    }
                    else
                    {
                        validType = true;
                    }
                }

                if (target.ItemType == itemType)
                {
                    validType = true;
                }
            }

            if (!validType)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {source.Name} cannot be used on an item of that type.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (validType)
            {
                var spellToAddId = (uint)source.SpellExtracted;
                var spellToAddlevel1Id = SpellLevelProgression.GetLevel1SpellId((SpellId)spellToAddId);
                var spellToAdd = new Spell(spellToAddId);

                var isProc = false;
                if (
                    spellToAddlevel1Id != SpellId.Undef
                    && (weaponProcs.FirstOrDefault(x => x.result == spellToAddlevel1Id) != default((SpellId, float)))
                )
                {
                    isProc = true;

                    if (target.ItemType != ItemType.MeleeWeapon && target.ItemType != ItemType.MissileWeapon)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"The {target.NameWithMaterial} cannot contain {spellToAdd.Name}.",
                                ChatMessageType.Craft
                            )
                        );
                        player.SendUseDoneEvent();
                        return;
                    }
                }

                // Get spells from target item to check conflicts/overlaps

                var spellsOnItem = target.Biota.GetKnownSpellsIds(target.BiotaDatabaseLock);

                if (target.ProcSpell != null && target.ProcSpell != 0)
                {
                    spellsOnItem.Add((int)target.ProcSpell);
                }

                Spell spellToReplace = null;
                foreach (var spellOnItemId in spellsOnItem)
                {
                    var spellOnItem = new Spell(spellOnItemId);

                    if (spellOnItemId == spellToAddId)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"The {target.NameWithMaterial} already contains {spellToAdd.Name}.",
                                ChatMessageType.Craft
                            )
                        );
                        player.SendUseDoneEvent();
                        return;
                    }
                    else if (spellOnItem.Category == spellToAdd.Category)
                    {
                        if (spellOnItem.Power > spellToAdd.Power)
                        {
                            player.Session.Network.EnqueueSend(
                                new GameMessageSystemChat(
                                    $"The {target.NameWithMaterial} already contains {spellOnItem.Name}, which is stronger than {spellToAdd.Name}.",
                                    ChatMessageType.Craft
                                )
                            );
                            player.SendUseDoneEvent();
                            return;
                        }
                        else if (spellOnItem.Power == spellToAdd.Power)
                        {
                            player.Session.Network.EnqueueSend(
                                new GameMessageSystemChat(
                                    $"The {target.NameWithMaterial} already contains {spellOnItem.Name}, which is equivalent to {spellToAdd.Name}.",
                                    ChatMessageType.Craft
                                )
                            );
                            player.SendUseDoneEvent();
                            return;
                        }
                        else
                        {
                            spellToReplace = spellOnItem;
                        }
                    }
                }

                if (!confirmed)
                {
                    var extraMessage = "";
                    if (isProc && target.ProcSpell != null)
                    {
                        var currentProc = new Spell(target.ProcSpell ?? 0);
                        extraMessage = $"\nThis will replace {currentProc.Name}!\n";
                    }
                    else if (spellToReplace != null)
                    {
                        extraMessage = $"\nThis will replace {spellToReplace.Name}!\n\n";
                    }

                    int? spellToReplaceId = null;
                    if (spellToReplace != null)
                    {
                        spellToReplaceId = (int)spellToReplace.Id;
                    }

                    var potentialArcaneLoreReq = CalculateArcaneLore(target, (int)spellToAdd.Id, spellToReplaceId, isProc);
                    var arcaneLoreString = potentialArcaneLoreReq > 0
                        ? $"The {target.Name}'s arcane lore activation requirement will become {potentialArcaneLoreReq}.\n\n" : "";

                    if (
                        !player.ConfirmationManager.EnqueueSend(
                            new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                            $"Transfer {spellToAdd.Name} to {target.NameWithMaterial}?\n\n" +
                            $"{arcaneLoreString}" +
                            $"The pearl will be consumed and the {target.Name} will become bound to your character.\n\n{extraMessage}"
                        )
                    )
                    {
                        player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
                    }
                    else
                    {
                        player.SendUseDoneEvent();
                    }

                    return;
                }

                var actionChain = new ActionChain();

                var animTime = 0.0f;

                player.IsBusy = true;

                if (player.CombatMode != CombatMode.NonCombat)
                {
                    var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
                    actionChain.AddDelaySeconds(stanceTime);

                    animTime += stanceTime;
                }

                animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

                actionChain.AddAction(
                    player,
                    () =>
                    {
                        if (!RecipeManager.VerifyUse(player, source, target, true))
                        {
                            player.SendTransientError(
                                "Either you or one of the items involved does not pass the requirements for this craft interaction."
                            );
                            return;
                        }

                        if (isProc)
                        {
                            var animLength = WeaponAnimationLength.GetWeaponAnimLength(target) / 100;
                            var procRate = animLength + (animLength * LootGenerationFactory.GetDiminishingRoll());

                            target.ProcSpellRate = procRate;
                            target.ProcSpell = (uint)spellToAddId;
                            target.ProcSpellSelfTargeted = spellToAdd.IsSelfTargeted;
                            //target.WieldRequirements2 = WieldRequirement.Training;
                            //target.WieldDifficulty2 = 2;

                            //if (spellToAdd.School == MagicSchool.LifeMagic)
                            //{
                            //    target.WieldSkillType2 = 33;
                            //}

                            //if (spellToAdd.School == MagicSchool.WarMagic)
                            //{
                            //    target.WieldSkillType2 = 34;
                            //}
                        }
                        else
                        {
                            if (spellToReplace != null)
                            {
                                target.Biota.TryRemoveKnownSpell((int)spellToReplace.Id, target.BiotaDatabaseLock);
                                target.Biota.GetOrAddKnownSpell((int)spellToAddId, target.BiotaDatabaseLock, out _);
                            }
                            else
                            {
                                target.Biota.GetOrAddKnownSpell((int)spellToAddId, target.BiotaDatabaseLock, out _);
                            }
                        }

                        var newManaRate = LootGenerationFactory.CalculateManaRate(target);
                        var tier = Math.Clamp((target.Tier ?? 1) - 1, 1, 7);
                        var allSpells = target.Biota.GetKnownSpellsIds(target.BiotaDatabaseLock);

                        if (target.ProcSpell != null && target.ProcSpell != 0)
                        {
                            allSpells.Add((int)target.ProcSpell);
                        }

                        var numSpells = allSpells.Count;
                        var newMaxMana = LootGenerationFactory.RollItemMaxMana_New(tier, numSpells);

                        if (newMaxMana > (target.ItemMaxMana ?? 0))
                        {
                            target.ItemMaxMana = newMaxMana;
                            target.ItemCurMana = Math.Clamp(target.ItemCurMana ?? 0, 0, target.ItemMaxMana ?? 0);

                            target.ManaRate = newManaRate;
                            target.LongDesc = LootGenerationFactory.GetLongDesc(target);
                        }

                        target.ItemDifficulty = CalculateArcaneLore(target);

                        if (target.ItemSpellcraft is null)
                        {
                            target.ItemSpellcraft = LootGenerationFactory.RollSpellcraft(target);
                        }

                        if (!target.UiEffects.HasValue)
                        {
                            target.UiEffects = ACE.Entity.Enum.UiEffects.Magical;
                        }

                        player.EnqueueBroadcast(new GameMessageUpdateObject(target));
                        player.PlayParticleEffect(PlayScript.EnchantUpBlue, player.Guid);

                        player.TryConsumeFromInventoryWithNetworking(source);
                        BroadcastSpellTransfer(player, spellToAdd.Name, target);
                    }
                );

                player.EnqueueMotion(actionChain, MotionCommand.Ready);

                actionChain.AddAction(
                    player,
                    () =>
                    {
                        player.IsBusy = false;
                    }
                );

                actionChain.EnqueueChain();

                player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
            }
        }
    }

    private static int CalculateArcaneLore(WorldObject target, int? potentialSpell = null, int? potentialRemovedSpell = null, bool isProcSpell = false)
    {
        var numSpells = 0;
        var increasedDifficulty = 0.0f;

        var targetSpellBook = target.Biota.PropertiesSpellBook;
        var targetProcSpell = target.ProcSpell;

        if (targetSpellBook != null)
        {
            var spellBook = new Dictionary<int, float>(targetSpellBook);

            if (potentialSpell != null)
            {
                spellBook.Add(potentialSpell.Value, 1.0f);
            }

            if (potentialRemovedSpell != null)
            {
                spellBook.Remove(potentialRemovedSpell.Value);
            }

            int MINOR = 0,
                MAJOR = 1,
                EPIC = 2,
                LEGENDARY = 3;

            foreach (SpellId spellId in spellBook.Keys)
            {
                numSpells++;

                var cantripLevels = SpellLevelProgression.GetSpellLevels(spellId);

                var cantripLevel = cantripLevels.IndexOf(spellId);

                if (cantripLevel == MINOR)
                {
                    increasedDifficulty += 5;
                }
                else if (cantripLevel == MAJOR)
                {
                    increasedDifficulty += 10;
                }
                else if (cantripLevel == EPIC)
                {
                    increasedDifficulty += 15;
                }
                else if (cantripLevel == LEGENDARY)
                {
                    increasedDifficulty += 20;
                }
            }
        }

        var tier = (target.Tier ?? 1) - 1;

        if (targetProcSpell != null || isProcSpell)
        {
            numSpells++;
            increasedDifficulty += Math.Max(5 * tier, 5);
        }

        var finalDifficulty = 0;
        var armorSlots = target.ArmorSlots ?? 1;
        var spellsPerSlot = (float)numSpells / armorSlots;

        if (spellsPerSlot > 1 || targetProcSpell != null || isProcSpell)
        {
            var baseDifficulty = ActivationDifficultyPerTier(tier);

            finalDifficulty = baseDifficulty + (int)(increasedDifficulty / armorSlots);
        }

        return finalDifficulty;
    }

    private static int ActivationDifficultyPerTier(int tier)
    {
        switch (tier)
        {
            case 1:
                return 75;
            case 2:
                return 175;
            case 3:
                return 225;
            case 4:
                return 275;
            case 5:
                return 325;
            case 6:
                return 375;
            case 7:
                return 425;
            default:
                return 50;
        }
    }

    public static ChanceTable<SpellId> weaponProcs = new ChanceTable<SpellId>(ChanceTableType.Weight)
    {
        (SpellId.StaminaToManaSelf1, 1.0f),
        (SpellId.ManaToStaminaSelf1, 1.0f),
        (SpellId.ManaToHealthSelf1, 1.0f),
        (SpellId.DrainMana1, 1.0f),
        (SpellId.DrainStamina1, 1.0f),
        (SpellId.DrainHealth1, 1.0f),
        (SpellId.ManaBoostSelf1, 1.0f),
        (SpellId.RevitalizeSelf1, 1.0f),
        (SpellId.HealSelf1, 1.0f),
        (SpellId.HarmOther1, 1.0f),
        (SpellId.ExhaustionOther1, 1.0f),
        (SpellId.ManaDrainOther1, 1.0f),
        (SpellId.WhirlingBlade1, 1.0f),
        (SpellId.ForceBolt1, 1.0f),
        (SpellId.ShockWave1, 1.0f),
        (SpellId.AcidStream1, 1.0f),
        (SpellId.FlameBolt1, 1.0f),
        (SpellId.FrostBolt1, 1.0f),
        (SpellId.LightningBolt1, 1.0f),
    };

    private static Dictionary<SpellId, uint> MiniSpellIcons = new Dictionary<SpellId, uint>()
    {
        {SpellId.CANTRIPSTRENGTH1, 100686688},
        {SpellId.CANTRIPSTRENGTH2, 100686688},
        {SpellId.CANTRIPSTRENGTH3, 100686688},
        {SpellId.CantripStrength4, 100686688},
        {SpellId.CANTRIPENDURANCE1, 100686648},
        {SpellId.CANTRIPENDURANCE2, 100686648},
        {SpellId.CANTRIPENDURANCE3, 100686648},
        {SpellId.CantripEndurance4, 100686648},
        {SpellId.CANTRIPCOORDINATION1, 100686641},
        {SpellId.CANTRIPCOORDINATION2, 100686641},
        {SpellId.CANTRIPCOORDINATION3, 100686641},
        {SpellId.CantripCoordination4, 100686641},
        {SpellId.CANTRIPQUICKNESS1, 100686680},
        {SpellId.CANTRIPQUICKNESS2, 100686680},
        {SpellId.CANTRIPQUICKNESS3, 100686680},
        {SpellId.CantripQuickness4, 100686680},
        {SpellId.CANTRIPFOCUS1, 100686652},
        {SpellId.CANTRIPFOCUS2, 100686652},
        {SpellId.CANTRIPFOCUS3, 100686652},
        {SpellId.CantripFocus4, 100686652},
        {SpellId.CANTRIPWILLPOWER1, 100686682},
        {SpellId.CANTRIPWILLPOWER2, 100686682},
        {SpellId.CANTRIPWILLPOWER3, 100686682},
        {SpellId.CantripWillpower4, 100686682},
        {SpellId.CANTRIPHEAVYWEAPONSAPTITUDE1, 100692248},
        {SpellId.CANTRIPHEAVYWEAPONSAPTITUDE2, 100692248},
        {SpellId.CANTRIPHEAVYWEAPONSAPTITUDE3, 100692248},
        {SpellId.CantripHeavyWeaponsAptitude4, 100692248},
        //{SpellCategory.CascadeAxeRaising, 100692248},
        //{SpellCategory.CascadeMaceRaising, 100692248},
        //{SpellCategory.ExtraSpearSkillRaising, 100692248},
        {SpellId.CANTRIPFINESSEWEAPONSAPTITUDE1, 100686644}, // Dagger
        {SpellId.CANTRIPFINESSEWEAPONSAPTITUDE2, 100686644},
        {SpellId.CANTRIPFINESSEWEAPONSAPTITUDE3, 100686644},
        {SpellId.CantripFinesseWeaponsAptitude4, 100686644},
        {SpellId.CANTRIPLIGHTWEAPONSAPTITUDE1, 100686686}, // Staff?
        {SpellId.CANTRIPLIGHTWEAPONSAPTITUDE2, 100686686},
        {SpellId.CANTRIPLIGHTWEAPONSAPTITUDE3, 100686686},
        {SpellId.CantripLightWeaponsAptitude4, 100686686},
        {SpellId.CANTRIPUNARMEDAPTITUDE1, 100686692}, // UA
        {SpellId.CANTRIPUNARMEDAPTITUDE2, 100686692}, // UA
        {SpellId.CANTRIPUNARMEDAPTITUDE3, 100686692}, // UA
        {SpellId.CANTRIPUNARMEDAPTITUDE4, 100686692}, // UA
        {SpellId.CANTRIPINVULNERABILITY1, 100686675},
        {SpellId.CANTRIPINVULNERABILITY2, 100686675},
        {SpellId.CANTRIPINVULNERABILITY3, 100686675},
        {SpellId.CantripInvulnerability4, 100686675},
        {SpellId.CANTRIPIMPREGNABILITY1, 100686675},
        {SpellId.CANTRIPIMPREGNABILITY2, 100686675},
        {SpellId.CANTRIPIMPREGNABILITY3, 100686675},
        {SpellId.CantripImpenetrability4, 100686675},
        {SpellId.CANTRIPMAGICRESISTANCE1, 100686671},
        {SpellId.CANTRIPMAGICRESISTANCE2, 100686671},
        {SpellId.CANTRIPMAGICRESISTANCE3, 100686671},
        {SpellId.CantripMagicResistance4, 100686671},
        {SpellId.CantripShieldAptitude1, 100692246},
        {SpellId.CantripShieldAptitude2, 100692246},
        {SpellId.CantripShieldAptitude3, 100692246},
        {SpellId.CantripShieldAptitude4, 100692246},
        {SpellId.CANTRIPMISSILEWEAPONSAPTITUDE1, 100686638},
        {SpellId.CANTRIPMISSILEWEAPONSAPTITUDE2, 100686638},
        {SpellId.CANTRIPMISSILEWEAPONSAPTITUDE3, 100686638},
        {SpellId.CantripMissileWeaponsAptitude4, 100686638},
        //{SpellCategory.ExtraCrossbowSkillRaising, 100686638},
        {SpellId.CANTRIPDECEPTIONPROWESS1, 100686645},
        {SpellId.CANTRIPDECEPTIONPROWESS2, 100686645},
        {SpellId.CANTRIPDECEPTIONPROWESS3, 100686645},
        {SpellId.CantripDeceptionProwess4, 100686645},
        {SpellId.CANTRIPMONSTERATTUNEMENT1, 100686631},
        {SpellId.CANTRIPMONSTERATTUNEMENT2, 100686631},
        {SpellId.CANTRIPMONSTERATTUNEMENT3, 100686631},
        {SpellId.CantripMonsterAttunement4, 100686631},
        {SpellId.CantripDualWieldAptitude1, 100692245},
        {SpellId.CantripDualWieldAptitude2, 100692245},
        {SpellId.CantripDualWieldAptitude3, 100692245},
        {SpellId.CantripDualWieldAptitude4, 100692245},
        {SpellId.CANTRIPTWOHANDEDAPTITUDE1, 100686633},
        {SpellId.CANTRIPTWOHANDEDAPTITUDE2, 100686633},
        {SpellId.CANTRIPTWOHANDEDAPTITUDE3, 100686633},
        {SpellId.CantripTwoHandedAptitude4, 100686633},
        {SpellId.CANTRIPSPRINT1, 100686681},
        {SpellId.CANTRIPSPRINT2, 100686681},
        {SpellId.CANTRIPSPRINT3, 100686681},
        {SpellId.CantripSprint4, 100686681},
        {SpellId.CANTRIPJUMPINGPROWESS1, 100686662},
        {SpellId.CANTRIPJUMPINGPROWESS2, 100686662},
        {SpellId.CANTRIPJUMPINGPROWESS3, 100686662},
        {SpellId.CantripJumpingProwess4, 100686662},
        {SpellId.CANTRIPHEALINGPROWESS1, 100686655},
        {SpellId.CANTRIPHEALINGPROWESS2, 100686655},
        {SpellId.CANTRIPHEALINGPROWESS3, 100686655},
        {SpellId.CantripHealingProwess4, 100686655},
        {SpellId.CANTRIPLOCKPICKPROWESS1, 100686668},
        {SpellId.CANTRIPLOCKPICKPROWESS2, 100686668},
        {SpellId.CANTRIPLOCKPICKPROWESS3, 100686668},
        {SpellId.CantripLockpickProwess4, 100686668},
        {SpellId.CANTRIPARCANEPROWESS1, 100686628},
        {SpellId.CANTRIPARCANEPROWESS2, 100686628},
        {SpellId.CANTRIPARCANEPROWESS3, 100686628},
        {SpellId.CantripArcaneProwess4, 100686628},
        {SpellId.CANTRIPWARMAGICAPTITUDE1, 100686693},
        {SpellId.CANTRIPWARMAGICAPTITUDE2, 100686693},
        {SpellId.CANTRIPWARMAGICAPTITUDE3, 100686693},
        {SpellId.CantripWarMagicAptitude4, 100686693},
        {SpellId.CANTRIPLIFEMAGICAPTITUDE1, 100686664},
        {SpellId.CANTRIPLIFEMAGICAPTITUDE2, 100686664},
        {SpellId.CANTRIPLIFEMAGICAPTITUDE3, 100686664},
        {SpellId.CantripLifeMagicAptitude4, 100686664},
        {SpellId.CANTRIPMANACONVERSIONPROWESS1, 100686664},
        {SpellId.CANTRIPMANACONVERSIONPROWESS2, 100686664},
        {SpellId.CANTRIPMANACONVERSIONPROWESS3, 100686664},
        {SpellId.CantripManaConversionProwess4, 100686664},

        {SpellId.CANTRIPARMOR1, 100686629},
        {SpellId.CANTRIPARMOR2, 100686629},
        {SpellId.CANTRIPARMOR3, 100686629},
        {SpellId.CantripArmor4, 100686629},
        {SpellId.CANTRIPACIDWARD1, 100686625},
        {SpellId.CANTRIPACIDWARD2, 100686625},
        {SpellId.CANTRIPACIDWARD3, 100686625},
        {SpellId.CantripAcidWard4, 100686625},
        {SpellId.CANTRIPFLAMEWARD1, 100686649},
        {SpellId.CANTRIPFLAMEWARD2, 100686649},
        {SpellId.CANTRIPFLAMEWARD3, 100686649},
        {SpellId.CantripFlameWard4, 100686649},
        {SpellId.CANTRIPFROSTWARD1, 100686654},
        {SpellId.CANTRIPFROSTWARD2, 100686654},
        {SpellId.CANTRIPFROSTWARD3, 100686654},
        {SpellId.CantripFrostWard4, 100686654},
        {SpellId.CANTRIPSTORMWARD1, 100686667},
        {SpellId.CANTRIPSTORMWARD2, 100686667},
        {SpellId.CANTRIPSTORMWARD3, 100686667},
        {SpellId.CantripStormWard4, 100686667},
        {SpellId.CANTRIPSLASHINGWARD1, 100686683},
        {SpellId.CANTRIPSLASHINGWARD2, 100686683},
        {SpellId.CANTRIPSLASHINGWARD3, 100686683},
        {SpellId.CantripSlashingWard4, 100686683},
        {SpellId.CANTRIPPIERCINGWARD1, 100686678},
        {SpellId.CANTRIPPIERCINGWARD2, 100686678},
        {SpellId.CANTRIPPIERCINGWARD3, 100686678},
        {SpellId.CantripPiercingWard4, 100686678},
        {SpellId.CANTRIPBLUDGEONINGWARD1, 100686637},
        {SpellId.CANTRIPBLUDGEONINGWARD2, 100686637},
        {SpellId.CANTRIPBLUDGEONINGWARD3, 100686637},
        {SpellId.CantripBludgeoningWard4, 100686637},
        {SpellId.CANTRIPHEALTHGAIN1, 100686656},
        {SpellId.CANTRIPHEALTHGAIN2, 100686656},
        {SpellId.CANTRIPHEALTHGAIN3, 100686656},
        {SpellId.CantripHealthGain4, 100686656},
        {SpellId.CANTRIPSTAMINAGAIN1, 100686687},
        {SpellId.CANTRIPSTAMINAGAIN2, 100686687},
        {SpellId.CANTRIPSTAMINAGAIN3, 100686687},
        {SpellId.CantripStaminaGain4, 100686687},
        {SpellId.CANTRIPMANAGAIN1, 100686674},
        {SpellId.CANTRIPMANAGAIN2, 100686674},
        {SpellId.CANTRIPMANAGAIN3, 100686674},
        {SpellId.CantripManaGain4, 100686674},
        {SpellId.CANTRIPBLOODTHIRST1, 100686635},
        {SpellId.CANTRIPBLOODTHIRST2, 100686635},
        {SpellId.CANTRIPBLOODTHIRST3, 100686635},
        {SpellId.CantripBloodThirst4, 100686635},
        {SpellId.CANTRIPHEARTTHIRST1, 100686657},
        {SpellId.CANTRIPHEARTTHIRST2, 100686657},
        {SpellId.CANTRIPHEARTTHIRST3, 100686657},
        {SpellId.CantripHeartThirst4, 100686657},
        {SpellId.CANTRIPDEFENDER1, 100686646},
        {SpellId.CANTRIPDEFENDER2, 100686646},
        {SpellId.CANTRIPDEFENDER3, 100686646},
        {SpellId.CantripDefender4, 100686646},
        {SpellId.CantripSpiritThirst1, 100686685},
        {SpellId.CantripSpiritThirst2, 100686685},
        {SpellId.CANTRIPSPIRITTHIRST3, 100686685},
        {SpellId.CantripSpiritThirst4, 100686685},
        {SpellId.CANTRIPSWIFTHUNTER1, 100686689},
        {SpellId.CANTRIPSWIFTHUNTER2, 100686689},
        {SpellId.CANTRIPSWIFTHUNTER3, 100686689},
        {SpellId.CantripSwiftHunter4, 100686689},
        {SpellId.CantripHermeticLink1, 100686658},
        {SpellId.CantripHermeticLink2, 100686658},
        {SpellId.CantripHermeticLink3, 100686658},
        {SpellId.CantripHermeticLink4, 100686658},
    };
}
