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
using Serilog;
using MotionCommand = ACE.Entity.Enum.MotionCommand;

namespace ACE.Server.WorldObjects;

public class TrophyEssence : WorldObject
{
    private static readonly ILogger _log = Log.ForContext(typeof(TrophyEssence));

    private static readonly HashSet<uint> ValidCookTargets =
    [
        1053937, 1053938, 1053939,
        1053941, 1053942, 1053943,
        1053945, 1053946, 1053947,
        1053949, 1053950, 1053951
    ];

    private static readonly HashSet<uint> ValidAlchTargets =
    [
        1053957, 1053958, 1053959,
        1053961, 1053962, 1053963,
        1053965, 1053966, 1053967,
        1053969, 1053970, 1053971
    ];

    private static readonly int[] DifficultyByQuality =
    [
    //  Q1  Q2  Q3  Q4  Q5   Q6   Q7   Q8   Q9  Q10
        0, 20, 40, 60, 80, 100, 130, 160, 190, 220
    ];

    // Output WCID ranges: spell foods 1054600-1054731 (12 bases × 11 spells),
    // spell potions 1054732-1054959 (12 bases × 19 spells), Sudden foods 1054960+ (12 bases × 3 vitals).
    private const uint FoodSpellOutputBase   = 1054600u;
    private const uint PotionSpellOutputBase = 1054732u;
    private const uint FoodSuddenOutputBase  = 1054960u;

    private const int CookSpellCount = 11;
    private const int AlchSpellCount = 19;
    private const int VitalCount     = 3;

    /// <summary>
    /// Maps each cooking target WCID to its row index (0-11) in the food variant WCID table.
    /// </summary>
    private static readonly Dictionary<uint, int> TargetFoodBaseIndex = new()
    {
        { 1053937, 0  }, { 1053938, 1  }, { 1053939, 2  }, // Gristly
        { 1053941, 3  }, { 1053942, 4  }, { 1053943, 5  }, // Standard
        { 1053945, 6  }, { 1053946, 7  }, { 1053947, 8  }, // Tender
        { 1053949, 9  }, { 1053950, 10 }, { 1053951, 11 }, // Choice
    };

    /// <summary>
    /// Maps each alchemy target WCID to its row index (0-11) in the potion variant WCID table,
    /// ordered Health/Stamina/Mana per tier to match the sequence starting at 1054732.
    /// </summary>
    private static readonly Dictionary<uint, int> TargetPotionBaseIndex = new()
    {
        { 1053958, 0  }, { 1053959, 1  }, { 1053957, 2  }, // Draught
        { 1053962, 3  }, { 1053963, 4  }, { 1053961, 5  }, // Potion
        { 1053966, 6  }, { 1053967, 7  }, { 1053965, 8  }, // Tonic
        { 1053970, 9  }, { 1053971, 10 }, { 1053969, 11 }, // Tincture
    };

    /// <summary>
    /// Maps the Q1 base spell ID for each cooking buff to its column index (0-10).
    /// </summary>
    private static readonly Dictionary<uint, int> CookBaseSpellIndex = new()
    {
        { (uint)SpellId.CookFoodStrength1,     0  },
        { (uint)SpellId.CookFoodEndurance1,    1  },
        { (uint)SpellId.CookFoodCoordination1, 2  },
        { (uint)SpellId.CookFoodQuickness1,    3  },
        { (uint)SpellId.CookFoodFocus1,        4  },
        { (uint)SpellId.CookFoodSelf1,         5  },
        { (uint)SpellId.CookFoodWarMagic1,     6  },
        { (uint)SpellId.CookFoodLifeMagic1,    7  },
        { (uint)SpellId.CookFoodRun1,          8  },
        { (uint)SpellId.CookFoodJump1,         9  },
        { (uint)SpellId.CookFoodThievery1,     10 },
    };

    private static readonly Dictionary<uint, int> CookLongSpellIndex = new()
    {
        { (uint)SpellId.CookFoodStrength1,     0  },
        { (uint)SpellId.CookFoodEndurance1,    1  },
        { (uint)SpellId.CookFoodCoordination1, 2  },
        { (uint)SpellId.CookFoodQuickness1,    3  },
        { (uint)SpellId.CookFoodFocus1,        4  },
        { (uint)SpellId.CookFoodSelf1,         5  },
    };

