using System;
using System.Linq;
using ACE.Common;
using ACE.Common.Extensions;
using ACE.DatLoader;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

partial class Player
{
    /// <summary>
    /// A player earns XP through natural progression, ie. kills and quests completed
    /// </summary>
    /// <param name="amount">The amount of XP being added</param>
    /// <param name="xpType">The source of XP being added</param>
    /// <param name="shareable">True if this XP can be shared with Fellowship</param>
    /// <param name="xpSourceLevel"></param>
    /// <param name="shareType"></param>
    /// <param name="creatureWcid"></param>
    public void EarnXP(
        long amount,
        XpType xpType,
        int? xpSourceLevel,
        ShareType shareType = ShareType.All,
        uint? creatureWcid = null
    )
    {
        //Console.WriteLine($"{Name}.EarnXP({amount}, {xpType}, {xpSourceLevel}, {xpSourceId}, {shareType})");

        amount = Math.Abs(amount);

        var xpMessage = "";

        var m_amount = amount;

        GrantXP(m_amount, xpType, xpSourceLevel, shareType, xpMessage, creatureWcid);
    }

    /// <summary>
    /// A player earns XP through killing a "boss" creature. Earned as "quest" xp for each available fellow member.
    /// </summary>
    /// <param name="xpSourceLevel">The level of the killed boss monster</param>
    /// <param name="bossKillXpMonsterMax">The percentage of the monster level cost to award as xp. Default is 5%.</param>
    /// <param name="bossKillXpPlayerMax">The percentage of the player level cost to award as xp. Default is 5%.</param>
    /// <param name="playerEarner"></param>
    public void EarnBossKillXP(
        int? xpSourceLevel,
        double? bossKillXpMonsterMax,
        double? bossKillXpPlayerMax,
        Player playerEarner
    )
    {
        var maxAwardPercentFromMonsterLevel = bossKillXpMonsterMax ?? 0.05;
        var maxAwardPercentFromPlayerLevel = bossKillXpPlayerMax ?? 0.05;

        var monsterLevelXpCost = GetXPBetweenLevels(xpSourceLevel ?? 1, (xpSourceLevel ?? 1) + 1);

        var distanceScalar = 1.0;
        var fellowSharePercent = 1.0;

        if (Fellowship != null)
        {
            distanceScalar = Fellowship.GetDistanceScalar(playerEarner, this, XpType.Kill);
            fellowSharePercent = Fellowship.GetMemberSharePercent();
        }

        var max = (long)(monsterLevelXpCost * maxAwardPercentFromMonsterLevel * distanceScalar * fellowSharePercent);

        GrantLevelProportionalXp(maxAwardPercentFromPlayerLevel, 0, max, xpSourceLevel);
    }

    /// <summary>
    /// A player earns XP through killing a creature with the ShroudKillXpReward flag.
    /// Xp amount is as if the killed creature is the same level as the player.
    /// Player must be Shrouded or less than or equal to the monster's level.
    /// Earned as "quest" xp for each available fellow member.
    /// </summary>
    /// <param name="playerEarner">The player who killed the creature. Only nearby fellows receive xp.</param>
    /// <param name="killXpMod">The xpModBonus generated from the creature's archetype stats.</param>
    public void EarnShroudKillXp(
        Player playerEarner,
        double killXpMod
    )
    {
        var softLevelCap = PropertyManager.GetLong("soft_level_cap").Item;
        var effectivePlayerLevel = (int)Math.Min(Level ?? 1, softLevelCap);
        var baseXp = GetCreatureDeathXP(effectivePlayerLevel, effectivePlayerLevel);

        var distanceScalar = 1.0;
        var fellowSharePercent = 1.0;

        if (Fellowship != null)
        {
            distanceScalar = Fellowship.GetDistanceScalar(playerEarner, this, XpType.Kill);
            fellowSharePercent = Fellowship.GetMemberSharePercent();
        }

        var overSoftCapPenalty = GetOverlevelPenalty(effectivePlayerLevel);

        var xp = (long)(baseXp * killXpMod * distanceScalar * fellowSharePercent * overSoftCapPenalty);

        GrantXP(xp, XpType.Quest, ShareType.None);
    }

