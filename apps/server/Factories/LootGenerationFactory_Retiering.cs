using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Cantrips;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    internal static bool ApplyUpgradeKitTierUpgrades(WorldObject target, int currentTier, int newTier)
    {
        if (target.ItemType is ItemType.Weapon or ItemType.MissileWeapon or ItemType.MeleeWeapon or ItemType.Caster)
        {
            if (target.WeaponSubtype == null)
            {
                _log.Error("MutateQuestItem() - WeaponSubType is null for ({Target}). Weapon upgrade aborted.", target);
                return false;
            }

            var weaponSubtype = (LootTables.WeaponSubtype)target.WeaponSubtype;

            ScaleUpDamage(target, weaponSubtype, currentTier, newTier);
            ScaleUpDamageMod(target, weaponSubtype, currentTier, newTier);
            ScaleUpElementalAndRestoMod(target, weaponSubtype, currentTier, newTier);
            ScaleUpElementalMod(target, weaponSubtype, currentTier, newTier);
            ScaleUpRestorationMod(target, weaponSubtype, currentTier, newTier);
            ScaleUpWeaponOffense(target, currentTier, newTier);
            ScaleUpWeaponPhysicalDefense(target, currentTier, newTier);
            ScaleUpWeaponMagicDefense(target, currentTier, newTier);
            ScaleUpWeaponSkillMod(PropertyFloat.WeaponLifeMagicMod, target, currentTier, newTier);
            ScaleUpWeaponSkillMod(PropertyFloat.WeaponWarMagicMod, target, currentTier, newTier);
        }

        if (target.WeenieType is WeenieType.Clothing || target.ItemType == ItemType.Armor)
        {
            if (target.ArmorStyle == null && target.ArmorLevel != null && target.ArmorLevel > 0)
            {
                _log.Error("MutateQuestItem() - ArmorStyle is null for ({target}). Armor upgrade aborted.", target);
                return false;
            }

            ScaleUpArmorLevel(target, currentTier, newTier);
            ScaleUpWardLevel(target, currentTier, newTier);

            ScaleUpArmorSkillMod(PropertyFloat.ArmorAttackMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorDeceptionMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorDualWieldMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorHealthMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorHealthRegenMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorLifeMagicMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorMagicDefMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorManaMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorManaRegenMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorPerceptionMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorPhysicalDefMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorRunMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorShieldMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorStaminaMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorStaminaRegenMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorThieveryMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorTwohandedCombatMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorWarMagicMod, target, currentTier, newTier);
        }

        if (target.ItemType == ItemType.Jewelry)
        {
            ScaleUpJewelryWardLevel(target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearMaxHealth, target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearMaxStamina, target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearMaxMana, target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearCritDamage, target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearCritResist, target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearDamage, target, currentTier, newTier);
            ScaleUpJewelryRating(PropertyInt.GearDamageResist, target, currentTier, newTier);

            ScaleUpArmorSkillMod(PropertyFloat.ArmorDeceptionMod, target, currentTier, newTier);
            ScaleUpArmorSkillMod(PropertyFloat.ArmorPerceptionMod, target, currentTier, newTier);
        }

        ScaleUpSpecialRatings(target, currentTier, newTier);
        return true;
    }

    internal static void ApplyUpgradeKitPostTierUpgrades(WorldObject target, int currentTier, int newTier)
    {
        ScaleUpSpells(target, currentTier, newTier);
        ScaleUpItemMana(target, newTier);
    }

    private static void ScaleUpDamage(WorldObject target, LootTables.WeaponSubtype weaponSubtype, int currentTier, int newTier)
    {
        if (target.Damage == null)
        {
            return;
        }

        if (target.WeenieType == WeenieType.MissileLauncher)
        {
            _log.Error("ScaleUpDamage() - {Target} damage is not null.");
            return;
        }

        var currentBaseStat = target.Damage.Value;
        var currentTierMinimum = LootTables.GetMeleeSubtypeMinimumDamage(weaponSubtype, currentTier);
        var currentRange = LootTables.GetMeleeSubtypeDamageRange(weaponSubtype, currentTier);
        var currentRoll = currentBaseStat - currentTierMinimum;
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierMinimum = LootTables.GetMeleeSubtypeMinimumDamage(weaponSubtype, newTier);
        var newTierRange = LootTables.GetMeleeSubtypeDamageRange(weaponSubtype, newTier);
        var amountAboveMinimum = newTierRange * rollPercentile;
        var final = Convert.ToInt32(newTierMinimum + amountAboveMinimum);

        target.SetProperty(PropertyInt.Damage, final);
    }

    private static void ScaleUpDamageMod(WorldObject target, LootTables.WeaponSubtype weaponSubtype, int currentTier, int newTier)
    {
        if (target.DamageMod == null)
        {
            return;
        }

        var currentBaseStat = target.DamageMod.Value;
        var currentTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, currentTier);
        var currentRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, currentTier);
        var currentRoll = currentBaseStat - currentTierMinimum;
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, newTier);
        var newTierRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, newTier);
        var amountAboveMinimum = newTierRange * rollPercentile;
        var final = newTierMinimum + amountAboveMinimum;

        target.SetProperty(PropertyFloat.DamageMod, final);
    }

    private static void ScaleUpElementalAndRestoMod(WorldObject target, LootTables.WeaponSubtype weaponSubtype, int currentTier, int newTier)
    {
        if (target is not { ElementalDamageMod: not null, WeaponRestorationSpellsMod: not null })
        {
            return;
        }

        var magicSkill = target.WieldSkillType2 == 34 ? Skill.WarMagic : Skill.LifeMagic;
        var elementalDamageMod = target.ElementalDamageMod.Value;
        var restorationSpellMod = target.WeaponRestorationSpellsMod.Value;

        var currentBaseStat = magicSkill == Skill.WarMagic ? elementalDamageMod : restorationSpellMod;
        var currentTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, currentTier);
        var currentRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, currentTier);
        var currentRoll = currentBaseStat - currentTierMinimum;
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, newTier);
        var newTierRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, newTier);
        var amountAboveMinimum = newTierRange * rollPercentile;
        var final = newTierMinimum + amountAboveMinimum;

        if (magicSkill == Skill.WarMagic)
        {
            target.SetProperty(PropertyFloat.ElementalDamageMod, final);
            target.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, 1 + (final - 1) / 4);
        }
        else
        {
            target.SetProperty(PropertyFloat.ElementalDamageMod, 1 + (final - 1) / 2);
            target.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, 1 + (final - 1) / 2);
        }
    }

    private static void ScaleUpElementalMod(WorldObject target, LootTables.WeaponSubtype weaponSubtype, int currentTier, int newTier)
    {
        if (target is not { WeaponRestorationSpellsMod: null, ElementalDamageMod: not null })
        {
            return;
        }

        var currentBaseStat = target.ElementalDamageMod.Value;
        var currentTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, currentTier);
        var currentRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, currentTier);
        var currentRoll = currentBaseStat - currentTierMinimum;
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, newTier);
        var newTierRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, newTier);
        var amountAboveMinimum = newTierRange * rollPercentile;
        var final = newTierMinimum + amountAboveMinimum;

        target.SetProperty(PropertyFloat.ElementalDamageMod, final);
    }

    private static void ScaleUpRestorationMod(WorldObject target, LootTables.WeaponSubtype weaponSubtype, int currentTier, int newTier)
    {
        if (target is not { WeaponRestorationSpellsMod: not null, ElementalDamageMod: null })
        {
            return;
        }

        var currentBaseStat = target.WeaponRestorationSpellsMod.Value;
        var currentTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, currentTier);
        var currentRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, currentTier);
        var currentRoll = currentBaseStat - currentTierMinimum;
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierMinimum = LootTables.GetMissileCasterSubtypeMinimumDamage(weaponSubtype, newTier);
        var newTierRange = LootTables.GetMissileCasterSubtypeDamageRange(weaponSubtype, newTier);
        var amountAboveMinimum = newTierRange * rollPercentile;
        var final = newTierMinimum + amountAboveMinimum;

        target.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, final);
    }

    private static void ScaleUpWeaponOffense(WorldObject target, int currentTier, int newTier)
    {
        var currentStat = target.WeaponOffense;

        if (currentStat == null)
        {
            return;
        }

        var currentTierBonus = LootTables.WeaponOffenseModBonusPerTier[currentTier];
        var newTierBonus = LootTables.WeaponOffenseModBonusPerTier[newTier];
        var difference = newTierBonus - currentTierBonus;

        var final = currentStat.Value + difference;

        target.SetProperty(PropertyFloat.WeaponOffense, final);
    }

    private static void ScaleUpWeaponPhysicalDefense(WorldObject target, int currentTier, int newTier)
    {
        var currentStat = target.WeaponPhysicalDefense;

        if (currentStat == null)
        {
            return;
        }

        var currentTierBonus = LootTables.WeaponDefenseModBonusPerTier[currentTier];
        var newTierBonus = LootTables.WeaponDefenseModBonusPerTier[newTier];
        var difference = newTierBonus - currentTierBonus;

        var final = currentStat.Value + difference;

        target.SetProperty(PropertyFloat.WeaponPhysicalDefense, final);
    }

    private static void ScaleUpWeaponMagicDefense(WorldObject target, int currentTier, int newTier)
    {
        var currentStat = target.WeaponMagicalDefense;

        if (currentStat == null)
        {
            return;
        }

        var currentTierBonus = LootTables.WeaponDefenseModBonusPerTier[currentTier];
        var newTierBonus = LootTables.WeaponDefenseModBonusPerTier[newTier];
        var difference = newTierBonus - currentTierBonus;

        var final = currentStat.Value + difference;

        target.SetProperty(PropertyFloat.WeaponMagicalDefense, final);
    }

    private static void ScaleUpWeaponSkillMod(PropertyFloat property, WorldObject target, int currentTier, int newTier)
    {
        var currentStat = target.GetProperty(property);

        if (currentStat == null)
        {
            return;
        }

        var currentTierBonus = LootTables.WeaponSkillModBonusPerTier[currentTier];
        var newTierBonus = LootTables.WeaponSkillModBonusPerTier[newTier];
        var difference = newTierBonus - currentTierBonus;

        var final = currentStat.Value + difference;

        target.SetProperty(property, final);
    }

    private static void ScaleUpArmorLevel(WorldObject target, int currentTier, int newTier)
    {
        if (target.ArmorLevel == null)
        {
            return;
        }

        var armorStyleBaseArmorLevel = 50;

        if (target.ArmorStyle != null)
        {
            armorStyleBaseArmorLevel = (ArmorStyle)target.ArmorStyle switch
            {
                ArmorStyle.Amuli or ArmorStyle.Chiran
                    or ArmorStyle.OlthoiAmuli or ArmorStyle.Leather
                    or ArmorStyle.Yoroi or ArmorStyle.Lorica
                    or ArmorStyle.Buckler or ArmorStyle.SmallShield
                    => 75,
                ArmorStyle.StuddedLeather or ArmorStyle.Koujia
                    or ArmorStyle.OlthoiKoujia
                    => 90,
                ArmorStyle.Chainmail or ArmorStyle.Scalemail
                    or ArmorStyle.Nariyid or ArmorStyle.StandardShield
                    => 100,
                ArmorStyle.LargeShield
                    => 105,
                ArmorStyle.Platemail or ArmorStyle.Celdon
                    or ArmorStyle.OlthoiCeldon or ArmorStyle.TowerShield
                    => 110,
                ArmorStyle.Covenant or ArmorStyle.OlthoiArmor
                    or ArmorStyle.CovenantShield
                    => 125,
                _ => armorStyleBaseArmorLevel
            };
        }

        var currentLevel = target.ArmorLevel.Value;
        var currentTierMinimum = armorStyleBaseArmorLevel * Math.Clamp(currentTier, 1, 7);
        var rolledAmount = currentLevel - currentTierMinimum;

        var newTierMinimum = armorStyleBaseArmorLevel * Math.Clamp(newTier, 1, 7);
        var final = newTierMinimum + rolledAmount;

        target.SetProperty(PropertyInt.ArmorLevel, final);
    }

    private static void ScaleUpWardLevel(WorldObject target, int currentTier, int newTier)
    {
        if (target.WardLevel == null)
        {
            return;
        }

        var armorStyleBaseWardLevel = 7;

        if (target.ArmorStyle != null)
        {
            armorStyleBaseWardLevel = (ArmorStyle)target.ArmorStyle switch
            {
                ArmorStyle.CovenantShield => 10,
                ArmorStyle.TowerShield => 8,
                ArmorStyle.LargeShield => 7,
                ArmorStyle.Amuli or ArmorStyle.Chiran
                    or ArmorStyle.OlthoiAmuli or ArmorStyle.StandardShield
                    => 6,
                ArmorStyle.Buckler or ArmorStyle.SmallShield => 5,
                ArmorStyle.Leather or ArmorStyle.StuddedLeather
                    or ArmorStyle.Koujia or ArmorStyle.OlthoiKoujia
                    or ArmorStyle.Chainmail or ArmorStyle.Scalemail
                    or ArmorStyle.Nariyid or ArmorStyle.Platemail
                    or ArmorStyle.Celdon or ArmorStyle.OlthoiCeldon
                    or ArmorStyle.Covenant or ArmorStyle.OlthoiArmor
                    => 5,
                _ => armorStyleBaseWardLevel
            };
        }

        var necklaceMulti = target.ValidLocations is EquipMask.NeckWear ? 2 : 1;
        armorStyleBaseWardLevel *= necklaceMulti;

        var currentLevel = target.WardLevel.Value;
        var armorSlots = target.ArmorSlots ?? 1;
        var wardPerSlot = currentLevel / armorSlots;

        var currentTierMinimum = armorStyleBaseWardLevel * Math.Clamp(currentTier, 1, 7);
        var rolledAmount = wardPerSlot - currentTierMinimum;

        var newTierMinimum = armorStyleBaseWardLevel * Math.Clamp(newTier, 1, 7);
        var final = (newTierMinimum + rolledAmount) * armorSlots;

        target.SetProperty(PropertyInt.WardLevel, final);
    }

    private static void ScaleUpArmorSkillMod(PropertyFloat property, WorldObject target, int currentTier, int newTier)
    {
        var currentStat = target.GetProperty(property);

        if (currentStat == null)
        {
            return;
        }

        var currentTierBonus = LootTables.ArmorSkillModBonusPerTier[currentTier];
        var newTierBonus = LootTables.ArmorSkillModBonusPerTier[newTier];
        var difference = newTierBonus - currentTierBonus;

        var final = currentStat.Value + difference;

        target.SetProperty(property, final);
    }

    private static void ScaleUpJewelryWardLevel(WorldObject target, int currentTier, int newTier)
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
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierRange =
            (jewelryBaseWardLevelPerTier[newTier + 1] * necklaceMultiplier)
            - (jewelryBaseWardLevelPerTier[newTier] * necklaceMultiplier);
        var amountAboveMinimum = newTierRange * rollPercentile;

        var newBaseLevelFromTier = jewelryBaseWardLevelPerTier[newTier] * necklaceMultiplier;
        var final = Convert.ToInt32(newBaseLevelFromTier + amountAboveMinimum);

        target.SetProperty(PropertyInt.WardLevel, final);
    }

    private static void ScaleUpJewelryRating(PropertyInt property, WorldObject target, int currentTier, int newTier)
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
        var rollPercentile = (float)currentRoll / currentRange;

        var newTierRange = jewelryBaseRatingPerTier[newTier];
        var amountAboveMinimum = newTierRange * rollPercentile;
        var newBaseLevelFromTier = jewelryBaseRatingPerTier[newTier];
        var final = Convert.ToInt32(newBaseLevelFromTier + amountAboveMinimum);

        target.SetProperty(property, final);
    }

    private static void ScaleUpItemMana(WorldObject target, int newTier)
    {
        var slots = 1;

        if (target.IsTwoHanded || target.WeenieType == WeenieType.MissileLauncher)
        {
            slots = 2;
        }
        else if (target.ArmorSlots != null)
        {
            slots = target.ArmorSlots.Value;
        }

        slots = Math.Clamp(slots, 1, LootTables.QuestItemManaRate.Length);
        newTier = Math.Clamp(newTier, 0, LootTables.QuestItemManaRate[0].Length - 1);

        var manaRate = LootTables.QuestItemManaRate[slots - 1][newTier];
        target.SetProperty(PropertyFloat.ManaRate, manaRate);

        var totalMana = LootTables.QuestItemTotalMana[slots - 1][newTier];
        target.SetProperty(PropertyInt.ItemMaxMana, totalMana);
        target.SetProperty(PropertyInt.ItemCurMana, totalMana);
    }

    private static void ScaleUpSpells(WorldObject target, int currentTier, int newTier)
    {
        if (newTier is < 3 or > 7)
        {
            return;
        }

        if (currentTier % 2 == 0 && newTier == currentTier + 1)
        {
            return;
        }

        ScaleUpSpellbookSpells(target, newTier);
        ScaleUpDidSpell(target, newTier);
        ScaleUpProcSpell(target, newTier);
    }

    private static void ScaleUpSpellbookSpells(WorldObject target, int newTier)
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
            var spellLevel = isCantrip
                ? CantripChance.GetQuestCantripLevelForTier(newTier)
                : Math.Clamp(newTier, 3, 7);

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

    private static void ScaleUpDidSpell(WorldObject target, int newTier)
    {
        var spellDid = target.SpellDID;

        if (spellDid == null)
        {
            return;
        }

        var minimumLevelSpellId = SpellLevelProgression.GetLevel1SpellId((SpellId)spellDid, true);
        var newSpell = SpellLevelProgression.GetSpellAtLevel(minimumLevelSpellId, newTier, true, true);

        target.SpellDID = (uint)newSpell;
    }

    private static void ScaleUpProcSpell(WorldObject target, int newTier)
    {
        var procSpell = target.ProcSpell;

        if (procSpell == null)
        {
            return;
        }

        var minimumLevelSpellId = SpellLevelProgression.GetLevel1SpellId((SpellId)procSpell, true);
        var newSpell = SpellLevelProgression.GetSpellAtLevel(minimumLevelSpellId, newTier, true, true);

        target.ProcSpell = (uint)newSpell;
    }

    private static void ScaleUpSpecialRatings(WorldObject target, int currentTier, int newTier)
    {
        var ratingList = (from id in GearRatingIds let ratingValue = target.GetProperty(id) where ratingValue != null select id).ToList();

        if (ratingList.Count == 0)
        {
            return;
        }

        var multiplier = GetAverageTierRatingMultiplier(currentTier, newTier);

        foreach (var itemRating in ratingList)
        {
            var currentRatingValue = target.GetProperty(itemRating) ?? 1;
            target.SetProperty(itemRating, Convert.ToInt32(currentRatingValue * multiplier));
        }
    }

    private static float GetAverageTierRatingMultiplier(int currentTier, int newTier)
    {
        int[] averageRatingValuesPerTier = [1, 2, 3, 4, 5, 6, 8, 10];
        return (float)averageRatingValuesPerTier[Math.Clamp(newTier, 0, averageRatingValuesPerTier.Length - 1)]
            / averageRatingValuesPerTier[Math.Clamp(currentTier, 0, averageRatingValuesPerTier.Length - 1)];
    }

    private static void ScaleUpRatingByMultiplier(WorldObject target, PropertyInt property, float multiplier)
    {
        var currentRatingValue = target.GetProperty(property);

        if (currentRatingValue == null)
        {
            return;
        }

        target.SetProperty(property, Convert.ToInt32(currentRatingValue.Value * multiplier));
    }

    private static readonly PropertyInt[] GearRatingIds =
    [
        PropertyInt.GearAcid,
        PropertyInt.GearBlock,
        PropertyInt.GearBludgeon,
        PropertyInt.GearBlueFury,
        PropertyInt.GearBravado,
        PropertyInt.GearCompBurn,
        PropertyInt.GearCoordination,
        PropertyInt.GearElementalist,
        PropertyInt.GearElementalWard,
        PropertyInt.GearEndurance,
        PropertyInt.GearExperienceGain,
        PropertyInt.GearFamiliarity,
        PropertyInt.GearFire,
        PropertyInt.GearFocus,
        PropertyInt.GearFrost,
        PropertyInt.GearHardenedDefense,
        PropertyInt.GearHealBubble,
        PropertyInt.GearHealthToMana,
        PropertyInt.GearHealthToStamina,
        PropertyInt.GearItemManaUsage,
        PropertyInt.GearLifesteal,
        PropertyInt.GearLightning,
        PropertyInt.GearMagicFind,
        PropertyInt.GearManasteal,
        PropertyInt.GearNullification,
        PropertyInt.GearPhysicalWard,
        PropertyInt.GearPierce,
        PropertyInt.GearPyrealFind,
        PropertyInt.GearQuickness,
        PropertyInt.GearRedFury,
        PropertyInt.GearReprisal,
        PropertyInt.GearSelf,
        PropertyInt.GearSelflessness,
        PropertyInt.GearSelfHarm,
        PropertyInt.GearSlash,
        PropertyInt.GearStaminasteal,
        PropertyInt.GearStrength,
        PropertyInt.GearThorns,
        PropertyInt.GearThreatGain,
        PropertyInt.GearThreatReduction,
        PropertyInt.GearVipersStrike,
        PropertyInt.GearVitalsTransfer,
        PropertyInt.GearWardPen,
        PropertyInt.GearYellowFury,
    ];
}