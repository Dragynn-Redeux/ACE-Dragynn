using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;

namespace ACE.Server.WorldObjects;

public static class DestabilizedLootEffects
{
    public const double BaseDestabilizePercent = 0.20;
    public const double ExceptionalChainChance = 0.001;
    public const int MaxExceptionalExtraPackages = 3;

    private const double PositiveWeight = 0.40;
    private const double MixedWeight = 0.40;

    private static readonly double[] TierFactors = { 0.85, 0.95, 1.00, 1.08, 1.16, 1.24, 1.32 };

    private static readonly PropertyFloat[] NumericFloatProperties =
    {
        PropertyFloat.WeaponOffense,
        PropertyFloat.DamageMod,
        PropertyFloat.WeaponPhysicalDefense,
        PropertyFloat.WeaponMagicalDefense,
        PropertyFloat.WeaponWarMagicMod,
        PropertyFloat.WeaponLifeMagicMod,
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
        PropertyFloat.ElementalDamageMod,
    };

    private static readonly PropertyInt[] NumericIntProperties =
    {
        PropertyInt.Damage,
        PropertyInt.ArmorLevel,
        PropertyInt.ItemMaxMana,
        PropertyInt.ElementalDamageBonus,
    };

    private static readonly ImbuedEffectType[] ImbueEffects =
    {
        ImbuedEffectType.CriticalStrike,
        ImbuedEffectType.CripplingBlow,
        ImbuedEffectType.ArmorRending,
        ImbuedEffectType.WardRending,
        ImbuedEffectType.ColdRending,
        ImbuedEffectType.PierceRending,
        ImbuedEffectType.AcidRending,
        ImbuedEffectType.SlashRending,
        ImbuedEffectType.ElectricRending,
        ImbuedEffectType.BludgeonRending,
    };

    private static readonly PropertyInt[] ConditionalTagProperties =
    {
        PropertyInt.GearReprisal,
        PropertyInt.GearFamiliarity,
        PropertyInt.GearBravado,
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

        var nonValueAppliedKeys = new HashSet<string>(StringComparer.Ordinal);
        var extraCount = RollExceptionalExtraPackageCount();
        var totalPackages = 1 + extraCount;

        for (var packageIndex = 0; packageIndex < totalPackages; packageIndex++)
        {
            var polarity = RollPolarity();
            if (!ApplyPackage(item, polarity, nonValueAppliedKeys, out var detail))
            {
                result.Success = false;
                result.FailureReason = "The forge could not imprint a destabilized outcome on that item.";
                return result;
            }

            result.AppliedPackageCount++;
            result.PackageDetails.Add(detail);
        }

        result.Success = true;
        result.ExceptionalExtraPackageCount = extraCount;
        return result;
    }

    private static int RollExceptionalExtraPackageCount()
    {
        var extra = 0;
        while (extra < MaxExceptionalExtraPackages && ThreadSafeRandom.Next(0.0f, 1.0f) < ExceptionalChainChance)
        {
            extra++;
        }

        return extra;
    }

    private static DestabilizePolarity RollPolarity()
    {
        var roll = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (roll < PositiveWeight)
        {
            return DestabilizePolarity.NetPositive;
        }

        if (roll < PositiveWeight + MixedWeight)
        {
            return DestabilizePolarity.MixedTradeoff;
        }

        return DestabilizePolarity.NetNegative;
    }

    private static bool ApplyPackage(
        WorldObject item,
        DestabilizePolarity polarity,
        HashSet<string> nonValueAppliedKeys,
        out string detail
    )
    {
        detail = null;

        switch (polarity)
        {
            case DestabilizePolarity.NetPositive:
                return ApplyNetPositive(item, nonValueAppliedKeys, out detail);
            case DestabilizePolarity.MixedTradeoff:
                return ApplyMixedTradeoff(item, nonValueAppliedKeys, out detail);
            default:
                return ApplyNumericComponent(item, isPositive: false, out detail);
        }
    }

    private static bool ApplyNetPositive(WorldObject item, HashSet<string> nonValueAppliedKeys, out string detail)
    {
        detail = null;

        // Keep non-value outcomes in rotation without dominating value-based property rolls.
        var tryNonValue = ThreadSafeRandom.Next(0.0f, 1.0f) < 0.30f;
        if (tryNonValue && TryApplyNonValueComponent(item, nonValueAppliedKeys, out detail))
        {
            return true;
        }

        return ApplyNumericComponent(item, isPositive: true, out detail);
    }

    private static bool ApplyMixedTradeoff(WorldObject item, HashSet<string> nonValueAppliedKeys, out string detail)
    {
        detail = null;

        var positiveDetail = string.Empty;
        var positiveFromNonValue = ThreadSafeRandom.Next(0.0f, 1.0f) < 0.30f
            && TryApplyNonValueComponent(item, nonValueAppliedKeys, out positiveDetail);

        if (!positiveFromNonValue && !ApplyNumericComponent(item, isPositive: true, out positiveDetail))
        {
            return false;
        }

        if (!ApplyNumericComponent(item, isPositive: false, out var negativeDetail))
        {
            return false;
        }

        detail = $"Mixed: {positiveDetail}; {negativeDetail}";
        return true;
    }