    /// <summary>
    /// Maps the Q1 base spell ID for each alchemy buff to its column index (0-18).
    /// </summary>
    private static readonly Dictionary<uint, int> AlchBaseSpellIndex = new()
    {
        { (uint)SpellId.AlchPotionArmorProtection1,       0  },
        { (uint)SpellId.AlchPotionWardProtection1,        1  },
        { (uint)SpellId.AlchPotionHealOverTime1,          2  },
        { (uint)SpellId.AlchPotionStaminaOverTime1,       3  },
        { (uint)SpellId.AlchPotionManaOverTime1,          4  },
        { (uint)SpellId.AlchPotionBloodDrinker1,          5  },
        { (uint)SpellId.AlchPotionSpiritDrinker1,         6  },
        { (uint)SpellId.AlchPotionHeartSeeker1,           7  },
        { (uint)SpellId.AlchPotionSwiftKiller1,           8  },
        { (uint)SpellId.AlchPotionDefender1,              9  },
        { (uint)SpellId.AlchPotionCriticalChance1,        10 },
        { (uint)SpellId.AlchPotionCriticalDamage1,        11 },
        { (uint)SpellId.AlchPotionSlashingProtection1,    12 },
        { (uint)SpellId.AlchPotionPiercingProtection1,    13 },
        { (uint)SpellId.AlchPotionBludgeoningProtection1, 14 },
        { (uint)SpellId.AlchPotionAcidProtection1,        15 },
        { (uint)SpellId.AlchPotionFireProtection1,        16 },
        { (uint)SpellId.AlchPotionColdProtection1,        17 },
        { (uint)SpellId.AlchPotionLightningProtection1,   18 },
    };

    private static readonly Dictionary<uint, int> AlchLongSpellIndex = new()
    {
        { (uint)SpellId.AlchPotionArmorProtection1,       0  },
        { (uint)SpellId.AlchPotionWardProtection1,        1  },
        { (uint)SpellId.AlchPotionSlashingProtection1,    2 },
        { (uint)SpellId.AlchPotionPiercingProtection1,    3 },
        { (uint)SpellId.AlchPotionBludgeoningProtection1, 4 },
        { (uint)SpellId.AlchPotionAcidProtection1,        5 },
        { (uint)SpellId.AlchPotionFireProtection1,        6 },
        { (uint)SpellId.AlchPotionColdProtection1,        7 },
        { (uint)SpellId.AlchPotionLightningProtection1,   8 },
    };

    private static readonly Dictionary<PropertyAttribute2nd, int> VitalToIndex = new()
    {
        { PropertyAttribute2nd.Health,  0 },
        { PropertyAttribute2nd.Stamina, 1 },
        { PropertyAttribute2nd.Mana,    2 },
    };

    private const uint TrophyEssenceWcid = 1053982u;

    /// <summary>
    /// Returns true if the WorldObject is a Trophy Essence item (WCID 1053982).
    /// Used to route use-on-target actions without going through RecipeManager.
    /// </summary>
    public static bool IsTrophyEssence(WorldObject wo) => wo.WeenieClassId == TrophyEssenceWcid;

    /// <summary>
    /// All SpellIds (qualities 1-10) produced by CookFood essences, plus base food spells (qualities 1-4).
    /// Used to identify and dispel previously active Long food buffs.
    /// </summary>
    public static readonly HashSet<uint> AllCookFoodSpellIds = BuildCookFoodLongSpellIds();

    /// <summary>
    /// All SpellIds (qualities 1-10) produced by AlchPotion essences, plus base regeneration spells (qualities 1-4).
    /// Used to identify and dispel previously active Long potion buffs.
    /// </summary>
    public static readonly HashSet<uint> AllAlchPotionSpellIds = BuildAlchPotionLongSpellIds();

    private static HashSet<uint> BuildSpellIdSet(IEnumerable<uint> baseSpellIds)
    {
        var set = new HashSet<uint>();
        foreach (var baseId in baseSpellIds)
        {
            for (var q = 0u; q < 10u; q++)
            {
                set.Add(baseId + q);
            }
        }

        return set;
    }

    private static HashSet<uint> BuildCookFoodLongSpellIds()
    {
        var set = BuildSpellIdSet(CookLongSpellIndex.Keys);
        for (var q = 0u; q < 4u; q++)
        {
            set.Add((uint)SpellId.CookFoodMaxHealth1 + q);
            set.Add((uint)SpellId.CookFoodMaxStamina1 + q);
            set.Add((uint)SpellId.CookFoodMaxMana1 + q);
        }
        return set;
    }

    private static HashSet<uint> BuildAlchPotionLongSpellIds()
    {
        var set = BuildSpellIdSet(AlchLongSpellIndex.Keys);
        for (var q = 0u; q < 4u; q++)
        {
            set.Add((uint)SpellId.AlchPotionHealthRegeneration1 + q);
            set.Add((uint)SpellId.AlchPotionStaminaRegeneration1 + q);
            set.Add((uint)SpellId.AlchPotionManaRegeneration1 + q);
        }
        return set;
    }

