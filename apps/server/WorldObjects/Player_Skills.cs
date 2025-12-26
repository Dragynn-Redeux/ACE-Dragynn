using System;
using System.Collections.Generic;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects;

partial class Player
{
    /// <summary>
    /// Handles the GameAction 0x46 - RaiseSkill network message from client
    /// </summary>
    public bool HandleActionRaiseSkill(Skill skill, uint amount, bool freeRankUp = false)
    {
        var creatureSkill = GetCreatureSkill(skill, false);

        if (creatureSkill == null || creatureSkill.AdvancementClass < SkillAdvancementClass.Trained)
        {
            _log.Error($"{Name}.HandleActionRaiseSkill({skill}, {amount}) - trained or specialized skill not found");
            Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
            return false;
        }

        if (!freeRankUp && amount > AvailableExperience)
        {
            _log.Error($"{Name}.HandleActionRaiseSkill({skill}, {amount}) - amount > AvailableExperience ({AvailableExperience})");
            Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
            return false;
        }

        var prevRank = creatureSkill.Ranks;

        if (!SpendSkillXp(creatureSkill, amount, true, freeRankUp))
        {
            return false;
        }

        Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));

        if (prevRank != creatureSkill.Ranks)
        {
            // if the skill ranks out at the top of our xp chart
            // then we will start fireworks effects and have special text!
            var suffix = "";
            if (creatureSkill.IsMaxRank)
            {
                // fireworks on rank up is 0x8D
                PlayParticleEffect(PlayScript.WeddingBliss, Guid);
                suffix = $" and has reached its upper limit";
            }
            var newSkill = (NewSkillNames)skill;
            var sound = new GameMessageSound(Guid, Sound.RaiseTrait);
            var msg = new GameMessageSystemChat(
                $"Your base {newSkill.ToSentence()} skill is now {creatureSkill.Base}{suffix}!",
                ChatMessageType.Advancement
            );

            Session.Network.EnqueueSend(sound, msg);

            // retail was missing the 'raise skill' runrate hook here
            if (skill == Skill.Run && PropertyManager.GetBool("runrate_add_hooks").Item)
            {
                HandleRunRateUpdate();
            }
        }

        return true;
    }

    private bool SpendSkillXp(CreatureSkill creatureSkill, uint amount, bool sendNetworkUpdate = true, bool freeRankUp = false)
    {
        var newSkill = (NewSkillNames)creatureSkill.Skill;
        var cannotRaiseMsg = $"You cannot raise your {newSkill.ToSentence()} skill directly.";

        if (!freeRankUp)
        {
            switch (creatureSkill.Skill)
            {
                case Skill.PortalMagic:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when you attune yourself to a new portal magic attunement device.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Leadership:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg +
                            " You gain experience towards it when your vassals earn experience for you.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Loyalty:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg +
                            " You gain experience towards it when you earn experience for your patron.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Alchemy:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Alchemy recipes. You may also purchase training from an Alchemist.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Cooking:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Cooking recipes. You may also purchase training from a Chef or Provisioner.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Blacksmithing:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Blacksmithing recipes. You may also purchase training from a Blacksmith, Weaponsmith, or Armorer.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Tailoring:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Tailoring recipes. You may also purchase training from a Tailor.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Woodworking:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Woodworking recipes. You may also purchase training from a Bowyer or Fletcher.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Jewelcrafting:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Jewelcrafting recipes. You may also purchase training from a Jeweler.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
                case Skill.Spellcrafting:
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            cannotRaiseMsg
                            + " You gain experience towards it when performing Spellcrafting recipes. You may also purchase training from an Archmage.",
                            ChatMessageType.Advancement
                        )
                    );
                    Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
                    return false;
            }
        }

        var skillXPTable = GetSkillXPTable(creatureSkill.AdvancementClass);
        if (skillXPTable == null)
        {
            ChatPacket.SendServerMessage(Session, $"You do not have enough experience to raise your {creatureSkill.Skill.ToSentence()} skill.", ChatMessageType.Broadcast);
            Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
            return false;
        }

        // ensure skill is not already max rank
        if (creatureSkill.IsMaxRank)
        {
            ChatPacket.SendServerMessage(Session, $"You do not have enough experience to raise your {creatureSkill.Skill.ToSentence()} skill.", ChatMessageType.Broadcast);
            Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));
            return false;
        }

        // the client should already handle this naturally,
        // but ensure player can't spend xp beyond the max rank
        var amountToEnd = creatureSkill.ExperienceLeft;

        if (amount > amountToEnd)
        {
            //log.Error($"{Name}.SpendSkillXp({creatureSkill.Skill}, {amount}) - player tried to raise skill beyond {amountToEnd} experience");
            return false; // returning error here, instead of setting amount to amountToEnd
        }

        // everything looks good at this point,
        // spend xp on skill
        if (!freeRankUp && !SpendXP(amount, sendNetworkUpdate))
        {
            _log.Error($"{Name}.SpendSkillXp({creatureSkill.Skill}, {amount}) - SpendXP failed");
            return false;
        }

        if (freeRankUp)
        {
            var xpTable = GetSkillXPTable(creatureSkill.AdvancementClass);
            var nextRankCost = xpTable[creatureSkill.Ranks + 1] - xpTable[creatureSkill.Ranks];
            amount = nextRankCost;
        }

        creatureSkill.ExperienceSpent += amount;

        // calculate new rank
        creatureSkill.Ranks = (ushort)CalcSkillRank(creatureSkill.AdvancementClass, creatureSkill.ExperienceSpent);

        return true;
    }

    /// <summary>
    /// Handles the GameAction 0x47 - TrainSkill network message from client
    /// </summary>
    public bool HandleActionTrainSkill(Skill skill, int creditsSpent)
    {
        if (creditsSpent > AvailableSkillCredits)
        {
            _log.Error(
                $"{Name}.HandleActionTrainSkill({skill}, {creditsSpent}) - not enough skill credits ({AvailableSkillCredits})"
            );
            return false;
        }

        // get the actual cost to train the skill.
        if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)skill, out var skillBase))
        {
            _log.Error($"{Name}.HandleActionTrainSkill({skill}, {creditsSpent}) - couldn't find skill base");
            return false;
        }

        if (creditsSpent != skillBase.TrainedCost)
        {
            _log.Error(
                $"{Name}.HandleActionTrainSkill({skill}, {creditsSpent}) - client value differs from skillBase.TrainedCost({skillBase.TrainedCost})"
            );
            return false;
        }

        // attempt to train the specified skill
        var success = TrainSkill(skill, creditsSpent);

        var availableSkillCredits = $"You now have {AvailableSkillCredits} credits available.";

        var newSkill = (NewSkillNames)skill;
        if (success)
        {
            var updateSkill = new GameMessagePrivateUpdateSkill(this, GetCreatureSkill(skill));
            var skillCredits = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.AvailableSkillCredits,
                AvailableSkillCredits ?? 0
            );

            var msg = new GameMessageSystemChat(
                $"{newSkill.ToSentence()} trained. {availableSkillCredits}",
                ChatMessageType.Advancement
            );

            Session.Network.EnqueueSend(updateSkill, skillCredits, msg);
        }
        else
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Failed to train {newSkill.ToSentence()}! {availableSkillCredits}",
                    ChatMessageType.Advancement
                )
            );
        }

        return success;
    }

    public bool TrainSkill(Skill skill)
    {
        // get the amount of skill credits required to train this skill
        if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)skill, out var skillBase))
        {
            _log.Error($"{Name}.TrainSkill({skill}) - couldn't find skill base");
            return false;
        }

        // attempt to train the specified skill
        return TrainSkill(skill, skillBase.TrainedCost);
    }

    /// <summary>
    /// Sets the skill to trained status for a character
    /// </summary>
    public bool TrainSkill(Skill skill, int creditsSpent, bool applyCreationBonusXP = false)
    {
        var creatureSkill = GetCreatureSkill(skill);

        if (creatureSkill.AdvancementClass >= SkillAdvancementClass.Trained || creditsSpent > AvailableSkillCredits)
        {
            return false;
        }

        creatureSkill.AdvancementClass = SkillAdvancementClass.Trained;
        creatureSkill.Ranks = 0;
        creatureSkill.InitLevel = 0;

        if (applyCreationBonusXP)
        {
            creatureSkill.ExperienceSpent = 526;
            creatureSkill.Ranks = 5;
        }
        else
        {
            creatureSkill.ExperienceSpent = 0;
        }

        AvailableSkillCredits -= creditsSpent;

        // Tinkering skills can be reset at Asheron's Castle and Enlightenment, so if player has the augmentation when they train the skill again immediately specialize it again.
        if (IsSkillSpecializedViaAugmentation(skill, out var playerHasAugmentation) && playerHasAugmentation)
        {
            SpecializeSkill(skill, 0, false);
        }

        return true;
    }

    public bool SpecializeSkill(Skill skill, bool resetSkill = true)
    {
        // get the amount of skill credits required to upgrade this skill
        // from trained -> specialized
        if (!DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)skill, out var skillBase))
        {
            _log.Error($"{Name}.SpecializeSkill({skill}, {resetSkill}) - couldn't find skill base");
            return false;
        }

        // attempt to specialize the specified skill
        return SpecializeSkill(skill, skillBase.UpgradeCostFromTrainedToSpecialized);
    }

    /// <summary>
    /// Sets the skill to specialized status
    /// </summary>
    /// <param name="resetSkill">only set to TRUE during character creation. set to FALSE during temple / asheron's castle</param>
    public bool SpecializeSkill(Skill skill, int creditsSpent, bool resetSkill = true)
    {
        var creatureSkill = GetCreatureSkill(skill);

        if (creatureSkill.AdvancementClass != SkillAdvancementClass.Trained || creditsSpent > AvailableSkillCredits)
        {
            return false;
        }

        if (resetSkill)
        {
            // this path only during char creation
            creatureSkill.Ranks = 0;
            creatureSkill.ExperienceSpent = 0;
        }
        else
        {
            // this path only during temple / asheron's castle
            creatureSkill.Ranks = (ushort)CalcSkillRank(
                SkillAdvancementClass.Specialized,
                creatureSkill.ExperienceSpent
            );
        }

        creatureSkill.InitLevel = 10;
        creatureSkill.AdvancementClass = SkillAdvancementClass.Specialized;

        AvailableSkillCredits -= creditsSpent;

        return true;
    }

    /// <summary>
    /// Sets the skill to untrained status
    /// </summary>
    public bool UntrainSkill(Skill skill, int creditsSpent)
    {
        var creatureSkill = GetCreatureSkill(skill);

        if (creatureSkill == null || creatureSkill.AdvancementClass == SkillAdvancementClass.Specialized)
        {
            return false;
        }

        if (creatureSkill.AdvancementClass < SkillAdvancementClass.Trained)
        {
            // only used to initialize untrained skills for character creation?
            creatureSkill.AdvancementClass = SkillAdvancementClass.Untrained; // should this always be Untrained? what about Inactive?
            creatureSkill.InitLevel = 0;
            creatureSkill.Ranks = 0;
            creatureSkill.ExperienceSpent = 0;
        }
        else
        {
            // refund xp and skill credits
            if (!IsTradeSkill(skill))
            {
                RefundXP(creatureSkill.ExperienceSpent);
            }

            // temple untraining 'always trained' skills:
            // cannot be untrained, but skill XP can be recovered
            if (IsSkillUntrainable(skill, (HeritageGroup)Heritage))
            {
                creatureSkill.AdvancementClass = SkillAdvancementClass.Untrained;
                creatureSkill.InitLevel = 0;

                AvailableSkillCredits += creditsSpent;
            }

            creatureSkill.Ranks = 0;
            creatureSkill.ExperienceSpent = 0;

            // CUSTOM - Trade Skills - Handle Quest Stamps on Untrain
            if (IsTradeSkill(skill))
            {
                if (QuestManager.HasQuest("TradeSkill"))
                {
                    var solves = QuestManager.GetCurrentSolves("TradeSkill");
                    if (solves == 1)
                    {
                        QuestManager.Erase("TradeSkill");
                    }
                    else
                    {
                        QuestManager.Decrement("TradeSkill");
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Lowers skill from Specialized to Trained and returns both skill credits and invested XP
    /// </summary>
    public bool UnspecializeSkill(Skill skill, int creditsSpent)
    {
        var creatureSkill = GetCreatureSkill(skill);

        if (creatureSkill == null || creatureSkill.AdvancementClass != SkillAdvancementClass.Specialized)
        {
            return false;
        }

        // refund xp and skill credits
        RefundXP(creatureSkill.ExperienceSpent);

        // salvaging / tinkering skills specialized through augmentation only
        // cannot be unspecialized here, only refund xp
        if (!IsSkillSpecializedViaAugmentation(skill, out var playerHasAugmentation) || !playerHasAugmentation)
        {
            creatureSkill.AdvancementClass = SkillAdvancementClass.Trained;
            creatureSkill.InitLevel = 0;
            AvailableSkillCredits += creditsSpent;
        }

        creatureSkill.Ranks = 0;
        creatureSkill.ExperienceSpent = 0;

        return true;
    }

    /// <summary>
    /// Increases a skill by some amount of points
    /// </summary>
    public void AwardSkillPoints(Skill skill, uint amount)
    {
        var creatureSkill = GetCreatureSkill(skill);

        for (var i = 0; i < amount; i++)
        {
            // get skill xp required for next rank
            var xpToNextRank = GetXpToNextRank(creatureSkill);

            if (xpToNextRank != null)
            {
                AwardSkillXP(skill, xpToNextRank.Value);
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>
    /// Wrapper method used for increasing totalXP and then using the amount granted by HandleActionRaiseSkill
    /// </summary>
    public void AwardSkillXP(Skill skill, uint amount, bool alertPlayer = false)
    {
        var playerSkill = GetCreatureSkill(skill);

        if (playerSkill.AdvancementClass < SkillAdvancementClass.Trained || playerSkill.IsMaxRank)
        {
            return;
        }

        amount = Math.Min(amount, playerSkill.ExperienceLeft);

        GrantXP(amount, XpType.Emote, ShareType.None);

        var raiseChain = new ActionChain();
        raiseChain.AddDelayForOneTick();
        raiseChain.AddAction(
            this,
            () =>
            {
                HandleActionRaiseSkill(skill, amount);
            }
        );
        raiseChain.EnqueueChain();

        var newSkill = (NewSkillNames)playerSkill.Skill;
        if (alertPlayer)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You've earned {amount:N0} experience in your {newSkill.ToSentence()} skill.",
                    ChatMessageType.Broadcast
                )
            );
        }
    }

    public void SpendAllAvailableSkillXp(CreatureSkill creatureSkill, bool sendNetworkUpdate = true)
    {
        var amountRemaining = creatureSkill.ExperienceLeft;

        if (amountRemaining > AvailableExperience)
        {
            amountRemaining = (uint)AvailableExperience;
        }

        SpendSkillXp(creatureSkill, amountRemaining, sendNetworkUpdate);
    }

    /// <summary>
    /// Grants skill XP proportional to the player's skill level
    /// </summary>
    public void GrantLevelProportionalSkillXP(Skill skill, double percent, long min, long max)
    {
        var creatureSkill = GetCreatureSkill(skill, false);
        if (creatureSkill == null || creatureSkill.IsMaxRank)
        {
            return;
        }

        var nextLevelXP = GetXPBetweenSkillLevels(
            creatureSkill.AdvancementClass,
            creatureSkill.Ranks,
            creatureSkill.Ranks + 1
        );
        if (nextLevelXP == null)
        {
            return;
        }

        var amount = (uint)Math.Round(nextLevelXP.Value * percent);

        if (max > 0 && max <= uint.MaxValue)
        {
            amount = Math.Min(amount, (uint)max);
        }

        amount = Math.Min(amount, creatureSkill.ExperienceLeft);

        if (min > 0)
        {
            amount = Math.Max(amount, (uint)min);
        }

        //Console.WriteLine($"{Name}.GrantLevelProportionalSkillXP({skill}, {percent}, {max:N0})");
        //Console.WriteLine($"Amount: {amount:N0}");

        AwardSkillXP(skill, amount, true);
    }

    public void GrantSkillRanks(Skill skill, int ranks)
    {
        var creatureSkill = GetCreatureSkill(skill, false);
        if (creatureSkill == null || creatureSkill.IsMaxRank)
        {
            return;
        }

        creatureSkill.InitLevel = (ushort)Math.Clamp(creatureSkill.InitLevel + ranks, 0, (Int32)ushort.MaxValue);

        Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));

        var newSkill = (NewSkillNames)skill;
        var sound = new GameMessageSound(Guid, Sound.RaiseTrait);
        var msg = new GameMessageSystemChat(
            $"Your base {newSkill.ToSentence()} skill is now {creatureSkill.Base}!",
            ChatMessageType.Advancement
        );

        Session.Network.EnqueueSend(sound, msg);
    }

    /// <summary>
    /// Returns the remaining XP required to the next skill level
    /// </summary>
    public uint? GetXpToNextRank(CreatureSkill skill)
    {
        if (skill.AdvancementClass < SkillAdvancementClass.Trained || skill.IsMaxRank)
        {
            return null;
        }

        var skillXPTable = GetSkillXPTable(skill.AdvancementClass);

        return skillXPTable[skill.Ranks + 1] - skill.ExperienceSpent;
    }

    /// <summary>
    /// Returns the XP curve table based on trained or specialized skill
    /// </summary>
    public static List<uint> GetSkillXPTable(SkillAdvancementClass status)
    {
        var xpTable = DatManager.PortalDat.XpTable;

        switch (status)
        {
            case SkillAdvancementClass.Trained:
                return xpTable.TrainedSkillXpList;

            case SkillAdvancementClass.Specialized:
                return xpTable.SpecializedSkillXpList;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the skill XP required to go between fromRank and toRank
    /// </summary>
    public ulong? GetXPBetweenSkillLevels(SkillAdvancementClass status, int fromRank, int toRank)
    {
        var skillXPTable = GetSkillXPTable(status);
        if (skillXPTable == null)
        {
            return null;
        }

        return skillXPTable[toRank] - skillXPTable[fromRank];
    }

    /// <summary>
    /// Returns the maximum rank that can be purchased with an xp amount
    /// </summary>
    /// <param name="sac">Trained or specialized skill</param>
    /// <param name="xpAmount">The amount of xp used to make the purchase</param>
    public static int CalcSkillRank(SkillAdvancementClass sac, uint xpAmount, bool freeRankUp = false)
    {
        var rankXpTable = GetSkillXPTable(sac);
        for (var i = rankXpTable.Count - 1; i >= 0; i--)
        {
            var rankAmount = rankXpTable[i];
            if (xpAmount >= rankAmount)
            {
                return i;
            }
        }
        return -1;
    }

    private const int magicSkillCheckMargin = 50;

    public bool CanReadScroll(Scroll scroll, out bool spec)
    {
        var power = (int)scroll.Spell.Power;

        // level 1/7/8 scrolls can be learned by anyone?
        //if (power < 50 || power >= 300) return true;

        var magicSkill = scroll.Spell.GetMagicSkill();

        if (magicSkill is Skill.VoidMagic)
        {
            magicSkill = Skill.LifeMagic;
        }

        var playerSkill = GetCreatureSkill(magicSkill);

        var minSkill = power - magicSkillCheckMargin;

        var skillAdvancementClass = IsAdvancedSpell(scroll.Spell)
            ? SkillAdvancementClass.Specialized
            : SkillAdvancementClass.Trained;
        spec = skillAdvancementClass == SkillAdvancementClass.Specialized;

        return playerSkill.AdvancementClass >= skillAdvancementClass && playerSkill.Current >= minSkill;
    }

    public void AddSkillCredits(int amount)
    {
        TotalSkillCredits += amount;
        AvailableSkillCredits += amount;

        Session.Network.EnqueueSend(
            new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.AvailableSkillCredits, AvailableSkillCredits ?? 0)
        );

        if (amount > 1)
        {
            SendTransientError($"You have been awarded {amount:N0} additional skill credits.");
        }
        else
        {
            SendTransientError("You have been awarded an additional skill credit.");
        }
    }

    /// <summary>
    /// Called on player login
    /// If a player has any skills trained that require updates from ACE-World-16-Patches,
    /// ensure these updates are installed, and if they aren't, send a helpful message to player with instructions for installation
    /// </summary>
    public void HandleDBUpdates()
    {
        //// dirty fighting
        //var dfSkill = GetCreatureSkill(Skill.DirtyFighting);
        //if (dfSkill.AdvancementClass >= SkillAdvancementClass.Trained)
        //{
        //    foreach (var spellID in SpellExtensions.DirtyFightingSpells)
        //    {
        //        var spell = new Server.Entity.Spell(spellID);
        //        if (spell.NotFound)
        //        {
        //            var actionChain = new ActionChain();
        //            actionChain.AddDelaySeconds(3.0f);
        //            actionChain.AddAction(this, () =>
        //            {
        //                Session.Network.EnqueueSend(new GameMessageSystemChat("To install Dirty Fighting, please apply the latest patches from https://github.com/ACEmulator/ACE-World-16PY-Patches", ChatMessageType.Broadcast));
        //            });
        //            actionChain.EnqueueChain();
        //        }
        //        break;  // performance improvement: only check first spell
        //    }
        //}

        //// void magic
        //var voidSkill = GetCreatureSkill(Skill.VoidMagic);
        //if (voidSkill.AdvancementClass >= SkillAdvancementClass.Trained)
        //{
        //    foreach (var spellID in SpellExtensions.VoidMagicSpells)
        //    {
        //        var spell = new Server.Entity.Spell(spellID);
        //        if (spell.NotFound)
        //        {
        //            var actionChain = new ActionChain();
        //            actionChain.AddDelaySeconds(3.0f);
        //            actionChain.AddAction(this, () =>
        //            {
        //                Session.Network.EnqueueSend(new GameMessageSystemChat("To install Void Magic, please apply the latest patches from https://github.com/ACEmulator/ACE-World-16PY-Patches", ChatMessageType.Broadcast));
        //            });
        //            actionChain.EnqueueChain();
        //        }
        //        break;  // performance improvement: only check first spell (measured 102ms to check 75 uncached void spells)
        //    }
        //}

        //// summoning
        //var summoning = GetCreatureSkill(Skill.Summoning);
        //if (summoning.AdvancementClass >= SkillAdvancementClass.Trained)
        //{
        //    uint essenceWCID = 48878;
        //    var weenie = DatabaseManager.World.GetCachedWeenie(essenceWCID);
        //    if (weenie == null)
        //    {
        //        var actionChain = new ActionChain();
        //        actionChain.AddDelaySeconds(3.0f);
        //        actionChain.AddAction(this, () =>
        //        {
        //            Session.Network.EnqueueSend(new GameMessageSystemChat("To install Summoning, please apply the latest patches from https://github.com/ACEmulator/ACE-World-16PY-Patches", ChatMessageType.Broadcast));
        //        });
        //        actionChain.EnqueueChain();
        //    }
        //}
    }

    public static HashSet<Skill> MeleeSkills = new HashSet<Skill>()
    {
        Skill.LightWeapons,
        Skill.MartialWeapons,
        Skill.FinesseWeapons,
        Skill.DualWield,
        Skill.TwoHandedCombat,
        // legacy
        Skill.Axe,
        Skill.Dagger,
        Skill.Mace,
        Skill.Spear,
        Skill.Staff,
        Skill.Sword,
        Skill.UnarmedCombat
    };

    public static HashSet<Skill> MissileSkills = new HashSet<Skill>()
    {
        Skill.MissileWeapons,
        // legacy
        Skill.Bow,
        Skill.Crossbow,
        Skill.Sling,
        Skill.ThrownWeapon
    };

    public static HashSet<Skill> MagicSkills = new HashSet<Skill>()
    {
        Skill.CreatureEnchantment,
        Skill.PortalMagic,
        Skill.LifeMagic,
        Skill.VoidMagic,
        Skill.WarMagic
    };

    public static List<Skill> AlwaysTrained = new List<Skill>() { Skill.Run, Skill.Jump };

    public static List<Skill> AugSpecSkills = new List<Skill>()
    {
        Skill.Tailoring,
        Skill.Jewelcrafting,
        Skill.Spellcrafting,
        Skill.Blacksmithing,
        Skill.Salvaging
    };

    public static HashSet<Skill> HeritageSkills = new HashSet<Skill>()
    {
        Skill.Dagger,
        Skill.Staff,
        Skill.UnarmedCombat
    };

    public static bool IsSkillUntrainable(Skill skill, HeritageGroup heritageGroup)
    {
        // Use this section if adding heritage starting skills
        switch (heritageGroup)
        {
            case HeritageGroup.Aluvian:
                if (skill == Skill.Dagger)
                {
                    return false;
                }

                break;
            case HeritageGroup.Gharundim:
                if (skill == Skill.Staff)
                {
                    return false;
                }

                break;
            case HeritageGroup.Sho:
                if (skill == Skill.UnarmedCombat)
                {
                    return false;
                }

                break;
        }

        return !AlwaysTrained.Contains(skill);
    }

    public bool IsSkillSpecializedViaAugmentation(Skill skill, out bool playerHasAugmentation)
    {
        playerHasAugmentation = false;

        switch (skill)
        {
            case Skill.Tailoring:
                playerHasAugmentation = AugmentationSpecializeArmorTinkering > 0;
                break;

            case Skill.Jewelcrafting:
                playerHasAugmentation = AugmentationSpecializeItemTinkering > 0;
                break;

            case Skill.Spellcrafting:
                playerHasAugmentation = AugmentationSpecializeMagicItemTinkering > 0;
                break;

            case Skill.Blacksmithing:
                playerHasAugmentation = AugmentationSpecializeWeaponTinkering > 0;
                break;

            case Skill.Salvaging:
                playerHasAugmentation = AugmentationSpecializeSalvaging > 0;
                break;
        }

        return AugSpecSkills.Contains(skill);
    }

    public override bool GetHeritageBonus(WorldObject weapon)
    {
        if (weapon == null || !weapon.IsMasterable)
        {
            return false;
        }

        if (PropertyManager.GetBool("universal_masteries").Item)
        {
            // https://asheron.fandom.com/wiki/Spring_2014_Update
            // end of retail - universal masteries
            return true;
        }
        else
        {
            return GetHeritageBonus(GetWeaponType(weapon));
        }
    }

    public bool GetHeritageBonus(WeaponType weaponType)
    {
        switch (HeritageGroup)
        {
            case HeritageGroup.Aluvian:
                if (weaponType == WeaponType.Dagger || weaponType == WeaponType.Bow)
                {
                    return true;
                }

                break;
            case HeritageGroup.Gharundim:
                if (weaponType == WeaponType.Staff || weaponType == WeaponType.Magic)
                {
                    return true;
                }

                break;
            case HeritageGroup.Sho:
                if (weaponType == WeaponType.Unarmed || weaponType == WeaponType.Bow)
                {
                    return true;
                }

                break;
            case HeritageGroup.Viamontian:
                if (weaponType == WeaponType.Sword || weaponType == WeaponType.Crossbow)
                {
                    return true;
                }

                break;
            case HeritageGroup.Shadowbound: // umbraen
            case HeritageGroup.Penumbraen:
                if (weaponType == WeaponType.Unarmed || weaponType == WeaponType.Crossbow)
                {
                    return true;
                }

                break;
            case HeritageGroup.Gearknight:
                if (weaponType == WeaponType.Mace || weaponType == WeaponType.Crossbow)
                {
                    return true;
                }

                break;
            case HeritageGroup.Undead:
                if (weaponType == WeaponType.Axe || weaponType == WeaponType.Thrown)
                {
                    return true;
                }

                break;
            case HeritageGroup.Empyrean:
                if (weaponType == WeaponType.Sword || weaponType == WeaponType.Magic)
                {
                    return true;
                }

                break;
            case HeritageGroup.Tumerok:
                if (weaponType == WeaponType.Spear || weaponType == WeaponType.Thrown)
                {
                    return true;
                }

                break;
            case HeritageGroup.Lugian:
                if (weaponType == WeaponType.Axe || weaponType == WeaponType.Thrown)
                {
                    return true;
                }

                break;
            case HeritageGroup.Olthoi:
            case HeritageGroup.OlthoiAcid:
                break;
        }
        return false;
    }

    /// <summary>
    /// If the WeaponType is missing from a weapon, tries to convert from WeaponSkill (for old data)
    /// </summary>
    public WeaponType GetWeaponType(WorldObject weapon)
    {
        if (weapon == null)
        {
            return WeaponType.Undef; // unarmed?
        }

        if (weapon is Caster)
        {
            return WeaponType.Magic;
        }

        var weaponType = weapon.GetProperty(PropertyInt.WeaponType);
        if (weaponType != null)
        {
            return (WeaponType)weaponType;
        }

        var weaponSkill = weapon.GetProperty(PropertyInt.WeaponSkill);
        if (weaponSkill != null && SkillToWeaponType.TryGetValue((Skill)weaponSkill, out var converted))
        {
            return converted;
        }
        else
        {
            return WeaponType.Undef;
        }
    }

    public static Dictionary<Skill, WeaponType> SkillToWeaponType = new Dictionary<Skill, WeaponType>()
    {
        { Skill.UnarmedCombat, WeaponType.Unarmed },
        { Skill.Sword, WeaponType.Sword },
        { Skill.Axe, WeaponType.Axe },
        { Skill.Mace, WeaponType.Mace },
        { Skill.Spear, WeaponType.Spear },
        { Skill.Dagger, WeaponType.Dagger },
        { Skill.Staff, WeaponType.Staff },
        { Skill.Bow, WeaponType.Bow },
        { Skill.Crossbow, WeaponType.Crossbow },
        { Skill.ThrownWeapon, WeaponType.Thrown },
        { Skill.TwoHandedCombat, WeaponType.TwoHanded },
        { Skill.CreatureEnchantment, WeaponType.Magic }, // only for war/void?
        { Skill.PortalMagic, WeaponType.Magic },
        { Skill.LifeMagic, WeaponType.Magic },
        { Skill.WarMagic, WeaponType.Magic },
        { Skill.VoidMagic, WeaponType.Magic },
    };

    public void HandleSkillCreditRefund()
    {
        if (!(GetProperty(PropertyBool.UntrainedSkills) ?? false))
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "Your trained skills have been reset due to an error with skill credits.\nYou have received a refund for these skill credits and experience.",
                        ChatMessageType.Broadcast
                    )
                );

                RemoveProperty(PropertyBool.UntrainedSkills);
            }
        );
        actionChain.EnqueueChain();
    }

    public void HandleSkillSpecCreditRefund()
    {
        if (!(GetProperty(PropertyBool.UnspecializedSkills) ?? false))
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "Your specialized skills have been unspecialized due to an error with skill credits.\nYou have received a refund for these skill credits and experience.",
                        ChatMessageType.Broadcast
                    )
                );

                RemoveProperty(PropertyBool.UnspecializedSkills);
            }
        );
        actionChain.EnqueueChain();
    }

    public void HandleFreeSkillResetRenewal()
    {
        if (!(GetProperty(PropertyBool.FreeSkillResetRenewed) ?? false))
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "Your opportunity to change your skills is renewed! Visit Fianhe to reset your skills.",
                        ChatMessageType.Magic
                    )
                );

                RemoveProperty(PropertyBool.FreeSkillResetRenewed);

                QuestManager.Erase("UsedFreeSkillReset");
            }
        );
        actionChain.EnqueueChain();
    }

    public void HandleFreeAttributeResetRenewal()
    {
        if (!(GetProperty(PropertyBool.FreeAttributeResetRenewed) ?? false))
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                // Your opportunity to change your attributes is renewed! Visit Chafulumisa to reset your skills [sic attributes].
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "Your opportunity to change your attributes is renewed! Visit Chafulumisa to reset your attributes.",
                        ChatMessageType.Magic
                    )
                );

                RemoveProperty(PropertyBool.FreeAttributeResetRenewed);

                QuestManager.Erase("UsedFreeAttributeReset");
            }
        );
        actionChain.EnqueueChain();
    }

    public void HandleSkillTemplesReset()
    {
        if (!(GetProperty(PropertyBool.SkillTemplesTimerReset) ?? false))
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "The Temples of Forgetfulness and Enlightenment have had the timer for their use reset due to skill changes.",
                        ChatMessageType.Magic
                    )
                );

                RemoveProperty(PropertyBool.SkillTemplesTimerReset);

                QuestManager.Erase("ForgetfulnessGems1");
                QuestManager.Erase("ForgetfulnessGems2");
                QuestManager.Erase("ForgetfulnessGems3");
                QuestManager.Erase("ForgetfulnessGems4");
                QuestManager.Erase("Forgetfulness6days");
                QuestManager.Erase("Forgetfulness13days");
                QuestManager.Erase("Forgetfulness20days");

                QuestManager.Erase("AttributeLoweringGemPickedUp");
                QuestManager.Erase("AttributeRaisingGemPickedUp");
                QuestManager.Erase("SkillEnlightenmentGemPickedUp");
                QuestManager.Erase("SkillForgetfulnessGemPickedUp");
                QuestManager.Erase("SkillPrimaryGemPickedUp");
                QuestManager.Erase("SkillSecondaryGemPickedUp");
            }
        );
        actionChain.EnqueueChain();
    }

    public void HandleFreeMasteryResetRenewal()
    {
        if (!(GetProperty(PropertyBool.FreeMasteryResetRenewed) ?? false))
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "Your opportunity to change your Masteries is renewed!",
                        ChatMessageType.Magic
                    )
                );

                RemoveProperty(PropertyBool.FreeMasteryResetRenewed);

                QuestManager.Erase("UsedFreeMeleeMasteryReset");
                QuestManager.Erase("UsedFreeRangedMasteryReset");
                QuestManager.Erase("UsedFreeSummoningMasteryReset");
            }
        );
        actionChain.EnqueueChain();
    }

    /// <summary>
    /// Resets the skill, refunds all experience and skill credits, if allowed.
    /// </summary>
    public bool ResetSkill(Skill skill, bool refund = true)
    {
        var creatureSkill = GetCreatureSkill(skill, false);

        if (creatureSkill == null || creatureSkill.AdvancementClass < SkillAdvancementClass.Trained)
        {
            return false;
        }

        // gather skill credits to refund
        DatManager.PortalDat.SkillTable.SkillBaseHash.TryGetValue((uint)creatureSkill.Skill, out var skillBase);

        if (skillBase == null)
        {
            return false;
        }

        // salvage / tinkering skills specialized via augmentations
        // Salvaging cannot be untrained or unspecialized => skillIsSpecializedViaAugmentation && !untrainable
        IsSkillSpecializedViaAugmentation(creatureSkill.Skill, out var skillIsSpecializedViaAugmentation);

        var typeOfSkill = creatureSkill.AdvancementClass.ToString().ToLower() + " ";
        var untrainable = IsSkillUntrainable(skill, (HeritageGroup)Heritage);
        var creditRefund =
            (
                creatureSkill.AdvancementClass == SkillAdvancementClass.Specialized
                && !(skillIsSpecializedViaAugmentation && !untrainable)
            ) || untrainable;

        if (
            creatureSkill.AdvancementClass == SkillAdvancementClass.Specialized
            && !(skillIsSpecializedViaAugmentation && !untrainable)
        )
        {
            creatureSkill.AdvancementClass = SkillAdvancementClass.Trained;
            creatureSkill.InitLevel = 0;
            if (!skillIsSpecializedViaAugmentation) // Tinkering skills can be unspecialized, but do not refund upgrade cost.
            {
                AvailableSkillCredits += skillBase.UpgradeCostFromTrainedToSpecialized;
            }
        }

        // temple untraining 'always trained' skills:
        // cannot be untrained, but skill XP can be recovered
        if (untrainable)
        {
            creatureSkill.AdvancementClass = SkillAdvancementClass.Untrained;
            creatureSkill.InitLevel = 0;
            AvailableSkillCredits += skillBase.TrainedCost;
        }

        if (refund)
        {
            RefundXP(creatureSkill.ExperienceSpent);
        }

        creatureSkill.ExperienceSpent = 0;
        creatureSkill.Ranks = 0;

        var updateSkill = new GameMessagePrivateUpdateSkill(this, creatureSkill);
        var availableSkillCredits = new GameMessagePrivateUpdatePropertyInt(
            this,
            PropertyInt.AvailableSkillCredits,
            AvailableSkillCredits ?? 0
        );

        var msg =
            $"Your {(untrainable ? $"{typeOfSkill}" : "")}{skill.ToSentence()} skill has been {(untrainable ? "removed" : "reset")}. ";
        msg +=
            $"All the experience {(creditRefund ? "and skill credits " : "")}that you spent on this skill have been refunded to you.";

        if (refund && !IsTradeSkill(skill))
        {
            Session.Network.EnqueueSend(
                updateSkill,
                availableSkillCredits,
                new GameMessageSystemChat(msg, ChatMessageType.Broadcast)
            );
        }
        else
        {
            Session.Network.EnqueueSend(updateSkill, new GameMessageSystemChat(msg, ChatMessageType.Broadcast));
        }

        return true;
    }

    public void NoContribSkillXp(Player player, Skill skill, uint amount, bool reduce)
    {
        player.AwardNoContribSkillXP(skill, amount, reduce);
    }

    private void AwardNoContribSkillXP(Skill skill, uint amount, bool reduce)
    {
        var playerSkill = GetCreatureSkill(skill);

        if (playerSkill.AdvancementClass < SkillAdvancementClass.Trained || playerSkill.IsMaxRank)
        {
            return;
        }

        amount = Math.Min(amount, playerSkill.ExperienceLeft);

        var raiseChain = new ActionChain();
        raiseChain.AddDelayForOneTick();
        raiseChain.AddAction(
            this,
            () =>
            {
                HandleActionModifyNoContribSkill(skill, amount, reduce);
            }
        );
        raiseChain.EnqueueChain();

        var newSkill = (NewSkillNames)playerSkill.Skill;
        if (skill != Skill.Loyalty && skill != Skill.Leadership)
        {
            if (!reduce)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You've earned {amount:N0} experience in your {newSkill.ToSentence()} skill.",
                        ChatMessageType.Broadcast
                    )
                );
            }

            if (reduce)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You've lost {amount:N0} experience in your {newSkill.ToSentence()} skill.",
                        ChatMessageType.Broadcast
                    )
                );
            }
        }
    }

    private bool HandleActionModifyNoContribSkill(Skill skill, uint amount, bool reduce)
    {
        var creatureSkill = GetCreatureSkill(skill, false);

        if (creatureSkill == null || creatureSkill.AdvancementClass < SkillAdvancementClass.Trained)
        {
            _log.Error(
                $"{Name}.HandleActionRaiseNoContribSkill({skill}, {amount}) - trained or specialized skill not found"
            );
            return false;
        }

        var prevRank = creatureSkill.Ranks;

        if (!ModifyNoContribSkill(creatureSkill, amount, reduce))
        {
            return false;
        }

        Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(this, creatureSkill));

        if (prevRank != creatureSkill.Ranks)
        {
            var suffix = "";
            if (creatureSkill.IsMaxRank)
            {
                PlayParticleEffect(PlayScript.WeddingBliss, Guid);
                suffix = $" and has reached its upper limit";
            }
            var newSkill = (NewSkillNames)skill;
            var sound = new GameMessageSound(Guid, Sound.RaiseTrait);
            var msg = new GameMessageSystemChat(
                $"Your base {newSkill.ToSentence()} skill is now {creatureSkill.Base}{suffix}!",
                ChatMessageType.Advancement
            );

            Session.Network.EnqueueSend(sound, msg);
        }

        return true;
    }

    private bool ModifyNoContribSkill(CreatureSkill creatureSkill, uint amount, bool reduce)
    {
        var skillXPTable = GetSkillXPTable(creatureSkill.AdvancementClass);
        if (skillXPTable == null)
        {
            _log.Error(
                $"{Name}.SpendSkillXp({creatureSkill.Skill}, {amount}) - player tried to raise {creatureSkill.AdvancementClass} skill"
            );
            return false;
        }
        if (creatureSkill.IsMaxRank)
        {
            _log.Error(
                $"{Name}.SpendSkillXp({creatureSkill.Skill}, {amount}) - player tried to raise skill beyond max rank"
            );
            return false;
        }

        var amountToEnd = creatureSkill.ExperienceLeft;

        if (amount > amountToEnd)
        {
            //log.Error($"{Name}.SpendSkillXp({creatureSkill.Skill}, {amount}) - player tried to raise skill beyond {amountToEnd} experience");
            return false; // returning error here, instead of setting amount to amountToEnd
        }

        if (reduce)
        {
            creatureSkill.ExperienceSpent -= amount;
        }
        else
        {
            creatureSkill.ExperienceSpent += amount;
        }

        if (creatureSkill.ExperienceSpent < 0)
        {
            creatureSkill.ExperienceSpent = 0;
        }

        creatureSkill.Ranks = (ushort)CalcSkillRank(creatureSkill.AdvancementClass, creatureSkill.ExperienceSpent);

        return true;
    }

    public bool IsTradeSkill(Skill skill)
    {
        if (
            skill == Skill.Alchemy
            || skill == Skill.Tailoring
            || skill == Skill.Cooking
            || skill == Skill.Woodworking
            || skill == Skill.Spellcrafting
            || skill == Skill.Jewelcrafting
            || skill == Skill.Blacksmithing
        )
        {
            return true;
        }

        return false;
    }

    static Player()
    {
        PlayerSkills.Add(Skill.MartialWeapons); // Martial Weapons
        PlayerSkills.Add(Skill.Dagger);
        PlayerSkills.Add(Skill.Staff);
        PlayerSkills.Add(Skill.UnarmedCombat);
        PlayerSkills.Add(Skill.Bow); // Bows
        PlayerSkills.Add(Skill.ThrownWeapon);

        PlayerSkills.Add(Skill.TwoHandedCombat);
        PlayerSkills.Add(Skill.DualWield);
        PlayerSkills.Add(Skill.Shield);
        PlayerSkills.Add(Skill.Healing);

        PlayerSkills.Add(Skill.WarMagic);
        PlayerSkills.Add(Skill.LifeMagic);
        PlayerSkills.Add(Skill.ManaConversion);
        PlayerSkills.Add(Skill.ArcaneLore);

        PlayerSkills.Add(Skill.Perception); // Perception
        PlayerSkills.Add(Skill.Deception);
        PlayerSkills.Add(Skill.Thievery); // Thievery

        PlayerSkills.Add(Skill.Leadership);
        PlayerSkills.Add(Skill.Loyalty);

        PlayerSkills.Add(Skill.PhysicalDefense);
        PlayerSkills.Add(Skill.MagicDefense);

        PlayerSkills.Add(Skill.Alchemy);
        PlayerSkills.Add(Skill.Cooking);
        PlayerSkills.Add(Skill.Woodworking); // Woodworking
        PlayerSkills.Add(Skill.Blacksmithing); // Blacksmithing
        PlayerSkills.Add(Skill.Tailoring); // Tailoring
        PlayerSkills.Add(Skill.Spellcrafting); // Spellcrafting
        PlayerSkills.Add(Skill.Jewelcrafting); // Jewelcrafting
        PlayerSkills.Add(Skill.PortalMagic);

        PlayerSkills.Remove(Skill.AssessPerson);
        PlayerSkills.Remove(Skill.Axe);
        PlayerSkills.Remove(Skill.Sword);
        PlayerSkills.Remove(Skill.Mace);
        PlayerSkills.Remove(Skill.Crossbow);
        PlayerSkills.Remove(Skill.Spear);
        PlayerSkills.Remove(Skill.Salvaging);
        PlayerSkills.Remove(Skill.Awareness);
        PlayerSkills.Remove(Skill.LightWeapons);
        PlayerSkills.Remove(Skill.FinesseWeapons);
        PlayerSkills.Remove(Skill.MissileWeapons);
        PlayerSkills.Remove(Skill.Recklessness);
        PlayerSkills.Remove(Skill.SneakAttack);
        PlayerSkills.Remove(Skill.DirtyFighting);
        PlayerSkills.Remove(Skill.VoidMagic);
        PlayerSkills.Remove(Skill.Summoning);
        PlayerSkills.Remove(Skill.CreatureEnchantment);
    }

    /// <summary>
    /// All of the skills players have access to @ end of retail
    /// </summary>
    public static HashSet<Skill> PlayerSkills = new HashSet<Skill>()
    {
        Skill.PhysicalDefense,
        Skill.MissileDefense,
        Skill.ArcaneLore,
        Skill.MagicDefense,
        Skill.ManaConversion,
        Skill.Jewelcrafting,
        Skill.AssessPerson,
        Skill.Deception,
        Skill.Healing,
        Skill.Jump,
        Skill.Thievery,
        Skill.Run,
        Skill.Perception,
        Skill.Blacksmithing,
        Skill.Tailoring,
        Skill.Spellcrafting,
        Skill.CreatureEnchantment,
        Skill.PortalMagic,
        Skill.LifeMagic,
        Skill.WarMagic,
        Skill.Leadership,
        Skill.Loyalty,
        Skill.Woodworking,
        Skill.Alchemy,
        Skill.Cooking,
        Skill.Salvaging,
        Skill.TwoHandedCombat,
        Skill.VoidMagic,
        Skill.MartialWeapons,
        Skill.LightWeapons,
        Skill.FinesseWeapons,
        Skill.MissileWeapons,
        Skill.Shield,
        Skill.DualWield,
        Skill.Recklessness,
        Skill.SneakAttack,
        Skill.DirtyFighting,
        Skill.Summoning
    };
}
