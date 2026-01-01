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
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    private static void AssignMagic(
        WorldObject wo,
        TreasureDeath profile,
        TreasureRoll roll,
        bool isArmor = false,
        bool isMagical = false
    )
    {
        var numSpells = 0;
        var numEpics = 0;
        var numLegendaries = 0;

        if (isMagical)
        {
            // new method
            if (!AssignMagic_New(wo, profile, roll, out numSpells))
            {
                return;
            }

            if (wo.IsCaster)
            {
                MutateCaster_SpellDID(wo, profile);
            }
        }

        if (RollProcSpell(wo, profile, roll))
        {
            numSpells++;
        }

        if (numSpells == 0 && wo.SpellDID == null && wo.ProcSpell == null)
        {
            // we ended up without any spells, revert to non-magic item.
            wo.ItemManaCost = null;
            wo.ItemMaxMana = null;
            wo.ItemCurMana = null;
            //wo.ItemSpellcraft = null;
            wo.ItemDifficulty = null;
        }
        else
        {
            if (!wo.UiEffects.HasValue) // Elemental effects take precendence over magical as it is more important to know the element of a weapon than if it has spells.
            {
                wo.UiEffects = UiEffects.Magical;
            }

            var combinedSpellCost = GetCombinedSpellManaCost(wo);

            if (!wo.IsCaster)
            {
                wo.ManaRate = CalculateManaRate(wo);
            }

            if (roll == null)
            {
                wo.ItemMaxMana = RollItemMaxMana(profile.Tier, numSpells);
                wo.ItemCurMana = wo.ItemMaxMana;

               //wo.ItemSpellcraft = RollSpellcraft(wo);
                wo.ItemDifficulty = RollItemDifficulty(wo, numEpics, numLegendaries);
            }
            else
            {
                var maxSpellMana = combinedSpellCost;

                if (wo.SpellDID != null)
                {
                    var spell = new Server.Entity.Spell(wo.SpellDID.Value);

                    var castableMana = (int)spell.BaseMana * 5;

                    if (castableMana > maxSpellMana)
                    {
                        maxSpellMana = castableMana;
                    }
                }

                wo.ItemMaxMana = RollItemMaxMana_New(wo.Tier ?? 1, wo.ArmorSlots ?? 1);
                wo.ItemCurMana = wo.ItemMaxMana;

                //wo.ItemSpellcraft = RollSpellcraft(wo, roll, profile);
                if (wo.ItemSpellcraft == 0)
                {
                    wo.ItemSpellcraft = null;
                }

                AddActivationRequirements(wo, profile, roll);
            }
        }
    }

    private static bool AssignMagic_Spells(
        WorldObject wo,
        TreasureDeath profile,
        bool isArmor,
        out int numSpells,
        out int epicCantrips,
        out int legendaryCantrips
    )
    {
        SpellId[][] spells;
        SpellId[][] cantrips;

        var lowSpellTier = GetLowSpellTier(profile.Tier);
        var highSpellTier = GetHighSpellTier(profile.Tier);

        switch (wo.WeenieType)
        {
            case WeenieType.Clothing:
                spells = ArmorSpells.Table;
                cantrips = ArmorCantrips.Table;
                break;
            case WeenieType.Caster:
                spells = WandSpells.Table;
                cantrips = WandCantrips.Table;
                break;
            case WeenieType.Generic:
                spells = JewelrySpells.Table;
                cantrips = JewelryCantrips.Table;
                break;
            case WeenieType.MeleeWeapon:
                spells = MeleeSpells.Table;
                cantrips = MeleeCantrips.Table;
                break;
            case WeenieType.MissileLauncher:
                spells = MissileSpells.Table;
                cantrips = MissileCantrips.Table;
                break;
            default:
                spells = null;
                cantrips = null;
                break;
        }

        if (wo.IsShield)
        {
            spells = ArmorSpells.Table;
            cantrips = ArmorCantrips.Table;
        }

        numSpells = 0;
        epicCantrips = 0;
        legendaryCantrips = 0;

        if (spells == null || cantrips == null)
        {
            return false;
        }

        // Refactor 3/2/2020 - HQ
        // Magic stats
        numSpells = GetSpellDistribution(
            profile,
            out var minorCantrips,
            out var majorCantrips,
            out epicCantrips,
            out legendaryCantrips
        );
        var numCantrips = minorCantrips + majorCantrips + epicCantrips + legendaryCantrips;

        if (numSpells - numCantrips > 0)
        {
            var indices = Enumerable.Range(0, spells.Length).ToList();

            for (var i = 0; i < numSpells - numCantrips; i++)
            {
                var idx = ThreadSafeRandom.Next(0, indices.Count - 1);
                var col = ThreadSafeRandom.Next(lowSpellTier - 1, highSpellTier - 1);
                var spellID = spells[indices[idx]][col];
                indices.RemoveAt(idx);
                wo.Biota.GetOrAddKnownSpell((int)spellID, wo.BiotaDatabaseLock, out _);
            }
        }

        // Per discord discussions: ALL armor/shields if it had any spells, had an Impen spell
        if (isArmor)
        {
            var impenSpells = SpellLevelProgression.Impenetrability;

            // Ensure that one of the Impen spells was not already added
            var impenFound = false;
            for (var i = 0; i < 8; i++)
            {
                if (wo.Biota.SpellIsKnown((int)impenSpells[i], wo.BiotaDatabaseLock))
                {
                    impenFound = true;
                    break;
                }
            }
            if (!impenFound)
            {
                var col = ThreadSafeRandom.Next(lowSpellTier - 1, highSpellTier - 1);
                var spellID = impenSpells[col];
                wo.Biota.GetOrAddKnownSpell((int)spellID, wo.BiotaDatabaseLock, out _);
            }
        }

        if (numCantrips > 0)
        {
            var indices = Enumerable.Range(0, cantrips.Length).ToList();

            // minor cantrips
            for (var i = 0; i < minorCantrips; i++)
            {
                var idx = ThreadSafeRandom.Next(0, indices.Count - 1);
                var spellID = cantrips[indices[idx]][0];
                indices.RemoveAt(idx);
                wo.Biota.GetOrAddKnownSpell((int)spellID, wo.BiotaDatabaseLock, out _);
            }
            // major cantrips
            for (var i = 0; i < majorCantrips; i++)
            {
                var idx = ThreadSafeRandom.Next(0, indices.Count - 1);
                var spellID = cantrips[indices[idx]][1];
                indices.RemoveAt(idx);
                wo.Biota.GetOrAddKnownSpell((int)spellID, wo.BiotaDatabaseLock, out _);
            }
            // epic cantrips
            for (var i = 0; i < epicCantrips; i++)
            {
                var idx = ThreadSafeRandom.Next(0, indices.Count - 1);
                var spellID = cantrips[indices[idx]][2];
                indices.RemoveAt(idx);
                wo.Biota.GetOrAddKnownSpell((int)spellID, wo.BiotaDatabaseLock, out _);
            }
            // legendary cantrips
            for (var i = 0; i < legendaryCantrips; i++)
            {
                var idx = ThreadSafeRandom.Next(0, indices.Count - 1);
                var spellID = cantrips[indices[idx]][3];
                indices.RemoveAt(idx);
                wo.Biota.GetOrAddKnownSpell((int)spellID, wo.BiotaDatabaseLock, out _);
            }
        }
        return true;
    }

    private static int GetSpellDistribution(
        TreasureDeath profile,
        out int numMinors,
        out int numMajors,
        out int numEpics,
        out int numLegendaries
    )
    {
        var numNonCantrips = 0;

        numMinors = 0;
        numMajors = 0;
        numEpics = 0;
        numLegendaries = 0;

        var nonCantripChance = ThreadSafeRandom.Next(1, 100000);

        numMinors = GetNumMinorCantrips(profile); // All tiers have a chance for at least one minor cantrip
        numMajors = GetNumMajorCantrips(profile);
        numEpics = GetNumEpicCantrips(profile);
        numLegendaries = GetNumLegendaryCantrips(profile);

        //  Fixing the absurd amount of spells on items - HQ 6/21/2020
        //  From Mags Data all tiers have about the same chance for a given number of spells on items.  This is the ratio for magical items.
        //  1 Spell(s) - 46.410 %
        //  2 Spell(s) - 27.040 %
        //  3 Spell(s) - 17.850 %
        //  4 Spell(s) - 6.875 %
        //  5 Spell(s) - 1.525 %
        //  6 Spell(s) - 0.235 %
        //  7 Spell(s) - 0.065 %

        if (nonCantripChance <= 46410)
        {
            numNonCantrips = 1;
        }
        else if (nonCantripChance <= 73450)
        {
            numNonCantrips = 2;
        }
        else if (nonCantripChance <= 91300)
        {
            numNonCantrips = 3;
        }
        else if (nonCantripChance <= 98175)
        {
            numNonCantrips = 4;
        }
        else if (nonCantripChance <= 99700)
        {
            numNonCantrips = 5;
        }
        else if (nonCantripChance <= 99935)
        {
            numNonCantrips = 6;
        }
        else
        {
            numNonCantrips = 7;
        }

        return numNonCantrips + numMinors + numMajors + numEpics + numLegendaries;
    }

    private static int GetNumMinorCantrips(TreasureDeath profile)
    {
        var numMinors = 0;

        var dropRate = PropertyManager.GetDouble("minor_cantrip_drop_rate").Item;
        if (dropRate <= 0)
        {
            return 0;
        }

        var dropRateMod = 1.0 / dropRate;

        double lootQualityMod = 1.0f;
        if (
            PropertyManager.GetBool("loot_quality_mod").Item
            && profile.LootQualityMod > 0
            && profile.LootQualityMod < 1
        )
        {
            lootQualityMod = 1.0f - profile.LootQualityMod;
        }

        switch (profile.Tier)
        {
            case 1:
                if (ThreadSafeRandom.Next(1, (int)(100 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 1;
                }

                break;
            case 2:
            case 3:
                if (ThreadSafeRandom.Next(1, (int)(50 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 1;
                }

                if (ThreadSafeRandom.Next(1, (int)(250 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 2;
                }

                break;
            case 4:
            case 5:
                if (ThreadSafeRandom.Next(1, (int)(50 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 1;
                }

                if (ThreadSafeRandom.Next(1, (int)(250 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 2;
                }

                if (ThreadSafeRandom.Next(1, (int)(1000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 3;
                }

                break;
            case 6:
            case 7:
            default:
                if (ThreadSafeRandom.Next(1, (int)(50 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 1;
                }

                if (ThreadSafeRandom.Next(1, (int)(250 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 2;
                }

                if (ThreadSafeRandom.Next(1, (int)(1000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 3;
                }

                if (ThreadSafeRandom.Next(1, (int)(5000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMinors = 4;
                }

                break;
        }

        return numMinors;
    }

    private static int GetNumMajorCantrips(TreasureDeath profile)
    {
        var numMajors = 0;

        var dropRate = PropertyManager.GetDouble("major_cantrip_drop_rate").Item;
        if (dropRate <= 0)
        {
            return 0;
        }

        var dropRateMod = 1.0 / dropRate;

        double lootQualityMod = 1.0f;
        if (
            PropertyManager.GetBool("loot_quality_mod").Item
            && profile.LootQualityMod > 0
            && profile.LootQualityMod < 1
        )
        {
            lootQualityMod = 1.0f - profile.LootQualityMod;
        }

        switch (profile.Tier)
        {
            case 1:
                numMajors = 0;
                break;
            case 2:
                if (ThreadSafeRandom.Next(1, (int)(500 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 1;
                }

                break;
            case 3:
                if (ThreadSafeRandom.Next(1, (int)(500 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 1;
                }

                if (ThreadSafeRandom.Next(1, (int)(10000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 2;
                }

                break;
            case 4:
            case 5:
            case 6:
                if (ThreadSafeRandom.Next(1, (int)(500 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 1;
                }

                if (ThreadSafeRandom.Next(1, (int)(5000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 2;
                }

                break;
            case 7:
            default:
                if (ThreadSafeRandom.Next(1, (int)(500 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 1;
                }

                if (ThreadSafeRandom.Next(1, (int)(5000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 2;
                }

                if (ThreadSafeRandom.Next(1, (int)(15000 * dropRateMod * lootQualityMod)) == 1)
                {
                    numMajors = 3;
                }

                break;
        }

        return numMajors;
    }

    private static int GetNumEpicCantrips(TreasureDeath profile)
    {
        var numEpics = 0;

        if (profile.Tier < 7)
        {
            return 0;
        }

        var dropRate = PropertyManager.GetDouble("epic_cantrip_drop_rate").Item;
        if (dropRate <= 0)
        {
            return 0;
        }

        var dropRateMod = 1.0 / dropRate;

        double lootQualityMod = 1.0f;
        if (
            PropertyManager.GetBool("loot_quality_mod").Item
            && profile.LootQualityMod > 0
            && profile.LootQualityMod < 1
        )
        {
            lootQualityMod = 1.0f - profile.LootQualityMod;
        }

        // 25% base chance for no epics for tier 7
        if (ThreadSafeRandom.Next(1, 4) > 1)
        {
            // 1% chance for 1 Epic, 0.1% chance for 2 Epics,
            // 0.01% chance for 3 Epics, 0.001% chance for 4 Epics
            if (ThreadSafeRandom.Next(1, (int)(100 * dropRateMod * lootQualityMod)) == 1)
            {
                numEpics = 1;
            }

            if (ThreadSafeRandom.Next(1, (int)(1000 * dropRateMod * lootQualityMod)) == 1)
            {
                numEpics = 2;
            }

            if (ThreadSafeRandom.Next(1, (int)(10000 * dropRateMod * lootQualityMod)) == 1)
            {
                numEpics = 3;
            }

            if (ThreadSafeRandom.Next(1, (int)(100000 * dropRateMod * lootQualityMod)) == 1)
            {
                numEpics = 4;
            }
        }

        return numEpics;
    }

    private static int GetNumLegendaryCantrips(TreasureDeath profile)
    {
        var numLegendaries = 0;

        if (profile.Tier < 8)
        {
            return 0;
        }

        var dropRate = PropertyManager.GetDouble("legendary_cantrip_drop_rate").Item;
        if (dropRate <= 0)
        {
            return 0;
        }

        var dropRateMod = 1.0 / dropRate;

        double lootQualityMod = 1.0f;
        if (
            PropertyManager.GetBool("loot_quality_mod").Item
            && profile.LootQualityMod > 0
            && profile.LootQualityMod < 1
        )
        {
            lootQualityMod = 1.0f - profile.LootQualityMod;
        }

        // 1% chance for a legendary, 0.02% chance for 2 legendaries
        if (ThreadSafeRandom.Next(1, (int)(100 * dropRateMod * lootQualityMod)) == 1)
        {
            numLegendaries = 1;
        }

        if (ThreadSafeRandom.Next(1, (int)(500 * dropRateMod * lootQualityMod)) == 1)
        {
            numLegendaries = 2;
        }

        return numLegendaries;
    }

    private static int GetLowSpellTier(int tier)
    {
        int lowSpellTier;
        switch (tier)
        {
            case 1:
                lowSpellTier = 1;
                break;
            case 2:
                lowSpellTier = 3;
                break;
            case 3:
                lowSpellTier = 4;
                break;
            case 4:
                lowSpellTier = 5;
                break;
            case 5:
            case 6:
                lowSpellTier = 6;
                break;
            default:
                lowSpellTier = 7;
                break;
        }

        return lowSpellTier;
    }

    private static int GetHighSpellTier(int tier)
    {
        int highSpellTier;
        switch (tier)
        {
            case 1:
                highSpellTier = 3;
                break;
            case 2:
                highSpellTier = 5;
                break;
            case 3:
            case 4:
                highSpellTier = 6;
                break;
            case 5:
            case 6:
                highSpellTier = 7;
                break;
            default:
                highSpellTier = 8;
                break;
        }

        return highSpellTier;
    }

    private static void Shuffle(int[] array)
    {
        // verified even distribution
        for (var i = 0; i < array.Length; i++)
        {
            var idx = ThreadSafeRandom.Next(i, array.Length - 1);

            var temp = array[idx];
            array[idx] = array[i];
            array[i] = temp;
        }
    }

    /// <summary>
    /// Returns the maximum BaseMana from the spells in item's spellbook
    /// </summary>
    public static int GetCombinedSpellManaCost(WorldObject wo)
    {
        var maxBaseMana = 0;

        if (wo.SpellDID != null)
        {
            var spell = new Server.Entity.Spell(wo.SpellDID.Value);

            maxBaseMana += (int)spell.BaseMana;
        }

        if (wo.Biota.PropertiesSpellBook != null)
        {
            foreach (var spellId in wo.Biota.PropertiesSpellBook.Keys)
            {
                var spell = new Server.Entity.Spell(spellId);

                maxBaseMana += (int)spell.BaseMana;
            }
        }

        if (wo.ProcSpell != null)
        {
            var spell = new Server.Entity.Spell(wo.ProcSpell.Value);

            maxBaseMana += (int)spell.BaseMana;
        }

        return maxBaseMana;
    }

    // old table / method

    private static readonly List<(int min, int max)> itemMaxMana_RandomRange = new List<(int min, int max)>()
    {
        (200, 400), // T1
        (400, 600), // T2
        (600, 800), // T3
        (800, 1000), // T4
        (1000, 1200), // T5
        (1200, 1400), // T6
        (1400, 1600), // T7
        (1600, 1800), // T8
    };

    public static int RollItemMaxMana(int tier, int numSpells)
    {
        var range = itemMaxMana_RandomRange[tier - 1];

        var rng = ThreadSafeRandom.Next(range.min, range.max);

        return rng * numSpells;
    }

    /// <summary>
    /// Rolls the ItemMaxMana for an object
    /// </summary>
    public static int RollItemMaxMana_New(int tier = 1, int armorSlots = 1)
    {
        var baseMaxMana = 15;
        switch (tier)
        {
            case 1:
                baseMaxMana = 15;
                break;
            case 2:
                baseMaxMana = 15;
                break;
            case 3:
                baseMaxMana = 30;
                break;
            case 4:
                baseMaxMana = 60;
                break;
            case 5:
                baseMaxMana = 120;
                break;
            case 6:
                baseMaxMana = 240;
                break;
            case 7:
                baseMaxMana = 480;
                break;
            case 8:
                baseMaxMana = 900;
                break;
        }

        var rng = ThreadSafeRandom.Next(0.8f, 1.2f);

        return (int)Math.Ceiling((decimal)(baseMaxMana * armorSlots * rng));
    }

    /// <summary>
    /// Calculates the ManaRate for an item
    /// </summary>
    public static float CalculateManaRate(WorldObject wo)
    {
        var tier = wo.Tier ?? 1;
        var armorSlots = wo.ArmorSlots ?? 1;
        var rng = (float)ThreadSafeRandom.Next(4.0f, 6.0f);

        var baseManaRate = -1.0f;
        switch (tier)
        {
            case 1:
                baseManaRate = -0.002083f;
                break;
            case 2:
                baseManaRate = -0.002083f;
                break;
            case 3:
                baseManaRate = -0.004167f;
                break;
            case 4:
                baseManaRate = -0.008333f;
                break;
            case 5:
                baseManaRate = -0.016667f;
                break;
            case 6:
                baseManaRate = -0.033333f;
                break;
            case 7:
                baseManaRate = -0.066667f;
                break;
            case 8:
                baseManaRate = -0.125000f;
                break;
        }

        // verified with eor data
        return baseManaRate * armorSlots / rng;
    }

    // old method / based on item type

    public static int RollSpellcraft(WorldObject wo, TreasureDeath td = null)
    {
        //var maxSpellPower = GetMaxSpellPower(wo);
        var maxSpellPower = GetTierSpellPower(wo);

        var lootQualityMod = wo.LootQualityMod ?? 0.0f;
        var bonusRoll = 200 * GetDiminishingRoll(td, (float)lootQualityMod);

        var spellcraft = (int)Math.Ceiling(maxSpellPower + bonusRoll);

        // retail was capped at 370
        spellcraft = Math.Min(spellcraft, 600);

        return spellcraft;
    }

    private static double GetTierSpellPower(WorldObject wo)
    {
        return wo.Tier switch
        {
            1 => 50,
            2 => 100,
            3 => 150,
            4 => 200,
            5 => 250,
            6 => 300,
            7 => 350,
            8 => 400,
            _ => 1
        };
    }

    // new method / based on treasure roll

    private static int RollSpellcraft(WorldObject wo, TreasureRoll roll, TreasureDeath treasureDeath)
    {
        var maxSpellPower = GetMaxSpellPower(wo);

        if (
            !roll.IsClothing
            && !roll.IsArmor
            && !roll.IsWeapon
            && !roll.IsJewelry
            && roll.IsDinnerware
            && !roll.IsGem
            && roll.ItemType != TreasureItemType_Orig.WeaponCaster
            && roll.ItemType != TreasureItemType_Orig.WeaponRogue
            && roll.ItemType != TreasureItemType_Orig.WeaponWarrior
            && roll.ItemType != TreasureItemType_Orig.ArmorCaster
            && roll.ItemType != TreasureItemType_Orig.ArmorRogue
            && roll.ItemType != TreasureItemType_Orig.ArmorWarrior
        )
        {
            _log.Error($"RollSpellcraft({wo.Name}, {roll.ItemType}) - unknown item type");
        }

        var rng = 0.5f * GetDiminishingRoll(treasureDeath);

        var spellcraft = (int)Math.Ceiling(maxSpellPower + maxSpellPower * rng);

        return spellcraft;
    }

    public static int GetSpellPower(Server.Entity.Spell spell)
    {
        switch (spell.Formula.Level)
        {
            case 1:
                return 50; // EoR is 1
            case 2:
                return 150; // EoR is 50
            case 3:
                return 200; // EoR is 100
            case 4:
                return 250; // EoR is 150
            case 5:
                return 300; // EoR is 200
            case 6:
                return 350; // EoR is 250
            case 7:
            default:
                return 400; // EoR is 300
        }
    }

    /// <summary>
    /// Returns the maximum power from the spells in item's SpellDID / spellbook / ProcSpell
    /// </summary>
    public static int GetMaxSpellPower(WorldObject wo)
    {
        var maxSpellPower = 0;

        if (wo.SpellDID != null)
        {
            var spell = new Server.Entity.Spell(wo.SpellDID.Value);

            var spellPower = GetSpellPower(spell);
            if (spellPower > maxSpellPower)
            {
                maxSpellPower = spellPower;
            }
        }

        //if (wo.Biota.PropertiesSpellBook != null)
        //{
        //    foreach (var spellId in wo.Biota.PropertiesSpellBook.Keys)
        //    {
        //        var spell = new Server.Entity.Spell(spellId);

        //        int spellPower = GetSpellPower(spell);
        //        if (spellPower > maxSpellPower)
        //            maxSpellPower = spellPower;
        //    }
        //}

        if (wo.ProcSpell != null)
        {
            var spell = new Server.Entity.Spell(wo.ProcSpell.Value);

            var spellPower = GetSpellPower(spell);
            if (spellPower > maxSpellPower)
            {
                maxSpellPower = spellPower;
            }
        }

        return maxSpellPower;
    }

    private static void AddActivationRequirements(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        // Arcane Lore / ItemDifficulty
        wo.ItemDifficulty = CalculateArcaneLore(wo, roll);
    }

    private static bool TryMutate_HeritageRequirement(WorldObject wo, TreasureDeath profile, TreasureRoll roll)
    {
        if (wo.Biota.PropertiesSpellBook == null && (wo.SpellDID ?? 0) == 0 && (wo.ProcSpell ?? 0) == 0)
        {
            return false;
        }

        var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (rng < 0.05)
        {
            if (roll.Heritage == TreasureHeritageGroup.Invalid)
            {
                roll.Heritage = (TreasureHeritageGroup)ThreadSafeRandom.Next(1, 3);
            }

            switch (roll.Heritage)
            {
                case TreasureHeritageGroup.Aluvian:
                    wo.HeritageGroup = HeritageGroup.Aluvian;
                    break;

                case TreasureHeritageGroup.Gharundim:
                    wo.HeritageGroup = HeritageGroup.Gharundim;
                    break;

                case TreasureHeritageGroup.Sho:
                    wo.HeritageGroup = HeritageGroup.Sho;
                    break;
            }
            return true;
        }
        return false;
    }

    private static bool TryMutate_ItemSkillLimit(WorldObject wo, TreasureRoll roll)
    {
        if (!RollItemSkillLimit(roll))
        {
            return false;
        }

        wo.ItemSkillLevelLimit = wo.ItemSpellcraft + 20;

        var skill = Skill.None;

        if (roll.IsMeleeWeapon || roll.IsMissileWeapon)
        {
            skill = wo.WeaponSkill;
            if (wo.WieldRequirements == WieldRequirement.RawSkill && wo.WieldDifficulty > wo.ItemSkillLevelLimit)
            {
                wo.ItemSkillLevelLimit = wo.WieldDifficulty + ThreadSafeRandom.Next(5, 20);
            }
        }
        else if (roll.IsArmor)
        {
            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (rng < 0.5f)
            {
                skill = Skill.PhysicalDefense;
            }
            else
            {
                skill = Skill.MissileDefense;
                wo.ItemSkillLevelLimit = (int)(wo.ItemSkillLevelLimit * 0.7f);
            }
        }
        else
        {
            _log.Error($"RollItemSkillLimit({wo.Name}, {roll.ItemType}) - unknown item type");
            return false;
        }
        if (wo.WieldRequirements != WieldRequirement.RawAttrib)
        {
            wo.ItemSkillLimit = wo.ConvertToMoASkill(skill);
        }
        return true;
    }

    private static bool RollItemSkillLimit(TreasureRoll roll)
    {
        if (roll.IsMeleeWeapon || roll.IsMissileWeapon)
        {
            return true;
        }
        else if (roll.IsArmor && !roll.IsClothArmor)
        {
            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);

            return rng < 0.55f;
        }
        return false;
    }

    // previous method - replaces itemSkillLevelLimit w/ WieldDifficulty,
    // and does not use treasure roll

    private static int RollItemDifficulty(WorldObject wo, int numEpics, int numLegendaries)
    {
        // - # of spells on item
        var num_spells = wo.Biota.PropertiesSpellBook.Count();

        if (wo.ProcSpell != null)
        {
            num_spells++;
        }

        var spellAddonChance = num_spells * (20.0f / (num_spells + 2.0f));
        var spellAddon = (float)ThreadSafeRandom.Next(1.0f, spellAddonChance) * num_spells;

        // - # of epics / legendaries on item
        var epicAddon = 0;
        var legAddon = 0;

        // wield difficulty - skill requirement
        var wieldFactor = 0.0f;

        if (wo.WieldDifficulty != null && wo.WieldRequirements == WieldRequirement.RawSkill)
        {
            wieldFactor = wo.WieldDifficulty.Value / 3.0f;
        }

        var itemDifficulty = wo.ItemSpellcraft.Value - wieldFactor;

        if (itemDifficulty < 0)
        {
            itemDifficulty = 0;
        }

        return (int)Math.Floor(itemDifficulty + spellAddon + epicAddon + legAddon);
    }

    /// <summary>
    /// Calculates the Arcane Lore requirement / ItemDifficulty
    /// </summary>
    public static int CalculateArcaneLore(WorldObject wo, TreasureRoll roll)
    {
        var numSpells = 0;
        var increasedDifficulty = 0.0f;

        if (wo.Biota.PropertiesSpellBook != null)
        {
            int MINOR = 0,
                MAJOR = 1,
                EPIC = 2,
                LEGENDARY = 3;

            foreach (SpellId spellId in wo.Biota.PropertiesSpellBook.Keys)
            {
                numSpells++;

                var cantripLevels = SpellLevelProgression.GetSpellLevels(spellId);

                var cantripLevel = cantripLevels.IndexOf(spellId);

                if (cantripLevel == MINOR)
                {
                    increasedDifficulty += 5;
                }
                else if (cantripLevel == MAJOR)
                {
                    increasedDifficulty += 10;
                }
                else if (cantripLevel == EPIC)
                {
                    increasedDifficulty += 15;
                }
                else if (cantripLevel == LEGENDARY)
                {
                    increasedDifficulty += 20;
                }
                else
                {
                    _log.Warning(
                        $"LootGenerationFactory_Magic.CalculateArcaneLore({wo.Name}, {roll}) - SpellId did not have correct cantrip level"
                    );
                }
            }
        }

        var tier = wo.Tier.Value - 1;

        if (wo.ProcSpell != null)
        {
            numSpells++;
            increasedDifficulty += Math.Max(5 * tier, 5);
        }

        var finalDifficulty = 0;
        var armorSlots = wo.ArmorSlots ?? 1;
        var spellsPerSlot = (float)numSpells / armorSlots;

        if (spellsPerSlot > 1 || wo.ProcSpell != null)
        {
            var baseDifficulty = ActivationDifficultyPerTier(tier);

            finalDifficulty = baseDifficulty + (int)(increasedDifficulty / armorSlots);
        }

        return finalDifficulty;
    }

    private static int ActivationDifficultyPerTier(int tier)
    {
        switch (tier)
        {
            case 1:
                return 75;
            case 2:
                return 175;
            case 3:
                return 225;
            case 4:
                return 275;
            case 5:
                return 325;
            case 6:
                return 375;
            case 7:
                return 425;
            default:
                return 50;
        }
    }
}
