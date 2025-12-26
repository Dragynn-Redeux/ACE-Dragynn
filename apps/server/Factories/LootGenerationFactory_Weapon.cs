using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    private static WorldObject CreateWeapon(TreasureDeath profile, bool isMagical)
    {
        var chance = ThreadSafeRandom.Next(1, 100);

        // Aligning drop ratio to better align with retail - HarliQ 11/11/19
        // Melee - 42%
        // Missile - 36%
        // Casters - 22%

        return chance switch
        {
            var rate when (rate < 43) => CreateMeleeWeapon(profile, isMagical),
            var rate when (rate > 42 && rate < 79) => CreateMissileWeapon(profile, isMagical),
            _ => CreateCaster(profile, isMagical),
        };
    }

    private static WorldObject CreateWeaponWarrior(TreasureDeath profile, bool isMagical)
    {
        var chance = ThreadSafeRandom.Next(1, 100);

        // Aligning drop ratio to better align with retail - HarliQ 11/11/19
        // Melee - 42%
        // Missile - 36%
        // Casters - 22%

        return chance switch
        {
            var rate when (rate < 43) => CreateMeleeWeapon(profile, isMagical),
            var rate when (rate > 42 && rate < 79) => CreateMissileWeapon(profile, isMagical),
            _ => CreateCaster(profile, isMagical),
        };
    }

    private static WorldObject CreateWeaponRogue(TreasureDeath profile, bool isMagical)
    {
        var chance = ThreadSafeRandom.Next(1, 100);

        // Aligning drop ratio to better align with retail - HarliQ 11/11/19
        // Melee - 42%
        // Missile - 36%
        // Casters - 22%

        return chance switch
        {
            var rate when (rate < 43) => CreateMeleeWeapon(profile, isMagical),
            var rate when (rate > 42 && rate < 79) => CreateMissileWeapon(profile, isMagical),
            _ => CreateCaster(profile, isMagical),
        };
    }

    private static WorldObject CreateWeaponCaster(TreasureDeath profile, bool isMagical)
    {
        var chance = ThreadSafeRandom.Next(1, 100);

        // Aligning drop ratio to better align with retail - HarliQ 11/11/19
        // Melee - 42%
        // Missile - 36%
        // Casters - 22%

        return chance switch
        {
            var rate when (rate < 43) => CreateMeleeWeapon(profile, isMagical),
            var rate when (rate > 42 && rate < 79) => CreateMissileWeapon(profile, isMagical),
            _ => CreateCaster(profile, isMagical),
        };
    }

    public const float MaxSpeedBonus = 0.1f;
    private static float RollWeaponSpeedMod(TreasureDeath treasureDeath)
    {
        // min/max range: 90% to 100%
        var bonus = MaxSpeedBonus * GetDiminishingRoll(treasureDeath);
        var weaponSpeedMod = 1.0f - bonus;

        //Console.WriteLine($"WeaponSpeedMod: {weaponSpeedMod}");

        return weaponSpeedMod;
    }

    private static float ApplyQualityModToExtraMutationChance(float chance, float lootQualityMod)
    {
        return chance * (1 + lootQualityMod);
    }

    private static bool RollSlayer(TreasureDeath treasureDeath, WorldObject wo)
    {
        var chance = SlayerTypeChance.GetSlayerChanceForTier(treasureDeath.Tier);
        chance = ApplyQualityModToExtraMutationChance(chance, treasureDeath.LootQualityMod);
        if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
        {
            wo.SlayerCreatureType = SlayerTypeChance.Roll(treasureDeath);
            wo.SlayerDamageBonus = 1.5f;
            wo.IconOverlayId = 0x06005EC0;
            return true;
        }
        return false;
    }

    private static bool TryMutateWeaponMods(
        WorldObject wo,
        TreasureDeath treasureDeath,
        out float highestModPercentile,
        bool caster = false
    )
    {
        highestModPercentile = 0.0f;

        var qualityMod = treasureDeath.LootQualityMod != 0.0f ? treasureDeath.LootQualityMod : 0.0f;

        var potentialTypes = new List<int>() { 1, 2 };
        var rolledTypes = GetRolledTypes(potentialTypes, qualityMod);

        // Multiplier for weapons that rolled multiple mods. Currently limited to 2 mod rolls, but ready for more.
        float numRolledTypesMultiplier;
        switch (rolledTypes.Count)
        {
            default:
            case 1:
                numRolledTypesMultiplier = 1.0f;
                break; // 100% per mod, 100% total.
            case 2:
                numRolledTypesMultiplier = 0.75f;
                break; // 75% per mod, 150% total.
            case 3:
                numRolledTypesMultiplier = 0.5833f;
                break; // 58.33% per mod, 175% total.
            case 4:
                numRolledTypesMultiplier = 0.475f;
                break; // 47.5% per mod, 190% total.
            case 5:
                numRolledTypesMultiplier = 0.4f;
                break; // 40% per mod, 200% total.
        }

        foreach (var type in rolledTypes)
        {
            float modPercentile;

            switch (type)
            {
                case 1: // Offense Mods
                    if (!caster)
                    {
                        wo.WeaponOffense =
                            GetWeaponModAmount(wo, treasureDeath, out modPercentile) * numRolledTypesMultiplier + 1;
                    }
                    else
                    {
                        if (wo.WieldSkillType2 == (int)Skill.WarMagic)
                        {
                            wo.WeaponWarMagicMod =
                                GetWeaponModAmount(wo, treasureDeath, out modPercentile) * numRolledTypesMultiplier;
                        }
                        else
                        {
                            wo.WeaponLifeMagicMod =
                                GetWeaponModAmount(wo, treasureDeath, out modPercentile) * numRolledTypesMultiplier;
                        }
                    }

                    if (modPercentile > highestModPercentile)
                    {
                        highestModPercentile = modPercentile;
                    }

                    break;
                case 2: // Defense Mods
                    float defenseModPercetile;
                    wo.WeaponPhysicalDefense =
                        GetWeaponModAmount(wo, treasureDeath, out modPercentile) * numRolledTypesMultiplier + 1;
                    defenseModPercetile = modPercentile;

                    wo.WeaponMagicalDefense =
                        GetWeaponModAmount(wo, treasureDeath, out modPercentile) * numRolledTypesMultiplier + 1;
                    defenseModPercetile += modPercentile;

                    defenseModPercetile /= 2;
                    if (defenseModPercetile > highestModPercentile)
                    {
                        highestModPercentile = modPercentile;
                    }

                    break;
            }
        }

        wo.BaseWeaponOffense = (wo.WeaponOffense == null ? 0 : wo.WeaponOffense);
        wo.BaseManaConversionMod = (wo.ManaConversionMod == null ? 0 : wo.ManaConversionMod);
        wo.BaseWeaponPhysicalDefense = (wo.WeaponPhysicalDefense == null ? 0 : wo.WeaponPhysicalDefense);
        wo.BaseWeaponMagicalDefense = (wo.WeaponMagicalDefense == null ? 0 : wo.WeaponMagicalDefense);

        return true;
    }

    private static double GetWeaponModAmount(WorldObject wo, TreasureDeath treasureDeath, out float modPercentile)
    {
        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, 7);
        float[] bonusModRollPerTier = { 0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f };

        var minMod = 0.1f;
        var weaponMod = minMod + minMod * GetDiminishingRoll(treasureDeath) + bonusModRollPerTier[tier];

        var maxPossibleMod = minMod + minMod + bonusModRollPerTier[7];

        modPercentile = (weaponMod - minMod) / (maxPossibleMod - minMod);
        return weaponMod;
    }

    private static int GetWeaponWorkmanship(
        WorldObject wo,
        float damagePercentile,
        float skillModsPercentile,
        float subtypeBonusesPercentile
    )
    {
        var divisor = 4; // damage x2 + mods + subtype

        // Average Percentile
        var finalPercentile = (damagePercentile * 2 + skillModsPercentile + subtypeBonusesPercentile) / divisor;

        //Console.WriteLine($"{wo.NameWithMaterialAndElement}\n -Mods %: {skillModsPercentile}\n" + $" -Damage %: {damagePercentile}\n" + $" -Divisor: {divisor}\n" +
        //    $" --FINAL: {finalPercentile}\n\n");

        // Workmanship Calculation
        //Console.WriteLine($"{wo.NameWithMaterialAndElement} - {Math.Max((int)(finalPercentile * 10), 1)}");

        return Math.Clamp((int)(finalPercentile * 10), 1, 10);
    }

    private static float[] GetCasterMaxDamageMod()
    {
        return LootTables.CasterMaxDamageMod;
    }

    private static float[] GetCasterMinDamageMod()
    {
        return LootTables.CasterMinDamageMod;
    }

    private static float GetWeaponBaseDps(int tier)
    {
        switch (tier)
        {
            default:
            case 1:
                return 5.0f;
            case 2:
                return 10.0f;
            case 3:
                return 15.0f;
            case 4:
                return 22.0f;
            case 5:
                return 33.0f;
            case 6:
                return 50.0f;
            case 7:
                return 75.0f;
            case 8:
                return 110.0f;
        }
    }

    private static int GetWeaponPrimaryAttribute(Skill weaponSkill)
    {
        switch (weaponSkill)
        {
            default:
            case Skill.Sword:
            case Skill.Axe:
            case Skill.Mace:
            case Skill.Spear:
            case Skill.TwoHandedCombat:
            case Skill.ThrownWeapon:
                return 1;
            case Skill.Bow:
            case Skill.Crossbow:
            case Skill.MissileWeapons:
            case Skill.Dagger:
            case Skill.Staff:
            case Skill.UnarmedCombat:
                return 4;
            case Skill.WarMagic:
            case Skill.LifeMagic:
                return 6;
        }
    }

    private static int GetWeaponWieldSkill(Skill weaponSkill)
    {
        switch (weaponSkill)
        {
            default:
            case Skill.Sword:
            case Skill.Axe:
            case Skill.Mace:
            case Skill.Spear:
                return (int)Skill.MartialWeapons;
            case Skill.ThrownWeapon:
                return (int)Skill.ThrownWeapon;
            case Skill.Bow:
            case Skill.Crossbow:
            case Skill.MissileWeapons:
                return (int)Skill.Bow;
            case Skill.Dagger:
                return (int)Skill.Dagger;
            case Skill.Staff:
                return (int)Skill.Staff;
            case Skill.UnarmedCombat:
                return (int)Skill.UnarmedCombat;
            case Skill.WarMagic:
                return (int)Skill.WarMagic;
            case Skill.LifeMagic:
                return (int)Skill.LifeMagic;
        }
    }

    private static void TryMutateWeaponSubtypeBonuses(
        WorldObject wo,
        TreasureDeath treasureDeath,
        out float subtypeBonusPercentile
    )
    {
        subtypeBonusPercentile = 0.0f;
        int subtype;

        switch (wo.WeaponSkill)
        {
            case Skill.Axe:
            case Skill.Dagger:
            case Skill.Bow:
                RollBonusCritChance(treasureDeath, wo, out subtypeBonusPercentile);
                break;
            case Skill.Mace:
            case Skill.Staff:
            case Skill.MissileWeapons:
                RollBonusCritDamage(treasureDeath, wo, out subtypeBonusPercentile);
                break;
            case Skill.Sword:
            case Skill.UnarmedCombat:
                RollBonusStaminaCostReduction(treasureDeath, wo, out subtypeBonusPercentile);
                break;
            case Skill.Spear:
            case Skill.Crossbow:
                RollBonusArmorCleaving(treasureDeath, wo, out subtypeBonusPercentile);
                break;
            case Skill.ThrownWeapon:
                subtype = GetThrownWeaponsSubType(wo);
                const int AXE = 0,
                    CLUB = 1,
                    DAGGER = 2,
                    DART = 3,
                    JAVELIN = 4,
                    SHOUKEN = 5;
                switch (subtype)
                {
                    case AXE:
                    case DAGGER:
                        RollBonusCritChance(treasureDeath, wo, out subtypeBonusPercentile);
                        break;
                    case CLUB:
                    case SHOUKEN:
                        RollBonusCritDamage(treasureDeath, wo, out subtypeBonusPercentile);
                        break;
                    case DART:
                    case JAVELIN:
                        RollBonusArmorCleaving(treasureDeath, wo, out subtypeBonusPercentile);
                        break;
                }
                break;
            case Skill.WarMagic:
            case Skill.LifeMagic:
                subtype = GetCasterSubType(wo);
                const int ORB = 0,
                    SCEPTER = 1,
                    WAND = 2,
                    STAFF = 3;
                switch (subtype)
                {
                    case ORB:
                        RollBonusWardCleaving(treasureDeath, wo, out subtypeBonusPercentile);
                        break;
                    case SCEPTER:
                        RollBonusCritChance(treasureDeath, wo, out subtypeBonusPercentile);
                        break;
                    case WAND:
                        RollBonusCritDamage(treasureDeath, wo, out subtypeBonusPercentile);
                        break;
                    case STAFF:
                        wo.WeaponPhysicalDefense += RollBonusDefenseMod(
                            treasureDeath,
                            wo,
                            out var physicalDefenseBonusPercentile
                        );
                        wo.WeaponMagicalDefense += RollBonusDefenseMod(
                            treasureDeath,
                            wo,
                            out var magicDefenseBonusPercentile
                        );
                        subtypeBonusPercentile =
                            physicalDefenseBonusPercentile > magicDefenseBonusPercentile
                                ? physicalDefenseBonusPercentile
                                : magicDefenseBonusPercentile;
                        break;
                }
                break;
            default:
                _log.Error(
                    "TryMutateWeaponSubtypeBonuses() - {Name} does not have a correct weapon skill set ({WeaponSkill}). Cannot add subtype bonus.",
                    wo.Name,
                    wo.WeaponSkill
                );
                break;
        }
    }
}
