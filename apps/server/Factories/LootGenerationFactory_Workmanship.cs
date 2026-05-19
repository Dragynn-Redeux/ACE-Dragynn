// Recomputes workmanship from the item's already-scaled stats so stabilization reflects
// "what this same roll would have been at the new tier" using the existing loot-family
// workmanship formulas, instead of remapping the old workmanship value directly.
using System;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    internal static void RecalculateWorkmanshipFromCurrentStats(WorldObject wo, int tier)
    {
        switch (wo.ItemType)
        {
            case ItemType.Jewelry:
                wo.Workmanship = GetJewelryWorkmanship(wo, GetJewelryRatingPercentileFromCurrentStats(wo));
                return;

            case ItemType.Caster:
                wo.ItemWorkmanship = GetCasterWorkmanship(
                    wo,
                    GetCasterDamagePercentileFromCurrentStats(wo),
                    GetWeaponModsPercentileFromCurrentStats(wo, tier, true),
                    GetWeaponSubtypePercentileFromCurrentStats(wo, tier)
                );
                return;

            case ItemType.Weapon:
            case ItemType.MeleeWeapon:
            case ItemType.MissileWeapon:
                wo.ItemWorkmanship = GetWeaponWorkmanship(
                    wo,
                    GetWeaponDamagePercentileFromCurrentStats(wo, tier),
                    GetWeaponModsPercentileFromCurrentStats(wo, tier, false),
                    GetWeaponSubtypePercentileFromCurrentStats(wo, tier)
                );
                return;

            case ItemType.Armor:
            case ItemType.Clothing:
                wo.ItemWorkmanship = GetArmorWorkmanship(
                    wo,
                    GetArmorSkillPercentileFromCurrentStats(wo, tier),
                    GetArmorGearRatingPercentileFromCurrentStats(wo)
                );
                return;
        }
    }

    private static float GetWeaponDamagePercentileFromCurrentStats(WorldObject wo, int tier)
    {
        if (wo.ItemType == ItemType.MissileWeapon)
        {
            return GetMissileDamagePercentileFromCurrentStats(wo, tier);
        }

        if (wo.Damage == null || wo.DamageVariance == null || wo.WeaponTime == null)
        {
            return 0.0f;
        }

        var effectiveAttacksPerSecond = GetWeaponAttacksPerSecond(wo, tier);
        if (effectiveAttacksPerSecond <= 0)
        {
            return 0.0f;
        }

        var targetBaseDps = GetWeaponBaseDps(8);
        var targetAverageHitDamage = targetBaseDps / effectiveAttacksPerSecond;
        var weaponVariance = wo.DamageVariance.Value;
        var averageBaseMaxDamage = targetAverageHitDamage / ((((1 - weaponVariance) + 1) / 2 * 0.9) + 0.2);
        const double damageRangePerTier = 0.25;
        var maximumBaseMaxDamage = (averageBaseMaxDamage * 2) / (1.0 + (1 - damageRangePerTier));

        if (maximumBaseMaxDamage <= 0)
        {
            return 0.0f;
        }

        return Math.Clamp((float)(wo.Damage.Value / maximumBaseMaxDamage), 0.0f, 1.0f);
    }

    private static float GetMissileDamagePercentileFromCurrentStats(WorldObject wo, int tier)
    {
        if (wo.DamageMod == null || wo.WeaponTime == null)
        {
            return 0.0f;
        }

        var effectiveAttacksPerSecond = GetWeaponAttacksPerSecond(wo, tier);
        if (effectiveAttacksPerSecond <= 0)
        {
            return 0.0f;
        }

        var targetBaseDps = GetWeaponBaseDps(8);
        var ammoMaxDamage = GetAmmoBaseMaxDamage(wo.WeaponSkill, 8);
        var weaponVariance = GetAmmoVariance(wo.WeaponSkill);
        var ammoMinDamage = ammoMaxDamage * (1 - weaponVariance);
        var ammoAverageDamage = (ammoMaxDamage + ammoMinDamage) / 2;
        var targetAverageHitDamage = targetBaseDps / effectiveAttacksPerSecond;
        var averageBaseDamageMod = targetAverageHitDamage / ((ammoAverageDamage * 0.9) + (ammoMaxDamage * 0.2));
        const double damageRangePerTier = 0.25;
        var maximumBaseMaxDamageMod = (averageBaseDamageMod * 2) / (1.0 + (1 - damageRangePerTier));

        if (maximumBaseMaxDamageMod <= 1)
        {
            return 0.0f;
        }

        return Math.Clamp((float)((wo.DamageMod.Value - 1) / (maximumBaseMaxDamageMod - 1)), 0.0f, 1.0f);
    }

    private static double GetCasterDamagePercentileFromCurrentStats(WorldObject wo)
    {
        var damageRoll = wo.WieldSkillType2 == (int)Skill.WarMagic
            ? wo.ElementalDamageMod ?? 1.0
            : ((wo.ElementalDamageMod ?? wo.WeaponRestorationSpellsMod ?? 1.0) - 1.0) * 2.0 + 1.0;

        var maxPossibleDamage = GetCasterMaxDamageMod()[7];
        if (maxPossibleDamage <= 1)
        {
            return 0.0;
        }

        return Math.Clamp((damageRoll - 1.0) / (maxPossibleDamage - 1.0), 0.0, 1.0);
    }

    private static float GetWeaponModsPercentileFromCurrentStats(WorldObject wo, int tier, bool caster)
    {
        var rolledTypeCount = GetWeaponRolledTypeCount(wo, caster, tier);
        if (rolledTypeCount == 0)
        {
            return 0.0f;
        }

        var multiplier = GetRolledTypesMultiplier(rolledTypeCount);
        var highestPercentile = 0.0f;

        if (caster)
        {
            var casterSubtype = GetCasterSubType(wo);
            if (casterSubtype != 3)
            {
                if ((wo.WeaponWarMagicMod ?? 0) > 0)
                {
                    highestPercentile = Math.Max(highestPercentile, GetWeaponModPercentile((wo.WeaponWarMagicMod ?? 0) / multiplier));
                }

                if ((wo.WeaponLifeMagicMod ?? 0) > 0)
                {
                    highestPercentile = Math.Max(highestPercentile, GetWeaponModPercentile((wo.WeaponLifeMagicMod ?? 0) / multiplier));
                }
            }
        }
        else if ((wo.WeaponOffense ?? 0) > 0)
        {
            highestPercentile = Math.Max(highestPercentile, GetWeaponModPercentile(((wo.WeaponOffense ?? 0) - 1) / multiplier));
        }

        if (ShouldUseWeaponDefenseForMods(wo, caster))
        {
            if ((wo.WeaponPhysicalDefense ?? 0) > 0)
            {
                highestPercentile = Math.Max(highestPercentile, GetWeaponModPercentile(((wo.WeaponPhysicalDefense ?? 0) - 1) / multiplier));
            }

            if ((wo.WeaponMagicalDefense ?? 0) > 0)
            {
                highestPercentile = Math.Max(highestPercentile, GetWeaponModPercentile(((wo.WeaponMagicalDefense ?? 0) - 1) / multiplier));
            }
        }

        return highestPercentile;
    }

    private static float GetWeaponSubtypePercentileFromCurrentStats(WorldObject wo, int tier)
    {
        return wo.WeaponSkill switch
        {
            Skill.Axe or Skill.Dagger or Skill.Bow => GetCritChancePercentile(wo),
            Skill.Mace or Skill.Staff or Skill.MissileWeapons => GetCritDamagePercentile(wo),
            Skill.Sword or Skill.UnarmedCombat => Math.Clamp((float)(wo.StaminaCostReductionMod ?? 0) / 0.2f, 0.0f, 1.0f),
            Skill.Spear or Skill.Crossbow => GetIgnoreArmorPercentile(wo),
            Skill.ThrownWeapon => GetThrownSubtypePercentile(wo),
            Skill.WarMagic or Skill.LifeMagic => GetCasterSubtypePercentile(wo),
            _ => 0.0f,
        };
    }

    private static double GetArmorSkillPercentileFromCurrentStats(WorldObject wo, int tier)
    {
        var rolledTypeCount = GetArmorRolledTypeCount(wo);
        if (rolledTypeCount == 0)
        {
            return 0.0;
        }

        var multiplier = GetRolledTypesMultiplier(rolledTypeCount);
        var armorSlotsMod = (wo.ArmorSlots ?? 1.0f) / 10;
        var miscClothingMultiplier = wo.ArmorType == (int)LootTables.ArmorType.MiscClothing && wo.ArmorWeightClass == 0 ? 0.5f : 1.0f;
        var valueMultiplier = multiplier * armorSlotsMod * miscClothingMultiplier;

        if (valueMultiplier <= 0)
        {
            return 0.0;
        }

        var highestPercentile = 0.0;

        foreach (var amount in GetArmorModValues(wo))
        {
            highestPercentile = Math.Max(highestPercentile, GetArmorSkillPercentile(amount / valueMultiplier, wo));
        }

        return highestPercentile;
    }

    private static double GetArmorGearRatingPercentileFromCurrentStats(WorldObject wo)
    {
        if (wo.ItemType == ItemType.Clothing)
        {
            return 0.0;
        }

        var armorSlots = wo.ArmorSlots ?? 1;

        return (ArmorWeightClass)(wo.ArmorWeightClass ?? 0) switch
        {
            ArmorWeightClass.Cloth => AveragePercentiles(
                GetGearRatingPercentile(wo.GearDamage, armorSlots, 0),
                GetGearRatingPercentile(wo.GearHealingBoost, armorSlots, 0)
            ),
            ArmorWeightClass.Light => AveragePercentiles(
                GetGearRatingPercentile(wo.GearCritDamage, armorSlots, 0),
                GetGearRatingPercentile(wo.GearCrit, armorSlots, 0)
            ),
            ArmorWeightClass.Heavy => AveragePercentiles(
                GetGearRatingPercentile(wo.GearDamageResist, armorSlots, 0),
                GetGearRatingPercentile(wo.GearCritResist, armorSlots, 1)
            ),
            _ => 0.0,
        };
    }

    private static double GetJewelryRatingPercentileFromCurrentStats(WorldObject wo)
    {
        var necklaceMultiplier = wo.ValidLocations is EquipMask.NeckWear ? 2 : 1;
        var wardPercentile = (double)(wo.WardLevel ?? 0) / (GetBaseWardOfTier(9) * necklaceMultiplier);

        double ratingPercentile;
        if (wo.ValidLocations == EquipMask.NeckWear)
        {
            ratingPercentile = (double)Math.Max(wo.GearHealingBoost ?? 0, wo.GearMaxHealth ?? 0) / 28.0;
        }
        else if (wo.ValidLocations == EquipMask.FingerWear)
        {
            ratingPercentile = (double)Math.Max(wo.GearCritDamage ?? 0, wo.GearCritDamageResist ?? 0) / 7.0;
        }
        else if (wo.ValidLocations == EquipMask.WristWear)
        {
            ratingPercentile = (double)Math.Max(wo.GearDamage ?? 0, wo.GearDamageResist ?? 0) / 7.0;
        }
        else
        {
            ratingPercentile = 0.0;
        }

        return Math.Clamp((ratingPercentile + wardPercentile) / 2.0, 0.0, 1.0);
    }

    private static float GetWeaponAttacksPerSecond(WorldObject wo, int tier)
    {
        if (wo.WeaponTime == null)
        {
            return 0.0f;
        }

        var baseAnimLength = WeaponAnimationLength.GetWeaponAnimLength(wo);
        int[] avgQuickPerTier = [45, 65, 93, 118, 140, 160, 180, 195];
        tier = Math.Clamp(tier, 0, avgQuickPerTier.Length - 1);
        var quick = (float)avgQuickPerTier[tier];
        var speedMod = 1.0f + (1 - (wo.WeaponTime.Value / 100.0)) + quick / 600;

        if (wo.ItemType == ItemType.MissileWeapon)
        {
            float reloadAnimLength;
            if (wo.WeaponSkill == Skill.Bow)
            {
                reloadAnimLength = 0.32f;
            }
            else if (wo.WeaponSkill == Skill.Crossbow)
            {
                reloadAnimLength = 0.26f;
            }
            else
            {
                reloadAnimLength = 0.73f;
            }

            return (float)(1 / (baseAnimLength - reloadAnimLength + (reloadAnimLength / speedMod)));
        }

        var effectiveAttacksPerSecond = (float)(1 / (baseAnimLength / speedMod));
        if (wo.IsTwoHanded || wo.W_AttackType == AttackType.DoubleStrike)
        {
            effectiveAttacksPerSecond *= 2;
        }
        else if (wo.W_AttackType == AttackType.TripleStrike)
        {
            effectiveAttacksPerSecond *= 3;
        }
        else if (wo.W_WeaponType == WeaponType.Thrown)
        {
            const float reloadLength = 0.9777778f;
            effectiveAttacksPerSecond = (float)(1 / (baseAnimLength - reloadLength + (reloadLength * speedMod)));
        }

        return effectiveAttacksPerSecond;
    }

    private static int GetWeaponRolledTypeCount(WorldObject wo, bool caster, int tier)
    {
        var count = 0;

        if (caster)
        {
            var casterSubtype = GetCasterSubType(wo);
            if (casterSubtype != 3 && ((wo.WeaponWarMagicMod ?? 0) > 0 || (wo.WeaponLifeMagicMod ?? 0) > 0))
            {
                count++;
            }
        }
        else if ((wo.WeaponOffense ?? 0) > 0)
        {
            count++;
        }

        if (ShouldUseWeaponDefenseForMods(wo, caster))
        {
            if ((wo.WeaponPhysicalDefense ?? 0) > 0 || (wo.WeaponMagicalDefense ?? 0) > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool ShouldUseWeaponDefenseForMods(WorldObject wo, bool caster)
    {
        if (!caster)
        {
            return true;
        }

        return GetCasterSubType(wo) != 3;
    }

    private static float GetWeaponModPercentile(double weaponMod)
    {
        var minMod = 0.1f;
        var maxPossibleMod = minMod + minMod + LootTables.WeaponSkillModBonusPerTier[7];
        return Math.Clamp((float)((weaponMod - minMod) / (maxPossibleMod - minMod)), 0.0f, 1.0f);
    }

    private static float GetThrownSubtypePercentile(WorldObject wo)
    {
        return GetThrownWeaponsSubType(wo) switch
        {
            0 or 2 => GetCritChancePercentile(wo),
            1 or 5 => GetCritDamagePercentile(wo),
            3 or 4 => GetIgnoreArmorPercentile(wo),
            _ => 0.0f,
        };
    }

    private static float GetCasterSubtypePercentile(WorldObject wo)
    {
        return GetCasterSubType(wo) switch
        {
            0 => GetIgnoreWardPercentile(wo),
            1 => GetCritChancePercentile(wo),
            2 => GetCritDamagePercentile(wo),
            _ => 0.0f,
        };
    }

    private static float GetCritChancePercentile(WorldObject wo)
    {
        return Math.Clamp((float)(((wo.CriticalFrequency ?? 0.1) - 0.1) / 0.1), 0.0f, 1.0f);
    }

    private static float GetCritDamagePercentile(WorldObject wo)
    {
        return Math.Clamp((float)(((wo.GetProperty(PropertyFloat.CriticalMultiplier) ?? 1.0) - 1.0) / 1.0), 0.0f, 1.0f);
    }

    private static float GetIgnoreArmorPercentile(WorldObject wo)
    {
        return Math.Clamp((float)((1.0 - (wo.IgnoreArmor ?? 1.0)) / 0.2), 0.0f, 1.0f);
    }

    private static float GetIgnoreWardPercentile(WorldObject wo)
    {
        return Math.Clamp((float)((1.0 - (wo.IgnoreWard ?? 1.0)) / 0.2), 0.0f, 1.0f);
    }

    private static int GetArmorRolledTypeCount(WorldObject wo)
    {
        if (wo.ArmorType == (int)LootTables.ArmorType.MiscClothing && wo.ArmorWeightClass == 0)
        {
            var count = 0;
            if ((wo.ArmorHealthMod ?? 0) > 0)
            {
                count++;
            }

            if ((wo.ArmorStaminaMod ?? 0) > 0)
            {
                count++;
            }

            if ((wo.ArmorManaMod ?? 0) > 0)
            {
                count++;
            }

            return count;
        }

        return (ArmorWeightClass)(wo.ArmorWeightClass ?? 0) switch
        {
            ArmorWeightClass.Cloth => CountTrue(
                (wo.ArmorWarMagicMod ?? 0) > 0,
                (wo.ArmorLifeMagicMod ?? 0) > 0,
                (wo.ArmorPerceptionMod ?? 0) > 0 || (wo.ArmorDeceptionMod ?? 0) > 0,
                (wo.ArmorManaRegenMod ?? 0) > 0,
                (wo.ManaConversionMod ?? 0) > 0
            ),
            ArmorWeightClass.Light => CountTrue(
                (wo.ArmorAttackMod ?? 0) > 0,
                (wo.ArmorDualWieldMod ?? 0) > 0 || (wo.ArmorShieldMod ?? 0) > 0,
                (wo.ArmorThieveryMod ?? 0) > 0 || (wo.ArmorRunMod ?? 0) > 0,
                (wo.ArmorStaminaRegenMod ?? 0) > 0,
                (wo.ArmorPerceptionMod ?? 0) > 0 || (wo.ArmorDeceptionMod ?? 0) > 0
            ),
            ArmorWeightClass.Heavy => CountTrue(
                (wo.ArmorAttackMod ?? 0) > 0,
                (wo.ArmorPhysicalDefMod ?? 0) > 0 || (wo.ArmorMagicDefMod ?? 0) > 0,
                (wo.ArmorShieldMod ?? 0) > 0 || (wo.ArmorTwohandedCombatMod ?? 0) > 0,
                (wo.ArmorPerceptionMod ?? 0) > 0 || (wo.ArmorDeceptionMod ?? 0) > 0,
                (wo.ArmorHealthRegenMod ?? 0) > 0
            ),
            _ => 0,
        };
    }

    private static double[] GetArmorModValues(WorldObject wo)
    {
        if (wo.ArmorType == (int)LootTables.ArmorType.MiscClothing && wo.ArmorWeightClass == 0)
        {
            return FilterPositive([
                wo.ArmorHealthMod ?? 0,
                wo.ArmorStaminaMod ?? 0,
                wo.ArmorManaMod ?? 0,
            ]);
        }

        return (ArmorWeightClass)(wo.ArmorWeightClass ?? 0) switch
        {
            ArmorWeightClass.Cloth => FilterPositive([
                wo.ArmorWarMagicMod ?? 0,
                wo.ArmorLifeMagicMod ?? 0,
                Math.Max(wo.ArmorPerceptionMod ?? 0, wo.ArmorDeceptionMod ?? 0),
                wo.ArmorManaRegenMod ?? 0,
                wo.ManaConversionMod ?? 0,
            ]),
            ArmorWeightClass.Light => FilterPositive([
                wo.ArmorAttackMod ?? 0,
                Math.Max(wo.ArmorDualWieldMod ?? 0, wo.ArmorShieldMod ?? 0),
                Math.Max(wo.ArmorThieveryMod ?? 0, wo.ArmorRunMod ?? 0),
                wo.ArmorStaminaRegenMod ?? 0,
                Math.Max(wo.ArmorPerceptionMod ?? 0, wo.ArmorDeceptionMod ?? 0),
            ]),
            ArmorWeightClass.Heavy => FilterPositive([
                wo.ArmorAttackMod ?? 0,
                Math.Max(wo.ArmorPhysicalDefMod ?? 0, wo.ArmorMagicDefMod ?? 0),
                Math.Max(wo.ArmorShieldMod ?? 0, wo.ArmorTwohandedCombatMod ?? 0),
                Math.Max(wo.ArmorPerceptionMod ?? 0, wo.ArmorDeceptionMod ?? 0),
                wo.ArmorHealthRegenMod ?? 0,
            ]),
            _ => [],
        };
    }

    private static double GetArmorSkillPercentile(double armorMod, WorldObject wo)
    {
        var doubleMod = wo.ValidLocations == EquipMask.HeadWear ? 2.0 : 1.0;
        var minMod = 0.1 * doubleMod;
        var maxPossibleMod = minMod + minMod + LootTables.ArmorSkillModBonusPerTier[7];

        return Math.Clamp((armorMod - minMod) / (maxPossibleMod - minMod), 0.0, 1.0);
    }

    private static double GetGearRatingPercentile(int? value, int armorSlots, int baseOffset)
    {
        if (!value.HasValue || armorSlots <= 0)
        {
            return 0.0;
        }

        return Math.Clamp(((double)value.Value / armorSlots - baseOffset) / 3.0, 0.0, 1.0);
    }

    private static double AveragePercentiles(double first, double second)
    {
        if (first <= 0 && second <= 0)
        {
            return 0.0;
        }

        if (first <= 0)
        {
            return second;
        }

        if (second <= 0)
        {
            return first;
        }

        return (first + second) / 2.0;
    }

    private static float GetRolledTypesMultiplier(int count)
    {
        return count switch
        {
            <= 1 => 1.0f,
            2 => 0.75f,
            3 => 0.5833f,
            4 => 0.475f,
            _ => 0.4f,
        };
    }

    private static int CountTrue(params bool[] values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (value)
            {
                count++;
            }
        }

        return count;
    }

    private static double[] FilterPositive(double[] values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (value > 0)
            {
                count++;
            }
        }

        var result = new double[count];
        var index = 0;
        foreach (var value in values)
        {
            if (value > 0)
            {
                result[index++] = value;
            }
        }

        return result;
    }
}