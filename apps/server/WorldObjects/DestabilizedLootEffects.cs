using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects;

public static class DestabilizedLootEffects
{
    public const double DefaultDestabilizeVariancePercent = 20.0;
    private const double MinimumAdditiveFloatValue = 0.001;
    private const double MinimumWeaponModifierValue = 1.0;
    private const int MinimumPositiveIntValue = 1;

    private static readonly PropertyFloat[] MeleeFloatProperties =
    {
        PropertyFloat.WeaponOffense,
        PropertyFloat.WeaponPhysicalDefense,
        PropertyFloat.WeaponMagicalDefense,
    };

    private static readonly PropertyInt[] MeleeIntProperties =
    {
        PropertyInt.Damage,
    };

    private static readonly PropertyFloat[] MissileFloatProperties =
    {
        PropertyFloat.DamageMod,
        PropertyFloat.WeaponOffense,
        PropertyFloat.WeaponPhysicalDefense,
        PropertyFloat.WeaponMagicalDefense,
        PropertyFloat.CriticalFrequency,
        PropertyFloat.CriticalMultiplier,
        PropertyFloat.IgnoreArmor,
    };

    private static readonly PropertyFloat[] ArmorOrClothingFloatProperties =
    {
        PropertyFloat.ArmorWarMagicMod,
        PropertyFloat.ArmorLifeMagicMod,
        PropertyFloat.ArmorAttackMod,
        PropertyFloat.ArmorPhysicalDefMod,
        PropertyFloat.ArmorMagicDefMod,
        PropertyFloat.ArmorShieldMod,
        PropertyFloat.ArmorTwohandedCombatMod,
        PropertyFloat.ArmorDualWieldMod,
        PropertyFloat.ArmorPerceptionMod,
        PropertyFloat.ArmorDeceptionMod,
        PropertyFloat.ArmorThieveryMod,
        PropertyFloat.ArmorManaRegenMod,
        PropertyFloat.ArmorStaminaRegenMod,
        PropertyFloat.ArmorHealthRegenMod,
        PropertyFloat.ManaConversionMod,
    };

    private static readonly PropertyInt[] ArmorOrClothingIntProperties =
    {
        PropertyInt.ArmorLevel,
    };

    private static readonly PropertyInt[] JewelryIntProperties =
    {
        PropertyInt.WardLevel,
        PropertyInt.GearHealingBoost,
        PropertyInt.GearMaxHealth,
        PropertyInt.GearMaxStamina,
        PropertyInt.GearMaxMana,
        PropertyInt.GearCritDamage,
        PropertyInt.GearCritResist,
        PropertyInt.GearCritDamageResist,
        PropertyInt.GearDamage,
        PropertyInt.GearDamageResist,
    };

    private static readonly PropertyFloat[] CasterFloatProperties =
    {
        PropertyFloat.ElementalDamageMod,
        PropertyFloat.WeaponRestorationSpellsMod,
        PropertyFloat.WeaponPhysicalDefense,
        PropertyFloat.WeaponMagicalDefense,
        PropertyFloat.WeaponWarMagicMod,
        PropertyFloat.WeaponLifeMagicMod,
        PropertyFloat.ManaConversionMod,
    };

    private static readonly PropertyFloat[] SigilFloatProperties =
    {
        PropertyFloat.CooldownDuration,
        PropertyFloat.SigilTrinketTriggerChance,
        PropertyFloat.SigilTrinketHealthReserved,
        PropertyFloat.SigilTrinketStaminaReserved,
        PropertyFloat.SigilTrinketManaReserved,
        PropertyFloat.SigilTrinketIntensity,
        PropertyFloat.SigilTrinketReductionAmount,
    };

    public static DestabilizedRollResult ApplyDestabilize(WorldObject item)
    {
        var result = new DestabilizedRollResult();
        if (item == null)
        {
            result.Success = false;
            result.FailureReason = "That item is unavailable.";
            return result;
        }

        var family = GetEligibleFamily(item);
        if (family == DestabilizeItemFamily.None)
        {
            result.Success = false;
            result.FailureReason = "That item family is not supported by destabilize.";
            return result;
        }

        foreach (var property in GetEligibleFloatCandidates(item, family))
        {
            if (TryApplyFloatRoll(item, property, out var detail))
            {
                result.PackageDetails.Add(detail);
            }
        }

        foreach (var property in GetEligibleIntCandidates(item, family))
        {
            if (TryApplyIntRoll(item, property, out var detail))
            {
                result.PackageDetails.Add(detail);
            }
        }

        NormalizeRolledItem(item, family);

        if (result.PackageDetails.Count == 0)
        {
            result.Success = false;
            result.FailureReason = "The forge found no destabilize-eligible affixes on that item.";
            return result;
        }

        result.Success = true;
        result.AppliedPackageCount = result.PackageDetails.Count;
        return result;
    }