    private static bool ApplyNumericComponent(WorldObject item, bool isPositive, out string detail)
    {
        detail = null;

        var tierFactor = GetTierFactor(item);
        var floatCandidates = GetExistingFloatCandidates(item);
        var intCandidates = GetExistingIntCandidates(item);

        if (floatCandidates.Count == 0 && intCandidates.Count == 0)
        {
            return false;
        }

        var useFloat = floatCandidates.Count > 0 && (intCandidates.Count == 0 || ThreadSafeRandom.Next(0, 2) == 0);
        if (useFloat)
        {
            var prop = floatCandidates[ThreadSafeRandom.Next(0, floatCandidates.Count)];
            var current = item.GetProperty(prop) ?? 0.0;
            var magnitude = current * BaseDestabilizePercent * tierFactor;
            var signedDelta = isPositive ? magnitude : -magnitude;
            var next = current + signedDelta;
            item.SetProperty(prop, (float)next);
            detail = $"{(isPositive ? "+" : "-")} {prop}";
            return true;
        }

        var intProp = intCandidates[ThreadSafeRandom.Next(0, intCandidates.Count)];
        var intCurrent = item.GetProperty(intProp) ?? 0;
        var rawDelta = Math.Abs(intCurrent) * BaseDestabilizePercent * tierFactor;
        var intDelta = Math.Max(1, (int)Math.Round(rawDelta));
        var intNext = isPositive ? intCurrent + intDelta : intCurrent - intDelta;
        item.SetProperty(intProp, intNext);
        detail = $"{(isPositive ? "+" : "-")} {intProp}";
        return true;
    }

    private static bool TryApplyNonValueComponent(WorldObject item, HashSet<string> nonValueAppliedKeys, out string detail)
    {
        detail = null;

        if (IsImbueEligible(item) && TryApplyImbue(item, nonValueAppliedKeys, out detail))
        {
            return true;
        }

        return TryApplyConditionalTag(item, nonValueAppliedKeys, out detail);
    }

    private static bool TryApplyImbue(WorldObject item, HashSet<string> nonValueAppliedKeys, out string detail)
    {
        detail = null;

        const int rerollAttempts = 20;
        var existing = item.GetProperty(PropertyInt.ImbuedEffect) ?? 0;
        for (var i = 0; i < rerollAttempts; i++)
        {
            var effect = ImbueEffects[ThreadSafeRandom.Next(0, ImbueEffects.Length)];
            var effectBit = (int)effect;
            var key = $"imbue:{effectBit}";

            if (nonValueAppliedKeys.Contains(key) || (existing & effectBit) == effectBit)
            {
                continue;
            }

            item.SetProperty(PropertyInt.ImbuedEffect, existing | effectBit);
            nonValueAppliedKeys.Add(key);
            detail = $"Tag: {effect}";
            return true;
        }

        return false;
    }

    private static bool TryApplyConditionalTag(WorldObject item, HashSet<string> nonValueAppliedKeys, out string detail)
    {
        detail = null;

        const int rerollAttempts = 20;
        for (var i = 0; i < rerollAttempts; i++)
        {
            var property = ConditionalTagProperties[ThreadSafeRandom.Next(0, ConditionalTagProperties.Length)];
            var key = $"cond:{property}";
            if (nonValueAppliedKeys.Contains(key) || (item.GetProperty(property) ?? 0) > 0)
            {
                continue;
            }

            item.SetProperty(property, 1);
            nonValueAppliedKeys.Add(key);
            detail = $"Tag: {property}";
            return true;
        }

        return false;
    }

    private static bool IsImbueEligible(WorldObject item)
    {
        var itemType = item.GetProperty(PropertyInt.ItemType);
        if (!itemType.HasValue)
        {
            return false;
        }

        var typedItem = (ItemType)itemType.Value;
        return typedItem.HasFlag(ItemType.MeleeWeapon)
            || typedItem.HasFlag(ItemType.MissileWeapon)
            || typedItem.HasFlag(ItemType.Caster);
    }

    private static double GetTierFactor(WorldObject item)
    {
        var tier = item.Tier ?? 1;
        if (tier < 1)
        {
            tier = 1;
        }

        if (tier > TierFactors.Length)
        {
            tier = TierFactors.Length;
        }

        return TierFactors[tier - 1];
    }

    private static List<PropertyFloat> GetExistingFloatCandidates(WorldObject item)
    {
        var results = new List<PropertyFloat>();
        foreach (var property in NumericFloatProperties)
        {
            if (item.GetProperty(property).HasValue)
            {
                results.Add(property);
            }
        }

        return results;
    }

    private static List<PropertyInt> GetExistingIntCandidates(WorldObject item)
    {
        var results = new List<PropertyInt>();
        foreach (var property in NumericIntProperties)
        {
            if (item.GetProperty(property).HasValue)
            {
                results.Add(property);
            }
        }

        return results;
    }
}

public enum DestabilizePolarity
{
    NetPositive,
    MixedTradeoff,
    NetNegative,
}

public sealed class DestabilizedRollResult
{
    public bool Success { get; set; }

    public string FailureReason { get; set; }

    public int AppliedPackageCount { get; set; }

    public int ExceptionalExtraPackageCount { get; set; }

    public List<string> PackageDetails { get; } = new();
}