    /// <summary>
    /// Maps target WCID to the vital it restores for Chug essences.
    /// </summary>
    private static readonly Dictionary<uint, PropertyAttribute2nd> ChugBoosterEnum = new()
    {
        // Cooking targets
        { 1053937, PropertyAttribute2nd.Health },
        { 1053938, PropertyAttribute2nd.Stamina },
        { 1053939, PropertyAttribute2nd.Mana },
        { 1053941, PropertyAttribute2nd.Health },
        { 1053942, PropertyAttribute2nd.Stamina },
        { 1053943, PropertyAttribute2nd.Mana },
        { 1053945, PropertyAttribute2nd.Health },
        { 1053946, PropertyAttribute2nd.Stamina },
        { 1053947, PropertyAttribute2nd.Mana },
        { 1053949, PropertyAttribute2nd.Health },
        { 1053950, PropertyAttribute2nd.Stamina },
        { 1053951, PropertyAttribute2nd.Mana },

        // Alchemy targets
        { 1053957, PropertyAttribute2nd.Mana },
        { 1053958, PropertyAttribute2nd.Health },
        { 1053959, PropertyAttribute2nd.Stamina },
        { 1053961, PropertyAttribute2nd.Mana },
        { 1053962, PropertyAttribute2nd.Health },
        { 1053963, PropertyAttribute2nd.Stamina },
        { 1053965, PropertyAttribute2nd.Mana },
        { 1053966, PropertyAttribute2nd.Health },
        { 1053967, PropertyAttribute2nd.Stamina },
        { 1053969, PropertyAttribute2nd.Mana },
        { 1053970, PropertyAttribute2nd.Health },
        { 1053971, PropertyAttribute2nd.Stamina },
    };

    /// <summary>
    /// Returns the boost value for a Chug essence based on the vital type and quality.
    /// Health: quality * 20, Stamina/Mana: quality * 30.
    /// Doubled for essences without a spell effect.
    /// </summary>
    private static int GetChugBoostValue(PropertyAttribute2nd vital, int trophyQuality) => vital switch
    {
        PropertyAttribute2nd.Health => trophyQuality * 20,
        _ => trophyQuality * 30,
    };

    private const int CookingShortSharedCooldown = 10076;
    private const int AlchemyShortSharedCooldown = 10077;
    private const double ShortCooldownDuration = 300;

    // EssenceEffect values matching TrophySolvent.EssenceEffect enum
    private const int EffectLong = 1;
    private const int EffectShort = 2;

    /// <summary>
    /// Maps target WCID to its consumable tier (1-4).
    /// </summary>
    private static readonly Dictionary<uint, int> TargetTier = new()
    {
        // Cooking targets
        { 1053937, 1 }, { 1053938, 1 }, { 1053939, 1 },
        { 1053941, 2 }, { 1053942, 2 }, { 1053943, 2 },
        { 1053945, 3 }, { 1053946, 3 }, { 1053947, 3 },
        { 1053949, 4 }, { 1053950, 4 }, { 1053951, 4 },

        // Alchemy targets
        { 1053957, 1 }, { 1053958, 1 }, { 1053959, 1 },
        { 1053961, 2 }, { 1053962, 2 }, { 1053963, 2 },
        { 1053965, 3 }, { 1053966, 3 }, { 1053967, 3 },
        { 1053969, 4 }, { 1053970, 4 }, { 1053971, 4 },
    };

