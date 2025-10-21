using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories.Tables;
using ACE.Server.Network.GameMessages.Messages;
using DamageType = ACE.Entity.Enum.DamageType;

namespace ACE.Server.WorldObjects;

partial class Jewel
{
    public static float GetJewelEffectMod(Player player, PropertyInt propertyInt, string rampQuestString = "", bool secondary = false, bool alternate = false)
    {
        if (player is null)
        {
            return 0.0f;
        }

        var rating = player.GetEquippedAndActivatedItemRatingSum(propertyInt);

        if (rating <= 0)
        {
            return 0.0f;
        }

        var jewelEffectInfo = JewelEffectInfoMain[JewelTypeToMaterial[propertyInt]];

        if (alternate)
        {
            jewelEffectInfo = JewelEffectInfoAlternate[JewelTypeToMaterial[propertyInt]];
        }

        var baseMod = (float)jewelEffectInfo.BasePrimary / 100;
        var bonusPerRating = jewelEffectInfo.BonusPrimary / 100;

        if (secondary)
        {
            baseMod = (float)jewelEffectInfo.BaseSecondary / 100;
            bonusPerRating = jewelEffectInfo.BonusSecondary / 100;
        }

        var rampPercentage = rampQuestString is "" ? 1.0f : 0.0f;

        if (player.QuestManager.HasQuest($"{player.Name},{rampQuestString}"))
        {
            rampPercentage = Math.Min((float)player.QuestManager.GetCurrentSolves($"{player.Name},{rampQuestString}") / 100, 1.0f);
        }

        // Console.WriteLine($"\nJewel Mod:\n" +
        //                   $" -Property: {propertyInt}\n" +
        //                   $" -Rating: {rating}\n" +
        //                   $" -baseMod: {baseMod}\n" +
        //                   $" -bonusPerRating {bonusPerRating}\n" +
        //                   $" -bonus: {bonusPerRating * rating}\n" +
        //                   $" -rampPercentage: {rampPercentage * 100}%\n" +
        //                   $" -TOTAL: {(baseMod + bonusPerRating * rating) * rampPercentage}");

        return (baseMod + bonusPerRating * rating) * rampPercentage;
    }

    // Caster and Physical Overlapping Bonuses
    public static void HandlePlayerAttackerBonuses(Player playerAttacker, Creature defender, float damage, DamageType damageType)
    {
        CheckForRatingLifeOnHit(playerAttacker, defender, damage);
        CheckForRatingStaminaOnHit(playerAttacker, defender, damage);
        CheckForRatingManaOnHit(playerAttacker, defender, damage);
        CheckForRatingSelfHarm(playerAttacker, damage);
        CheckForRatingElementalHotspot(playerAttacker, defender, damageType);
        CheckForRatingMagicFindStamps(playerAttacker, defender, damage);
        CheckForRatingProsperityFindStamps(playerAttacker, defender, damage);
    }

    public static void HandlePlayerDefenderBonuses(Player playerDefender, Creature attacker, float damage)
    {
        CheckForRatingHealthToStamina(playerDefender, attacker, damage);
        CheckForRatingHealthToMana(playerDefender, attacker, damage);
    }

    public static void HandlePlayerHealerBonuses(Player playerHealer, Creature targetPlayer)
    {
        CheckForRatingHealBubbleHotspot(playerHealer, targetPlayer);
    }

    public static void HandleMeleeMissileAttackerRampingQuestStamps(Player playerAttacker, Creature defender, DamageType damageType)
    {
        var scaledStamps = GetMeleeMissileScaledStamps(playerAttacker);

        AddRatingQuestStamps(playerAttacker, defender, PropertyInt.GearFamiliarity, "Familiarity", scaledStamps);

        switch (damageType)
        {
            case DamageType.Bludgeon:
                AddRatingQuestStamps(playerAttacker, defender, PropertyInt.GearBludgeon, "Bludgeon", scaledStamps);
                break;
            case DamageType.Pierce:
                AddRatingQuestStamps(playerAttacker, defender, PropertyInt.GearPierce, "Pierce", scaledStamps);
                break;
        }
    }

    public static void HandleMeleeMissileDefenderRampingQuestStamps(Player playerDefender)
    {
        AddRatingQuestStamps(playerDefender, null, PropertyInt.GearHardenedDefense, "Hardened Defense", 10);
        AddRatingQuestStamps(playerDefender, null, PropertyInt.GearBravado, "Bravado", 10);
    }

    public static void HandleCasterAttackerRampingQuestStamps(Player sourcePlayer, Creature targetCreature, Spell spell, ProjectileSpellType projectileSpellTyper)
    {
        var scaledStamps = GetCasterScaledStamps(spell.Level, projectileSpellTyper);

        AddRatingQuestStamps(sourcePlayer, targetCreature, PropertyInt.GearFamiliarity, "Familiarity", scaledStamps);
        AddRatingQuestStamps(sourcePlayer, targetCreature, PropertyInt.GearWardPen, "WardPen", scaledStamps);
        AddRatingQuestStamps(sourcePlayer, targetCreature, PropertyInt.GearElementalist, "Elementalist", scaledStamps, true);

        switch (spell.DamageType)
        {
            case DamageType.Bludgeon:
                AddRatingQuestStamps(sourcePlayer, targetCreature, PropertyInt.GearBludgeon, "Bludgeon", scaledStamps, true);
                break;
            case DamageType.Pierce:
                AddRatingQuestStamps(sourcePlayer, targetCreature, PropertyInt.GearPierce, "Pierce", scaledStamps, true);
                break;
        }
    }

    public static void HandleCasterDefenderRampingQuestStamps(Player targetPlayer, Creature sourceCreature)
    {
        AddRatingQuestStamps(targetPlayer, sourceCreature, PropertyInt.GearNullification, "Nullification", 10, true);
    }

    private static int GetMeleeMissileScaledStamps(Player playerAttacker)
    {
        const int baseStamps = 10;

        var powerBarScalar = playerAttacker.GetPowerAccuracyBar() * 2;

        var equippedWeapon = playerAttacker.GetCombatStance() is MotionStance.DualWieldCombat
            ? playerAttacker.GetEquippedMeleeWeapon()
            : playerAttacker.GetEquippedWeapon();

        var weaponAnimationLength = WeaponAnimationLength.GetWeaponAnimLength(equippedWeapon);
        var weaponTime = equippedWeapon is null ? 100 : equippedWeapon.WeaponTime ?? 100;
        var attacksPerSecondScalar = 1 / (weaponAnimationLength / (1.0f + (1 - (weaponTime / 100.0))));

        return Convert.ToInt32(baseStamps * powerBarScalar * attacksPerSecondScalar);
    }

    private static int GetCasterScaledStamps(uint spellLevel, ProjectileSpellType projectileSpellType)
    {
        const int baseStamps = 10;

        var castAnimationLengthScalar = WeaponAnimationLength.GetSpellCastAnimationLength(projectileSpellType, spellLevel);

        var spellTypeScalar = projectileSpellType switch
        {
            ProjectileSpellType.Blast or ProjectileSpellType.Volley => 0.33f,
            ProjectileSpellType.Ring or ProjectileSpellType.Wall => 0.25f,
            _ => 1.0f
        };

        return Convert.ToInt32(baseStamps * castAnimationLengthScalar * spellTypeScalar);
    }

    private static void AddRatingQuestStamps(Player sourcePlayer, Creature targetCreature, PropertyInt propertyInt, string questString, int amount, bool questManagerOfPlayer = false)
    {
        if (sourcePlayer is null || targetCreature is null)
        {
            return;
        }

        if (sourcePlayer.GetEquippedAndActivatedItemRatingSum(propertyInt) <= 0)
        {
            return;
        }

        var questTarget = questManagerOfPlayer ? sourcePlayer : targetCreature;

        if (questTarget.QuestManager.HasQuest($"{sourcePlayer.Name},{questString}"))
        {
            if (questTarget.QuestManager.GetCurrentSolves($"{sourcePlayer.Name},{questString}") < 100)
            {
                questTarget.QuestManager.Increment($"{sourcePlayer.Name},{questString}", amount);
            }
        }
        else
        {
            questTarget.QuestManager.Stamp($"{sourcePlayer.Name},{questString}");
            questTarget.QuestManager.Increment($"{sourcePlayer.Name},{questString}", amount);
        }
    }

    public static float HandleElementalBonuses(Player playerAttacker, DamageType damageType)
    {
        var jewelElemental = 1.0f;

        if (playerAttacker == null)
        {
            return jewelElemental;
        }

        jewelElemental = damageType switch
        {
            DamageType.Acid => 1.0f + GetJewelEffectMod(playerAttacker, PropertyInt.GearAcid),
            DamageType.Fire => 1.0f + GetJewelEffectMod(playerAttacker, PropertyInt.GearFire),
            DamageType.Cold => 1.0f + GetJewelEffectMod(playerAttacker, PropertyInt.GearFrost),
            DamageType.Electric => 1.0f + GetJewelEffectMod(playerAttacker, PropertyInt.GearLightning),
            _ => jewelElemental
        };

        return jewelElemental;
    }

    /// <summary>
    /// RATING - Pyreal Find: Adds quest stamps for prosperity find.
    /// (JEWEL - Green Jade)
    /// </summary>
    private static void CheckForRatingProsperityFindStamps(Player playerAttacker, Creature defender, float damage)
    {
        var pyrealFind = GetJewelEffectMod(playerAttacker, PropertyInt.GearPyrealFind);

        defender.QuestManager.Stamp($"{playerAttacker.Name}/Prosperity/{pyrealFind}/{damage}");
    }