    public static bool CanDestabilize(WorldObject item, out string reason)
    {
        reason = null;

        if (item == null)
        {
            reason = "That item is unavailable.";
            return false;
        }

        var family = GetEligibleFamily(item);
        if (family == DestabilizeItemFamily.None)
        {
            reason = "That item family is not supported by destabilize.";
            return false;
        }

        if (GetEligibleFloatCandidates(item, family).Count == 0 && GetEligibleIntCandidates(item, family).Count == 0)
        {
            reason = "The forge found no destabilize-eligible affixes on that item.";
            return false;
        }

        return true;
    }

    private static bool TryApplyFloatRoll(WorldObject item, PropertyFloat property, out string detail)
    {
        detail = null;

        var current = item.GetProperty(property) ?? 0.0;
        var deltaPercent = RollDeltaPercent();
        var next = UsesBaselineWeaponMultiplierMath(property)
            ? 1.0 + ((current - 1.0) * (1 + deltaPercent))
            : current * (1 + deltaPercent);
        next = Math.Max(GetMinimumFloatValue(property), next);

        if (AreNearlyEqual(current, next))
        {
            return false;
        }

        item.SetProperty(property, (float)next);
        detail = FormatFloatDetail(property, current, next, next - current);
        return true;
    }

    private static bool TryApplyIntRoll(WorldObject item, PropertyInt property, out string detail)
    {
        detail = null;

        var current = item.GetProperty(property) ?? 0;
        var deltaPercent = RollDeltaPercent();
        var next = (int)Math.Round(current * (1 + deltaPercent));
        next = Math.Max(GetMinimumIntValue(property), next);

        if (next == current)
        {
            return false;
        }

        item.SetProperty(property, next);
        detail = FormatIntDetail(property, current, next, next - current);
        return true;
    }

    private static double RollDeltaPercent()
    {
        var variancePercent = Math.Clamp(
            PropertyManager.GetDouble("destabilize_variance", DefaultDestabilizeVariancePercent).Item,
            0.0,
            100.0
        );
        var varianceDecimal = variancePercent / 100.0;

        return ThreadSafeRandom.Next((float)-varianceDecimal, (float)varianceDecimal);
    }

    private static DestabilizeItemFamily GetEligibleFamily(WorldObject item)
    {
        if (item == null)
        {
            return DestabilizeItemFamily.None;
        }

        if (item.WeenieType == WeenieType.SigilTrinket)
        {
            return DestabilizeItemFamily.Sigil;
        }

        var itemType = item.ItemType;
        if (itemType.HasFlag(ItemType.Caster))
        {
            return DestabilizeItemFamily.Caster;
        }

        if (itemType.HasFlag(ItemType.MissileWeapon))
        {
            return DestabilizeItemFamily.MissileWeapon;
        }

        if (itemType.HasFlag(ItemType.MeleeWeapon))
        {
            return DestabilizeItemFamily.MeleeWeapon;
        }

        if (itemType == ItemType.Armor || itemType == ItemType.Clothing || item.WeenieType == WeenieType.Clothing)
        {
            return DestabilizeItemFamily.ArmorOrClothing;
        }

        if (itemType == ItemType.Jewelry)
        {
            return DestabilizeItemFamily.Jewelry;
        }

        return DestabilizeItemFamily.None;
    }

    private static List<PropertyFloat> GetEligibleFloatCandidates(WorldObject item, DestabilizeItemFamily family)
    {
        var results = new List<PropertyFloat>();
        foreach (var property in GetAllowedFloatProperties(family))
        {
            var current = item.GetProperty(property) ?? 0.0;
            if (!IsEligibleFloatValue(property, current))
            {
                continue;
            }

            results.Add(property);
        }

        return results;
    }

    private static List<PropertyInt> GetEligibleIntCandidates(WorldObject item, DestabilizeItemFamily family)
    {
        var results = new List<PropertyInt>();
        foreach (var property in GetAllowedIntProperties(family))
        {
            var current = item.GetProperty(property) ?? 0;
            if (current <= 0)
            {
                continue;
            }

            results.Add(property);
        }

        return results;
    }

    private static IReadOnlyList<PropertyFloat> GetAllowedFloatProperties(DestabilizeItemFamily family)
    {
        return family switch
        {
            DestabilizeItemFamily.MeleeWeapon => MeleeFloatProperties,
            DestabilizeItemFamily.MissileWeapon => MissileFloatProperties,
            DestabilizeItemFamily.ArmorOrClothing => ArmorOrClothingFloatProperties,
            DestabilizeItemFamily.Caster => CasterFloatProperties,
            DestabilizeItemFamily.Sigil => SigilFloatProperties,
            _ => Array.Empty<PropertyFloat>(),
        };
    }