    /// <summary>
    /// Returns the minimum consumable tier required for a given essence quality.
    /// Q1-2 = any tier, Q3-4 = tier 2+, Q5-6 = tier 3+, Q7+ = tier 4 only.
    /// </summary>
    private static int GetMinimumTier(int trophyQuality) => trophyQuality switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 6 => 3,
        _    => 4,
    };

    private static uint? GetOutputFoodWcid(uint targetWcid, uint baseSpellId)
    {
        if (!TargetFoodBaseIndex.TryGetValue(targetWcid, out var baseIndex))
        {
            return null;
        }

        if (!CookBaseSpellIndex.TryGetValue(baseSpellId, out var spellIndex))
        {
            return null;
        }

        return FoodSpellOutputBase + (uint)(baseIndex * CookSpellCount + spellIndex);
    }

    private static uint? GetOutputPotionWcid(uint targetWcid, uint baseSpellId)
    {
        if (!TargetPotionBaseIndex.TryGetValue(targetWcid, out var baseIndex))
        {
            return null;
        }

        if (!AlchBaseSpellIndex.TryGetValue(baseSpellId, out var spellIndex))
        {
            return null;
        }

        return PotionSpellOutputBase + (uint)(baseIndex * AlchSpellCount + spellIndex);
    }

    private static uint? GetSuddenFoodWcid(uint targetWcid, PropertyAttribute2nd vital)
    {
        if (!TargetFoodBaseIndex.TryGetValue(targetWcid, out var baseIndex))
        {
            return null;
        }

        if (!VitalToIndex.TryGetValue(vital, out var vitalIndex))
        {
            return null;
        }

        return FoodSuddenOutputBase + (uint)(baseIndex * VitalCount + vitalIndex);
    }

    private static int GetDifficulty(int trophyQuality)
    {
        var index = Math.Clamp(trophyQuality, 1, DifficultyByQuality.Length) - 1;
        return DifficultyByQuality[index];
    }



    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public TrophyEssence(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public TrophyEssence(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        HandleTrophyEssenceCrafting(player, this, target);
    }

    public static void HandleTrophyEssenceCrafting(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        var isCookTarget = ValidCookTargets.Contains(target.WeenieClassId);
        var isAlchTarget = ValidAlchTargets.Contains(target.WeenieClassId);

        if (!isCookTarget && !isAlchTarget)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} is not a valid target for this essence.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // Check if the target already has an added spell from a TrophyEssence
        if (target.SpellDID != null && target.SpellDID != 0)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} has already been enhanced with an essence.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // Validate essence quality vs target tier
        var trophyQuality = source.TrophyQuality ?? 1;
        var minimumTier = GetMinimumTier(trophyQuality);
        var targetTier = TargetTier.GetValueOrDefault(target.WeenieClassId, 1);

        if (targetTier < minimumTier)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.NameWithMaterial} is too powerful for the {target.NameWithMaterial}. It requires a stronger consumable.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var effectType = source.TrophyEssenceEffectType ?? 0;
        var isLong = effectType == EffectLong;
        var isShort = effectType == EffectShort;

        // Get the spell ID from the essence (may be null for booster-only essences)
        var spellId = source.TrophyEssenceSpellId;

        // Validate the essence's skill matches the target type
        var essenceSkill = (Skill)(source.TrophyEssenceSkill ?? 0);
        if ((isCookTarget && essenceSkill != Skill.Cooking) || (isAlchTarget && essenceSkill != Skill.Alchemy))
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.NameWithMaterial} cannot be applied to the {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (!isLong && !isShort)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.NameWithMaterial} cannot be applied to the {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var hasSpell = spellId != null && spellId != 0;

        // Determine output WCID before rolling so we can fail early on missing data.
        uint? outputWcid = null;
        var chugVital = PropertyAttribute2nd.Undef;

        if (isShort)
        {
            ChugBoosterEnum.TryGetValue(target.WeenieClassId, out chugVital);
        }

        if (hasSpell)
        {
            var baseSpellId = (uint)(spellId.Value - (trophyQuality - 1));
            outputWcid = isCookTarget
                ? GetOutputFoodWcid(target.WeenieClassId, baseSpellId)
                : GetOutputPotionWcid(target.WeenieClassId, baseSpellId);
        }
        else if (isShort && isCookTarget && chugVital != PropertyAttribute2nd.Undef)
        {
            outputWcid = GetSuddenFoodWcid(target.WeenieClassId, chugVital);
        }

        if (outputWcid == null)
        {
            player.SendTransientError($"No output item found for {source.NameWithMaterial} on {target.NameWithMaterial}.");
            player.SendUseDoneEvent();
            return;
        }

        // Skill check
        var difficulty = GetDifficulty(trophyQuality);
        var craftSkill = isCookTarget ? Skill.Cooking : Skill.Alchemy;
        var skill = player.GetCreatureSkill(craftSkill);
        var playerSkill = (int)skill.Current;
        var successChance = SkillCheck.GetSkillChance(playerSkill, difficulty);

        if (PropertyManager.GetBool("bypass_crafting_checks").Item)
        {
            successChance = 1.0;
        }

        if (!confirmed)
        {
            var showDialog = player.GetCharacterOption(CharacterOption.UseCraftingChanceOfSuccessDialog);

            var confirmationMessage = showDialog
                ? $"You determine that you have a {Math.Round(successChance * 100)} percent chance to succeed.\n\nApplying {source.NameWithMaterial} to {target.NameWithMaterial} will consume both items on failure.\n\n"
                : $"Apply {source.NameWithMaterial} to {target.NameWithMaterial}?\n\nBoth items will be destroyed on failure.\n\n";

            if (!player.ConfirmationManager.EnqueueSend(new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid), confirmationMessage))
            {
                player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
                return;
            }

            if (PropertyManager.GetBool("craft_exact_msg").Item)
            {
                var exactMsg = $"You have a {successChance * 100}% chance of applying {source.NameWithMaterial} to {target.NameWithMaterial}.";
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
                var success = ThreadSafeRandom.Next(0.0f, 1.0f) < successChance;

                if (!success)
                {
                    player.TryConsumeFromInventoryWithNetworking(source, 1);
                    player.TryConsumeFromInventoryWithNetworking(target, 1);

                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You fail to apply the {source.NameWithMaterial} to the {target.NameWithMaterial}. Both items are destroyed.",
                            ChatMessageType.Craft
                        )
                    );

                    _log.Debug(
                        "[TROPHY_ESSENCE] {PlayerName} failed to apply {SourceName} to {TargetName} | Chance: {Chance}",
                        player.Name,
                        source.NameWithMaterial,
                        target.NameWithMaterial,
                        successChance
                    );

                    Player.TryAwardCraftingXp(player, player.GetCreatureSkill(craftSkill), craftSkill, difficulty, fail: true);
                    return;
                }

                // Create the output item from its pre-built WCID; icons and name are already set,
                // and Spell2 is initialized here but may be cleared or overridden by ApplyShortEffect() for short outputs.
                var mutatedItem = WorldObjectFactory.CreateNewWorldObject(outputWcid.Value);
                if (mutatedItem == null)
                {
                    player.SendTransientError($"Failed to create {target.NameWithMaterial}.");
                    return;
                }

                mutatedItem.SetStackSize(1);

                if (hasSpell)
                {
                    ApplySpellEffect(mutatedItem, spellId.Value);
                }

                if (isShort)
                {
                    ApplyShortEffect(mutatedItem, trophyQuality, isCookTarget, hasSpell, chugVital);
                }

                player.TryConsumeFromInventoryWithNetworking(target, 1);
                player.TryConsumeFromInventoryWithNetworking(source, 1);

                if (!player.TryCreateInInventoryWithNetworking(mutatedItem))
                {
                    player.SendTransientError($"Failed to add {mutatedItem.NameWithMaterial} to inventory.");
                    mutatedItem.Destroy();
                    return;
                }

                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You successfully apply the {source.NameWithMaterial} to the {mutatedItem.NameWithMaterial}.",
                        ChatMessageType.Craft
                    )
                );

                _log.Debug(
                    "[TROPHY_ESSENCE] {PlayerName} applied {SourceName} to {TargetName} | Effect: {Effect} | Chance: {Chance}",
                    player.Name,
                    source.NameWithMaterial,
                    mutatedItem.NameWithMaterial,
                    isLong ? "Long" : "Short",
                    successChance
                );

                Player.TryAwardCraftingXp(player, player.GetCreatureSkill(craftSkill), craftSkill, difficulty);
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

    private static void ApplySpellEffect(WorldObject target, int spellId)
    {
        target.SetProperty(PropertyDataId.Spell, (uint)spellId);
        target.UiEffects = ACE.Entity.Enum.UiEffects.Magical;
    }

    private static void ApplyShortEffect(WorldObject target, int trophyQuality, bool isCookTarget, bool hasSpell, PropertyAttribute2nd chugVital)
    {
        target.CooldownDuration = ShortCooldownDuration;
        target.CooldownId = isCookTarget ? CookingShortSharedCooldown : AlchemyShortSharedCooldown;

        if (chugVital != PropertyAttribute2nd.Undef)
        {
            target.BoosterEnum = chugVital;
            target.BoostValue = GetChugBoostValue(chugVital, trophyQuality) * (hasSpell ? 1 : 2);

            var verb = target.ItemType == ItemType.Food ? "Eat" : "Drink";
            var vitalName = chugVital switch
            {
                PropertyAttribute2nd.Health  => "Health",
                PropertyAttribute2nd.Stamina => "Stamina",
                PropertyAttribute2nd.Mana    => "Mana",
                _                            => chugVital.ToString(),
            };

            var newFirstSentence = $"{verb} this to restore {vitalName}.";
            var existingUse = target.Use;
            if (string.IsNullOrEmpty(existingUse))
            {
                target.Use = newFirstSentence;
            }
            else
            {
                var dotIndex = existingUse.IndexOf('.');
                target.Use = dotIndex >= 0
                    ? newFirstSentence + existingUse[(dotIndex + 1)..]
                    : newFirstSentence;
            }
        }

        target.Spell2 = null;
    }
}
