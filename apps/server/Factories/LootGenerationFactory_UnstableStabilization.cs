using System;
using System.Collections.Generic;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    internal static bool ApplyUnstableStabilizationTierUpgrades(WorldObject target, int currentTier, int newTier)
    {
        if (target.ItemType != ItemType.Jewelry)
        {
            return ApplyUpgradeKitTierUpgrades(target, currentTier, newTier);
        }

        ScaleUpClampedJewelryWardLevel(target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearMaxHealth, target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearMaxStamina, target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearMaxMana, target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearCritDamage, target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearCritResist, target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearDamage, target, currentTier, newTier);
        ScaleUpClampedJewelryRating(PropertyInt.GearDamageResist, target, currentTier, newTier);

        ScaleUpArmorSkillMod(PropertyFloat.ArmorDeceptionMod, target, currentTier, newTier);
        ScaleUpArmorSkillMod(PropertyFloat.ArmorPerceptionMod, target, currentTier, newTier);
        ScaleUpSpecialRatings(target, currentTier, newTier);

        return true;
    }

    internal static void ApplyUnstableStabilizationPostTierUpgrades(WorldObject target, int currentTier, int newTier)
    {
        ScaleUpStabilizationSpells(target, currentTier, newTier);
        ScaleUpItemMana(target, newTier);
    }

    internal static void ApplyUnstableStabilizationParity(WorldObject target, int currentTier, int newTier)
    {
        if (target.ItemType is ItemType.Weapon or ItemType.MissileWeapon or ItemType.MeleeWeapon or ItemType.Caster)
        {
            ScaleUpWeaponCritChance(target, currentTier, newTier);
            ScaleUpWeaponCritDamage(target, currentTier, newTier);
            ScaleUpWeaponStaminaCostReduction(target, currentTier, newTier);
            RemoveCasterTierOneDamagePenalty(target, currentTier, newTier);
        }

        if (target.WeenieType is WeenieType.Clothing || target.ItemType == ItemType.Armor)
        {
            ScaleUpArmorCoreGearRatings(target, currentTier, newTier);
        }

        if (target.ItemType == ItemType.Jewelry)
        {
            ScaleUpClampedJewelryRating(PropertyInt.GearHealingBoost, target, currentTier, newTier);
            ScaleUpClampedJewelryRating(PropertyInt.GearCritDamageResist, target, currentTier, newTier);
        }

        ScaleUpGeneratedValue(target, currentTier, newTier);
    }

    private static void ScaleUpArmorCoreGearRatings(WorldObject target, int currentTier, int newTier)
    {
        var multiplier = GetAverageTierRatingMultiplier(currentTier, newTier);

        foreach (var ratingId in ArmorCoreGearRatingIds)
        {
            ScaleUpRatingByMultiplier(target, ratingId, multiplier);
        }
    }

    private static void ScaleUpClampedJewelryWardLevel(WorldObject target, int currentTier, int newTier)
    {
        var currentBaseStat = target.GetProperty(PropertyInt.WardLevel);

        if (currentBaseStat == null)
        {
            return;
        }

        var jewelryBaseWardLevelPerTier = LootTables.JewelryBaseWardLeverPerTier;
        currentTier = Math.Clamp(currentTier, 0, jewelryBaseWardLevelPerTier.Length - 2);
        newTier = Math.Clamp(newTier, 0, jewelryBaseWardLevelPerTier.Length - 2);

        var necklaceMultiplier = target.ValidLocations is EquipMask.NeckWear ? 2 : 1;
        var currentBaseLevelFromTier = jewelryBaseWardLevelPerTier[currentTier] * necklaceMultiplier;
        var currentRange =
            (jewelryBaseWardLevelPerTier[currentTier + 1] * necklaceMultiplier)
            - (jewelryBaseWardLevelPerTier[currentTier] * necklaceMultiplier);

        if (currentRange <= 0)
        {
            currentRange = 1;
        }

        var currentRoll = currentBaseStat - currentBaseLevelFromTier;
        var rollPercentile = Math.Clamp((float)currentRoll / currentRange, 0.0f, 1.0f);

        var newTierRange =
            (jewelryBaseWardLevelPerTier[newTier + 1] * necklaceMultiplier)
            - (jewelryBaseWardLevelPerTier[newTier] * necklaceMultiplier);
        var amountAboveMinimum = newTierRange * rollPercentile;

        var newBaseLevelFromTier = jewelryBaseWardLevelPerTier[newTier] * necklaceMultiplier;
        var final = Convert.ToInt32(newBaseLevelFromTier + amountAboveMinimum);

        target.SetProperty(PropertyInt.WardLevel, final);
    }

    private static void ScaleUpClampedJewelryRating(PropertyInt property, WorldObject target, int currentTier, int newTier)
    {
        var currentBaseStat = target.GetProperty(property);

        if (currentBaseStat == null)
        {
            return;
        }

        var jewelryBaseRatingPerTier = LootTables.JewelryBaseRatingPerTier;
        var currentBaseLevelFromTier = jewelryBaseRatingPerTier[currentTier];
        var currentRange = currentBaseLevelFromTier;

        var currentRoll = currentBaseStat - currentBaseLevelFromTier;
        var rollPercentile = Math.Clamp((float)currentRoll / currentRange, 0.0f, 1.0f);

        var newTierRange = jewelryBaseRatingPerTier[newTier];
        var amountAboveMinimum = newTierRange * rollPercentile;
        var newBaseLevelFromTier = jewelryBaseRatingPerTier[newTier];
        var final = Convert.ToInt32(newBaseLevelFromTier + amountAboveMinimum);

        target.SetProperty(property, final);
    }

    private static void ScaleUpStabilizationSpells(WorldObject target, int currentTier, int newTier)
    {
        if (newTier is < 3 or > 7)
        {
            return;
        }

        if (currentTier % 2 == 0 && newTier == currentTier + 1)
        {
            return;
        }

        ScaleUpStabilizationSpellbookSpells(target, newTier);
        ScaleUpDidSpell(target, newTier);
        ScaleUpProcSpell(target, newTier);
    }

    private static void ScaleUpStabilizationSpellbookSpells(WorldObject target, int newTier)
    {
        var spellBook = target.Biota.PropertiesSpellBook;

        if (spellBook == null)
        {
            return;
        }

        var spellsToRemove = new List<int>();
        var spellsToAdd = new List<int>();

        foreach (var spellId in spellBook.Keys)
        {
            var minimumLevelSpellId = SpellLevelProgression.GetLevel1SpellId((SpellId)spellId, true);
            var spellProgressionList = SpellLevelProgression.GetSpellLevels((SpellId)spellId);

            if (spellProgressionList is null)
            {
                continue;
            }

            var isCantrip = spellProgressionList.Count < 5;
            var spellLevel = StabilizationSpellProfile.GetWeightedSpellLevel(newTier, isCantrip);

            spellsToRemove.Add(spellId);
            spellsToAdd.Add((int)SpellLevelProgression.GetSpellAtLevel(minimumLevelSpellId, spellLevel, true, true));
        }

        if (spellsToAdd.Count == 0)
        {
            return;
        }

        foreach (var spellId in spellsToRemove)
        {
            target.Biota.TryRemoveKnownSpell(spellId, target.BiotaDatabaseLock);
        }

        foreach (var spellId in spellsToAdd)
        {
            target.Biota.GetOrAddKnownSpell(spellId, target.BiotaDatabaseLock, out _);
        }
    }

    private static void ScaleUpWeaponCritChance(WorldObject target, int currentTier, int newTier)
    {
        if (target.CriticalFrequency == null)
        {
            return;
        }

        float[] minCritChancePerTier = [0.0f, 0.01f, 0.015f, 0.02f, 0.025f, 0.03f, 0.04f, 0.05f];
        const float rollRange = 0.05f;
        var currentBonus = target.CriticalFrequency.Value - 0.1f;
        var currentMinimum = minCritChancePerTier[Math.Clamp(currentTier, 0, minCritChancePerTier.Length - 1)];
        var newMinimum = minCritChancePerTier[Math.Clamp(newTier, 0, minCritChancePerTier.Length - 1)];
        var rollPercentile = Math.Clamp((currentBonus - currentMinimum) / rollRange, 0.0f, 1.0f);
        var final = 0.1f + newMinimum + rollRange * rollPercentile;

        target.SetProperty(PropertyFloat.CriticalFrequency, final);
    }

    private static void ScaleUpWeaponCritDamage(WorldObject target, int currentTier, int newTier)
    {
        var currentCritMultiplier = target.GetProperty(PropertyFloat.CriticalMultiplier);
        if (currentCritMultiplier == null)
        {
            return;
        }

        float[] minCritDamagePerTier = [0.0f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f];
        const float rollRange = 0.5f;
        var currentBonus = currentCritMultiplier.Value - 1.0f;
        var currentMinimum = minCritDamagePerTier[Math.Clamp(currentTier, 0, minCritDamagePerTier.Length - 1)];
        var newMinimum = minCritDamagePerTier[Math.Clamp(newTier, 0, minCritDamagePerTier.Length - 1)];
        var rollPercentile = Math.Clamp((currentBonus - currentMinimum) / rollRange, 0.0f, 1.0f);
        var final = 1.0f + newMinimum + rollRange * rollPercentile;

        target.SetProperty(PropertyFloat.CriticalMultiplier, final);
    }

    private static void ScaleUpWeaponStaminaCostReduction(WorldObject target, int currentTier, int newTier)
    {
        var currentStaminaReduction = target.StaminaCostReductionMod;
        if (currentStaminaReduction == null)
        {
            return;
        }

        float[] minStaminaReductionPerTier = [0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f];
        const float rollRange = 0.1f;
        var currentMinimum = minStaminaReductionPerTier[Math.Clamp(currentTier, 0, minStaminaReductionPerTier.Length - 1)];
        var newMinimum = minStaminaReductionPerTier[Math.Clamp(newTier, 0, minStaminaReductionPerTier.Length - 1)];
        var rollPercentile = Math.Clamp((currentStaminaReduction.Value - currentMinimum) / rollRange, 0.0f, 1.0f);
        var final = newMinimum + rollRange * rollPercentile;

        target.SetProperty(PropertyFloat.StaminaCostReductionMod, final);
    }

    private static void RemoveCasterTierOneDamagePenalty(WorldObject target, int currentTier, int newTier)
    {
        if (target.ItemType != ItemType.Caster || currentTier != 0 || newTier == 0)
        {
            return;
        }

        if ((target.GearDamage ?? 0) < 0)
        {
            target.SetProperty(PropertyInt.GearDamage, 0);
        }

        if ((target.DamageRating ?? 0) < 0)
        {
            target.SetProperty(PropertyInt.DamageRating, 0);
        }
    }

    private static void ScaleUpGeneratedValue(WorldObject target, int currentTier, int newTier)
    {
        if (target.Value == null)
        {
            return;
        }

        currentTier = Math.Clamp(currentTier, 0, itemValue_RandomRange.Count - 1);
        newTier = Math.Clamp(newTier, 0, itemValue_RandomRange.Count - 1);

        var materialModifier = LootTables.getMaterialValueModifier(target);
        var gemModifier = LootTables.getGemMaterialValueModifier(target);
        var currentTierFactor = Math.Ceiling((currentTier + 1) / 2.0f);
        var newTierFactor = Math.Ceiling((newTier + 1) / 2.0f);

        var currentRange = itemValue_RandomRange[currentTier];
        var currentMinimum = currentRange.min * gemModifier * materialModifier * currentTierFactor;
        var currentMaximum = currentRange.max * gemModifier * materialModifier * currentTierFactor;
        if (currentMaximum <= currentMinimum)
        {
            return;
        }

        var currentValue = target.Value.Value;
        var rollPercentile = Math.Clamp((currentValue - currentMinimum) / (currentMaximum - currentMinimum), 0.0f, 1.0f);

        var newRange = itemValue_RandomRange[newTier];
        var newMinimum = newRange.min * gemModifier * materialModifier * newTierFactor;
        var newMaximum = newRange.max * gemModifier * materialModifier * newTierFactor;
        var finalValue = (int)Math.Round(newMinimum + (newMaximum - newMinimum) * rollPercentile);

        target.SetProperty(PropertyInt.Value, finalValue);
    }
    private static readonly PropertyInt[] ArmorCoreGearRatingIds =
    [
        PropertyInt.GearDamage,
        PropertyInt.GearHealingBoost,
        PropertyInt.GearCritDamage,
        PropertyInt.GearCrit,
        PropertyInt.GearDamageResist,
        PropertyInt.GearCritResist,
    ];

}