    /// <summary>
    /// RATING - Magic Find: Adds quest stamps for magic find.
    /// (JEWEL - Sapphire)
    /// </summary>
    private static void CheckForRatingMagicFindStamps(Player playerAttacker, Creature defender, float damage)
    {
        var magicFind = GetJewelEffectMod(playerAttacker, PropertyInt.GearMagicFind);

        defender.QuestManager.Stamp($"{playerAttacker.Name}/MagicFind/{magicFind}/{damage}");
    }

    /// <summary>
    /// RATING - Fire/Frost/Acid/Lightning: Chance to generate elemental hotspot on hit.
    /// (JEWEL - Aquamarine, Emerald, Jet, Red Garnet)
    /// </summary>
    private static void CheckForRatingElementalHotspot(Player playerAttacker, Creature defender, DamageType damageType)
    {
        if (playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearFire) <= 0
            && playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearFrost) <= 0
            && playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearAcid) <= 0
            && playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearLightning) <= 0)
        {
            return;
        }

        if (playerAttacker.Level == null)
        {
            return;
        }

        var tier = Math.Round((float)playerAttacker.Level / 10);
        Hotspot.TryGenHotspot(playerAttacker, defender, (int)tier, damageType);
    }

    /// <summary>
    /// RATING - Healing: Chance to generate heal bubble hotspot on hit.
    /// (JEWEL - )
    /// </summary>
    private static void CheckForRatingHealBubbleHotspot(Player playerHealer, Creature defender)
    {
        if (playerHealer.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearHealBubble) <= 0)
        {
            return;
        }

        if (playerHealer.Level == null)
        {
            return;
        }

        var tier = Math.Round((float)playerHealer.Level / 10);
        Hotspot.TryGenHotspot(playerHealer, defender, (int)tier, DamageType.Health);
    }

    /// <summary>
    /// RATING - Self Harm: 10% chance to damage self when dealing damage. (Also grants bonus damage from a different function)
    /// (JEWEL - Hematite)
    /// </summary>
    private static void CheckForRatingSelfHarm(Player playerAttacker, float damage)
    {
        var selfHarmMod = GetJewelEffectMod(playerAttacker, PropertyInt.GearSelfHarm);

        if (selfHarmMod <= 0)
        {
            return;
        }

        if (damage is <= 0 or > uint.MaxValue)
        {
            return;
        }

        const float selfHarmChance = 0.1f;

        if (ThreadSafeRandom.Next(0.0f, 1.0f) > selfHarmChance)
        {
            return;
        }

        var selfHarmAmount = Convert.ToInt32(damage * selfHarmMod);

        playerAttacker.UpdateVitalDelta(playerAttacker.Health, -selfHarmAmount);
        playerAttacker.DamageHistory.Add(playerAttacker, DamageType.Health, (uint)selfHarmAmount);

        var message = $"In a blood frenzy, you deal {selfHarmAmount} damage to yourself!";
        playerAttacker.Session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.CombatEnemy));

        if (!playerAttacker.IsDead)
        {
            return;
        }

        var lastDamager = playerAttacker.DamageHistory.LastDamager;
        playerAttacker.OnDeath(lastDamager, DamageType.Health);
        playerAttacker.Die();
    }

    /// <summary>
    /// RATING - Manasteal: Gain mana on hit
    /// (JEWEL - Opal)
    /// </summary>
    private static void CheckForRatingManaOnHit(Player playerAttacker, Creature defender, float damage)
    {
        var absDamage = Math.Abs(damage);

        if (absDamage is <= 0 or > uint.MaxValue)
        {
            return;
        }

        var chance = GetJewelEffectMod(playerAttacker, PropertyInt.GearManasteal);

        if (playerAttacker == defender || ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        var restoreAmount = (uint)Math.Round(absDamage * 0.1f);

        playerAttacker.UpdateVitalDelta(playerAttacker.Mana, restoreAmount);
        playerAttacker.DamageHistory.OnHeal(restoreAmount);
        playerAttacker.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Mana Leech! Your attack restores {restoreAmount} mana.",
                ChatMessageType.Broadcast
            )
        );
    }

    /// <summary>
    /// RATING - Staminasteal: Gain stamina on hit
    /// (JEWEL - Citrine)
    /// </summary>
    private static void CheckForRatingStaminaOnHit(Player playerAttacker, Creature defender, float damage)
    {
        if (damage is <= 0 or > uint.MaxValue)
        {
            return;
        }

        var chance = GetJewelEffectMod(playerAttacker, PropertyInt.GearStaminasteal);

        if (playerAttacker == defender || ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        var restoreAmount = (uint)Math.Round(damage * 0.1f);

        playerAttacker.UpdateVitalDelta(playerAttacker.Stamina, restoreAmount);
        playerAttacker.DamageHistory.OnHeal(restoreAmount);
        playerAttacker.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Mana Leech! Your attack restores {restoreAmount} stamina.",
                ChatMessageType.Broadcast
            )
        );
    }

    /// <summary>
    /// RATING - Lifesteal: Gain life on hit
    /// (JEWEL - Bloodstone)
    /// </summary>
    private static void CheckForRatingLifeOnHit(Player playerAttacker, Creature defender, float damage)
    {
        if (damage is <= 0 or > uint.MaxValue)
        {
            return;
        }

        var chance = GetJewelEffectMod(playerAttacker, PropertyInt.GearLifesteal);

        if (playerAttacker == defender || ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        var healAmount = (uint)Math.Round(damage * 0.1f);

        playerAttacker.UpdateVitalDelta(playerAttacker.Health, healAmount);
        playerAttacker.DamageHistory.OnHeal(healAmount);
        playerAttacker.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Life Steal! Your attack restores {healAmount} health.",
                ChatMessageType.Broadcast
            )
        );
    }

    /// <summary>
    /// RATING - Health to Mana: Chance to gain mana on taking damage.
    /// (JEWEL - Lapis Lazuli)
    /// </summary>
    public static void CheckForRatingHealthToMana(Player playerDefender, Creature attacker, float damage)
    {
        if (damage is <= 0 or > uint.MaxValue)
        {
            return;
        }

        var chance = GetJewelEffectMod(playerDefender, PropertyInt.GearHealthToMana, "", false, true);

        if (playerDefender == attacker || ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        var manaAmount = Convert.ToUInt32(damage);
        playerDefender.UpdateVitalDelta(playerDefender.Mana, manaAmount);
        playerDefender.DamageHistory.OnHeal(manaAmount);
        playerDefender.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Austere Anchorite restores {manaAmount} points of Mana!",
                ChatMessageType.Broadcast
            )
        );
    }

    /// <summary>
    /// RATING - Health to Stamina: Chance to gain stamina on taking damage.
    /// (JEWEL - Amber)
    /// </summary>
    public static void CheckForRatingHealthToStamina(Player playerDefender, Creature attacker, float damage)
    {
        if (damage is <= 0 or > uint.MaxValue)
        {
            return;
        }

        var chance = GetJewelEffectMod(playerDefender, PropertyInt.GearHealthToStamina, "", false, true);

        if (playerDefender == attacker || ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        var staminaAmount = Convert.ToUInt32(damage);

        playerDefender.UpdateVitalDelta(playerDefender.Stamina, staminaAmount);
        playerDefender.DamageHistory.OnHeal(staminaAmount);
        playerDefender.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Masochist restores {staminaAmount} points of Stamina!",
                ChatMessageType.Broadcast
            )
        );
    }

    /// <summary>
    /// RATING - Red Fury: .
    /// (JEWEL - Ruby)
    /// </summary>
    public static float GetJewelRedFury(Player playerAttacker)
    {
        if (playerAttacker is null)
        {
            return 0.0f;
        }

        if (playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearRedFury) <= 0)
        {
            return 0.0f;
        }

        var percentHealthRemaining = (float)playerAttacker.Health.Current / playerAttacker.Health.MaxValue;
        var inverseHealthRemaining = Math.Min((1.0f - percentHealthRemaining), 1.0f);
        var ratingMod = GetJewelEffectMod(playerAttacker, PropertyInt.GearRedFury);

        return inverseHealthRemaining * ratingMod;
    }

    /// <summary>
    /// RATING - Yellow Fury: .
    /// (JEWEL - ??)
    /// </summary>
    public static float GetJewelYellowFury(Player playerAttacker)
    {
        if (playerAttacker is null)
        {
            return 0.0f;
        }

        if (playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearYellowFury) <= 0)
        {
            return 0.0f;
        }

        var percentStaminaRemaining = (float)playerAttacker.Stamina.Current / playerAttacker.Stamina.MaxValue;
        var inverseHealthRemaining = Math.Min((1.0f - percentStaminaRemaining), 1.0f);
        var ratingMod = GetJewelEffectMod(playerAttacker, PropertyInt.GearYellowFury);

        return inverseHealthRemaining * ratingMod;
    }

    /// <summary>
    /// RATING - Blue Fury: .
    /// (JEWEL - ??)
    /// </summary>
    public static float GetJewelBlueFury(Player playerAttacker)
    {
        if (playerAttacker is null)
        {
            return 0.0f;
        }

        if (playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearBlueFury) <= 0)
        {
            return 0.0f;
        }

        var percentManaRemaining = (float)playerAttacker.Mana.Current / playerAttacker.Mana.MaxValue;
        var inverseHealthRemaining = Math.Min((1.0f - percentManaRemaining), 1.0f);
        var ratingMod = GetJewelEffectMod(playerAttacker, PropertyInt.GearBlueFury);

        return inverseHealthRemaining * ratingMod;
    }

    // 0 - prepended quality, 1 - gemstone type, 2 - appended name, 3 - property type, 4 - amount of property, 5 - original gem workmanship
    public static string GetJewelDescription(WorldObject jewel)
    {
        var quality = jewel.JewelQuality ?? 1;
        var materialType = jewel.JewelMaterialType;

        // legacy support
        if (jewel.JewelSocket1 is not null and not "Empty")
        {
            var jewelString = jewel.JewelSocket1.Split('/');

            if (StringToMaterialType.TryGetValue(jewelString[1], out var jewelMaterial))
            {
                materialType = jewelMaterial;
                jewel.JewelMaterialType = jewelMaterial;
            }

            if (JewelQualityStringToValue.TryGetValue(jewelString[0], out var jewelQuality))
            {
                quality = jewelQuality;
                jewel.JewelQuality = jewelQuality;
            }

            jewel.JewelSocket1 = null;
        }

        if (materialType is null)
        {
            return "";
        }

        var name = "";
        var equipmentType = "";
        var baseRating = 0;
        var bonusPerQuality = 0.0f;
        var baseRatingSecondary = 0;
        var bonusPerQualitySecondary = 0.0f;

        if (JewelEffectInfoMain.TryGetValue(materialType.Value, out var jewelEffectInfoMain))
        {
            name = jewelEffectInfoMain.Name;
            equipmentType = jewelEffectInfoMain.Slot;
            baseRating = jewelEffectInfoMain.BasePrimary;
            bonusPerQuality = jewelEffectInfoMain.BonusPrimary;
            baseRatingSecondary = jewelEffectInfoMain.BaseSecondary;
            bonusPerQualitySecondary = jewelEffectInfoMain.BonusSecondary;
        }

        var nameAlternate = "";
        var equipmentTypeAlternate = "";
        var baseRatingAlternate = 0;
        var bonusPerQualityAlternate = 0.0f;
        var baseRatingSecondaryAlternate = 0;
        var bonusPerQualitySecondaryAlternate = 0.0f;

        if (JewelEffectInfoAlternate.TryGetValue(materialType.Value, out var jewelEffectInfoAlternate))
        {
            nameAlternate = jewelEffectInfoAlternate.Name;
            equipmentTypeAlternate = jewelEffectInfoAlternate.Slot;
            baseRatingAlternate = jewelEffectInfoAlternate.BasePrimary;
            bonusPerQualityAlternate = jewelEffectInfoAlternate.BonusPrimary;
            baseRatingSecondaryAlternate = jewelEffectInfoAlternate.BaseSecondary;
            bonusPerQualitySecondaryAlternate = jewelEffectInfoAlternate.BonusSecondary;
        }

        var alternateText = nameAlternate is not "" ? $" OR in a {equipmentTypeAlternate} to gain {nameAlternate}" : "";
        var description = $"Socket this jewel in a {equipmentType} to gain {name}{alternateText}, while equipped. " +
                          $"The target must be workmanship {quality} or greater.\n\n";

        switch (materialType)
        {
            //necklace
            case ACE.Entity.Enum.MaterialType.Sunstone:
                description +=
                    $"~ {name}: Gain {baseRating}% increased experience from monster kills (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Sapphire:
                description +=
                    $"~ {name}: Gain a {baseRating}% bonus to loot quality from monster kills (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.GreenJade:
                description +=
                    $"~ {name}: Gain a {baseRating}% chance to receive an extra item from monster kills (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // ring right
            case ACE.Entity.Enum.MaterialType.Carnelian:
                description +=
                    $"~ {name}: Gain {baseRating} Strength (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the {equipmentType} can only be worn on the right finger.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Azurite:
                description +=
                    $"~ {name}: Gain {baseRating} Self (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the {equipmentType} can only be worn on the right finger.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.TigerEye:
                description +=
                    $"~ {name}: Gain {baseRating} Coordination (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the {equipmentType} can only be worn on the right finger.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // ring left
            case ACE.Entity.Enum.MaterialType.RedJade:
                description +=
                    $"~ {name}: Gain {baseRating} Focus (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the {equipmentType} can only be worn on the left finger.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.YellowTopaz:
                description +=
                    $"~ {name}: Gain {baseRating} Endurance (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the {equipmentType} can only be worn on the left finger.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Peridot:
                description +=
                    $"~ {name}: Gain {baseRating} Quickness (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the {equipmentType} can only be worn on the left finger.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // bracelet left
            case ACE.Entity.Enum.MaterialType.Agate:
                description +=
                    $"~ {name}: Gain {baseRating} increased threat from your actions (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the bracelet can only be worn on the left wrist.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.SmokeyQuartz:
                description +=
                    $"~ {name}: Gain {baseRating} reduced threat from your actions (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the bracelet can only be worn on the left wrist.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Moonstone:
                description +=
                    $"~ {name}: Gain {baseRating}% reduced mana consumed by items (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the bracelet can only be worn on the left wrist.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Malachite:
                description +=
                    $"~ {name}: Gain {baseRating}% reduced chance to burn spell components (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the bracelet can only be worn on the left wrist.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // bracelet right
            case ACE.Entity.Enum.MaterialType.Onyx:
                description +=
                    $"~ {name}: Gain {baseRating}% reduced damage taken from slashing, bludgeoning, and piercing damage types (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the bracelet can only be worn on the right wrist.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Zircon:
                description +=
                    $"~ {name}: Gain {baseRating}% reduced damage taken from acid, fire, cold, and electric damage types (+{bonusPerQuality}% per equipped rating). " +
                    $"Once socketed, the bracelet can only be worn on the right wrist.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // bracelet right or armor
            case ACE.Entity.Enum.MaterialType.Amethyst:
                description +=
                    $"~ {name}: Gain up to {baseRating}% reduced magic damage taken (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently been hit with a damaging spell. " +
                    $"Once socketed, the bracelet can only be worn on the right wrist.\n\n" +
                    $"~ {nameAlternate}: Gain +{baseRatingAlternate} Physical Defense (+{bonusPerQualityAlternate} per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, null, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Diamond:
                description +=
                    $"~ {name}: Gain up to {baseRating}% reduced physical damage taken (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently been hit with a damaging physical attack. " +
                    $"Once socketed, the bracelet can only be worn on the right wrist.\n\n" +
                    $"~ {nameAlternate}: Gain +{baseRatingAlternate} Physical Defense (+{bonusPerQualityAlternate} per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, null, nameAlternate)}\n\n";
                break;

            // shield
            case ACE.Entity.Enum.MaterialType.Turquoise:
                description +=
                    $"~ {name}: Gain {baseRating}% increased block chance (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.WhiteQuartz:
                description +=
                    $"~ {name}: Deflect {baseRating}% damage from a blocked attack back to a close-range attacker (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // weapon + shield
            case ACE.Entity.Enum.MaterialType.BlackOpal:
                description +=
                    $"~ {name}: Gain a {baseRating}% chance to evade a critical attack (+{bonusPerQuality}% per equipped rating).  " +
                    $"Your next attack after a the evade is a guaranteed critical.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.FireOpal:
                description +=
                    $"~ {name}: Gain up to {baseRating}% increased evade and resist chances, against the target you are attacking (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently hit the target.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.YellowGarnet:
                description +=
                    $"~ {name}: Gain up to {baseRating}% increased physical attack skill (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently hit the target.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // weapon or armor
            case ACE.Entity.Enum.MaterialType.Ruby:
                description +=
                    $"~ {name}: Gain up to {baseRating}% increased damage as your health approaches 0 (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Amber:
                description +=
                    $"~ {name}: Gain up to {baseRating}% increased damage as your stamina approaches 0 (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain a {baseRatingAlternate}% chance after taking damage to gain the same amount as stamina (+{bonusPerQualityAlternate} per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.LapisLazuli:
                description +=
                    $"~ {name}: Gain up to {baseRating}% increased damage as your mana approaches 0 (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain a {baseRatingAlternate}% chance after taking damage to gain the same amount as mana (+{bonusPerQualityAlternate} per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;

            // weapon only
            case ACE.Entity.Enum.MaterialType.Bloodstone:
                description +=
                    $"~ {name}: Gain a {baseRating}% chance on hit to gain health (+{bonusPerQuality}% per equipped rating). " +
                    $"Amount stolen is equal to 10% of damage dealt.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Citrine:
                description +=
                    $"~ {name}: Gain {baseRating}% chance on hit to gain stamina (+{bonusPerQuality}% per equipped rating). " +
                    $"Amount stolen is equal to 10% of damage dealt.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Opal:
                description +=
                    $"~ {name}: Gain a {baseRating}% chance on hit to gain mana (+{bonusPerQuality}% per equipped rating). " +
                    $"Amount stolen is equal to 10% of damage dealt.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Hematite:
                description +=
                    $"~ {name}: Gain {baseRating}% increased damage with all attacks (+{bonusPerQuality}% per equipped rating). " +
                    $"However, 10% of your attacks will deal the extra damage to yourself as well.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.RoseQuartz:
                description +=
                    $"~ {name}: Gain a {baseRating}% bonus to your transfer spells (+{bonusPerQuality}% per equipped rating). " +
                    $"Receive an equivalent reduction in the effectiveness of your other restoration spells.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.LavenderJade:
                description +=
                    $"~ {name}: Gain a {baseRating}% bonus to your restoration spells on others (+{bonusPerQuality}% per equipped rating). " +
                    $"Receive an equivalent reduction in the effectiveness when cast on yourself.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.GreenGarnet:
                description +=
                    $"~ {name}: Gain up to {baseRating}% increased war magic damage (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently hit the target.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Tourmaline:
                description +=
                    $"~ {name}: Gain up to {baseRating}% ward cleaving (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently hit the target.\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.WhiteJade:
                description +=
                    $"~ {name}: Gain a {baseRating}% bonus to your restoration spells (+{bonusPerQuality}% per equipped rating). " +
                    $"Also grants a {baseRatingSecondary}% chance to create a sphere of healing energy on top of your target when casting a restoration spell (+{bonusPerQualitySecondary}% per equipped rating).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Aquamarine:
                description +=
                    $"~ {name}: Gain {baseRating}% increased cold damage (+{bonusPerQuality}% per equipped rating). " +
                    $"Also grants a {baseRatingSecondary}% chance to surround your target with chilling mist (+{bonusPerQualitySecondary}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Frost Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, bonusPerQualitySecondary, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.BlackGarnet:
                description +=
                    $"~ {name}: Gain {baseRating}% piercing resistance penetration (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently hit the target.\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Piercing Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, null, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Emerald:
                description +=
                    $"~ {name}: Gain {baseRating}% increased acid damage (+{bonusPerQuality}% per equipped rating). " +
                    $"Also grants a {baseRatingSecondary}% chance to surround your target with acidic mist (+{bonusPerQualitySecondary}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Acid Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, bonusPerQualitySecondary, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.ImperialTopaz:
                description +=
                    $"~ {name}: Gain a {baseRating}% chance to cleave an additional target (+{bonusPerQuality}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Slashing Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, null, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.Jet:
                description +=
                    $"~ {name}: Gain {baseRating}% increased electric damage (+{bonusPerQuality}% per equipped rating). " +
                    $"Also grants a {baseRatingSecondary}% chance to electrify the ground beneath your target (+{bonusPerQualitySecondary}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Lightning Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, bonusPerQualitySecondary, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.RedGarnet:
                description +=
                    $"~ {name}: Gain {baseRating}% increased fire damage (+{bonusPerQuality}% per equipped rating). " +
                    $"Also grants a {baseRatingSecondary}% chance to set the ground beneath your target ablaze (+{bonusPerQualitySecondary}% per equipped rating).\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Flame Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, bonusPerQualitySecondary, nameAlternate)}\n\n";
                break;
            case ACE.Entity.Enum.MaterialType.WhiteSapphire:
                description +=
                    $"~ {name}: Gain {baseRating}% bludgeon critical damage (+{bonusPerQuality}% per equipped rating). " +
                    $"The amount builds up from 0%, based on how often you have recently hit the target.\n\n" +
                    $"~ {nameAlternate}: Gain +{Math.Round(baseRatingAlternate * 0.01f, 2)} Bludgeoning Protection to all equipped armor (+{Math.Round(bonusPerQualityAlternate * 0.01f, 2)} per equipped rating). " +
                    $"This bonus caps at a protection level rating of 1.2 (Above Average).\n\n" +
                    $"{JewelStatsDescription(baseRating, quality, bonusPerQuality, name, null, nameAlternate)}\n\n";
                break;
        }

        return description;
    }

    private static string JewelStatsDescription(int baseRating, int amount, float bonusPerQuality, string name, float? bonusPerQualitySecondary = null, string altName = "")
    {
        var secondaryBonus = bonusPerQualitySecondary != null
            ? $"\nSecondary Bonus Rating: {bonusPerQualitySecondary * amount} ({bonusPerQualitySecondary} x Quality)"
            : "";

        var altAdditionalSources = altName is "" ? "" : $" or {altName}";

        return //$"Base Rating: {baseRating}\n" +
            $"Quality: {amount} ({JewelQuality[amount]})\n" +
            $"Bonus Rating: {bonusPerQuality * amount} ({bonusPerQuality} x Quality)" +
            $"{secondaryBonus}" +
        $"\n\nAdditional sources of {name}{altAdditionalSources} will only add the bonus rating.";
    }

    public static string GetSocketDescription(MaterialType materialType, int quality)
    {
        var materialString = Jewel.MaterialTypeToString[materialType];

        return $"\n\t Socket: {materialString} ({quality})\n";
    }

    private static readonly Dictionary<MaterialType, DamageType> MaterialDamage = new()
    {
        { ACE.Entity.Enum.MaterialType.Aquamarine, DamageType.Cold },
        { ACE.Entity.Enum.MaterialType.BlackGarnet, DamageType.Pierce },
        { ACE.Entity.Enum.MaterialType.Emerald, DamageType.Acid },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, DamageType.Slash },
        { ACE.Entity.Enum.MaterialType.Jet, DamageType.Electric },
        { ACE.Entity.Enum.MaterialType.RedGarnet, DamageType.Fire },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, DamageType.Bludgeon },
    };

    private new static readonly Dictionary<int, string> JewelQuality = new()
    {
        { 1, "Scuffed" },
        { 2, "Flawed" },
        { 3, "Mediocre" },
        { 4, "Fine" },
        { 5, "Admirable" },
        { 6, "Superior" },
        { 7, "Excellent" },
        { 8, "Magnificent" },
        { 9, "Peerless" },
        { 10, "Flawless" }
    };

    public new static readonly Dictionary<string, int> JewelQualityStringToValue = new()
    {
        { "Scuffed", 1 },
        { "Flawed" , 2 },
        { "Mediocre" , 3 },
        { "Fine", 4  },
        { "Admirable", 5  },
        { "Superior", 6  },
        { "Excellent", 7  },
        { "Magnificent", 8  },
        { "Peerless", 9  },
        { "Flawless", 10  }
    };

    private static readonly Dictionary<MaterialType?, int> JewelUiEffect = new()
    {
        { ACE.Entity.Enum.MaterialType.Diamond, 512 },
        { ACE.Entity.Enum.MaterialType.Moonstone, 512 },
        { ACE.Entity.Enum.MaterialType.WhiteJade, 512 },
        { ACE.Entity.Enum.MaterialType.WhiteQuartz, 512 },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, 512 },
        { ACE.Entity.Enum.MaterialType.SmokeyQuartz, 512 },
        { ACE.Entity.Enum.MaterialType.Zircon, 512 },
        // Black
        { ACE.Entity.Enum.MaterialType.BlackGarnet, 512 },
        { ACE.Entity.Enum.MaterialType.BlackOpal, 512 },
        { ACE.Entity.Enum.MaterialType.Jet, 512 },
        { ACE.Entity.Enum.MaterialType.Onyx, 512 },
        // Gray
        { ACE.Entity.Enum.MaterialType.Agate, 512 },
        { ACE.Entity.Enum.MaterialType.Hematite, 512 },
        { ACE.Entity.Enum.MaterialType.Bloodstone, 512 },
        // Red
        { ACE.Entity.Enum.MaterialType.Carnelian, 4 },
        { ACE.Entity.Enum.MaterialType.FireOpal, 4 },
        { ACE.Entity.Enum.MaterialType.RedGarnet, 4 },
        { ACE.Entity.Enum.MaterialType.Ruby, 4 },
        // Orange
        { ACE.Entity.Enum.MaterialType.Citrine, 16 },
        { ACE.Entity.Enum.MaterialType.Sunstone, 16 },
        // Yellow
        { ACE.Entity.Enum.MaterialType.Amber, 16 },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, 16 },
        { ACE.Entity.Enum.MaterialType.TigerEye, 16 },
        { ACE.Entity.Enum.MaterialType.YellowGarnet, 16 },
        { ACE.Entity.Enum.MaterialType.YellowTopaz, 16 },
        // Green
        { ACE.Entity.Enum.MaterialType.Emerald, 256 },
        { ACE.Entity.Enum.MaterialType.GreenGarnet, 256 },
        { ACE.Entity.Enum.MaterialType.GreenJade, 256 },
        { ACE.Entity.Enum.MaterialType.Malachite, 256 },
        { ACE.Entity.Enum.MaterialType.Peridot, 256 },
        // Blue
        { ACE.Entity.Enum.MaterialType.Aquamarine, 1 },
        { ACE.Entity.Enum.MaterialType.Azurite, 1 },
        { ACE.Entity.Enum.MaterialType.LapisLazuli, 1 },
        { ACE.Entity.Enum.MaterialType.Opal, 1 },
        { ACE.Entity.Enum.MaterialType.Sapphire, 1 },
        { ACE.Entity.Enum.MaterialType.Tourmaline, 1 },
        // Purple and Pink
        { ACE.Entity.Enum.MaterialType.Amethyst, 64 },
        { ACE.Entity.Enum.MaterialType.LavenderJade, 64 },
        { ACE.Entity.Enum.MaterialType.RedJade, 64 },
        { ACE.Entity.Enum.MaterialType.RoseQuartz, 64 },
        // Teal
        { ACE.Entity.Enum.MaterialType.Turquoise, 1 }
    };

    private static readonly Dictionary<MaterialType, uint> GemstoneIconMap = new()
    {
        { ACE.Entity.Enum.MaterialType.Agate, 0x06002CAD },
        {ACE.Entity.Enum.MaterialType.Amber, 0x06002CAE },
        {ACE.Entity.Enum.MaterialType.Amethyst, 0x06002CAF },
        {ACE.Entity.Enum.MaterialType.Aquamarine, 0x06002CB0 },
        {ACE.Entity.Enum.MaterialType.Azurite, 0x06002CB1 },
        {ACE.Entity.Enum.MaterialType.BlackGarnet, 0x06002CB2 },
        {ACE.Entity.Enum.MaterialType.BlackOpal, 0x06002CB3 },
        {ACE.Entity.Enum.MaterialType.Bloodstone, 0x06002CA7 },
        {ACE.Entity.Enum.MaterialType.Carnelian, 0x06002CA8 },
        {ACE.Entity.Enum.MaterialType.Citrine, 0x06002CA9 },
        {ACE.Entity.Enum.MaterialType.Diamond, 0x06002CAA },
        {ACE.Entity.Enum.MaterialType.Emerald, 0x06002CAB },
        {ACE.Entity.Enum.MaterialType.FireOpal, 0x06002CAC },
        {ACE.Entity.Enum.MaterialType.GreenGarnet, 0x06002CB4 },
        {ACE.Entity.Enum.MaterialType.GreenJade, 0x06002CB5 },
        {ACE.Entity.Enum.MaterialType.Hematite, 0x06002CB6 },
        {ACE.Entity.Enum.MaterialType.ImperialTopaz, 0x06002CB7 },
        {ACE.Entity.Enum.MaterialType.Jet, 0x06002CB8 },
        {ACE.Entity.Enum.MaterialType.LapisLazuli, 0x06002CB9 },
        {ACE.Entity.Enum.MaterialType.LavenderJade, 0x06002CBA },
        {ACE.Entity.Enum.MaterialType.Malachite, 0x06002CBB },
        {ACE.Entity.Enum.MaterialType.Moonstone, 0x06002CBC },
        {ACE.Entity.Enum.MaterialType.Onyx, 0x06002CBD },
        {ACE.Entity.Enum.MaterialType.Opal, 0x06002CBE },
        {ACE.Entity.Enum.MaterialType.Peridot, 0x06002CBF },
        {ACE.Entity.Enum.MaterialType.RedGarnet, 0x06002CC0 },
        {ACE.Entity.Enum.MaterialType.RedJade, 0x06002C98 },
        {ACE.Entity.Enum.MaterialType.RoseQuartz, 0x06002C99 },
        {ACE.Entity.Enum.MaterialType.Ruby, 0x06002C9A },
        {ACE.Entity.Enum.MaterialType.Sapphire, 0x06002C9B },
        {ACE.Entity.Enum.MaterialType.SmokeyQuartz, 0x06002C9C },
        {ACE.Entity.Enum.MaterialType.Sunstone, 0x06002C9D },
        {ACE.Entity.Enum.MaterialType.TigerEye, 0x06002C9E },
        {ACE.Entity.Enum.MaterialType.Tourmaline, 0x06002C9F },
        {ACE.Entity.Enum.MaterialType.Turquoise, 0x06002CA0 },
        {ACE.Entity.Enum.MaterialType.WhiteJade, 0x06002CA1 },
        {ACE.Entity.Enum.MaterialType.WhiteQuartz, 0x06002CA2 },
        {ACE.Entity.Enum.MaterialType.WhiteSapphire, 0x06002CA3 },
        {ACE.Entity.Enum.MaterialType.YellowGarnet, 0x06002CA4 },
        {ACE.Entity.Enum.MaterialType.YellowTopaz, 0x06002CA5 },
        {ACE.Entity.Enum.MaterialType.Zircon, 0x06002CA6 }
    };

    private static readonly Dictionary<MaterialType, string> MaterialTypeToString = new()
    {
        { ACE.Entity.Enum.MaterialType.Unknown, "Unknown" },
        { ACE.Entity.Enum.MaterialType.Ceramic, "Ceramic" },
        { ACE.Entity.Enum.MaterialType.Porcelain, "Porcelain" },
        { ACE.Entity.Enum.MaterialType.Cloth, "Cloth" },
        { ACE.Entity.Enum.MaterialType.Linen, "Linen" },
        { ACE.Entity.Enum.MaterialType.Satin, "Satin" },
        { ACE.Entity.Enum.MaterialType.Silk, "Silk" },
        { ACE.Entity.Enum.MaterialType.Velvet, "Velvet" },
        { ACE.Entity.Enum.MaterialType.Wool, "Wool" },
        { ACE.Entity.Enum.MaterialType.Gem, "Gem" },
        { ACE.Entity.Enum.MaterialType.Agate, "Agate" },
        { ACE.Entity.Enum.MaterialType.Amber, "Amber" },
        { ACE.Entity.Enum.MaterialType.Amethyst, "Amethyst" },
        { ACE.Entity.Enum.MaterialType.Aquamarine, "Aquamarine" },
        { ACE.Entity.Enum.MaterialType.Azurite, "Azurite" },
        { ACE.Entity.Enum.MaterialType.BlackGarnet, "Black Garnet" },
        { ACE.Entity.Enum.MaterialType.BlackOpal, "Black Opal" },
        { ACE.Entity.Enum.MaterialType.Bloodstone, "Bloodstone" },
        { ACE.Entity.Enum.MaterialType.Carnelian, "Carnelian" },
        { ACE.Entity.Enum.MaterialType.Citrine, "Citrine" },
        { ACE.Entity.Enum.MaterialType.Diamond, "Diamond" },
        { ACE.Entity.Enum.MaterialType.Emerald, "Emerald" },
        { ACE.Entity.Enum.MaterialType.FireOpal, "Fire Opal" },
        { ACE.Entity.Enum.MaterialType.GreenGarnet, "Green Garnet" },
        { ACE.Entity.Enum.MaterialType.GreenJade, "Green Jade" },
        { ACE.Entity.Enum.MaterialType.Hematite, "Hematite" },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, "Imperial Topaz" },
        { ACE.Entity.Enum.MaterialType.Jet, "Jet" },
        { ACE.Entity.Enum.MaterialType.LapisLazuli, "Lapis Lazuli" },
        { ACE.Entity.Enum.MaterialType.LavenderJade, "Lavender Jade" },
        { ACE.Entity.Enum.MaterialType.Malachite, "Malachite" },
        { ACE.Entity.Enum.MaterialType.Moonstone, "Moonstone" },
        { ACE.Entity.Enum.MaterialType.Onyx, "Onyx" },
        { ACE.Entity.Enum.MaterialType.Opal, "Opal" },
        { ACE.Entity.Enum.MaterialType.Peridot, "Peridot" },
        { ACE.Entity.Enum.MaterialType.RedGarnet, "Red Garnet" },
        { ACE.Entity.Enum.MaterialType.RedJade, "Red Jade" },
        { ACE.Entity.Enum.MaterialType.RoseQuartz, "Rose Quartz" },
        { ACE.Entity.Enum.MaterialType.Ruby, "Ruby" },
        { ACE.Entity.Enum.MaterialType.Sapphire, "Sapphire" },
        { ACE.Entity.Enum.MaterialType.SmokeyQuartz, "Smokey Quartz" },
        { ACE.Entity.Enum.MaterialType.Sunstone, "Sunstone" },
        { ACE.Entity.Enum.MaterialType.TigerEye, "Tiger Eye" },
        { ACE.Entity.Enum.MaterialType.Tourmaline, "Tourmaline" },
        { ACE.Entity.Enum.MaterialType.Turquoise, "Turquoise" },
        { ACE.Entity.Enum.MaterialType.WhiteJade, "White Jade" },
        { ACE.Entity.Enum.MaterialType.WhiteQuartz, "White Quartz" },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, "White Sapphire" },
        { ACE.Entity.Enum.MaterialType.YellowGarnet, "Yellow Garnet" },
        { ACE.Entity.Enum.MaterialType.YellowTopaz, "Yellow Topaz" },
        { ACE.Entity.Enum.MaterialType.Zircon, "Zircon" },
        { ACE.Entity.Enum.MaterialType.Ivory, "Ivory" },
        { ACE.Entity.Enum.MaterialType.Leather, "Leather" },
        { ACE.Entity.Enum.MaterialType.ArmoredilloHide, "Armoredillo Hide" },
        { ACE.Entity.Enum.MaterialType.GromnieHide, "Gromnie Hide" },
        { ACE.Entity.Enum.MaterialType.ReedSharkHide, "Reed Shark Hide" },
        { ACE.Entity.Enum.MaterialType.Metal, "Metal" },
        { ACE.Entity.Enum.MaterialType.Brass, "Brass" },
        { ACE.Entity.Enum.MaterialType.Bronze, "Bronze" },
        { ACE.Entity.Enum.MaterialType.Copper, "Copper" },
        { ACE.Entity.Enum.MaterialType.Gold, "Gold" },
        { ACE.Entity.Enum.MaterialType.Iron, "Iron" },
        { ACE.Entity.Enum.MaterialType.Pyreal, "Pyreal" },
        { ACE.Entity.Enum.MaterialType.Silver, "Silver" },
        { ACE.Entity.Enum.MaterialType.Steel, "Steel" },
        { ACE.Entity.Enum.MaterialType.Stone, "Stone" },
        { ACE.Entity.Enum.MaterialType.Alabaster, "Alabaster" },
        { ACE.Entity.Enum.MaterialType.Granite, "Granite" },
        { ACE.Entity.Enum.MaterialType.Marble, "Marble" },
        { ACE.Entity.Enum.MaterialType.Obsidian, "Obsidian" },
        { ACE.Entity.Enum.MaterialType.Sandstone, "Sandstone" },
        { ACE.Entity.Enum.MaterialType.Serpentine, "Serpentine" },
        { ACE.Entity.Enum.MaterialType.Wood, "Wood" },
        { ACE.Entity.Enum.MaterialType.Ebony, "Ebony" },
        { ACE.Entity.Enum.MaterialType.Mahogany, "Mahogany" },
        { ACE.Entity.Enum.MaterialType.Oak, "Oak" },
        { ACE.Entity.Enum.MaterialType.Pine, "Pine" },
        { ACE.Entity.Enum.MaterialType.Teak, "Teak" }
    };

    public static readonly Dictionary<string, MaterialType> StringToMaterialType = new()
    {
        { "Unknown", ACE.Entity.Enum.MaterialType.Unknown },
        { "Ceramic", ACE.Entity.Enum.MaterialType.Ceramic },
        { "Porcelain", ACE.Entity.Enum.MaterialType.Porcelain },
        { "Cloth", ACE.Entity.Enum.MaterialType.Cloth },
        { "Linen", ACE.Entity.Enum.MaterialType.Linen },
        { "Satin", ACE.Entity.Enum.MaterialType.Satin },
        { "Silk", ACE.Entity.Enum.MaterialType.Silk },
        { "Velvet", ACE.Entity.Enum.MaterialType.Velvet },
        { "Wool", ACE.Entity.Enum.MaterialType.Wool },
        { "Gem", ACE.Entity.Enum.MaterialType.Gem },
        { "Agate", ACE.Entity.Enum.MaterialType.Agate },
        { "Amber", ACE.Entity.Enum.MaterialType.Amber },
        { "Amethyst", ACE.Entity.Enum.MaterialType.Amethyst },
        { "Aquamarine", ACE.Entity.Enum.MaterialType.Aquamarine },
        { "Azurite", ACE.Entity.Enum.MaterialType.Azurite },
        { "Black Garnet", ACE.Entity.Enum.MaterialType.BlackGarnet },
        { "Black Opal", ACE.Entity.Enum.MaterialType.BlackOpal },
        { "Bloodstone", ACE.Entity.Enum.MaterialType.Bloodstone },
        { "Carnelian", ACE.Entity.Enum.MaterialType.Carnelian },
        { "Citrine", ACE.Entity.Enum.MaterialType.Citrine },
        { "Diamond", ACE.Entity.Enum.MaterialType.Diamond },
        { "Emerald", ACE.Entity.Enum.MaterialType.Emerald },
        { "Fire Opal", ACE.Entity.Enum.MaterialType.FireOpal },
        { "Green Garnet", ACE.Entity.Enum.MaterialType.GreenGarnet },
        { "Green Jade", ACE.Entity.Enum.MaterialType.GreenJade },
        { "Hematite", ACE.Entity.Enum.MaterialType.Hematite },
        { "Imperial Topaz", ACE.Entity.Enum.MaterialType.ImperialTopaz },
        { "Jet", ACE.Entity.Enum.MaterialType.Jet },
        { "Lapis Lazuli", ACE.Entity.Enum.MaterialType.LapisLazuli },
        { "Lavender Jade", ACE.Entity.Enum.MaterialType.LavenderJade },
        { "Malachite", ACE.Entity.Enum.MaterialType.Malachite },
        { "Moonstone", ACE.Entity.Enum.MaterialType.Moonstone },
        { "Onyx", ACE.Entity.Enum.MaterialType.Onyx },
        { "Opal", ACE.Entity.Enum.MaterialType.Opal },
        { "Peridot", ACE.Entity.Enum.MaterialType.Peridot },
        { "Red Garnet", ACE.Entity.Enum.MaterialType.RedGarnet },
        { "Red Jade", ACE.Entity.Enum.MaterialType.RedJade },
        { "Rose Quartz", ACE.Entity.Enum.MaterialType.RoseQuartz },
        { "Ruby", ACE.Entity.Enum.MaterialType.Ruby },
        { "Sapphire", ACE.Entity.Enum.MaterialType.Sapphire },
        { "Smokey Quartz", ACE.Entity.Enum.MaterialType.SmokeyQuartz },
        { "Sunstone", ACE.Entity.Enum.MaterialType.Sunstone },
        { "Tiger Eye", ACE.Entity.Enum.MaterialType.TigerEye },
        { "Tourmaline", ACE.Entity.Enum.MaterialType.Tourmaline },
        { "Turquoise", ACE.Entity.Enum.MaterialType.Turquoise },
        { "White Jade", ACE.Entity.Enum.MaterialType.WhiteJade },
        { "White Quartz", ACE.Entity.Enum.MaterialType.WhiteQuartz },
        { "White Sapphire", ACE.Entity.Enum.MaterialType.WhiteSapphire },
        { "Yellow Garnet", ACE.Entity.Enum.MaterialType.YellowGarnet },
        { "Yellow Topaz", ACE.Entity.Enum.MaterialType.YellowTopaz },
        { "Zircon", ACE.Entity.Enum.MaterialType.Zircon },
        { "Ivory", ACE.Entity.Enum.MaterialType.Ivory },
        { "Leather", ACE.Entity.Enum.MaterialType.Leather },
        { "Armoredillo Hide", ACE.Entity.Enum.MaterialType.ArmoredilloHide },
        { "Gromnie Hide", ACE.Entity.Enum.MaterialType.GromnieHide },
        { "Reed Shark Hide", ACE.Entity.Enum.MaterialType.ReedSharkHide },
        { "Metal", ACE.Entity.Enum.MaterialType.Metal },
        { "Brass", ACE.Entity.Enum.MaterialType.Brass },
        { "Bronze", ACE.Entity.Enum.MaterialType.Bronze },
        { "Copper", ACE.Entity.Enum.MaterialType.Copper },
        { "Gold", ACE.Entity.Enum.MaterialType.Gold },
        { "Iron", ACE.Entity.Enum.MaterialType.Iron },
        { "Pyreal", ACE.Entity.Enum.MaterialType.Pyreal },
        { "Silver", ACE.Entity.Enum.MaterialType.Silver },
        { "Steel", ACE.Entity.Enum.MaterialType.Steel },
        { "Stone", ACE.Entity.Enum.MaterialType.Stone },
        { "Alabaster", ACE.Entity.Enum.MaterialType.Alabaster },
        { "Granite", ACE.Entity.Enum.MaterialType.Granite },
        { "Marble", ACE.Entity.Enum.MaterialType.Marble },
        { "Obsidian", ACE.Entity.Enum.MaterialType.Obsidian },
        { "Sandstone", ACE.Entity.Enum.MaterialType.Sandstone },
        { "Serpentine", ACE.Entity.Enum.MaterialType.Serpentine },
        { "Wood", ACE.Entity.Enum.MaterialType.Wood },
        { "Ebony", ACE.Entity.Enum.MaterialType.Ebony },
        { "Mahogany", ACE.Entity.Enum.MaterialType.Mahogany },
        { "Oak", ACE.Entity.Enum.MaterialType.Oak },
        { "Pine", ACE.Entity.Enum.MaterialType.Pine },
        { "Teak", ACE.Entity.Enum.MaterialType.Teak }
    };

    public static readonly Dictionary<PropertyInt, MaterialType> JewelTypeToMaterial = new()
    {
        { PropertyInt.GearThreatGain, ACE.Entity.Enum.MaterialType.Agate },
        { PropertyInt.GearYellowFury, ACE.Entity.Enum.MaterialType.Amber },
        { PropertyInt.GearNullification, ACE.Entity.Enum.MaterialType.Amethyst },
        { PropertyInt.GearFrost, ACE.Entity.Enum.MaterialType.Aquamarine },
        { PropertyInt.GearSelf, ACE.Entity.Enum.MaterialType.Azurite },
        { PropertyInt.GearPierce, ACE.Entity.Enum.MaterialType.BlackGarnet },
        { PropertyInt.GearReprisal, ACE.Entity.Enum.MaterialType.BlackOpal },
        { PropertyInt.GearLifesteal, ACE.Entity.Enum.MaterialType.Bloodstone },
        { PropertyInt.GearStrength, ACE.Entity.Enum.MaterialType.Carnelian },
        { PropertyInt.GearStaminasteal, ACE.Entity.Enum.MaterialType.Citrine },
        { PropertyInt.GearHardenedDefense, ACE.Entity.Enum.MaterialType.Diamond },
        { PropertyInt.GearAcid, ACE.Entity.Enum.MaterialType.Emerald },
        { PropertyInt.GearFamiliarity, ACE.Entity.Enum.MaterialType.FireOpal },
        { PropertyInt.GearElementalist, ACE.Entity.Enum.MaterialType.GreenGarnet },
        { PropertyInt.GearPyrealFind, ACE.Entity.Enum.MaterialType.GreenJade },
        { PropertyInt.GearSelfHarm, ACE.Entity.Enum.MaterialType.Hematite },
        { PropertyInt.GearSlash, ACE.Entity.Enum.MaterialType.ImperialTopaz },
        { PropertyInt.GearLightning, ACE.Entity.Enum.MaterialType.Jet },
        { PropertyInt.GearBlueFury, ACE.Entity.Enum.MaterialType.LapisLazuli },
        { PropertyInt.GearSelflessness, ACE.Entity.Enum.MaterialType.LavenderJade },
        { PropertyInt.GearCompBurn, ACE.Entity.Enum.MaterialType.Malachite },
        { PropertyInt.GearItemManaUsage, ACE.Entity.Enum.MaterialType.Moonstone },
        { PropertyInt.GearPhysicalWard, ACE.Entity.Enum.MaterialType.Onyx },
        { PropertyInt.GearManasteal, ACE.Entity.Enum.MaterialType.Opal },
        { PropertyInt.GearQuickness, ACE.Entity.Enum.MaterialType.Peridot },
        { PropertyInt.GearFire, ACE.Entity.Enum.MaterialType.RedGarnet },
        { PropertyInt.GearFocus, ACE.Entity.Enum.MaterialType.RedJade },
        { PropertyInt.GearVitalsTransfer, ACE.Entity.Enum.MaterialType.RoseQuartz },
        { PropertyInt.GearRedFury, ACE.Entity.Enum.MaterialType.Ruby },
        { PropertyInt.GearMagicFind, ACE.Entity.Enum.MaterialType.Sapphire },
        { PropertyInt.GearThreatReduction, ACE.Entity.Enum.MaterialType.SmokeyQuartz },
        { PropertyInt.GearExperienceGain, ACE.Entity.Enum.MaterialType.Sunstone },
        { PropertyInt.GearCoordination, ACE.Entity.Enum.MaterialType.TigerEye },
        { PropertyInt.GearWardPen, ACE.Entity.Enum.MaterialType.Tourmaline },
        { PropertyInt.GearBlock, ACE.Entity.Enum.MaterialType.Turquoise },
        { PropertyInt.GearHealBubble, ACE.Entity.Enum.MaterialType.WhiteJade },
        { PropertyInt.GearThorns, ACE.Entity.Enum.MaterialType.WhiteQuartz },
        { PropertyInt.GearBludgeon, ACE.Entity.Enum.MaterialType.WhiteSapphire },
        { PropertyInt.GearBravado, ACE.Entity.Enum.MaterialType.YellowGarnet },
        { PropertyInt.GearEndurance, ACE.Entity.Enum.MaterialType.YellowTopaz },
        { PropertyInt.GearElementalWard, ACE.Entity.Enum.MaterialType.Zircon },

        // Alternate
        { PropertyInt.GearToughness, ACE.Entity.Enum.MaterialType.Diamond },
        { PropertyInt.GearResistance, ACE.Entity.Enum.MaterialType.Amethyst },
        { PropertyInt.GearHealthToStamina, ACE.Entity.Enum.MaterialType.Amber },
        { PropertyInt.GearHealthToMana, ACE.Entity.Enum.MaterialType.LapisLazuli },
        { PropertyInt.GearSlashBane, ACE.Entity.Enum.MaterialType.ImperialTopaz },
        { PropertyInt.GearBludgeonBane, ACE.Entity.Enum.MaterialType.WhiteSapphire },
        { PropertyInt.GearPierceBane, ACE.Entity.Enum.MaterialType.BlackGarnet },
        { PropertyInt.GearAcidBane, ACE.Entity.Enum.MaterialType.Emerald },
        { PropertyInt.GearFireBane, ACE.Entity.Enum.MaterialType.RedGarnet },
        { PropertyInt.GearFrostBane, ACE.Entity.Enum.MaterialType.Aquamarine },
        { PropertyInt.GearLightningBane, ACE.Entity.Enum.MaterialType.Jet },
    };

    public static readonly Dictionary<MaterialType, (PropertyInt PrimaryRating, PropertyInt AlternateRating)> JewelMaterialToType = new()
    {
        { ACE.Entity.Enum.MaterialType.Agate, (PropertyInt.GearThreatGain, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Amber, (PropertyInt.GearYellowFury, (PropertyInt.GearHealthToStamina)) },
        { ACE.Entity.Enum.MaterialType.Amethyst, (PropertyInt.GearNullification, (PropertyInt.GearResistance)) },
        { ACE.Entity.Enum.MaterialType.Aquamarine, (PropertyInt.GearFrost, (PropertyInt.GearFrostBane)) },
        { ACE.Entity.Enum.MaterialType.Azurite, (PropertyInt.GearSelf, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.BlackGarnet, (PropertyInt.GearPierce, (PropertyInt.GearPierceBane)) },
        { ACE.Entity.Enum.MaterialType.BlackOpal, (PropertyInt.GearReprisal, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Bloodstone, (PropertyInt.GearLifesteal, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Carnelian, (PropertyInt.GearStrength, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Citrine, (PropertyInt.GearStaminasteal, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Diamond, (PropertyInt.GearHardenedDefense, (PropertyInt.GearToughness)) },
        { ACE.Entity.Enum.MaterialType.Emerald, (PropertyInt.GearAcid, (PropertyInt.GearAcidBane)) },
        { ACE.Entity.Enum.MaterialType.FireOpal, (PropertyInt.GearFamiliarity, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.GreenGarnet, (PropertyInt.GearElementalist, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.GreenJade, (PropertyInt.GearPyrealFind, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Hematite, (PropertyInt.GearSelfHarm, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, (PropertyInt.GearSlash, (PropertyInt.GearSlashBane)) },
        { ACE.Entity.Enum.MaterialType.Jet, (PropertyInt.GearLightning, (PropertyInt.GearLightningBane)) },
        { ACE.Entity.Enum.MaterialType.LapisLazuli, (PropertyInt.GearBlueFury, (PropertyInt.GearHealthToMana)) },
        { ACE.Entity.Enum.MaterialType.LavenderJade, (PropertyInt.GearSelflessness, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Malachite, (PropertyInt.GearCompBurn, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Moonstone, (PropertyInt.GearItemManaUsage, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Onyx, (PropertyInt.GearPhysicalWard, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Opal, (PropertyInt.GearManasteal, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Peridot, (PropertyInt.GearQuickness, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.RedGarnet, (PropertyInt.GearFire, (PropertyInt.GearFireBane)) },
        { ACE.Entity.Enum.MaterialType.RedJade, (PropertyInt.GearFocus, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.RoseQuartz, (PropertyInt.GearVitalsTransfer, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Ruby, (PropertyInt.GearRedFury, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Sapphire, (PropertyInt.GearMagicFind, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.SmokeyQuartz, (PropertyInt.GearThreatReduction, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Sunstone, (PropertyInt.GearExperienceGain, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.TigerEye, (PropertyInt.GearCoordination, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Tourmaline, (PropertyInt.GearWardPen, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Turquoise, (PropertyInt.GearBlock, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.WhiteJade, (PropertyInt.GearHealBubble, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.WhiteQuartz, (PropertyInt.GearThorns, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, (PropertyInt.GearBludgeon, (PropertyInt.GearBludgeonBane)) },
        { ACE.Entity.Enum.MaterialType.YellowGarnet, (PropertyInt.GearBravado, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.YellowTopaz, (PropertyInt.GearEndurance, (PropertyInt.Undef)) },
        { ACE.Entity.Enum.MaterialType.Zircon, (PropertyInt.GearElementalWard, (PropertyInt.Undef)) },
    };

    private static readonly Dictionary<PropertyInt, EquipMask> RatingToEquipLocations = new()
    {
        // neck
        { PropertyInt.GearMagicFind, EquipMask.NeckWear },
        { PropertyInt.GearPyrealFind, EquipMask.NeckWear },
        { PropertyInt.GearExperienceGain, EquipMask.NeckWear },
        // wrist
        { PropertyInt.GearThreatGain, EquipMask.WristWear },
        { PropertyInt.GearThreatReduction, EquipMask.WristWear },
        { PropertyInt.GearCompBurn, EquipMask.WristWear },
        { PropertyInt.GearItemManaUsage, EquipMask.WristWear },
        { PropertyInt.GearNullification, EquipMask.WristWear },
        { PropertyInt.GearHardenedDefense, EquipMask.WristWear },
        { PropertyInt.GearPhysicalWard, EquipMask.WristWear },
        { PropertyInt.GearElementalWard, EquipMask.WristWear },
        // finger
        { PropertyInt.GearSelf, EquipMask.FingerWear },
        { PropertyInt.GearEndurance, EquipMask.FingerWear },
        { PropertyInt.GearCoordination, EquipMask.FingerWear },
        { PropertyInt.GearQuickness, EquipMask.FingerWear },
        { PropertyInt.GearFocus, EquipMask.FingerWear },
        { PropertyInt.GearStrength, EquipMask.FingerWear },
        // weapon
        { PropertyInt.GearFrost, EquipMask.Weapon },
        { PropertyInt.GearPierce, EquipMask.Weapon },
        { PropertyInt.GearLifesteal, EquipMask.Weapon },
        { PropertyInt.GearAcid, EquipMask.Weapon },
        { PropertyInt.GearSelfHarm, EquipMask.Weapon },
        { PropertyInt.GearSlash, EquipMask.Weapon },
        { PropertyInt.GearLightning, EquipMask.Weapon },
        { PropertyInt.GearManasteal, EquipMask.Weapon },
        { PropertyInt.GearFire, EquipMask.Weapon },
        { PropertyInt.GearVitalsTransfer, EquipMask.Weapon },
        { PropertyInt.GearHealBubble, EquipMask.Weapon },
        { PropertyInt.GearBludgeon, EquipMask.Weapon },
        { PropertyInt.GearStaminasteal, EquipMask.Weapon },
        { PropertyInt.GearRedFury, EquipMask.Weapon },
        // weapon or shield
        { PropertyInt.GearBravado, EquipMask.WeaponAndShield },
        { PropertyInt.GearReprisal, EquipMask.WeaponAndShield },
        { PropertyInt.GearFamiliarity, EquipMask.WeaponAndShield },
        // weapon/shield or armor
        { PropertyInt.GearYellowFury, EquipMask.WeaponAndArmor },
        { PropertyInt.GearBlueFury, EquipMask.WeaponAndArmor },
        // wand
        { PropertyInt.GearElementalist, EquipMask.Held },
        { PropertyInt.GearSelflessness, EquipMask.Held },
        { PropertyInt.GearWardPen, EquipMask.Held },
        // shield
        { PropertyInt.GearBlock, EquipMask.Shield },
        { PropertyInt.GearThorns, EquipMask.Shield },
        // Armor
        { PropertyInt.GearToughness, EquipMask.Armor },
        { PropertyInt.GearResistance, EquipMask.Armor },
        { PropertyInt.GearHealthToStamina, EquipMask.Armor },
        { PropertyInt.GearHealthToMana, EquipMask.Armor },
        { PropertyInt.GearSlashBane, EquipMask.Armor },
        { PropertyInt.GearBludgeonBane, EquipMask.Armor },
        { PropertyInt.GearPierceBane, EquipMask.Armor },
        { PropertyInt.GearAcidBane, EquipMask.Armor },
        { PropertyInt.GearFireBane, EquipMask.Armor },
        { PropertyInt.GearFrostBane, EquipMask.Armor },
        { PropertyInt.GearLightningBane, EquipMask.Armor },
        // undef
        {PropertyInt.Undef, EquipMask.None}
    };

    public static readonly Dictionary<MaterialType?, EquipMask> MaterialValidLocations = new()
    {
        // wand only
        { ACE.Entity.Enum.MaterialType.Tourmaline, EquipMask.Held },
        { ACE.Entity.Enum.MaterialType.GreenGarnet, EquipMask.Held },
        { ACE.Entity.Enum.MaterialType.LavenderJade, EquipMask.Held },
        // any weapon
        { ACE.Entity.Enum.MaterialType.Opal, EquipMask.Weapon },
        { ACE.Entity.Enum.MaterialType.RoseQuartz, EquipMask.Weapon },
        { ACE.Entity.Enum.MaterialType.Hematite, EquipMask.Weapon },
        { ACE.Entity.Enum.MaterialType.Bloodstone, EquipMask.Weapon },
        { ACE.Entity.Enum.MaterialType.WhiteJade, EquipMask.Weapon },
        // weapon or armor
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.BlackGarnet,EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.Jet, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.RedGarnet, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.Aquamarine, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.Emerald, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.Amber, EquipMask.WeaponAndArmor },
        { ACE.Entity.Enum.MaterialType.LapisLazuli, EquipMask.WeaponAndArmor },
        // shield only
        { ACE.Entity.Enum.MaterialType.WhiteQuartz, EquipMask.Shield },
        { ACE.Entity.Enum.MaterialType.Turquoise, EquipMask.Shield },
        // shield or melee weapon
        { ACE.Entity.Enum.MaterialType.Ruby,  EquipMask.WeaponAndShield },
        { ACE.Entity.Enum.MaterialType.BlackOpal, EquipMask.WeaponAndShield },
        { ACE.Entity.Enum.MaterialType.FireOpal, EquipMask.WeaponAndShield },
        { ACE.Entity.Enum.MaterialType.YellowGarnet, EquipMask.WeaponAndShield },
        // bracelet only (left rest)
        { ACE.Entity.Enum.MaterialType.SmokeyQuartz, EquipMask.WristWear },
        { ACE.Entity.Enum.MaterialType.Agate, EquipMask.WristWear },
        { ACE.Entity.Enum.MaterialType.Moonstone, EquipMask.WristWear },
        { ACE.Entity.Enum.MaterialType.Citrine, EquipMask.WristWear },
        { ACE.Entity.Enum.MaterialType.Malachite, EquipMask.WristWear },
        // bracelet only (right rest)
        { ACE.Entity.Enum.MaterialType.Onyx, EquipMask.WristWear },
        { ACE.Entity.Enum.MaterialType.Zircon, EquipMask.WristWear },
        // bracelet only OR armor
        { ACE.Entity.Enum.MaterialType.Diamond, EquipMask.WristAndArmor },
        { ACE.Entity.Enum.MaterialType.Amethyst, EquipMask.WristAndArmor },
        // ring only (left rest)
        { ACE.Entity.Enum.MaterialType.Peridot, EquipMask.FingerWear },
        { ACE.Entity.Enum.MaterialType.RedJade, EquipMask.FingerWear },
        { ACE.Entity.Enum.MaterialType.YellowTopaz, EquipMask.FingerWear },
        // ring only (right rest)
        { ACE.Entity.Enum.MaterialType.Carnelian, EquipMask.FingerWear },
        { ACE.Entity.Enum.MaterialType.Azurite, EquipMask.FingerWear },
        { ACE.Entity.Enum.MaterialType.TigerEye, EquipMask.FingerWear },
        // necklace only
        { ACE.Entity.Enum.MaterialType.Sapphire, EquipMask.NeckWear },
        { ACE.Entity.Enum.MaterialType.Sunstone, EquipMask.NeckWear },
        { ACE.Entity.Enum.MaterialType.GreenJade, EquipMask.NeckWear },
    };

    public static readonly Dictionary<MaterialType,
        (PropertyInt PropertyName,
        string Name,
        string Slot,
        int BasePrimary,
        float BonusPrimary,
        int BaseSecondary,
        float BonusSecondary)> JewelEffectInfoMain = new()
    {
        // neck
        { ACE.Entity.Enum.MaterialType.Sunstone, (PropertyInt.GearExperienceGain, "Illuminated Mind", "necklace", 5, 0.25f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Sapphire, (PropertyInt.GearMagicFind, "Seeker", "necklace", 5, 0.25f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.GreenJade, (PropertyInt.GearPyrealFind, "Prosperity", "necklace", 5, 0.25f, 0, 0.0f) },

        // ring
        { ACE.Entity.Enum.MaterialType.Carnelian, (PropertyInt.GearStrength, "Mighty Thews", "ring", 10, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Azurite, (PropertyInt.GearSelf, "Erudite Mind", "ring", 10, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.TigerEye, (PropertyInt.GearCoordination, "Dexterous Hand", "ring", 10, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.RedJade, (PropertyInt.GearFocus, "Focused Mind", "ring", 10, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.YellowTopaz, (PropertyInt.GearEndurance, "Perserverence", "ring", 10, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Peridot, (PropertyInt.GearQuickness, "Swift-footed", "ring", 10, 1.0f, 0, 0.0f) },

        // bracelet
        { ACE.Entity.Enum.MaterialType.Agate, (PropertyInt.GearThreatGain, "Provocation", "bracelet", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.SmokeyQuartz, (PropertyInt.GearThreatReduction, "Clouded Vision", "bracelet", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Moonstone, (PropertyInt.GearItemManaUsage, "Meticulous Magus", "bracelet", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Malachite, (PropertyInt.GearCompBurn, "Thrifty Scholar", "bracelet", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Onyx, (PropertyInt.GearPhysicalWard, "Black Bulwark", "bracelet", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Zircon, (PropertyInt.GearElementalWard, "Prismatic Ward", "bracelet", 10, 0.5f, 0, 0.0f) },

        // bracelet (or armor)
        { ACE.Entity.Enum.MaterialType.Diamond, (PropertyInt.GearHardenedDefense, "Hardened Fortification", "bracelet", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Amethyst, (PropertyInt.GearNullification, "Nullification", "bracelet", 20, 1.0f, 0, 0.0f) },

        // shield
        { ACE.Entity.Enum.MaterialType.Turquoise, (PropertyInt.GearBlock, "Stalwart Defense", "shield", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.WhiteQuartz, (PropertyInt.GearThorns, "Swift Retrbution", "shield", 10, 0.5f, 0, 0.0f) },

        // weapon
        { ACE.Entity.Enum.MaterialType.Hematite, (PropertyInt.GearSelfHarm, "Blood Frenzy", "weapon", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Bloodstone, (PropertyInt.GearLifesteal, "Sanguine Thirst", "weapon", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Citrine, (PropertyInt.GearStaminasteal, "Vigor Siphon", "weapon", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Opal, (PropertyInt.GearManasteal, "Ophidian", "weapon", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.WhiteJade, (PropertyInt.GearHealBubble, "Purified Soul", "weapon", 10, 0.5f, 2, 0.1f) },
        { ACE.Entity.Enum.MaterialType.RoseQuartz, (PropertyInt.GearVitalsTransfer, "Tilted-scales", "weapon", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Ruby, (PropertyInt.GearRedFury, "Red Fury", "weapon", 20, 1.0f, 0, 0.0f) },

        // weapon (or armor)
        { ACE.Entity.Enum.MaterialType.Jet, (PropertyInt.GearLightning, "Astyrrian Rage", "weapon", 10, 0.5f, 2, 0.1f) },
        { ACE.Entity.Enum.MaterialType.RedGarnet, (PropertyInt.GearFire, "Blazing Brand", "weapon", 10, 0.5f, 2, 0.1f) },
        { ACE.Entity.Enum.MaterialType.Aquamarine, (PropertyInt.GearFrost, "Bone-chiller", "weapon", 10, 0.5f, 2, 0.1f) },
        { ACE.Entity.Enum.MaterialType.Emerald, (PropertyInt.GearAcid, "Devouring Mist", "weapon", 10, 0.5f, 2, 0.1f) },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, (PropertyInt.GearSlash, "Falcon's Gyre", "weapon", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.BlackGarnet, (PropertyInt.GearPierce, "Precision Strikes", "weapon", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, (PropertyInt.GearBludgeon, "Skull-cracker", "weapon", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.LapisLazuli, (PropertyInt.GearBlueFury, "Blue Fury", "weapon", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Amber, (PropertyInt.GearYellowFury, "Yellow Fury", "weapon", 20, 1.0f, 0, 0.0f) },

        // weapon or shield
        { ACE.Entity.Enum.MaterialType.YellowGarnet, (PropertyInt.GearBravado, "Bravado", "weapon or shield", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.FireOpal, (PropertyInt.GearFamiliarity, "Familiar Foe", "weapon or shield", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.BlackOpal, (PropertyInt.GearReprisal, "Vicious Reprisal", "weapon or shield", 5, 0.25f, 0, 0.0f) },

        // wand
        { ACE.Entity.Enum.MaterialType.GreenGarnet, (PropertyInt.GearElementalist, "Elementalist", "wand", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Tourmaline, (PropertyInt.GearWardPen, "Ruthless Discernment", "wand", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.LavenderJade, (PropertyInt.GearSelflessness, "Selfless Spirit", "wand", 10, 0.5f, 0, 0.0f) },
    };

    public static readonly Dictionary<MaterialType,
        (PropertyInt PropertyName,
        string Name,
        string Slot,
        int BasePrimary,
        float BonusPrimary,
        int BaseSecondary,
        float BonusSecondary)> JewelEffectInfoAlternate = new()
    {
        // armor
        { ACE.Entity.Enum.MaterialType.Diamond, (PropertyInt.GearToughness, "Toughness", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Amethyst, (PropertyInt.GearResistance, "Resistance", "piece of armor", 20, 1.0f, 0, 0.0f) },

        { ACE.Entity.Enum.MaterialType.Jet, (PropertyInt.GearLightningBane, "Astyrrian's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.RedGarnet, (PropertyInt.GearFireBane, "Inferno's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Aquamarine, (PropertyInt.GearFrostBane, "Gelidite's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Emerald, (PropertyInt.GearAcidBane, "Olthoi's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, (PropertyInt.GearSlashBane, "Swordsman's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.BlackGarnet, (PropertyInt.GearPierceBane, "Archer's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, (PropertyInt.GearBludgeonBane, "Tusker's Bane", "piece of armor", 20, 1.0f, 0, 0.0f) },

        { ACE.Entity.Enum.MaterialType.LapisLazuli, (PropertyInt.GearHealthToMana, "Austere Anchorite", "piece of armor", 10, 0.5f, 0, 0.0f) },
        { ACE.Entity.Enum.MaterialType.Amber, (PropertyInt.GearHealthToStamina, "Masochist", "piece of armor", 10, 0.5f, 0, 0.0f) },
    };
}
