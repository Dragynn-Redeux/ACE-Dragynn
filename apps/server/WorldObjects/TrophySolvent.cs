using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using MotionCommand = ACE.Entity.Enum.MotionCommand;
using WCN = ACE.Entity.Enum.WeenieClassName;

namespace ACE.Server.WorldObjects;

public class TrophySolvent : Stackable
{
    private const uint EssenceWCID = 1053982;

    private enum EssenceEffect
    {
        None,
        Long,
        Short
    }

    /// <summary>
    /// Maps trophy WCID to base essence properties (base SpellDID for quality 1).
    /// Final spell ID = base spell ID + (TrophyQuality - 1), since spells are in sequential sets of 10.
    /// </summary>
    private static readonly Dictionary<uint, (EssenceEffect EssenceEffect, Skill Skill, uint? BaseSpellId)> TrophyEssenceMap = new()
    {
        //                                                Effect               Skill            BaseSpellId
        { (uint)WCN.W_ARMOREDILLOHIDETROPHY_CLASS,  (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionSlashingProtection1) },  // Prot - Slash
        { (uint)WCN.W_ARMOREDILLOSPINE_CLASS,       (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodQuickness1) },             // Attribute - Quick
        { (uint)WCN.W_AUROCHMEAT_CLASS,             (EssenceEffect.Short, Skill.Cooking, null) },                                         // Chug Health
        { (uint)WCN.W_AUROCHHORNTROPHY_CLASS,      (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionHeartSeeker1) },          // Item - Heart Seeker
        { (uint)WCN.W_BANDERLINGSCALPTROPHY_CLASS,  (EssenceEffect.Short,  Skill.Cooking, (uint)SpellId.CookFoodJump1) },                   // Skill - Jump
        { (uint)WCN.W_BANDERLINGBLOOD_CLASS,        (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionHealOverTime1) },         // HoT - Health
        { (uint)WCN.W_CHITTICKSPINE_CLASS,          (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionManaOverTime1) },         // HoT - Mana
        { (uint)WCN.W_CHITTICKHEAD_CLASS,           (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodCoordination1) },           // Attribute - Coordination
        { (uint)WCN.W_DRUDGECHARMTROPHY_CLASS,      (EssenceEffect.Short,  Skill.Cooking, (uint)SpellId.CookFoodThievery1) },               // Skill - Thievery
        { (uint)WCN.W_DRUDGEGUTS_CLASS,             (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionManaOverTime1) },         // HoT - Mana
        { (uint)WCN.W_ECTOPLASM_CLASS,              (EssenceEffect.Short, Skill.Cooking, null) },                                         // Chug Mana
        { (uint)WCN.W_DOLLMASK_CLASS,               (EssenceEffect.Short,  Skill.Cooking, (uint)SpellId.CookFoodLifeMagic1) },              // Skill - Life Magic
        { (uint)WCN.W_VIOLETENERGY_CLASS,           (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionSpiritDrinker1) },        // Item - Spirit Drinker
        { (uint)WCN.W_GRIEVVERSILK_CLASS,           (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodFocus1) },                  // Attribute - Focus
        { (uint)WCN.W_GRIEVVERTIBIA_CLASS,          (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionCriticalDamage1) },       // Rating - Crit Damage
        { (uint)WCN.W_GROMNIETOOTH_CLASS,           (EssenceEffect.Short,  Skill.Alchemy, (uint)SpellId.AlchPotionBloodDrinker1) },         // Item - Blood Drinker
        { (uint)WCN.W_GROMNIEWINGTROPHY_CLASS,       (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodJump1) },                   // Skill - Jump
        { (uint)WCN.W_BROWNLUMPTROPHY_CLASS,        (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionSpiritDrinker1) },        // Item - Spirit Drinker
        { (uint)WCN.W_KNATHEGG_CLASS,               (EssenceEffect.Short, Skill.Cooking, null) },                                         // Chug Mana
        { (uint)WCN.W_LUGIANBLOOD_CLASS,            (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionBludgeoningProtection1) },// Prot - Blunt
        { (uint)WCN.W_LUGIANSINEW_CLASS,            (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodStrength1) },               // Attribute - Strength
        { (uint)WCN.W_MATTEKARHIDETROPHY_CLASS,     (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionColdProtection1) },       // Prot - Cold
        { (uint)WCN.W_MATTEKARHORN_CLASS,           (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionHeartSeeker1) },          // Item - Heart Seeker
        { (uint)WCN.W_MITEFUR_CLASS,                (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionSwiftKiller1) },          // Item - Swift Killer
        { (uint)WCN.W_MITEHEART_CLASS,              (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodRun1) },                    // Skill - Run
        { (uint)WCN.W_MONOUGATTROPHY_CLASS,         (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodEndurance1) },              // Attribute - Endurance
        { (uint)WCN.W_MONOUGASKULL_CLASS,           (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionHealOverTime1) },         // HoT - Health
        { (uint)WCN.W_MOSSWARTEGGS_CLASS,           (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodFocus1) },                  // Attribute - Focus
        { (uint)WCN.W_SWAMPSTONE_CLASS,             (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionFireProtection1) },       // Prot - Fire
        { (uint)WCN.W_MUMIYAHARM_CLASS,             (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodWarMagic1) },               // Skill - War Magic
        { (uint)WCN.W_TOMBDUST_CLASS,               (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionStaminaOverTime1) },      // HoT - Stamina
        { (uint)WCN.W_NIFFISSHELL_CLASS,            (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionDefender1) },             // Item - Defender
        { (uint)WCN.W_NIFFISPEARL_CLASS,            (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodLifeMagic1) },              // Skill - Life Magic
        { (uint)WCN.W_OLTHOICLAWTROPHY_CLASS,       (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionCriticalDamage1) },       // Rating - Crit Damage
        { (uint)WCN.W_OLTHOIICHOR_CLASS,            (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionAcidProtection1) },       // Prot - Acid
        { (uint)WCN.W_WASPVENOM_CLASS,              (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionStaminaOverTime1) },      // HoT - Stamina
        { (uint)WCN.W_WASPWINGTRPHY_CLASS,          (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodSelf1) },                   // Attribute - Self
        { (uint)WCN.W_RATTAIL_CLASS,                (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionCriticalChance1) },       // Rating - Crit Chance
        { (uint)WCN.W_RATSALIVA_CLASS,              (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodQuickness1) },              // Attribute - Quick
        { (uint)WCN.W_REEDSHARKFANG_CLASS,          (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionSwiftKiller1) },          // Item - Swift Killer
        { (uint)WCN.W_REEDSHARKHIDETROPHY_CLASS,    (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodEndurance1) },              // Attribute - Endurance
        { (uint)WCN.W_SCLAVUSHIDETROPHY_CLASS,      (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionStaminaOverTime1) },      // HoT - Stamina
        { (uint)WCN.W_SCLAVUSTONGUE_CLASS,          (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodRun1) },                    // Skill - Run
        { (uint)WCN.W_SHRETHTOOTH_CLASS,            (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionCriticalChance1) },       // Rating - Crit Chance
        { (uint)WCN.W_SHRETHHIDETROPHY_CLASS,       (EssenceEffect.Short, Skill.Cooking, null) },                                         // Chug Stamina
        { (uint)WCN.W_OLDBONE_CLASS,                (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionPiercingProtection1) },   // Prot - Pierce
        { (uint)WCN.W_SKULLTROPHY_CLASS,             (EssenceEffect.Short, Skill.Cooking, null) },                                         // Chug Health
        { (uint)WCN.W_TUSKERPELT_CLASS,             (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionBludgeoningProtection1) },// Prot - Blunt
        { (uint)WCN.W_TUSKERTUSK_CLASS,             (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodStrength1) },               // Attribute - Strength
        { (uint)WCN.W_UNDEADLEG_CLASS,              (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionPiercingProtection1) },   // Prot - Pierce
        { (uint)WCN.W_MNEMOSYNETRPHY_CLASS,         (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodThievery1) },               // Skill - Thievery
        { (uint)WCN.W_URSUINFANGTROPHY_CLASS,       (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionBloodDrinker1) },         // Item - Blood Drinker
        { (uint)WCN.W_URSUINHIDE_CLASS,             (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionSlashingProtection1) },   // Prot - Slash
        { (uint)WCN.W_ZEFIRGOSSAMER_CLASS,          (EssenceEffect.Short, Skill.Cooking, (uint)SpellId.CookFoodWarMagic1) },               // Skill - War Magic
        { (uint)WCN.W_ZEFIRWINGTRPHY_CLASS,         (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionLightningProtection1) },  // Prot - Lightning
        { (uint)WCN.W_WISPHEARTTROPHY_CLASS,        (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodSelf1) },                   // Attribute - Self
        { (uint)WCN.W_WISPESSENCE_CLASS,            (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionManaOverTime1) },         // HoT - Mana
        { (uint)WCN.W_TUMEROKINSIGNIATROPHY_CLASS,  (EssenceEffect.Long,  Skill.Cooking, (uint)SpellId.CookFoodCoordination1) },           // Attribute - Coordination
        { (uint)WCN.W_TUMEROKSALTEDMEATS_CLASS,     (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionDefender1) },             // Item - Defender
        { (uint)WCN.W_MOARSMUCK_CLASS,              (EssenceEffect.Short, Skill.Alchemy, null) },                                         // Chug Stamina
        { (uint)WCN.W_MOARSMANHEAD_CLASS,           (EssenceEffect.Short, Skill.Alchemy, (uint)SpellId.AlchPotionHealOverTime1) },         // HoT - Health
        { (uint)WCN.W_CRYSTALIZEDFIRE_CLASS,        (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionFireProtection1) },       // Prot - Fire
        { (uint)WCN.W_CRYSTALIZEDFROST_CLASS,       (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionColdProtection1) },       // Prot - Cold
        { (uint)WCN.W_CRYSTALIZEDACID_CLASS,        (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionAcidProtection1) },       // Prot - Acid
        { (uint)WCN.W_CRYSTALIZEDLIGHTNING_CLASS,   (EssenceEffect.Long,  Skill.Alchemy, (uint)SpellId.AlchPotionLightningProtection1) },  // Prot - Lightning
    };