    /// <summary>
    /// Directly grants XP to the player, from dev commands, allegiance passup, proficiency checks, and skill gains
    /// </summary>
    /// <param name="amount">The amount of XP to grant to the player</param>
    /// <param name="xpType">The source of the XP being granted</param>
    /// <param name="shareable">If TRUE, this XP can be shared with fellowship members</param>
    public void GrantXP(long amount, XpType xpType, ShareType shareType = ShareType.All, uint? creatureWcid = null)
    {
        if (IsOlthoiPlayer)
        {
            if (HasVitae)
            {
                UpdateXpVitae(amount);
            }

            return;
        }

        // Make sure UpdateXpAndLevel is done on this players thread
        EnqueueAction(new ActionEventDelegate(() => UpdateXpAndLevel(amount, amount, xpType)));

        // for passing XP up the allegiance chain,
        // this function is only called at the very beginning, to start the process.
        if (shareType.HasFlag(ShareType.Allegiance))
        {
            UpdateXpAllegiance(amount);
        }

        // only certain types of XP are granted to items
        if (xpType == XpType.Kill || xpType == XpType.Quest)
        {
            GrantItemXP(amount);
        }
    }

    /// <summary>
    /// Directly grants XP to the player, kills, quests, and emotes
    /// </summary>
    /// <param name="amount">The amount of XP to grant to the player</param>
    /// <param name="xpType">The source of the XP being granted</param>
    /// <param name="shareable">If TRUE, this XP can be shared with fellowship members</param>
    /// <param name="xpSourceLevel"></param>
    /// <param name="shareType"></param>
    /// <param name="xpMessage"></param>
    /// <param name="creatureWcid"></param>
    public void GrantXP(
        long amount,
        XpType xpType,
        int? xpSourceLevel,
        ShareType shareType = ShareType.All,
        string xpMessage = "",
        uint? creatureWcid = null
    )
    {
        //Console.WriteLine($"{Name}.GrantXP({amount}, {xpType}, {shareType})");

        if (IsOlthoiPlayer)
        {
            if (HasVitae)
            {
                UpdateXpVitae(amount);
            }

            return;
        }

        if (Fellowship != null && Fellowship.ShareXP && shareType.HasFlag(ShareType.Fellowship))
        {
            // this will divy up the XP, and re-call this function
            // with ShareType.Fellowship removed
            Fellowship.SplitXp((ulong)amount, xpType, xpSourceLevel, shareType, this, xpMessage);
            return;
        }

        var m_amount = amount;

        if (xpType == XpType.Kill)
        {
            // Gain full xp for creatures up to 5 levels higher than the player. Above that, the same xp as a creature 5 levels higher.
            // if (Level != null)
            // {
            //     var softlevelCap = PropertyManager.GetLong("soft_level_cap").Item;
            //     var maxFullXpKillLevel = Math.Min(Level.Value + 5, (int)softlevelCap);
            //     var maxXpPerKill = (long)GetCreatureDeathXP(maxFullXpKillLevel, maxFullXpKillLevel);
            //
            //     m_amount = Math.Min(m_amount, maxXpPerKill);
            // }
        }

        // Max possible quest xp gained is equal to 50% of your current level cost (200% of current level cost if under level 10)
        if (xpType == XpType.Quest)
        {
            //if (Level.Value < 10)
            //{
            //    var currentLevelCost = DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value + 1] - DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value];
            //    var maxXpPerQuest = (long)(currentLevelCost * 2);

            //    m_amount = Math.Min(m_amount, maxXpPerQuest);
            //}
            //else if (Level.Value < 20)
            //{
            //    var currentLevelCost = DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value + 1] - DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value];
            //    var maxXpPerQuest = (long)(currentLevelCost);

            //    m_amount = Math.Min(m_amount, maxXpPerQuest);
            //}
            //else if (Level.Value <= 126)
            //{
            //    var currentLevelCost = DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value + 1] - DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value];
            //    var maxXpPerQuest = (long)(currentLevelCost * 0.5);

            //    m_amount = Math.Min(m_amount, maxXpPerQuest);
            //}
        }

        var amountBeforeMods = m_amount;

        // Gain less xp for killing monsters below your level. TODO: give quests a source level.
        var overlevelPenalty = xpSourceLevel != null ? GetOverlevelPenalty((int)xpSourceLevel) : 1.0f;

        // Kill XP bonus for having higher level alt characters on your account. Doesn't share with fellow.
        var altBonus = GetAltXpBonus();

        // Kill XP bonus for having regional debuffs (ie Olthoi North)
        var regionalDebuffBonus = GetRegionalDebuffBonus(creatureWcid);

        // apply xp modifier
        var modifier = PropertyManager.GetDouble("xp_modifier").Item;

        var enchantment = GetXPAndLuminanceModifier(xpType);

        m_amount = (long)Math.Round(amountBeforeMods * modifier * enchantment * altBonus * regionalDebuffBonus * overlevelPenalty);

        var amountWithoutLevelPenalty = (long)Math.Round(amountBeforeMods * modifier * enchantment * altBonus * regionalDebuffBonus);

        // Console.WriteLine($"GrantXp(amount = {amount}, xpType = {xpType}, xpSourceLevel = {xpSourceLevel}, shareType = {shareType}, xpMessage = {xpMessage}, creatureWcid = {creatureWcid})\n" +
        //                   $"-amountBeforeMods: {amountBeforeMods}\n" +
        //                   $"-serverMod: {modifier}\n" +
        //                   $"-enchantmentMod: {enchantment}\n" +
        //                   $"-altBonusMod: {altBonus}\n" +
        //                   $"-regionalDebuffBonusMod: {regionalDebuffBonus}\n" +
        //                   $"-overlevelPenalty: {overlevelPenalty}");

        // Make sure UpdateXpAndLevel is done on this players thread
        EnqueueAction(new ActionEventDelegate(() => UpdateXpAndLevel(m_amount, amountWithoutLevelPenalty, xpType, xpMessage)));

        // for passing XP up the allegiance chain,
        // this function is only called at the very beginning, to start the process.
        if (shareType.HasFlag(ShareType.Allegiance))
        {
            if (!WithPatron || !FellowedWithPatron)
            {
                amount /= 2;
            }

            // if fellowship, we reverse the fellow sharing bonus to find the base amount, then split it evenly
            if (Fellowship != null && Fellowship.ShareXP)
            {
                double reciprocal = 0;
                var members = Fellowship.TotalMembers;

                switch (members)
                {
                    case 1:
                        reciprocal = 1;
                        break;
                    case 2:
                        reciprocal = 1 / 0.75;
                        break;
                    case 3:
                        reciprocal = 1 / 0.6;
                        break;
                    case 4:
                        reciprocal = 1 / .55;
                        break;
                    case 5:
                        reciprocal = 1 / .5;
                        break;
                    case 6:
                        reciprocal = 1 / .45;
                        break;
                    case 7:
                        reciprocal = 1 / .4;
                        break;
                    case 8:
                        reciprocal = 1 / .35;
                        break;
                    case 9:
                        reciprocal = 1 / .3;
                        break;
                }
                amount = (long)((amount * reciprocal) / members);
            }

            UpdateXpAllegiance(amount);
        }

        // only certain types of XP are granted to items
        if (xpType is XpType.Kill or XpType.Quest or XpType.Fellowship)
        {
            GrantItemXP(amount);
        }
    }