    private static IReadOnlyList<PropertyInt> GetAllowedIntProperties(DestabilizeItemFamily family)
    {
        return family switch
        {
            DestabilizeItemFamily.MeleeWeapon => MeleeIntProperties,
            DestabilizeItemFamily.ArmorOrClothing => ArmorOrClothingIntProperties,
            DestabilizeItemFamily.Jewelry => JewelryIntProperties,
            _ => Array.Empty<PropertyInt>(),
        };
    }

    private static void NormalizeRolledItem(WorldObject item, DestabilizeItemFamily family)
    {
        if (family == DestabilizeItemFamily.Sigil && item is SigilTrinket sigilTrinket)
        {
            StabilizationDevice.ClampSigilStatsToGenerationBounds(sigilTrinket);
        }
    }

    private static bool IsEligibleFloatValue(PropertyFloat property, double value)
    {
        return property switch
        {
            PropertyFloat.DamageMod
                or PropertyFloat.ElementalDamageMod
                or PropertyFloat.WeaponRestorationSpellsMod
                or PropertyFloat.WeaponOffense
                or PropertyFloat.WeaponPhysicalDefense
                or PropertyFloat.WeaponMagicalDefense
                => value > 1.001,
            PropertyFloat.CriticalFrequency
                or PropertyFloat.CriticalMultiplier
                or PropertyFloat.IgnoreArmor => value > 0.001,
            _ => value >= MinimumAdditiveFloatValue,
        };
    }

    private static double GetMinimumFloatValue(PropertyFloat property)
    {
        return property switch
        {
            PropertyFloat.DamageMod
                or PropertyFloat.ElementalDamageMod
                or PropertyFloat.WeaponRestorationSpellsMod
                or PropertyFloat.WeaponOffense
                or PropertyFloat.WeaponPhysicalDefense
                or PropertyFloat.WeaponMagicalDefense
                => MinimumWeaponModifierValue,
            PropertyFloat.CriticalFrequency
                or PropertyFloat.CriticalMultiplier
                or PropertyFloat.IgnoreArmor => MinimumAdditiveFloatValue,
            _ => MinimumAdditiveFloatValue,
        };
    }

    private static int GetMinimumIntValue(PropertyInt property)
    {
        return property switch
        {
            PropertyInt.Damage
                or PropertyInt.ArmorLevel
                or PropertyInt.WardLevel
                or PropertyInt.GearHealingBoost
                or PropertyInt.GearMaxHealth
                or PropertyInt.GearMaxStamina
                or PropertyInt.GearMaxMana
                or PropertyInt.GearCritDamage
                or PropertyInt.GearCritResist
                or PropertyInt.GearCritDamageResist
                or PropertyInt.GearDamage
                or PropertyInt.GearDamageResist => MinimumPositiveIntValue,
            _ => 0,
        };
    }