    private static readonly int[] DifficultyByQuality =
    [
    //  Q1  Q2  Q3  Q4  Q5   Q6   Q7   Q8   Q9  Q10
        0, 20, 40, 60, 80, 100, 130, 160, 190, 220
    ];

    private static int GetDifficulty(int trophyQuality)
    {
        var index = Math.Clamp(trophyQuality, 1, DifficultyByQuality.Length) - 1;
        return DifficultyByQuality[index];
    }

    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public TrophySolvent(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public TrophySolvent(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    private static void BroadcastTrophyConversion(
        Player player,
        string trophyName,
        string essenceName,
        int numberOfSolventsConsumed,
        bool success
    )
    {
        // send local broadcast
        if (success)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"You successfully convert {trophyName} into {essenceName}, consuming {numberOfSolventsConsumed} Trophy Solvents.",
                    ChatMessageType.Broadcast
                ),
                8f,
                ChatMessageType.Broadcast
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"You fail to convert {trophyName}.",
                    ChatMessageType.Broadcast
                ),
                8f,
                ChatMessageType.Broadcast
            );
        }
    }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        var solventStackSize = source.StackSize ?? 1;

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

        // Check if target is a trophy
        var trophyQuality = target.TrophyQuality;
        if (trophyQuality == null || trophyQuality < 1 || trophyQuality > 10)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} is not a valid trophy item.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (target.Retained)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} is retained and cannot be altered.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // Verify the trophy has a valid essence mapping with a supported effect
        if (!TrophyEssenceMap.TryGetValue(target.WeenieClassId, out var mapEntry)
            || mapEntry.EssenceEffect == EssenceEffect.None)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} cannot be converted into an essence.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // Calculate amount of solvents needed based on trophy quality (1-10)
        // Using squared formula similar to SpellPurge's workmanship calculation
        var amountToConsume = trophyQuality.Value * trophyQuality.Value;

        if (solventStackSize < amountToConsume)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You require a stack of {amountToConsume} Trophy Solvents to convert {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var essenceName = $"Essence of {target.Name}";

        // Compute skill check up front so the dialog can show the real success chance
        var craftSkill = player.GetCreatureSkill(mapEntry.Skill);
        var difficulty = GetDifficulty(trophyQuality.Value);
        var successChance = SkillCheck.GetSkillChance((int)craftSkill.Current, difficulty);

        if (PropertyManager.GetBool("bypass_crafting_checks").Item)
        {
            successChance = 1.0;
        }

        if (!confirmed)
        {
            var showDialog = player.GetCharacterOption(CharacterOption.UseCraftingChanceOfSuccessDialog);

            var confirmationMessage = showDialog
                ? $"You determine that you have a {Math.Round(successChance * 100)} percent chance to succeed.\n\nConverting {target.NameWithMaterial} into {essenceName} will consume the trophy and {amountToConsume} Trophy Solvents. Failure will destroy both.\n\n"
                : $"Convert {target.NameWithMaterial} into {essenceName}?\n\nThis will consume the trophy and {amountToConsume} Trophy Solvents. Failure will destroy both.\n\n";

            if (!player.ConfirmationManager.EnqueueSend(new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid), confirmationMessage))
            {
                player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
                return;
            }

            if (PropertyManager.GetBool("craft_exact_msg").Item)
            {
                var exactMsg = $"You have a {successChance * 100}% chance of converting {target.NameWithMaterial} into essence.";
                player.Session.Network.EnqueueSend(new GameMessageSystemChat(exactMsg, ChatMessageType.Craft));
            }

            player.SendUseDoneEvent();
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

                // Recalculate amount to consume to ensure consistency
                var trophyQualityValue = target.TrophyQuality ?? 1;
                var finalAmountToConsume = trophyQualityValue * trophyQualityValue;

                var success = ThreadSafeRandom.Next(0.0f, 1.0f) < successChance;

                if (!success)
                {
                    player.TryConsumeFromInventoryWithNetworking(target, 1);
                    player.TryConsumeFromInventoryWithNetworking(source, finalAmountToConsume);
                    BroadcastTrophyConversion(player, target.NameWithMaterial, essenceName, finalAmountToConsume, false);

                    _log.Debug(
                        "[TROPHY_SOLVENT] {PlayerName} failed to convert {TargetName} | Chance: {Chance}",
                        player.Name,
                        target.NameWithMaterial,
                        successChance
                    );

                    Player.TryAwardCraftingXp(player, player.GetCreatureSkill(mapEntry.Skill), mapEntry.Skill, difficulty, fail: true);
                    return;
                }

                // Create the essence item
                var essence = CreateEssenceFromTrophy(target);
                if (essence == null)
                {
                    _log.Error("UseObjectOnTarget() - Failed to create essence from {Target}", target);
                    player.SendTransientError("Failed to create essence from trophy.");
                    return;
                }

                // Remove the trophy and consume solvents before adding the result
                player.TryConsumeFromInventoryWithNetworking(target, 1);
                player.TryConsumeFromInventoryWithNetworking(source, finalAmountToConsume);

                // Add essence to player's inventory
                if (!player.TryCreateInInventoryWithNetworking(essence))
                {
                    _log.Error("UseObjectOnTarget() - Failed to add essence to player inventory");
                    player.SendTransientError("Failed to add essence to inventory.");
                    essence.Destroy();
                    return;
                }

                BroadcastTrophyConversion(player, target.NameWithMaterial, essenceName, finalAmountToConsume, true);

                Player.TryAwardCraftingXp(player, player.GetCreatureSkill(mapEntry.Skill), mapEntry.Skill, difficulty);
            }
        );

        actionChain.AddAction(
            player,
            () =>
            {
                player.IsBusy = false;
            }
        );

        player.EnqueueMotion(actionChain, MotionCommand.Ready);

        actionChain.EnqueueChain();

        player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
    }

    private static WorldObject CreateEssenceFromTrophy(WorldObject trophy)
    {
        var essence = WorldObjectFactory.CreateNewWorldObject(EssenceWCID);

        if (essence == null)
        {
            _log.Error("CreateEssenceFromTrophy() - Failed to create essence with WCID {EssenceWCID}", EssenceWCID);
            return null;
        }

        // Set the essence name
        essence.Name = $"Essence of {trophy.Name}";
        essence.PluralName = $"Essences of {trophy.Name}";

        // Set target type so the essence can be used on Misc and Food items
        essence.TargetType = ItemType.Misc | ItemType.Food;

        // Transfer the trophy's icon to the essence (IconUnderlayId remains unchanged from the base essence)
        if (trophy.IconId != 0)
        {
            essence.SetProperty(PropertyDataId.Icon, trophy.IconId);
        }

        // Transfer the trophy quality
        if (trophy.TrophyQuality.HasValue)
        {
            essence.SetProperty(PropertyInt.TrophyQuality, trophy.TrophyQuality.Value);
        }

        // Transfer the trophy value
        if (trophy.Value.HasValue)
        {
            essence.SetProperty(PropertyInt.Value, trophy.Value.Value);
        }

        var qualityOffset = (trophy.TrophyQuality ?? 1) - 1;

        if (TrophyEssenceMap.TryGetValue(trophy.WeenieClassId, out var essenceData))
        {
            // Store the essence effect type and skill
            essence.SetProperty(PropertyInt.TrophyEssenceEffectType, (int)essenceData.EssenceEffect);
            essence.SetProperty(PropertyInt.TrophyEssenceSkill, (int)essenceData.Skill);

            // Set spell ID if present (base + quality offset)
            if (essenceData.BaseSpellId.HasValue && essenceData.BaseSpellId.Value != 0)
            {
                essence.SetProperty(PropertyInt.TrophyEssenceSpellId, (int)(essenceData.BaseSpellId.Value + qualityOffset));
            }

            // Auto-generate Use description based on skill
            var hasCook = essenceData.Skill == Skill.Cooking;
            var hasAlch = essenceData.Skill == Skill.Alchemy;

            var useDescription = (hasCook, hasAlch) switch
            {
                (true, true) => "This item is used in Cooking and Alchemy.",
                (true, false) => "This item is used in Cooking.",
                (false, true) => "This item is used in Alchemy.",
                _ => ""
            };

            if (!string.IsNullOrEmpty(useDescription))
            {
                essence.SetProperty(PropertyString.Use, useDescription);
            }
        }

        return essence;
    }
}
