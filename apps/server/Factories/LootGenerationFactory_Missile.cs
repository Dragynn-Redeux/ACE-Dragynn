using System.Linq;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    /// <summary>
    /// Creates and optionally mutates a new MissileWeapon
    /// </summary>
    public static WorldObject CreateMissileWeapon(TreasureDeath profile, bool isMagical, bool mutate = true)
    {
        int weaponWeenie;

        var wieldDifficulty = RollWieldDifficulty(profile.Tier, TreasureWeaponType.MissileWeapon);

        // Changing based on wield, not tier. Refactored, less code, best results.  HarliQ 11/18/19
        if (wieldDifficulty < 250) // under t5
        {
            weaponWeenie = GetNonElementalMissileWeapon();
        }
        else
        {
            if (ThreadSafeRandom.Next(0, 1) == 0)
            {
                weaponWeenie = GetElementalMissileWeapon();
            }
            else
            {
                weaponWeenie = GetNonElementalMissileWeapon();
            }
        }

        var wo = WorldObjectFactory.CreateNewWorldObject((uint)weaponWeenie);

        if (wo != null && mutate)
        {
            MutateMissileWeapon(wo, profile, isMagical, wieldDifficulty);
        }

        return wo;
    }

    private static void MutateMissileWeapon(
        WorldObject wo,
        TreasureDeath profile,
        bool isMagical,
        int? wieldDifficulty = null,
        TreasureRoll roll = null
    )
    {
        if (roll == null)
        {
            // previous method
            // DamageMod
            wo.DamageMod = GetMissileDamageMod(wieldDifficulty.Value, wo.W_WeaponType);

            // ElementalDamageBonus
            if (wo.W_DamageType != DamageType.Undef)
            {
                var elementalBonus = GetElementalDamageBonus(wieldDifficulty.Value);
                if (elementalBonus > 0)
                {
                    wo.ElementalDamageBonus = elementalBonus;
                }
            }

            // Wield Requirements
            if (wieldDifficulty > 0)
            {
                wo.WieldDifficulty = wieldDifficulty;
                wo.WieldRequirements = WieldRequirement.RawSkill;
                wo.WieldSkillType = (int)Skill.MissileWeapons;
            }
            else
            {
                wo.WieldDifficulty = null;
                wo.WieldRequirements = WieldRequirement.Invalid;
                wo.WieldSkillType = null;
            }

            // WeaponDefense
            var meleeDMod = RollWeaponDefense(wieldDifficulty.Value, profile);
            if (meleeDMod > 0.0f)
            {
                wo.WeaponDefense = meleeDMod;
            }
        }

        MaterialType[] material =
        [
            MaterialType.Ebony,
            MaterialType.Mahogany,
            MaterialType.Oak,
            MaterialType.Pine,
            MaterialType.Teak
        ];
        var materialType = ThreadSafeRandom.Next(0, 4);
        wo.MaterialType = material[materialType];

        // Add element to weapons above starter tier:
        // Longbow, Shortbow, Nayin, Shouyumi, Yag, Yumi, Warbow, Heavy Xbow, Light Xbow, Atlatl, Royal Atlatl
        if (wo.Tier > 1)
        {
            uint[] nonElementalMissileWeapons = { 306, 307, 334, 341, 360, 363, 30625, 311, 312, 12463, 20640 };
            if (nonElementalMissileWeapons.Contains(wo.WeenieClassId))
            {
                RollMissileElement(profile, wo);
            }

            MutateColor(wo);

            // Don't overwrite explicit unstable naming applied later
            if (wo.GetProperty(PropertyBool.IsUnstable) != true)
            {
                wo.Name = wo.NameWithMaterialAndElement == null ? wo.Name : wo.NameWithMaterial;
            }
        }

        // Wield Difficulty
        wo.WieldRequirements = WieldRequirement.RawAttrib;
        wo.WieldDifficulty = GetWieldDifficultyPerTier(profile.Tier);
        wo.WieldSkillType = GetWeaponPrimaryAttribute(wo.WeaponSkill);

        wo.WieldRequirements2 = WieldRequirement.Training;
        wo.WieldDifficulty2 = 2;
        wo.WieldSkillType2 = GetWeaponWieldSkill(wo.WeaponSkill);

        // Damage
        TryMutateMissileWeaponDamage(wo, roll, profile, out var damageModPercentile);

        // weapon speed
        if (wo.WeaponTime != null)
        {
            var weaponSpeedMod = RollWeaponSpeedMod(profile);
            wo.WeaponTime = (int)(wo.WeaponTime * weaponSpeedMod);
        }

        // weapon mods and subtype bonuses
        float totalModsPercentile;
        TryMutateWeaponMods(wo, profile, out totalModsPercentile);

        float subtypeBonusesPercentile;
        TryMutateWeaponSubtypeBonuses(wo, profile, out subtypeBonusesPercentile);

        // item color
        MutateColor(wo);

        // gem count / gem material
        if (wo.GemCode != null)
        {
            wo.GemCount = GemCountChance.Roll(wo.GemCode.Value, profile.Tier);
        }
        else
        {
            wo.GemCount = ThreadSafeRandom.Next(1, 5);
        }

        wo.GemType = RollGemType(profile.Tier);

        // workmanship
        wo.ItemWorkmanship = GetWeaponWorkmanship(
            wo,
            (float)damageModPercentile,
            totalModsPercentile,
            subtypeBonusesPercentile
        );

        // burden
        MutateBurden(wo, profile, true);

        // spells
        AssignMagic(wo, profile, roll, false, isMagical);

        // item value
        //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
        MutateValue(wo, profile.Tier, roll);

        // long description
        wo.LongDesc = GetLongDesc(wo);

        wo.BaseDamageMod = (wo.DamageMod == null ? 0 : wo.DamageMod);
        wo.BaseWeaponTime = (wo.WeaponTime == null ? 0 : wo.WeaponTime);
        // assign jewel slots
        AssignJewelSlots(wo);
    }

    private static bool GetMutateMissileWeaponData(uint wcid, int tier)
    {
        for (var isElemental = 0; isElemental < LootTables.MissileWeaponsMatrices.Count; isElemental++)
        {
            var table = LootTables.MissileWeaponsMatrices[isElemental];
            for (var missileType = 0; missileType < table.Length; missileType++)
            {
                if (table[missileType].Contains((int)wcid))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Get Missile Wield Index.
    /// </summary>
    private static int GetMissileWieldToIndex(int wieldDiff)
    {
        var index = 0;

        index = wieldDiff switch
        {
            250 => 1,
            270 => 2,
            290 => 3,
            315 => 4,
            335 => 5,
            360 => 6,
            375 => 7,
            385 => 8,
            _ => 0, // Default/Else
        };
        return index;
    }

    /// <summary>
    /// Rolls for a DamageMod for missile weapons
    /// </summary>
    private static float GetMissileDamageMod(int wieldDiff, WeaponType weaponType)
    {
        // should this be setting defaults?
        if (weaponType == WeaponType.Undef)
        {
            weaponType = WeaponType.Bow;
        }

        var damageMod = weaponType switch
        {
            WeaponType.Bow => LootTables.MissileDamageMod[0][GetMissileWieldToIndex(wieldDiff)],
            WeaponType.Crossbow => LootTables.MissileDamageMod[1][GetMissileWieldToIndex(wieldDiff)],
            WeaponType.Thrown => LootTables.MissileDamageMod[2][GetMissileWieldToIndex(wieldDiff)],
            _ => 1.5f, // Default/Else
        };

        // Added variance for Damage Modifier.  Full Modifier was rare in retail
        var modChance = ThreadSafeRandom.Next(0, 99);
        if (modChance < 20)
        {
            damageMod -= 0.09f;
        }
        else if (modChance < 35)
        {
            damageMod -= 0.08f;
        }
        else if (modChance < 50)
        {
            damageMod -= 0.07f;
        }
        else if (modChance < 65)
        {
            damageMod -= 0.06f;
        }
        else if (modChance < 75)
        {
            damageMod -= 0.05f;
        }
        else if (modChance < 85)
        {
            damageMod -= 0.04f;
        }
        else if (modChance < 90)
        {
            damageMod -= 0.03f;
        }
        else if (modChance < 94)
        {
            damageMod -= 0.02f;
        }
        else if (modChance < 98)
        {
            damageMod -= 0.01f;
        }

        return damageMod;
    }

    /// <summary>
    /// Get Missile Elemental Damage based on Wield.
    /// </summary>
    /// <param name="wield"></param>
    /// <returns>Missile Weapon Wield Requirement</returns>
    private static int GetElementalDamageBonus(int wield)
    {
        var chance = 0;
        var eleMod = 0;
        switch (wield)
        {
            case 315:
                chance = ThreadSafeRandom.Next(0, 99);
                if (chance < 20)
                {
                    eleMod = 1;
                }
                else if (chance < 40)
                {
                    eleMod = 2;
                }
                else if (chance < 70)
                {
                    eleMod = 3;
                }
                else if (chance < 95)
                {
                    eleMod = 4;
                }
                else
                {
                    eleMod = 5;
                }

                break;
            case 335:
                chance = ThreadSafeRandom.Next(0, 99);
                if (chance < 20)
                {
                    eleMod = 5;
                }
                else if (chance < 40)
                {
                    eleMod = 6;
                }
                else if (chance < 70)
                {
                    eleMod = 7;
                }
                else if (chance < 95)
                {
                    eleMod = 8;
                }
                else
                {
                    eleMod = 9;
                }

                break;
            case 360:
                chance = ThreadSafeRandom.Next(0, 99);
                if (chance < 20)
                {
                    eleMod = 10;
                }
                else if (chance < 30)
                {
                    eleMod = 11;
                }
                else if (chance < 45)
                {
                    eleMod = 12;
                }
                else if (chance < 60)
                {
                    eleMod = 13;
                }
                else if (chance < 75)
                {
                    eleMod = 14;
                }
                else if (chance < 95)
                {
                    eleMod = 15;
                }
                else
                {
                    eleMod = 16;
                }

                break;
            case 375: // Added +19 Elemental (like retail) and readjusted odds (odds are approximate, no hard data).  HarliQ 11/17/19
                chance = ThreadSafeRandom.Next(0, 99);
                if (chance < 5)
                {
                    eleMod = 12;
                }
                else if (chance < 15)
                {
                    eleMod = 13;
                }
                else if (chance < 30)
                {
                    eleMod = 14;
                }
                else if (chance < 50)
                {
                    eleMod = 15;
                }
                else if (chance < 65)
                {
                    eleMod = 16;
                }
                else if (chance < 80)
                {
                    eleMod = 17;
                }
                else if (chance < 95)
                {
                    eleMod = 18;
                }
                else
                {
                    eleMod = 19;
                }

                break;
            case 385:
                chance = ThreadSafeRandom.Next(0, 99);
                if (chance < 20)
                {
                    eleMod = 16;
                }
                else if (chance < 30)
                {
                    eleMod = 17;
                }
                else if (chance < 45)
                {
                    eleMod = 18;
                }
                else if (chance < 60)
                {
                    eleMod = 19;
                }
                else if (chance < 75)
                {
                    eleMod = 20;
                }
                else if (chance < 95)
                {
                    eleMod = 21;
                }
                else
                {
                    eleMod = 22;
                }

                break;
            default:
                eleMod = 0;
                break;
        }

        return eleMod;
    }

    /// <summary>
    /// Determines Type of Missile Weapon, and the element.
    /// </summary>
    /// <returns>Missile Type, Element</returns>
    private static int GetElementalMissileWeapon()
    {
        // Determine missile weapon type: 0 - Bow, 1 - Crossbows, 2 - Atlatl, 3 - Slingshot, 4 - Compound Bow, 5 - Compound Crossbow
        var missileType = ThreadSafeRandom.Next(
            0,
            ThreadSafeRandom.Next(0, LootTables.ElementalMissileWeaponsMatrix.Length - 1)
        );

        // Determine element type: 0 - Slashing, 1 - Piercing, 2 - Blunt, 3 - Frost, 4 - Fire, 5 - Acid, 6 - Electric
        var element = ThreadSafeRandom.Next(0, 6);

        return LootTables.ElementalMissileWeaponsMatrix[missileType][element];
    }

    /// <summary>
    /// Determines Non Elemental type of missile weapon (No Wields).
    /// </summary>
    /// <returns>Missile Weapon Type and SubType</returns>
    private static int GetNonElementalMissileWeapon()
    {
        // Determine missile weapon type: 0 - Bow, 1 - Crossbows, 2 - Atlatl
        var missileType = ThreadSafeRandom.Next(0, LootTables.NonElementalMissileWeaponsMatrix.Length - 1);
        var subType = ThreadSafeRandom.Next(0, LootTables.NonElementalMissileWeaponsMatrix[missileType].Length - 1);
        return LootTables.NonElementalMissileWeaponsMatrix[missileType][subType];
    }

    private static void TryMutateMissileWeaponDamage(
        WorldObject wo,
        TreasureRoll roll,
        TreasureDeath profile,
        out double damageModPercentile
    )
    {
        damageModPercentile = 0;

        if (wo.WeaponTime == null)
        {
            _log.Error(
                "TryMutateMissileWeaponDamage({WeaponName}, {TreasureRoll}, {TreasureDeath}) - WeaponTime is null.",
                wo.Name,
                roll,
                profile
            );
            return;
        }

        // target dps per tier
        var targetBaseDps = GetWeaponBaseDps(wo.Tier.Value);

        // animation speed
        var baseAnimLength = WeaponAnimationLength.GetWeaponAnimLength(wo);
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
            reloadAnimLength = 0.73f; // atlatl
        }

        int[] avgQuickPerTier = { 45, 65, 93, 118, 140, 160, 180, 195 };
        var quick = (float)avgQuickPerTier[profile.Tier - 1];
        var speedMod = 1.0f + (1 - (wo.WeaponTime.Value / 100.0)) + (quick / 600);
        var effectiveAttacksPerSecond = 1 / (baseAnimLength - reloadAnimLength + (reloadAnimLength / speedMod));

        // target weapon hit damage
        var ammoMaxDamage = GetAmmoBaseMaxDamage(wo.WeaponSkill, wo.Tier.Value);
        var weaponVariance = GetAmmoVariance(wo.WeaponSkill);
        var ammoMinDamage = ammoMaxDamage * (1 - weaponVariance);
        var ammoAverageDamage = (ammoMaxDamage + ammoMinDamage) / 2;
        var targetAvgHitDamage = targetBaseDps / effectiveAttacksPerSecond;

        // find average damage mod, considering ammo and critical strikes
        var averageBaseDamageMod = targetAvgHitDamage / ((ammoAverageDamage * 0.9) + (ammoMaxDamage * 0.2));

        // get low-end and high-end max damage range
        var damageRangePerTier = 0.25;
        var maximumBaseMaxDamageMod = (averageBaseDamageMod * 2) / (1.0 + (1 - damageRangePerTier));

        // roll and assign weapon damage
        var minimumBaseMaxDamageMod = maximumBaseMaxDamageMod * (1 - damageRangePerTier);
        var diminishedRoll = (averageBaseDamageMod - minimumBaseMaxDamageMod) * GetDiminishingRoll(profile);
        var finalMaxDamageMod = minimumBaseMaxDamageMod + diminishedRoll;
        wo.DamageMod = finalMaxDamageMod;

        // debug
        //var averageHitDamage = ((ammoAverageDamage * 0.9) + (ammoMaxDamage * 0.2)) * averageBaseDamageMod;
        //Console.WriteLine($"\nTryMutateMissileWeaponDamage()\n" +
        //    $" TargetBaseDps: {targetBaseDps}\n" +
        //    $" BaseAnimLength: {baseAnimLength}\n" +
        //    $" WeaponTime: {wo.WeaponTime.Value}\n" +
        //    $" Quick: {quick}\n" +
        //    $" SpeedMod: {speedMod} FullFormula: 1.0f + ({1 - (wo.WeaponTime.Value /100.0)}) + ({quick / 600})\n" +
        //    $" AttacksPerSecond: {effectiveAttacksPerSecond}\n" +
        //    $" TargetAvgHitDamage: {targetAvgHitDamage}\n" +
        //    $" AmmoMaxDamage: {ammoMaxDamage} AmmoAvgDamage: {ammoAverageDamage} WeaponVariance: {weaponVariance}\n\n" +
        //    $" AverageBaseMaxDamageMod: {averageBaseDamageMod}\n" +
        //    $" MaximumBaseMaxDamageMod: {maximumBaseMaxDamageMod}\n" +
        //    $" MinimumBaseMaxDamageMod: {minimumBaseMaxDamageMod}\n" +
        //    $" DiminishedRoll: {diminishedRoll}\n" +
        //    $" FinalMaxDamageMod: {finalMaxDamageMod}\n\n" +
        //    $" Non-Crit average Hit Damage: {ammoAverageDamage * averageBaseDamageMod}\n" +
        //    $" Average Hit Damage: {averageHitDamage}\n" +
        //    $" Avg DPS: {averageHitDamage * effectiveAttacksPerSecond}");

        // max possible damage (for workmanship)
        targetBaseDps = GetWeaponBaseDps(8);
        ammoMaxDamage = GetAmmoBaseMaxDamage(wo.WeaponSkill, 8);
        ammoMinDamage = ammoMaxDamage * (1 - weaponVariance);
        ammoAverageDamage = (ammoMaxDamage + ammoMinDamage) / 2;
        targetAvgHitDamage = targetBaseDps / effectiveAttacksPerSecond;
        averageBaseDamageMod = targetAvgHitDamage / ((ammoAverageDamage * 0.9) + (ammoMaxDamage * 0.2));
        maximumBaseMaxDamageMod = (averageBaseDamageMod * 2) / (1.0 + (1 - damageRangePerTier));

        damageModPercentile = (finalMaxDamageMod - 1) / (maximumBaseMaxDamageMod - 1);
        //Console.WriteLine($"damMod: {wo.DamageMod - 1} maxDamMod: {maxPossibleDamageMod} damModPercentile: {damageModPercentile}");
    }

    /// <summary>
    /// Rolls for the Element for casters
    /// </summary>
    private static void RollMissileElement(TreasureDeath profile, WorldObject wo)
    {
        var roll = ThreadSafeRandom.Next(1, 7);
        var elementType = 0;
        //var materialType = 0;
        var uiEffect = 0;
        var elementString = "";

        switch (roll)
        {
            case 1:
                elementType = 0x1; // slash
                //materialType = 0x0000001A; // imperial topaz
                uiEffect = 0x0400;
                elementString = "Slashing";
                break;
            case 2:
                elementType = 0x2; // pierce
                //materialType = 0x0000000F; // black garnet
                uiEffect = 0x0800;
                elementString = "Piercing";
                break;
            case 3:
                elementType = 0x4; // bludge
                //materialType = 0x0000002F; // white sapphire
                uiEffect = 0x0200;
                elementString = "Blunt";
                break;
            case 4:
                elementType = 0x8; // cold
                //materialType = 0x0000000D; // aquamarine
                uiEffect = 0x0080;
                elementString = "Frost";
                break;
            case 5:
                elementType = 0x10; // fire
                //materialType = 0x00000023; // red garnet
                uiEffect = 0x0020;
                elementString = "Fire";
                break;
            case 6:
                elementType = 0x20; // acid
                //materialType = 0x00000015; // emerald
                uiEffect = 0x0100;
                elementString = "Acid";
                break;
            case 7:
                elementType = 0x40; // electric
                //materialType = 0x0000001B; // jet
                uiEffect = 0x0040;
                elementString = "Lightning";
                break;
        }
        wo.W_DamageType = (DamageType)elementType;
        //wo.MaterialType = (MaterialType)materialType;
        wo.UiEffects = (UiEffects)uiEffect;
        wo.Name = elementString + " " + wo.Name;
    }

    private static int GetAmmoBaseMaxDamage(Skill weaponSkill, int tier)
    {
        switch (weaponSkill)
        {
            default:
            case Skill.Bow:
                switch (tier)
                {
                    default:
                    case 1:
                        return 6;
                    case 2:
                        return 8;
                    case 3:
                        return 10;
                    case 4:
                        return 12;
                    case 5:
                        return 14;
                    case 6:
                        return 16;
                    case 7:
                        return 18;
                    case 8:
                        return 20;
                }
            case Skill.Crossbow:
                switch (tier)
                {
                    default:
                    case 1:
                        return 9;
                    case 2:
                        return 12;
                    case 3:
                        return 15;
                    case 4:
                        return 18;
                    case 5:
                        return 21;
                    case 6:
                        return 24;
                    case 7:
                        return 27;
                    case 8:
                        return 30;
                }
            case Skill.ThrownWeapon:
                switch (tier)
                {
                    default:
                    case 1:
                        return 12;
                    case 2:
                        return 16;
                    case 3:
                        return 20;
                    case 4:
                        return 24;
                    case 5:
                        return 28;
                    case 6:
                        return 32;
                    case 7:
                        return 36;
                    case 8:
                        return 40;
                }
        }
    }

    private static float GetAmmoVariance(Skill weaponSkill)
    {
        switch (weaponSkill)
        {
            default:
            case Skill.Bow:
                return 0.6f;
            case Skill.Crossbow:
                return 0.4f;
            case Skill.MissileWeapons:
                return 0.75f;
        }
    }

    /// <summary>
    /// Return caster subtype
    /// Axe = 0, Club = 1, Dagger = 2, Javelin = 3, Dart = 4, Shouken = 5
    /// </summary>
    public static int GetThrownWeaponsSubType(WorldObject wo)
    {
        var subType = 0;

        foreach (var type in LootTables.ThrownWeaponMatrix)
        {
            if (type.Contains(wo.WeenieClassId))
            {
                break;
            }

            subType++;
        }

        return subType;
    }
}