    private static bool AreNearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) < 0.0001;
    }

    private static string FormatFloatDetail(PropertyFloat property, double oldValue, double newValue, double delta)
    {
        if (UsesPercentPackageDetail(property))
        {
            return $"{FormatPropertyName(property)} changed from {FormatPercentNumber(oldValue)} to {FormatPercentNumber(newValue)} ({FormatSignedPercentNumber(delta)}).";
        }

        return $"{FormatPropertyName(property)} changed from {FormatFloatNumber(oldValue)} to {FormatFloatNumber(newValue)} ({FormatSignedFloatNumber(delta)}).";
    }

    private static string FormatIntDetail(PropertyInt property, int oldValue, int newValue, int delta)
    {
        return $"{FormatPropertyName(property)} changed from {oldValue} to {newValue} ({FormatSignedIntNumber(delta)}).";
    }

    private static string FormatFloatNumber(double value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPercentNumber(double value)
    {
        return (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatSignedFloatNumber(double value)
    {
        return value >= 0
            ? $"+{FormatFloatNumber(value)}"
            : $"-{FormatFloatNumber(Math.Abs(value))}";
    }

    private static string FormatSignedPercentNumber(double value)
    {
        return value >= 0
            ? $"+{FormatPercentNumber(value)}"
            : $"-{FormatPercentNumber(Math.Abs(value))}";
    }

    private static string FormatSignedIntNumber(int value)
    {
        return value >= 0
            ? $"+{value}"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool UsesPercentPackageDetail(PropertyFloat property)
    {
        return property switch
        {
            PropertyFloat.CooldownDuration
                or PropertyFloat.DamageMod
                or PropertyFloat.ElementalDamageMod
                or PropertyFloat.WeaponRestorationSpellsMod
                or PropertyFloat.WeaponOffense
                or PropertyFloat.WeaponPhysicalDefense
                or PropertyFloat.WeaponMagicalDefense
                or PropertyFloat.CriticalMultiplier => false,
            _ => true,
        };
    }

    private static bool UsesBaselineWeaponMultiplierMath(PropertyFloat property)
    {
        return property switch
        {
            PropertyFloat.DamageMod
                or PropertyFloat.ElementalDamageMod
                or PropertyFloat.WeaponRestorationSpellsMod
                or PropertyFloat.WeaponOffense
                or PropertyFloat.WeaponPhysicalDefense
                or PropertyFloat.WeaponMagicalDefense
                or PropertyFloat.CriticalMultiplier => true,
            _ => false,
        };
    }

    private static string FormatPropertyName(Enum property)
    {
        return property switch
        {
            PropertyFloat.DamageMod => "Damage Bonus",
            PropertyFloat.ElementalDamageMod => "Elemental Damage Bonus",
            PropertyFloat.WeaponRestorationSpellsMod => "Restoration Spell Bonus",
            PropertyFloat.WeaponOffense => "Attack Bonus",
            PropertyFloat.WeaponPhysicalDefense => "Physical Defense",
            PropertyFloat.WeaponMagicalDefense => "Magic Defense",
            PropertyFloat.CriticalFrequency => "Critical Chance",
            PropertyFloat.CriticalMultiplier => "Critical Damage",
            PropertyFloat.IgnoreArmor => "Armor Cleaving",
            PropertyFloat.WeaponWarMagicMod => "War Magic Bonus",
            PropertyFloat.WeaponLifeMagicMod => "Life Magic Bonus",
            PropertyFloat.ManaConversionMod => "Mana Conversion Bonus",
            PropertyFloat.CooldownDuration => "Cooldown Duration",
            PropertyFloat.SigilTrinketTriggerChance => "Proc Chance",
            PropertyFloat.SigilTrinketHealthReserved => "Health Reservation",
            PropertyFloat.SigilTrinketStaminaReserved => "Stamina Reservation",
            PropertyFloat.SigilTrinketManaReserved => "Mana Reservation",
            PropertyFloat.SigilTrinketIntensity => "Intensity",
            PropertyFloat.SigilTrinketReductionAmount => "Reduction Amount",
            PropertyFloat.ArmorWarMagicMod => "War Magic Bonus",
            PropertyFloat.ArmorLifeMagicMod => "Life Magic Bonus",
            PropertyFloat.ArmorAttackMod => "Attack Bonus",
            PropertyFloat.ArmorPhysicalDefMod => "Physical Defense Bonus",
            PropertyFloat.ArmorMagicDefMod => "Magic Defense Bonus",
            PropertyFloat.ArmorShieldMod => "Shield Bonus",
            PropertyFloat.ArmorTwohandedCombatMod => "Two-Handed Combat Bonus",
            PropertyFloat.ArmorDualWieldMod => "Dual Wield Bonus",
            PropertyFloat.ArmorPerceptionMod => "Perception Bonus",
            PropertyFloat.ArmorDeceptionMod => "Deception Bonus",
            PropertyFloat.ArmorThieveryMod => "Thievery Bonus",
            PropertyFloat.ArmorManaRegenMod => "Mana Regen Bonus",
            PropertyFloat.ArmorStaminaRegenMod => "Stamina Regen Bonus",
            PropertyFloat.ArmorHealthRegenMod => "Health Regen Bonus",
            PropertyInt.Damage => "Damage",
            PropertyInt.ArmorLevel => "Armor Level",
            PropertyInt.WardLevel => "Ward Level",
            PropertyInt.GearHealingBoost => "Healing Boost Rating",
            PropertyInt.GearMaxHealth => "Max Health Rating",
            PropertyInt.GearMaxStamina => "Max Stamina Rating",
            PropertyInt.GearMaxMana => "Max Mana Rating",
            PropertyInt.GearCritDamage => "Critical Damage Rating",
            PropertyInt.GearCritResist => "Critical Resist Rating",
            PropertyInt.GearCritDamageResist => "Critical Damage Resist Rating",
            PropertyInt.GearDamage => "Damage Rating",
            PropertyInt.GearDamageResist => "Damage Resist Rating",
            _ => SplitPascalCase(property.ToString()),
        };
    }

    private static string SplitPascalCase(string source)
    {
        var builder = new StringBuilder(source.Length + 8);

        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];

            if (index > 0 && char.IsUpper(current))
            {
                var previous = source[index - 1];
                if (!char.IsUpper(previous))
                {
                    builder.Append(' ');
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}

public enum DestabilizeItemFamily
{
    None,
    MeleeWeapon,
    MissileWeapon,
    ArmorOrClothing,
    Caster,
    Jewelry,
    Sigil,
}

public sealed class DestabilizedRollResult
{
    public bool Success { get; set; }

    public string FailureReason { get; set; }

    public int AppliedPackageCount { get; set; }

    public int ExceptionalExtraPackageCount { get; set; }

    public List<string> PackageDetails { get; } = new();
}
