using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Cantrips;
using ACE.Server.Factories.Tables.Spells;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public partial class LootGenerationFactory
{
    private static bool AssignMagic_New(WorldObject wo, TreasureDeath profile, TreasureRoll roll, out int numSpells)
    {
        var spells = RollSpells(wo, profile, roll);

        foreach (var spell in spells)
        {
            wo.Biota.GetOrAddKnownSpell((int)spell, wo.BiotaDatabaseLock, out _);
        }
        numSpells = spells.Count;

        return true;
    }

    private static List<SpellId> RollSpells(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        var spells = new List<SpellId>();

        var cantrips = RollCantrips(wo, profile, roll);

        if (cantrips != null)
        {
            spells.AddRange(cantrips);

            roll.ItemDifficulty += RollCantripDifficulty(cantrips);
        }

        return spells;
    }

    public static bool RollProcSpell(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        // 10% chance for a melee/missile weapon item to have a Proc Spell
        var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (rng > 0.1f || (!roll.IsMeleeWeapon && !roll.IsMissileWeapon))
        {
            return false;
        }

        var itemProc = RollIProcSpellId(wo, profile, roll);

        if (itemProc != SpellId.Undef)
        {
            var procRate = RollProcSpellRate(wo, profile);

            var spell = new Server.Entity.Spell(itemProc);
            wo.ProcSpellRate = procRate;
            wo.ProcSpell = (uint)itemProc;
            wo.ProcSpellSelfTargeted = spell.IsSelfTargeted;
        }
        return true;
    }

    /// <summary>
    /// Base proc rate is derived from weapon animation length, doubled.
    /// Also used by Spell Transference so transferred procs match lootgen rates.
    /// </summary>
    public static double RollProcSpellRate(WorldObject wo, TreasureDeath profile = null)
    {
        var animLength = WeaponAnimationLength.GetWeaponAnimLength(wo) / 100;
        var procRate = animLength + (animLength * GetDiminishingRoll(profile));

        return procRate * 2.0;
    }

    /// <summary>
    /// Returns the lowest and highest proc rate RollProcSpellRate can produce for this weapon.
    /// </summary>
    public static (double min, double max) GetProcSpellRateRange(WorldObject wo)
    {
        var animLength = WeaponAnimationLength.GetWeaponAnimLength(wo) / 100;

        return (animLength * 2.0, (animLength + animLength) * 2.0);
    }

    private static SpellId RollIProcSpellId(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        var procSpellId = SpellId.Undef;

        // wo.WieldRequirements3 = WieldRequirement.Training;
        // wo.WieldDifficulty3 = 2;

        var warSpell = ThreadSafeRandom.Next(0, 1) == 0 ? true : false;
        //if (warSpell)
        //{
        //    wo.WieldSkillType3 = (int)Skill.WarMagic;
        //}
        //else
        //{
        //    wo.WieldSkillType3 = (int)Skill.LifeMagic;
        //}

        if (roll.IsMeleeWeapon)
        {
            procSpellId = MeleeSpells.RollProc(wo, profile, warSpell);
        }
        else if (roll.IsMissileWeapon)
        {
            procSpellId = MissileSpells.RollProc(wo, profile, warSpell);
        }
        else
        {
            _log.Error($"RollItemProc({wo.Name}) - item is not melee or missile weapon");
            return SpellId.Undef;
        }

        if (procSpellId != SpellId.Undef)
        {
            return RollProcLevel(wo, profile, procSpellId);
        }

        return SpellId.Undef;
    }

    private static SpellId RollProcLevel(WorldObject wo, TreasureDeath profile, SpellId procSpellId)
    {
        var spellLevel = Math.Max(profile.Tier - 1, 1);

        var spellLevels = SpellLevelProgression.GetSpellLevels(procSpellId);

        if (spellLevels.Count != 8)
        {
            _log.Error(
                $"RollSpellLevels({wo.Name}, {procSpellId}) - spell level progression returned {spellLevels.Count}, expected 8"
            );
            return SpellId.Undef;
        }

        return (spellLevels[spellLevel - 1]);
    }

    private static List<SpellId> RollItemSpells(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        List<SpellId> spells = null;

        if (roll.IsClothArmor)
        {
            spells = ClothArmorSpells.Roll(profile);
        }
        else if (roll.HasArmorLevel(wo))
        {
            spells = ArmorSpells.Roll(profile);
        }
        else if (roll.IsMeleeWeapon)
        {
            spells = MeleeSpells.Roll(profile);
        }
        else if (roll.IsMissileWeapon)
        {
            spells = MissileSpells.Roll(profile);
        }
        else if (roll.IsCaster)
        {
            spells = WandSpells.Roll(wo, profile);
        }
        else
        {
            _log.Error($"RollItemSpells({wo.Name}) - item is not clothing / armor / weapon");
            return null;
        }

        if (spells != null)
        {
            return RollSpellLevels(wo, profile, spells);
        }
        else
        {
            return null;
        }
    }

    private static List<SpellId> RollSpellLevels(WorldObject wo, TreasureDeath profile, IEnumerable<SpellId> spells)
    {
        var finalSpells = new List<SpellId>();

        foreach (var spell in spells)
        {
            var spellLevel = SpellLevelChance.Roll(profile.Tier);

            var spellLevels = SpellLevelProgression.GetSpellLevels(spell);

            if (spellLevels.Count != 8)
            {
                _log.Error(
                    $"RollSpellLevels({wo.Name}, {spell}) - spell level progression returned {spellLevels.Count}, expected 8"
                );
                continue;
            }

            finalSpells.Add(spellLevels[spellLevel - 1]);
        }

        return finalSpells;
    }

    private static List<SpellId> RollEnchantments(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        /*if (wo.SpellSelectionCode == null)
        {
            _log.Warning($"RollEnchantments({wo.Name}) - missing spell selection code / PropertyInt.TsysMutationData");
            return null;
        }*/

        // test method: determine spell selection code dynamically
        var spellSelectionCode = GetSpellSelectionCode_Dynamic(wo, roll);

        if (spellSelectionCode == 0 || spellSelectionCode == 6 || spellSelectionCode == 17)
        {
            return null;
        }

        //Console.WriteLine($"Using spell selection code {spellSelectionCode} for {wo.Name}");

        var numEnchantments = RollNumEnchantments(wo, profile, roll);

        if (numEnchantments <= 0)
        {
            return null;
        }

        var numAttempts = numEnchantments * 3;

        var spells = new HashSet<SpellId>();

        for (var i = 0; i < numAttempts && spells.Count < numEnchantments; i++)
        {
            var spell = SpellSelectionTable.Roll(spellSelectionCode);

            if (spell != SpellId.Undef)
            {
                spells.Add(spell);
            }
        }

        return RollSpellLevels(wo, profile, spells);
    }

    private static int RollNumEnchantments(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        if (roll.IsArmor || roll.IsWeapon)
        {
            return RollNumEnchantments_Armor_Weapon(wo, profile, roll);
        }
        // confirmed:
        // - crowns (classified as TreasureItemType.Jewelry) used this table
        // - clothing w/ al also used this table
        else if (roll.IsClothing || roll.IsJewelry || roll.IsDinnerware)
        {
            return RollNumEnchantments_Clothing_Jewelry_Dinnerware(wo, profile, roll);
        }
        else
        {
            _log.Warning(
                $"RollNumEnchantments({wo.Name}, {profile.TreasureType}, {roll.ItemType}) - unknown item type"
            );
            return 1; // gems?
        }
    }

    private static readonly List<float> EnchantmentChances_Armor_MeleeMissileWeapon = new List<float>()
    {
        0.00f, // T1
        0.05f, // T2
        0.10f, // T3
        0.20f, // T4
        0.40f, // T5
        0.60f, // T6
        0.60f, // T7
        0.60f, // T8
    };

    private static readonly List<float> EnchantmentChances_Caster = new List<float>()
    {
        0.60f, // T1
        0.60f, // T2
        0.60f, // T3
        0.60f, // T4
        0.60f, // T5
        0.75f, // T6
        0.75f, // T7
        0.75f, // T8
    };

    private static int RollNumEnchantments_Armor_Weapon(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        var tierChances = roll.IsCaster ? EnchantmentChances_Caster : EnchantmentChances_Armor_MeleeMissileWeapon;

        var chance = tierChances[profile.Tier - 1];

        var rng = ThreadSafeRandom.NextInterval(profile.LootQualityMod);

        if (rng < chance)
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }

    private static int RollNumEnchantments_Clothing_Jewelry_Dinnerware(
        WorldObject wo,
        TreasureDeath profile,
        TreasureRoll roll
    )
    {
        var chance = 0.1f;

        var rng = ThreadSafeRandom.NextInterval(profile.LootQualityMod);

        if (rng >= chance)
        {
            return 1;
        }
        else if (profile.Tier < 6)
        {
            return 2;
        }

        // tier 6+ has a chance for 3 enchantments
        rng = ThreadSafeRandom.NextInterval(profile.LootQualityMod * 0.1f);

        if (rng >= chance * 0.5f)
        {
            return 2;
        }
        else
        {
            return 3;
        }
    }

    public static float RollEnchantmentDifficulty(List<SpellId> spellIds)
    {
        var spells = new List<Server.Entity.Spell>();

        foreach (var spellId in spellIds)
        {
            var spell = new Server.Entity.Spell(spellId);
            spells.Add(spell);
        }

        spells = spells.OrderBy(i => i.Formula.Level).ToList();

        var itemDifficulty = 0.0f;

        // exclude highest spell
        for (var i = 0; i < spells.Count - 1; i++)
        {
            var spell = spells[i];

            var rng = (float)ThreadSafeRandom.Next(0.5f, 1.5f);

            itemDifficulty += spell.Formula.Level * 5.0f * rng;
        }

        return itemDifficulty;
    }

    private static List<SpellId> RollCantrips(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        // no cantrips on dinnerware?
        if (roll.ItemType == TreasureItemType_Orig.ArtObject)
        {
            return null;
        }

        var numCantrips = CantripChance.RollNumCantrips(profile);

        if (numCantrips == 0)
        {
            return null;
        }

        if (
            (wo.ArmorStyle == (int)ArmorStyle.Covenant || wo.ArmorStyle == (int)ArmorStyle.OlthoiArmor)
            && numCantrips > 1
        )
        {
            numCantrips = 1;
        }

        if (wo.ArmorSlots > 1)
        {
            numCantrips *= wo.ArmorSlots ?? 1;
        }

        var numAttempts = numCantrips * 3;

        var cantrips = new HashSet<SpellId>();

        for (var i = 0; i < numAttempts && cantrips.Count < numCantrips; i++)
        {
            var cantrip = RollCantrip(wo, profile, roll);

            if (cantrip != SpellId.Undef)
            {
                cantrips.Add(cantrip);
            }
        }

        var finalCantrips = new List<SpellId>();

        var hasLegendary = false;

        foreach (var cantrip in cantrips)
        {
            var cantripLevel = CantripChance.RollCantripLevel(profile);

            var cantripLevels = SpellLevelProgression.GetSpellLevels(cantrip);

            if (cantripLevels.Count != 4)
            {
                _log.Error(
                    $"RollCantrips({wo.Name}, {profile.TreasureType}, {roll.ItemType}) - {cantrip} has {cantripLevels.Count} cantrip levels, expected 4"
                );
                continue;
            }

            finalCantrips.Add(cantripLevels[cantripLevel - 1]);

            if (cantripLevel == 4)
            {
                hasLegendary = true;
            }
        }

        // if a legendary cantrip dropped on this item
        if (hasLegendary && roll.ArmorType != TreasureArmorType.Society)
        {
            // and if the item has a level requirement, ensure the level requirement is at least 180
            // if the item does not already contain a level requirement, don't add one?

            if (wo.WieldRequirements == WieldRequirement.Level && wo.WieldDifficulty < 180)
            {
                wo.WieldDifficulty = 180;
            }

            if (wo.WieldRequirements2 == WieldRequirement.Level && wo.WieldDifficulty2 < 180)
            {
                wo.WieldDifficulty2 = 180;
            }
        }

        return finalCantrips;
    }

    private static SpellId RollCantrip(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        if (wo.WeenieClassId == (uint)Enum.WeenieClassName.leggingschiran)
        {
            //Console.WriteLine($"Chiran Leggings ({wo.WeenieClassId}), IsClothArmor? ({roll.IsClothArmor}) ArmorType: ({wo.ArmorType}) ItemType: ({roll.ItemType})");
        }
        if (roll.IsClothArmor || roll.Wcid == Enum.WeenieClassName.crown)
        {
            // robes
            return ClothArmorCantrips.Roll();
        }
        else if (roll.HasArmorLevel(wo) || roll.IsClothing)
        {
            // armor / clothing cantrip
            // this table also applies to crowns (treasureitemtype.jewelry w/ al)
            return ArmorCantrips.Roll();
        }
        else if (roll.IsMeleeWeapon)
        {
            // melee cantrip
            var meleeCantrip = MeleeCantrips.Roll();

            // adjust for weapon skill
            if (meleeCantrip == SpellId.CANTRIPLIGHTWEAPONSAPTITUDE1)
            {
                meleeCantrip = AdjustForWeaponMastery(wo);
            }

            return meleeCantrip;
        }
        else if (roll.IsMissileWeapon)
        {
            // missile cantrip
            var missileCantrip = MissileCantrips.Roll();

            // adjust for weapon skill
            if (missileCantrip == SpellId.CANTRIPMISSILEWEAPONSAPTITUDE1)
            {
                missileCantrip = AdjustForWeaponMastery(wo);
            }

            return missileCantrip;
        }
        else if (roll.IsCaster)
        {
            // caster cantrip
            var casterCantrip = WandCantrips.Roll();

            if (casterCantrip == SpellId.CANTRIPWARMAGICAPTITUDE1)
            {
                casterCantrip = AdjustForDamageType(wo, casterCantrip);
            }

            return casterCantrip;
        }
        else if (roll.IsJewelry)
        {
            // jewelry cantrip
            return JewelryCantrips.Roll();
        }
        else
        {
            _log.Error($"RollCantrip({wo.Name}, {profile.TreasureType}, {roll.ItemType}) - unknown item type");
            return SpellId.Undef;
        }
    }

    public static float RollCantripDifficulty(List<SpellId> cantripIds)
    {
        var itemDifficulty = 0.0f;

        foreach (var cantripId in cantripIds)
        {
            var cantripLevels = SpellLevelProgression.GetSpellLevels(cantripId);

            if (cantripLevels == null || (cantripLevels.Count != 3 && cantripLevels.Count != 4))
            {
                _log.Error($"RollCantripDifficulty({cantripId}) - unknown cantrip");
                continue;
            }

            var cantripLevel = cantripLevels.IndexOf(cantripId);

            if (cantripLevel == 0)
            {
                itemDifficulty += (float)ThreadSafeRandom.Next(5.0f, 10.0f);
            }
            else
            {
                itemDifficulty += (float)ThreadSafeRandom.Next(10.0f, 20.0f);
            }
        }
        return itemDifficulty;
    }

    private static SpellId AdjustForWeaponMastery(WorldObject wo)
    {
        if (wo.WeaponSkill != Skill.TwoHandedCombat && wo.WeaponSkill != Skill.MissileWeapons)
        {
            // 10% chance to adjust to dual wielding
            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (rng < 0.1f)
            {
                return SpellId.CantripDualWieldAptitude1;
            }
        }

        switch (wo.WeaponSkill)
        {
            case Skill.TwoHandedCombat:
                return SpellId.CANTRIPTWOHANDEDAPTITUDE1;
            case Skill.MartialWeapons:
                return SpellId.CANTRIPHEAVYWEAPONSAPTITUDE1;
            case Skill.LightWeapons:
                return SpellId.CANTRIPLIGHTWEAPONSAPTITUDE1;
            case Skill.FinesseWeapons:
                return SpellId.CANTRIPFINESSEWEAPONSAPTITUDE1;
            case Skill.MissileWeapons:
                return SpellId.CANTRIPMISSILEWEAPONSAPTITUDE1;
            case Skill.Axe:
                return SpellId.CANTRIPHEAVYWEAPONSAPTITUDE1;
            case Skill.Dagger:
                return SpellId.CANTRIPFINESSEWEAPONSAPTITUDE1; // CANTRIPDAGGERAPTITUDE1
            case Skill.Mace:
                return SpellId.CANTRIPHEAVYWEAPONSAPTITUDE1;
            case Skill.Spear:
                return SpellId.CANTRIPHEAVYWEAPONSAPTITUDE1;
            case Skill.Staff:
                return SpellId.CANTRIPLIGHTWEAPONSAPTITUDE1; // CANTRIPSTAFFAPTITUDE1
            case Skill.Sword:
                return SpellId.CANTRIPHEAVYWEAPONSAPTITUDE1; // CANTRIPMARTIALWEAPONSAPTITUDE1
            case Skill.UnarmedCombat:
                return SpellId.CANTRIPUNARMEDAPTITUDE1;
            case Skill.Bow:
                return SpellId.CANTRIPMISSILEWEAPONSAPTITUDE1; // CANTRIPBOWAPTITUDE1
            case Skill.Crossbow:
                return SpellId.CANTRIPMISSILEWEAPONSAPTITUDE1;
            case Skill.ThrownWeapon:
                return SpellId.CANTRIPTHROWNAPTITUDE1;
        }
        return SpellId.Undef;
    }

    private static SpellId AdjustForDamageType(WorldObject wo, SpellId spell)
    {
        return SpellId.CANTRIPWARMAGICAPTITUDE1;

        // (If Nether magic exists)

        //if (wo.W_DamageType == DamageType.Nether)
        //    return SpellId.CantripVoidMagicAptitude1;

        //if (wo.W_DamageType != DamageType.Undef)
        //    return SpellId.CANTRIPWARMAGICAPTITUDE1;

        //// even split? retail was broken here
        //var rng = ThreadSafeRandom.Next(0.0f, 1.0f);

        //if (rng < 0.5f)
        //    return SpellId.CANTRIPWARMAGICAPTITUDE1;
        //else
        //    return SpellId.CantripVoidMagicAptitude1;
    }

    /// <summary>
    /// An alternate method to using the SpellSelectionCode from PropertyInt.TSysMutationdata
    /// </summary>
    private static int GetSpellSelectionCode_Dynamic(WorldObject wo, TreasureRoll roll)
    {
        if (wo is Gem)
        {
            return 1;
        }
        else if (roll.ItemType == TreasureItemType_Orig.Jewelry)
        {
            if (!roll.HasArmorLevel(wo))
            {
                return 2;
            }
            else
            {
                return 3;
            }
        }
        else if (roll.Wcid == Enum.WeenieClassName.orb)
        {
            return 4;
        }
        else if (roll.IsCaster && wo.W_DamageType != DamageType.Nether)
        {
            return 5;
        }
        else if (roll.IsMeleeWeapon && wo.WeaponSkill != Skill.TwoHandedCombat)
        {
            return 6;
        }
        else if ((roll.IsArmor || roll.IsClothing) && !wo.IsShield)
        {
            return GetSpellCode_Dynamic_ClothingArmor(wo, roll);
        }
        else if (wo.IsShield)
        {
            return 8;
        }
        else if (roll.IsDinnerware)
        {
            if (roll.Wcid == Enum.WeenieClassName.flasksimple)
            {
                return 0;
            }
            else
            {
                return 16;
            }
        }
        else if (roll.IsMissileWeapon || wo.IsTwoHanded)
        {
            return 17;
        }
        else if (roll.IsCaster && wo.W_DamageType == DamageType.Nether)
        {
            return 19;
        }

        _log.Error($"GetSpellCode_Dynamic({wo.Name}) - couldn't determine spell selection code");

        return 0;
    }

    private static readonly CoverageMask upperArmor =
        CoverageMask.OuterwearChest
        | CoverageMask.OuterwearUpperArms
        | CoverageMask.OuterwearLowerArms
        | CoverageMask.OuterwearAbdomen;
    private static readonly CoverageMask lowerArmor = CoverageMask.OuterwearUpperLegs | CoverageMask.OuterwearLowerLegs; // check abdomen

    private static readonly CoverageMask clothing =
        CoverageMask.UnderwearChest
        | CoverageMask.UnderwearUpperArms
        | CoverageMask.UnderwearLowerArms
        | CoverageMask.UnderwearAbdomen
        | CoverageMask.UnderwearUpperLegs
        | CoverageMask.UnderwearLowerLegs;

    private static int GetSpellCode_Dynamic_ClothingArmor(WorldObject wo, TreasureRoll roll)
    {
        // special cases
        switch (roll.Wcid)
        {
            case Enum.WeenieClassName.glovescloth:
                return 14;
            case Enum.WeenieClassName.capleather:
                return 20;
        }

        if (roll.IsClothArmor || wo.ArmorWeightClass == (int)ArmorWeightClass.Cloth)
        {
            return 21;
        }

        var coverageMask = wo.ClothingPriority ?? 0;
        var isArmor = roll.IsArmor;

        if ((coverageMask & upperArmor) != 0 && (coverageMask & CoverageMask.OuterwearLowerLegs) == 0)
        {
            return 7;
        }

        if (coverageMask == CoverageMask.Hands && isArmor)
        {
            return 9;
        }

        if (coverageMask == CoverageMask.Head && roll.BaseArmorLevel >= 20)
        {
            return 10;
        }

        // base weenie armorLevel >= 20
        if ((coverageMask & CoverageMask.Feet) != 0 && roll.BaseArmorLevel >= 20)
        {
            return 11;
        }

        if ((coverageMask & clothing) != 0)
        {
            return 12;
        }

        // metal cap?
        if (coverageMask == CoverageMask.Head && !isArmor)
        {
            return 13;
        }

        if (coverageMask == CoverageMask.Hands && !isArmor)
        {
            return 14;
        }

        // leggings
        if ((coverageMask & lowerArmor) != 0)
        {
            return 15;
        }

        if (coverageMask == CoverageMask.Feet)
        {
            return 18;
        }

        _log.Error(
            $"GetSpellCode_Dynamic_ClothingArmor({wo.Name}) - couldn't determine spell selection code for {coverageMask}, {isArmor}"
        );
        return 0;
    }
}