    /// <summary>
    /// Adds XP to a player's total XP, handles triggers (vitae, level up)
    /// </summary>
    private void UpdateXpAndLevel(long amount, long amountWithoutLevelPenalty, XpType xpType, string xpMessage = "")
    {
        // until we are max level we must make sure that we send
        var xpTable = DatManager.PortalDat.XpTable;

        var maxLevel = GetMaxLevel();
        var maxLevelXp = xpTable.CharacterLevelXPList[(int)maxLevel];

        var allowXpAtMaxLevel = PropertyManager.GetBool("allow_xp_at_max_level").Item;
        var totalXpCap = maxLevelXp; // 0 disables the xp cap
        var availableXpCap = uint.MaxValue; // 0 disables the xp cap

        if (Level != maxLevel || allowXpAtMaxLevel)
        {
            var addAmount = amount;

            var amountLeftToEnd = (long)maxLevelXp - TotalExperience ?? 0;
            if (!allowXpAtMaxLevel && amount > amountLeftToEnd)
            {
                addAmount = amountLeftToEnd;
            }

            TotalExperience += addAmount;
            if (totalXpCap > 0 && TotalExperience > (long)totalXpCap)
            {
                TotalExperience = (long)totalXpCap;
            }

            AvailableExperience += addAmount;
            if (availableXpCap > 0 && AvailableExperience > (long)availableXpCap)
            {
                AvailableExperience = (long)availableXpCap;
            }

            var xpTotalUpdate = new GameMessagePrivateUpdatePropertyInt64(
                this,
                PropertyInt64.TotalExperience,
                TotalExperience ?? 0
            );
            var xpAvailUpdate = new GameMessagePrivateUpdatePropertyInt64(
                this,
                PropertyInt64.AvailableExperience,
                AvailableExperience ?? 0
            );
            Session.Network.EnqueueSend(xpTotalUpdate, xpAvailUpdate);

            CheckForLevelup();
        }

        if (xpType == XpType.Quest)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You've earned {amount:N0} experience. {xpMessage}",
                    ChatMessageType.Broadcast
                )
            );
        }
        else
        {
            if (xpType == XpType.Fellowship)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Your fellowship shared {amount:N0} experience with you!",
                        ChatMessageType.Broadcast
                    )
                );
            }

            if (xpType == XpType.Kill && xpMessage != "")
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You've earned {amount:N0} experience! {xpMessage}",
                        ChatMessageType.Broadcast
                    )
                );
            }
        }

        if (HasVitae && xpType != XpType.Allegiance)
        {
            UpdateXpVitae(amountWithoutLevelPenalty);
        }

        var loyalty = GetCreatureSkill(Skill.Loyalty);
        if (loyalty.AdvancementClass >= SkillAdvancementClass.Trained && !loyalty.IsMaxRank)
        {
            UpdateLoyalty(loyalty, amountWithoutLevelPenalty);
        }
    }

    /// <summary>
    /// Update a player's Loyalty skill xp progress when XP has been earned.
    /// <para>FORMULA = ProportionCurrentLevelXpEarned * LevelOffset * LoyaltyRankXpCost * FellowshipMod * TimeSwornMod</para>
    /// <para>LevelOffset = x1 to x10, depending on player level up to 100. To offset the slower pace of later levels.</para>
    /// <para>FelloshipMod = x2, if patron and vassal are fellowed and on the same landblock.</para>
    /// <para>TimeSwornMod = x1 up to x2, depending on how long vassal has been sworn to patron (maxes at 3 months).</para>
    /// </summary>
    private void UpdateLoyalty(Entity.CreatureSkill loyalty, long amount)
    {
        if (Level == null)
        {
            return;
        }

        var nextLevelXP = GetXPBetweenLevels(Level.Value, Level.Value + 1);
        var proportion = (double)amount / (double)nextLevelXP;

        var playerLevel = Level ?? 1;
        var levelOffset = Math.Clamp((float)playerLevel / 10, 1, 10);

        var nextRankXP = GetXPBetweenSkillLevels(loyalty.AdvancementClass, loyalty.Ranks, loyalty.Ranks + 1);
        if (nextRankXP == null)
        {
            return;
        }

        var baseLoyaltyXP = (long)nextRankXP * proportion * levelOffset;

        double fellowshipMod = 1;
        double timeMod = 1;

        if (WithPatron && FellowedWithPatron)
        {
            fellowshipMod = 2;
        }

        if (SworeAllegiance != null)
        {
            timeMod = 1 + (Time.GetUnixTime() - (double)SworeAllegiance) / 7892352;
            if (timeMod > 2)
            {
                timeMod = 2;
            }
        }

        var loyaltyXp = (uint)Math.Max(baseLoyaltyXP * fellowshipMod * timeMod, 1);
        AwardNoContribSkillXP(Skill.Loyalty, loyaltyXp, false);
    }

    /// <summary>
    /// Optionally passes XP up the Allegiance tree
    /// </summary>
    private void UpdateXpAllegiance(long amount)
    {
        if (!HasAllegiance)
        {
            return;
        }

        AllegianceManager.PassXP(AllegianceNode, (ulong)amount, true);
    }

    /// <summary>
    /// Handles updating the vitae penalty through earned XP
    /// </summary>
    /// <param name="amount">The amount of XP to apply to the vitae penalty</param>
    private void UpdateXpVitae(long amount)
    {
        var vitae = EnchantmentManager.GetVitae();

        if (vitae == null)
        {
            _log.Error(
                $"{Name}.UpdateXpVitae({amount}) vitae null, likely due to cross-thread operation or corrupt EnchantmentManager cache. Please report this."
            );
            _log.Error(Environment.StackTrace);
            return;
        }

        var vitaePenalty = vitae.StatModValue;
        var startPenalty = vitaePenalty;

        var maxPool = (int)VitaeCPPoolThreshold(vitaePenalty, DeathLevel.Value);
        var curPool = VitaeCpPool + amount;
        while (curPool >= maxPool)
        {
            curPool -= maxPool;
            vitaePenalty = EnchantmentManager.ReduceVitae();
            if (vitaePenalty == 1.0f)
            {
                break;
            }

            maxPool = (int)VitaeCPPoolThreshold(vitaePenalty, DeathLevel.Value);
        }
        VitaeCpPool = (int)curPool;

        Session.Network.EnqueueSend(
            new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.VitaeCpPool, VitaeCpPool.Value)
        );

        if (vitaePenalty != startPenalty)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat("Your experience has reduced your Vitae penalty!", ChatMessageType.Magic)
            );
            EnchantmentManager.SendUpdateVitae();
        }

        if (vitaePenalty.EpsilonEquals(1.0f) || vitaePenalty > 1.0f)
        {
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(2.0f);
            actionChain.AddAction(
                this,
                () =>
                {
                    var vitae = EnchantmentManager.GetVitae();
                    if (vitae != null)
                    {
                        var curPenalty = vitae.StatModValue;
                        if (curPenalty.EpsilonEquals(1.0f) || curPenalty > 1.0f)
                        {
                            EnchantmentManager.RemoveVitae();
                        }
                    }
                }
            );
            actionChain.EnqueueChain();
        }
    }

    public void RelieveVitaePenalty(int? amount)
    {
        if (HasVitae)
        {
            RelieveVitae(amount);
        }
    }

    private void RelieveVitae(int? amount)
    {
        var vitae = EnchantmentManager.GetVitae();

        if (vitae == null)
        {
            _log.Error(
                $"{Name}.UpdateXpVitae({amount}) vitae null, likely due to cross-thread operation or corrupt EnchantmentManager cache. Please report this."
            );
            _log.Error(Environment.StackTrace);
            return;
        }

        var vitaePenalty = vitae.StatModValue;
        var vitaeRelieved = 0;

        while (amount > 0)
        {
            amount -= 1;
            vitaeRelieved += 1;
            vitaePenalty = EnchantmentManager.ReduceVitae();
            if (vitaePenalty == 1.0f)
            {
                break;
            }
        }

        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Your Vitae penalty has been relieved by {vitaeRelieved}%!",
                ChatMessageType.Magic
            )
        );
        EnchantmentManager.SendUpdateVitae();

        if (vitaePenalty.EpsilonEquals(1.0f) || vitaePenalty > 1.0f)
        {
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(2.0f);
            actionChain.AddAction(
                this,
                () =>
                {
                    var vitae = EnchantmentManager.GetVitae();
                    if (vitae != null)
                    {
                        var curPenalty = vitae.StatModValue;
                        if (curPenalty.EpsilonEquals(1.0f) || curPenalty > 1.0f)
                        {
                            EnchantmentManager.RemoveVitae();
                        }
                    }
                }
            );
            actionChain.EnqueueChain();
        }
    }

    /// <summary>
    /// Returns the maximum possible character level
    /// </summary>
    public static uint GetMaxLevel()
    {
        var maxPossibleLevel = (uint)DatManager.PortalDat.XpTable.CharacterLevelXPList.Count - 1;
        var maxSettingLevel = (uint)PropertyManager.GetLong("max_level").Item;
        return (Math.Min(maxPossibleLevel, maxSettingLevel));
    }

    /// <summary>
    /// Returns TRUE if player >= MaxLevel
    /// </summary>
    public bool IsMaxLevel => Level >= GetMaxLevel();

    /// <summary>
    /// Returns the remaining XP required to reach a level
    /// </summary>
    public long? GetRemainingXP(uint level)
    {
        var maxLevel = GetMaxLevel();
        if (level < 1 || level > maxLevel)
        {
            return null;
        }

        var levelTotalXP = DatManager.PortalDat.XpTable.CharacterLevelXPList[(int)level];

        return (long)levelTotalXP - TotalExperience.Value;
    }

    /// <summary>
    /// Returns the remaining XP required to the next level
    /// </summary>
    public ulong GetRemainingXP()
    {
        var maxLevel = GetMaxLevel();
        if (Level >= maxLevel)
        {
            return 0;
        }

        var nextLevelTotalXP = DatManager.PortalDat.XpTable.CharacterLevelXPList[Level.Value + 1];
        return nextLevelTotalXP - (ulong)TotalExperience.Value;
    }

    /// <summary>
    /// Returns the total XP required to reach a level
    /// </summary>
    public static ulong GetTotalXP(int level)
    {
        var maxLevel = GetMaxLevel();
        if (level < 0 || level > maxLevel)
        {
            return 0;
        }

        return DatManager.PortalDat.XpTable.CharacterLevelXPList[level];
    }

    /// <summary>
    /// Returns the total amount of XP required for a player reach max level
    /// </summary>
    public static long MaxLevelXP
    {
        get
        {
            var xpTable = DatManager.PortalDat.XpTable.CharacterLevelXPList;

            return (long)xpTable[xpTable.Count - 1];
        }
    }

    /// <summary>
    /// Returns the XP required to go from level A to level B
    /// </summary>
    public ulong GetXPBetweenLevels(int levelA, int levelB)
    {
        // special case for max level
        var maxLevel = (int)GetMaxLevel();

        levelA = Math.Clamp(levelA, 1, maxLevel - 1);
        levelB = Math.Clamp(levelB, 1, maxLevel);

        var levelA_totalXP = DatManager.PortalDat.XpTable.CharacterLevelXPList[levelA];
        var levelB_totalXP = DatManager.PortalDat.XpTable.CharacterLevelXPList[levelB];

        return levelB_totalXP - levelA_totalXP;
    }


    public ulong GetXPToNextLevel(int level)
    {
        return GetXPBetweenLevels(level, level + 1);
    }

    public int GetSkillCreditsAtLevel(int level)
    {
        switch (level)
        {
            case >= 100:
                return 32;
            case >= 90:
                return 30;
            case >= 80:
                return 28;
            case >= 70:
                return 26;
            case >= 60:
                return 24;
            case >= 50:
                return 22;
            case >= 40:
                return 20;
            case >= 30:
                return 17;
            case >= 20:
                return 14;
            case >= 10:
                return 9;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Determines if the player has advanced a level
    /// </summary>
    private void CheckForLevelup()
    {
        var xpTable = DatManager.PortalDat.XpTable;

        var maxLevel = GetMaxLevel();

        if (Level >= maxLevel)
        {
            return;
        }

        var startingLevel = Level;
        var creditEarned = false;

        // increases until the correct level is found
        while ((ulong)(TotalExperience ?? 0) >= xpTable.CharacterLevelXPList[(Level ?? 0) + 1])
        {
            Level++;

            // increase the skill credits if the chart allows this level to grant a credit
            if (xpTable.CharacterLevelSkillCreditList[Level ?? 0] > 0)
            {
                AvailableSkillCredits += (int)xpTable.CharacterLevelSkillCreditList[Level ?? 0];
                TotalSkillCredits += (int)xpTable.CharacterLevelSkillCreditList[Level ?? 0];
                creditEarned = true;
            }

            // break if we reach max
            if (Level == maxLevel)
            {
                PlayParticleEffect(PlayScript.WeddingBliss, Guid);
                break;
            }
        }

        if (Level > startingLevel)
        {
            var message =
                (Level == maxLevel) ? $"You have reached the maximum level of {Level}!" : $"You are now level {Level}!";

            message +=
                (AvailableSkillCredits > 0)
                    ? $"\nYou have {AvailableExperience:#,###0} experience points and {AvailableSkillCredits} skill credits available to raise skills and attributes."
                    : $"\nYou have {AvailableExperience:#,###0} experience points available to raise skills and attributes.";

            var levelUp = new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.Level, Level ?? 1);
            var currentCredits = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.AvailableSkillCredits,
                AvailableSkillCredits ?? 0
            );

            if (Level != maxLevel && !creditEarned)
            {
                var nextLevelWithCredits = 0;

                for (var i = (Level ?? 0) + 1; i <= maxLevel; i++)
                {
                    if (xpTable.CharacterLevelSkillCreditList[i] > 0)
                    {
                        nextLevelWithCredits = i;
                        break;
                    }
                }
                message += $"\nYou will earn another skill credit at level {nextLevelWithCredits}.";
            }

            if (Fellowship != null)
            {
                Fellowship.OnFellowLevelUp(this);
            }

            if (AllegianceNode != null)
            {
                AllegianceNode.OnLevelUp();
            }

            Session.Network.EnqueueSend(levelUp);

            SetMaxVitals();

            // play level up effect
            PlayParticleEffect(PlayScript.LevelUp, Guid);

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(message, ChatMessageType.Advancement),
                currentCredits
            );

            if (GetEquippedTrinket() is CombatFocus combatFocus)
            {
                combatFocus.OnLevelUp(this, (int)startingLevel);
            }

            SetPlayerAllegianceRankContribution();

            CheckForBetaQuestComplete();
        }
    }

    private void CheckForBetaQuestComplete()
    {
        switch (Level)
        {
            case >= 50:
                QuestManager.Stamp("ACCOUNT_BetaLevel50");
                QuestManager.Stamp("ACCOUNT_BetaLevel40");
                QuestManager.Stamp("ACCOUNT_BetaLevel30");
                QuestManager.Stamp("ACCOUNT_BetaLevel20");
                QuestManager.Stamp("ACCOUNT_BetaLevel10");
                break;
            case >= 40:
                QuestManager.Stamp("ACCOUNT_BetaLevel40");
                QuestManager.Stamp("ACCOUNT_BetaLevel30");
                QuestManager.Stamp("ACCOUNT_BetaLevel20");
                QuestManager.Stamp("ACCOUNT_BetaLevel10");
                break;
            case >= 30:
                QuestManager.Stamp("ACCOUNT_BetaLevel30");
                QuestManager.Stamp("ACCOUNT_BetaLevel20");
                QuestManager.Stamp("ACCOUNT_BetaLevel10");
                break;
            case >= 20:
                QuestManager.Stamp("ACCOUNT_BetaLevel20");
                QuestManager.Stamp("ACCOUNT_BetaLevel10");
                break;
            case >= 10:
                QuestManager.Stamp("ACCOUNT_BetaLevel10");
                break;
        }
    }

    /// <summary>
    /// Spends the amount of XP specified, deducting it from available experience
    /// </summary>
    public bool SpendXP(long amount, bool sendNetworkUpdate = true)
    {
        if (amount > AvailableExperience)
        {
            return false;
        }

        AvailableExperience -= amount;

        if (sendNetworkUpdate)
        {
            Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt64(
                    this,
                    PropertyInt64.AvailableExperience,
                    AvailableExperience ?? 0
                )
            );
        }

        return true;
    }

    /// <summary>
    /// Tries to spend all of the players Xp into Attributes, Vitals and Skills
    /// </summary>
    public void SpendAllXp(bool sendNetworkUpdate = true)
    {
        SpendAllAvailableAttributeXp(Strength, sendNetworkUpdate);
        SpendAllAvailableAttributeXp(Endurance, sendNetworkUpdate);
        SpendAllAvailableAttributeXp(Coordination, sendNetworkUpdate);
        SpendAllAvailableAttributeXp(Quickness, sendNetworkUpdate);
        SpendAllAvailableAttributeXp(Focus, sendNetworkUpdate);
        SpendAllAvailableAttributeXp(Self, sendNetworkUpdate);

        SpendAllAvailableVitalXp(Health, sendNetworkUpdate);
        SpendAllAvailableVitalXp(Stamina, sendNetworkUpdate);
        SpendAllAvailableVitalXp(Mana, sendNetworkUpdate);

        foreach (var skill in Skills)
        {
            if (skill.Value.AdvancementClass >= SkillAdvancementClass.Trained)
            {
                SpendAllAvailableSkillXp(skill.Value, sendNetworkUpdate);
            }
        }
    }

    /// <summary>
    /// Gives available XP of the amount specified, without increasing total XP
    /// </summary>
    public void RefundXP(long amount)
    {
        AvailableExperience += amount;

        var xpUpdate = new GameMessagePrivateUpdatePropertyInt64(
            this,
            PropertyInt64.AvailableExperience,
            AvailableExperience ?? 0
        );
        Session.Network.EnqueueSend(xpUpdate);
    }

    public void HandleMissingXp()
    {
        var verifyXp = GetProperty(PropertyInt64.VerifyXp) ?? 0;
        if (verifyXp == 0)
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(5.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                var xpType = verifyXp > 0 ? "unassigned experience" : "experience points";

                var msg =
                    $"This character was missing some {xpType} --\nYou have gained an additional {Math.Abs(verifyXp).ToString("N0")} {xpType}!";

                Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));

                if (verifyXp < 0)
                {
                    // add to character's total XP
                    TotalExperience -= verifyXp;

                    CheckForLevelup();
                }

                RemoveProperty(PropertyInt64.VerifyXp);
            }
        );

        actionChain.EnqueueChain();
    }

    /// <summary>
    /// Returns the total amount of XP required to go from vitae to vitae + 0.01
    /// </summary>
    /// <param name="vitae">The current player life force, ie. 0.95f vitae = 5% penalty</param>
    /// <param name="level">The player DeathLevel, their level on last death</param>
    private double VitaeCPPoolThreshold(float vitae, int level)
    {
        // http://acpedia.org/wiki/Announcements_-_2005/07_-_Throne_of_Destiny_(expansion)#FAQ_-_AC:TD_Level_Cap_Update
        // "The vitae system has not changed substantially since Asheron's Call launched in 1999.
        // Since that time, the experience awarded by killing creatures has increased considerably.
        // This means that a 5% vitae loss currently is much easier to work off now than it was in the past.
        // In addition, the maximum cost to work off a point of vitae was capped at 12,500 experience points."
        return Math.Min((Math.Pow(level, 2) * 5 + 20) * Math.Pow(vitae, 5.0) + 0.5, 12500);
    }

    /// <summary>
    /// Raise the available XP by a percentage of the current level XP or a maximum
    /// </summary>
    public void GrantLevelProportionalXp(double percent, long min, long max, int? xpSourceLevel)
    {
        if (max == 0)
        {
            return;
        }

        var level = Level ?? 1;

        var nextLevelXP = GetXPBetweenLevels(level, level + 1);

        var scaledXP = (long)Math.Round(nextLevelXP * percent);

        if (max > 0)
        {
            scaledXP = Math.Min(scaledXP, max);
        }

        if (min > 0)
        {
            scaledXP = Math.Max(scaledXP, min);
        }

        // apply xp modifiers?
        EarnXP(scaledXP, XpType.Quest, xpSourceLevel, ShareType.Allegiance);
    }

    /// <summary>
    /// The player earns XP for items that can be leveled up
    /// by killing creatures and completing quests,
    /// while those items are equipped.
    /// </summary>
    public void GrantItemXP(long amount)
    {
        foreach (var item in EquippedObjects.Values.Where(i => i.HasItemLevel))
        {
            GrantItemXP(item, amount);
        }
    }

    public void GrantItemXP(WorldObject item, long amount)
    {
        var prevItemLevel = item.ItemLevel.Value;
        var addItemXP = item.AddItemXP(amount);

        if (addItemXP > 0)
        {
            Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt64(item, PropertyInt64.ItemTotalXp, item.ItemTotalXp.Value)
            );
        }

        // handle item leveling up
        var newItemLevel = item.ItemLevel.Value;
        if (newItemLevel > prevItemLevel)
        {
            OnItemLevelUp(item, prevItemLevel);

            if (item.WeenieType == WeenieType.SigilTrinket)
            {
                if (item is SigilTrinket sigilTrinket)
                {
                    sigilTrinket.OnLevelUp();
                }
            }

            var actionChain = new ActionChain();
            actionChain.AddAction(
                this,
                () =>
                {
                    var msg = $"Your {item.Name} has increased in power to level {newItemLevel}!";
                    Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));

                    EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.AetheriaLevelUp));
                }
            );
            actionChain.EnqueueChain();
        }
    }

    /// <summary>
    /// Returns the multiplier to XP and Luminance from Trinkets and Augmentations
    /// </summary>
    public float GetXPAndLuminanceModifier(XpType xpType)
    {
        var enchantmentBonus = EnchantmentManager.GetXpBonus();

        var augBonus = 0.0f;
        if (xpType == XpType.Kill && AugmentationBonusXp > 0)
        {
            augBonus = AugmentationBonusXp * 0.05f;
        }

        // JEWEL - Sunstone: Bonus experience gain
        if (xpType == XpType.Kill && GetEquippedAndActivatedItemRatingSum(PropertyInt.GearExperienceGain) > 0)
        {
            augBonus *= Jewel.GetJewelEffectMod(this, PropertyInt.GearExperienceGain);
        }

        var modifier = 1.0f + enchantmentBonus + augBonus;
        //Console.WriteLine($"XPAndLuminanceModifier: {modifier}");

        return modifier;
    }

    /// <summary>
    /// Returns XP modifier for having characters on your account that are higher than your current character level
    /// </summary>
    private float GetAltXpBonus()
    {
        var accountCharacters = GetAccountPlayers(Account.AccountId);
        int? levelDifference = 0;

        foreach (var character in accountCharacters)
        {
            if (character.Level > Level)
            {
                //Console.WriteLine($"{character.Name}: Level {character.Level}");
                levelDifference += character.Level - Level;
            }
        }

        var xpBonusMod = 1.0f;
        if (levelDifference > 0)
        {
            xpBonusMod += (float)levelDifference / 100;
        }

        //Console.WriteLine($"Level Difference: {levelDifference}  XP Bonus Mod: {xpBonusMod}");

        return xpBonusMod;
    }

    /// <summary>
    /// Returns XP modifier for having a regional debuff and killing enemies in that region (i.e. Olthoi North)
    /// </summary>
    private float GetRegionalDebuffBonus(uint? enemyWcid)
    {
        var xpBonusMod = 1.0f;

        if (enemyWcid == null)
        {
            return xpBonusMod;
        }

        if (StackableSpellTables.OlthoiNorthCreatureWcids.Contains(enemyWcid.Value))
        {
            xpBonusMod = (float)GetOlthoiNorthSpellStacks() / 100 + 1;
        }

        //Console.WriteLine($"RegionalDebuffBonus: {xpBonusMod}");

        return xpBonusMod;
    }

    /// <summary>
    /// Returns XP modifier for killing enemies below your level.
    /// 10% less per level, multiplicatively. (50% per level once soft level cap has been reached)
    /// </summary>
    private float GetOverlevelPenalty(int? xpSourceLevel)
    {
        if (xpSourceLevel == null)
        {
            return 1.0f;
        }

        var levelDifference = Level - xpSourceLevel;
        var atSoftCap = Level >= PropertyManager.GetLong("soft_level_cap").Item;

        var penalty = 1.0f;

        if (levelDifference > 0)
        {
            for (var i = 0; i < levelDifference; i++)
            {
                penalty *= atSoftCap ? 0.5f : 0.9f;
            }
        }
        //Console.WriteLine($"LevelDifference: {levelDifference}.  Penalty: {Math.Round(penalty * 100)}%");

        return penalty;
    }

    // TODO
    //private int GetQuestSourceLevel(int amount)
    //{
    //    var xpCost10 = (int)(DatManager.PortalDat.XpTable.CharacterLevelXPList[10 + 1]);
    //    var constXpCost10 = xpCost10 * 0.25f;

    //    switch (amount)
    //    {
    //        case < (int)constXpCost10:
    //        default: return 10;
    //        case < 10000: return 15;
    //        case < 20000: return 20;
    //    }
    //}

    //private int GetXpToLevel(int level)
    //{
    //    switch (level)
    //    {
    //        case < 10: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[10 + 1];
    //        case < 20: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[15 + 1];
    //        case < 30: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[20 + 1];
    //        case < 40: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[20 + 1];
    //        case < 50: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[20 + 1];
    //        case < 75: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[20 + 1];
    //        case < 100: return (int)DatManager.PortalDat.XpTable.CharacterLevelXPList[20 + 1];
    //    }
    //}
}
