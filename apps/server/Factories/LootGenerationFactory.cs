using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.Factories.Tables.Wcids.Weapons;
using ACE.Server.Factories.Tables.Wcids.Weapons.Legacy;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using Serilog;
using WeenieClassName = ACE.Server.Factories.Enum.WeenieClassName;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    private static readonly ILogger _log = Log.ForContext(typeof(LootGenerationFactory));

    // Used for cumulative ServerPerformanceMonitor event recording
    // private static readonly ThreadLocal<Stopwatch> stopwatch = new ThreadLocal<Stopwatch>(() => new Stopwatch());

    static LootGenerationFactory()
    {
        InitRares();
        InitClothingColors();

        coinRanges = new List<(int, int)>()
        {
            (5, 25), // T1
            (10, 50), // T2
            (15, 75), // T3
            (20, 100), // T4
            (25, 125), // T5
            (30, 150), // T6
            (35, 175), // T7
            (40, 200), // T8
        };

        ItemValue_TierMod = new List<int>()
        {
            25, // T1
            50, // T2
            65, // T3
            80, // T4
            95, // T5
            110, // T6
            125, // T7
            140, // T8
        };
    }

    public static TreasureDeath GetTweakedDeathTreasureProfile(uint deathTreasureId, object tweakedFor)
    {
        if (deathTreasureId == 338) // Leave Steel Chests alone!
        {
            return DatabaseManager.World.GetCachedDeathTreasure(deathTreasureId);
        }

        TreasureDeath deathTreasure;
        TreasureDeath tweakedDeathTreasure;

        var creature = tweakedFor as Creature;
        if (creature != null)
        {
            deathTreasure = DatabaseManager.World.GetCachedDeathTreasure(deathTreasureId);
            if (deathTreasure == null)
            {
                return deathTreasure;
            }

            tweakedDeathTreasure = new TreasureDeath(deathTreasure);

            // Adjust loot drops within a tier based on monster level within the tier
            // Monsters towards the end of the tier should have higher drop rates and improved quality loot
            var minLevelOfTier = 1;
            var maxLevelOfTier = 1;
            var creatureTier = creature.Tier ?? 1;

            switch (creatureTier)
            {
                case 1:
                    minLevelOfTier = 1;
                    maxLevelOfTier = 9;
                    break;
                case 2:
                    minLevelOfTier = 10;
                    maxLevelOfTier = 19;
                    break;
                case 3:
                    minLevelOfTier = 20;
                    maxLevelOfTier = 29;
                    break;
                case 4:
                    minLevelOfTier = 30;
                    maxLevelOfTier = 39;
                    break;
                case 5:
                    minLevelOfTier = 40;
                    maxLevelOfTier = 49;
                    break;
                case 6:
                    minLevelOfTier = 50;
                    maxLevelOfTier = 74;
                    break;
                case 7:
                    minLevelOfTier = 75;
                    maxLevelOfTier = 99;
                    break;
                case 8:
                    minLevelOfTier = 100;
                    maxLevelOfTier = 125;
                    break;
            }

            var minimumMod = 1.0f;

            // Monsters that are higher level within their tier drop better loot.
            var levelsPerTier = maxLevelOfTier - minLevelOfTier + 1;
            var percentile = (float)(creature.Level - minLevelOfTier + 1) / levelsPerTier;
            var tierMod = 1.0f / creatureTier; // Reduce the potency of this effect at higher tiers.

            var mod = minimumMod + (percentile * minimumMod * tierMod);
            mod = Math.Min(1.9f, mod); // cap at +90%

            var baseItemChance = tweakedDeathTreasure.ItemChance;
            var baseMagicItemChance = tweakedDeathTreasure.MagicItemChance;
            var baseMundaneItemChance = tweakedDeathTreasure.MundaneItemChance;

            tweakedDeathTreasure.ItemChance = (int)(baseItemChance * mod);
            tweakedDeathTreasure.MagicItemChance = (int)(baseMagicItemChance * mod);
            tweakedDeathTreasure.MundaneItemChance = (int)(baseMundaneItemChance * mod);

            // if drop chances were modified to be above 100%, give a chance for an additional item.
            if (
                tweakedDeathTreasure.ItemChance > 100
                && ThreadSafeRandom.Next(1, 100) < tweakedDeathTreasure.ItemChance - 100
            )
            {
                tweakedDeathTreasure.ItemMinAmount += 1;
                tweakedDeathTreasure.ItemMaxAmount += 1;
            }

            if (
                tweakedDeathTreasure.MagicItemChance > 100
                && ThreadSafeRandom.Next(1, 100) < tweakedDeathTreasure.MagicItemChance - 100
            )
            {
                tweakedDeathTreasure.MagicItemMinAmount += 1;
                tweakedDeathTreasure.MagicItemMaxAmount += 1;
            }

            if (
                tweakedDeathTreasure.MundaneItemChance > 100
                && ThreadSafeRandom.Next(1, 100) < tweakedDeathTreasure.MundaneItemChance - 100
            )
            {
                tweakedDeathTreasure.MundaneItemMinAmount += 1;
                tweakedDeathTreasure.MundaneItemMaxAmount += 1;
            }

            // Loot Quality receives a boost of of +0% to +90%, depending on an enemy's tier and level within its tier.
            var qualityMod = mod - 1.0f;
            var qualityBonus = (1 - tweakedDeathTreasure.LootQualityMod) * qualityMod;

            // JEWEL - Sappphire: Bonus Loot Quality
            var updatedQualityMod = creature.QuestManager.HandleMagicFind();

            qualityBonus += (float)updatedQualityMod;

            tweakedDeathTreasure.LootQualityMod += qualityBonus;

            // JEWEL - Green Jade: Bonus Min Item Chance
            var prosperityMod = creature.QuestManager.HandleProsperity();

            if (prosperityMod >= ThreadSafeRandom.Next(0f, 1f))
            {
                tweakedDeathTreasure.ItemMinAmount += 1;
            }

            if (PropertyManager.GetBool("debug_loot_quality_system").Item)
            {
                Console.WriteLine(
                    $"\n{creature.Name}\n"
                        + $" -CreatureLevel: {creature.Level} TierMinLv: {minLevelOfTier} TierMaxLv: {maxLevelOfTier} LevelsPerTier: {levelsPerTier} Percentile: {percentile} Mod: {mod}\n"
                        + $" -ItemBaseChance: {baseItemChance} -ModdedItemChance: {(int)(tweakedDeathTreasure.ItemChance)}\n"
                        + $" -MagicBaseChance: {baseMagicItemChance} -ModdedMagicChance: {(int)(tweakedDeathTreasure.MagicItemChance)}\n"
                        + $" -MundaneBaseChance: {baseMundaneItemChance} -ModdedMundaneChance: {(int)(tweakedDeathTreasure.MundaneItemChance)}\n"
                        + $" -QualityMod: {qualityMod}, QualityBonus: {qualityBonus}, FinalLootQualityMod: {tweakedDeathTreasure.LootQualityMod}"
                );
            }

            return tweakedDeathTreasure;
        }

        deathTreasure = DatabaseManager.World.GetCachedDeathTreasure(deathTreasureId);
        if (deathTreasure == null)
        {
            return deathTreasure;
        }

        if (tweakedFor is Container)
        {
            // Some overrides to make chests more interesting, ideally this should be done in the data but as a quick tweak this will do.
            tweakedDeathTreasure = new TreasureDeath(deathTreasure);

            return tweakedDeathTreasure;
        }
        else if (tweakedFor is GenericObject generic && generic.GeneratorProfiles != null) // Ground item spawners
        {
            tweakedDeathTreasure = new TreasureDeath(deathTreasure);

            return tweakedDeathTreasure;
        }
        else
        {
            return deathTreasure;
        }
    }

    public static List<WorldObject> CreateRandomLootObjects(TreasureDeath profile)
    {
        if (!PropertyManager.GetBool("legacy_loot_system").Item)
        {
            return CreateRandomLootObjects_New(profile);
        }

        // stopwatch.Value.Restart();

        try
        {
            int numItems;
            WorldObject lootWorldObject;

            var lootBias = LootBias.UnBiased;
            var loot = new List<WorldObject>();

            switch (profile.TreasureType)
            {
                case 1001: // Mana Forge Chest, Advanced Equipment Chest, and Mixed Equipment Chest
                case 2001:
                    lootBias = LootBias.MixedEquipment;
                    break;
                case 1002: // Armor Chest
                case 2002:
                    lootBias = LootBias.Armor;
                    break;
                case 1003: // Magic Chest
                case 2003:
                    lootBias = LootBias.MagicEquipment;
                    break;
                case 1004: // Weapon Chest
                case 2004:
                    lootBias = LootBias.Weapons;
                    break;
                default: // Default to unbiased loot profile
                    break;
            }

            // For Society Armor - Only generates 2 pieces of Society Armor.
            // breaking it out here to Generate Armor
            if (profile.TreasureType >= 2971 && profile.TreasureType <= 2999)
            {
                numItems = ThreadSafeRandom.Next(profile.MagicItemMinAmount, profile.MagicItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    lootWorldObject = CreateSocietyArmor(profile);
                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }

                return loot;
            }

            var itemChance = ThreadSafeRandom.Next(1, 100);
            if (itemChance <= profile.ItemChance)
            {
                numItems = ThreadSafeRandom.Next(profile.ItemMinAmount, profile.ItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    // verify this works as intended. this will also be true for MixedEquipment...
                    if (lootBias == LootBias.MagicEquipment)
                    {
                        lootWorldObject = CreateRandomLootObjects(profile, false, LootBias.Weapons);
                    }
                    else
                    {
                        lootWorldObject = CreateRandomLootObjects(profile, false, lootBias);
                    }

                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }
            }

            itemChance = ThreadSafeRandom.Next(1, 100);
            if (itemChance <= profile.MagicItemChance)
            {
                numItems = ThreadSafeRandom.Next(profile.MagicItemMinAmount, profile.MagicItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    lootWorldObject = CreateRandomLootObjects(profile, true, lootBias);
                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }
            }

            itemChance = ThreadSafeRandom.Next(1, 100);
            if (itemChance <= profile.MundaneItemChance)
            {
                var dropRate = PropertyManager.GetDouble("aetheria_drop_rate").Item;
                var dropRateMod = 1.0 / dropRate;

                // Coalesced Aetheria doesn't drop in loot tiers less than 5
                // According to wiki, Weapon Mana Forge chests don't drop Aetheria
                // An Aetheria drop was in addition to the normal drops of the mundane profile
                // https://asheron.fandom.com/wiki/Announcements_-_2010/04_-_Shedding_Skin :: May 5th, 2010 entry
                if (profile.Tier > 4 && lootBias != LootBias.Weapons && dropRate > 0)
                {
                    if (ThreadSafeRandom.Next(1, (int)(100 * dropRateMod)) <= 2) // base 1% to drop aetheria?
                    {
                        lootWorldObject = CreateAetheria(profile.Tier);
                        if (lootWorldObject != null)
                        {
                            loot.Add(lootWorldObject);
                        }
                    }
                }

                numItems = ThreadSafeRandom.Next(profile.MundaneItemMinAmount, profile.MundaneItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    if (lootBias != LootBias.UnBiased)
                    {
                        lootWorldObject = CreateRandomScroll(profile);
                    }
                    else
                    {
                        lootWorldObject = CreateGenericObjects(profile);
                    }

                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }
            }

            return loot;
        }
        finally
        {
            // ServerPerformanceMonitor.AddToCumulativeEvent(
            //     ServerPerformanceMonitor.CumulativeEventHistoryType.LootGenerationFactory_CreateRandomLootObjects,
            //     stopwatch.Value.Elapsed.TotalSeconds
            // );
        }
    }

    public static List<WorldObject> CreateRandomLootObjects_New(TreasureDeath profile)
    {
        // stopwatch.Value.Restart();

        try
        {
            int numItems;
            WorldObject lootWorldObject;

            var loot = new List<WorldObject>();

            //Console.WriteLine(profile.Id);
            double itemChance;
            if (profile.ItemChance == 100)
            {
                numItems = ThreadSafeRandom.Next(profile.ItemMinAmount, profile.ItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.Item);

                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }
            }
            else if (profile.ItemChance > 0)
            {
                itemChance = ThreadSafeRandom.NextInterval(profile.LootQualityMod);

                //Console.WriteLine($"\n\nItemChance: {profile.ItemChance} Roll: {itemChance * 100}");

                if (itemChance < profile.ItemChance / 100.0)
                {
                    // If we roll this bracket we are guaranteed at least ItemMinAmount of items, with an extra roll for each additional item under itemMaxAmount.
                    for (var i = 0; i < profile.ItemMinAmount; i++)
                    {
                        lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.Item);

                        if (lootWorldObject != null)
                        {
                            loot.Add(lootWorldObject);
                        }
                    }

                    for (var i = 0; i < profile.ItemMaxAmount - profile.ItemMinAmount; i++)
                    {
                        itemChance = ThreadSafeRandom.NextInterval(profile.LootQualityMod);
                        if (itemChance < profile.ItemChance / 100.0)
                        {
                            lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.Item);

                            //Console.WriteLine($"Success! Item: {lootWorldObject.Name}");

                            if (lootWorldObject != null)
                            {
                                loot.Add(lootWorldObject);
                            }
                        }
                    }
                }
            }

            if (profile.MagicItemChance == 100)
            {
                numItems = ThreadSafeRandom.Next(profile.MagicItemMinAmount, profile.MagicItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.MagicItem);

                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }
            }
            else if (profile.MagicItemChance > 0)
            {
                itemChance = ThreadSafeRandom.NextInterval(profile.LootQualityMod);

                if (itemChance < profile.MagicItemChance / 100.0)
                {
                    // If we roll this bracket we are guaranteed at least MagicItemMinAmount of items, with an extra roll for each additional item under MagicItemMaxAmount.
                    for (var i = 0; i < profile.MagicItemMinAmount; i++)
                    {
                        lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.MagicItem);

                        if (lootWorldObject != null)
                        {
                            loot.Add(lootWorldObject);
                        }
                    }

                    for (var i = 0; i < profile.MagicItemMaxAmount - profile.MagicItemMinAmount; i++)
                    {
                        itemChance = ThreadSafeRandom.NextInterval(profile.LootQualityMod);
                        if (itemChance < profile.MagicItemChance / 100.0)
                        {
                            lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.MagicItem);

                            //Console.WriteLine($"Success! Item: {lootWorldObject.Name}");

                            if (lootWorldObject != null)
                            {
                                loot.Add(lootWorldObject);
                            }
                        }
                    }
                }
            }

            if (profile.MundaneItemChance == 100)
            {
                numItems = ThreadSafeRandom.Next(profile.MundaneItemMinAmount, profile.MundaneItemMaxAmount);

                for (var i = 0; i < numItems; i++)
                {
                    lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.MundaneItem);

                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }

                // extra roll for mundane:
                // https://asheron.fandom.com/wiki/Announcements_-_2010/04_-_Shedding_Skin :: May 5th, 2010 entry
                // aetheria and coalesced mana were handled in here
                lootWorldObject = TryRollMundaneAddon(profile);

                if (lootWorldObject != null)
                {
                    loot.Add(lootWorldObject);
                }
            }
            else if (profile.MundaneItemChance > 0)
            {
                itemChance = ThreadSafeRandom.NextInterval(profile.LootQualityMod);

                //Console.WriteLine($"\nMaundaneItemChance: {profile.MundaneItemChance} Roll: {itemChance * 100}");

                if (itemChance < profile.MundaneItemChance / 100.0)
                {
                    for (var i = 0; i < profile.MundaneItemMinAmount; i++)
                    {
                        lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.MundaneItem);

                        //Console.WriteLine($"Success! Item: {lootWorldObject.Name}");

                        if (lootWorldObject != null)
                        {
                            loot.Add(lootWorldObject);
                        }
                    }

                    for (var i = 0; i < profile.MundaneItemMaxAmount - profile.MundaneItemMinAmount; i++)
                    {
                        itemChance = ThreadSafeRandom.NextInterval(profile.LootQualityMod);
                        if (itemChance < profile.MundaneItemChance / 100.0)
                        {
                            lootWorldObject = CreateRandomLootObjects_New(profile, TreasureItemCategory.MundaneItem);

                            if (lootWorldObject != null)
                            {
                                loot.Add(lootWorldObject);
                            }
                        }
                    }

                    // extra roll for mundane:
                    // https://asheron.fandom.com/wiki/Announcements_-_2010/04_-_Shedding_Skin :: May 5th, 2010 entry
                    // aetheria and coalesced mana were handled in here
                    lootWorldObject = TryRollMundaneAddon(profile);

                    if (lootWorldObject != null)
                    {
                        loot.Add(lootWorldObject);
                    }
                }
            }

            return loot;
        }
        finally
        {
            // ServerPerformanceMonitor.AddToCumulativeEvent(
            //     ServerPerformanceMonitor.CumulativeEventHistoryType.LootGenerationFactory_CreateRandomLootObjects,
            //     stopwatch.Value.Elapsed.TotalSeconds
            // );
        }
    }

    /// <summary>
    /// Mundane additional items.
    /// ie. Coalesced mana, Aetheria
    /// </summary>
    private static WorldObject TryRollMundaneAddon(TreasureDeath profile)
    {
        return null; // Change this to enable drops of Coalisced Mana and Aetheria

        //// coalesced mana only dropped in tiers 1-4
        //if (profile.Tier <= 4)
        //    return TryRollCoalescedMana(profile);

        //// aetheria dropped in tiers 5+
        //else
        //    return TryRollAetheria(profile);
    }

    private static WorldObject TryRollCoalescedMana(TreasureDeath profile)
    {
        // 2% chance in here, which turns out to be less per corpse w/ MundaneItemChance > 0,
        // when the outer MundaneItemChance roll is factored in

        // loot quality mod?
        var rng = ThreadSafeRandom.Next(0.0f, 1.0f);

        if (rng < 0.02f)
        {
            return CreateCoalescedMana(profile);
        }
        else
        {
            return null;
        }
    }

    private static WorldObject TryRollAetheria(TreasureDeath profile)
    {
        var aetheria_drop_rate = (float)PropertyManager.GetDouble("aetheria_drop_rate").Item;

        if (aetheria_drop_rate <= 0.0f)
        {
            return null;
        }

        var dropRateMod = 1.0f / aetheria_drop_rate;

        // 2% base chance in here, which turns out to be less per corpse w/ MundaneItemChance > 0,
        // when the outer MundaneItemChance roll is factored in

        // loot quality mod?
        var rng = ThreadSafeRandom.Next(0.0f, 1.0f * dropRateMod);

        if (rng < 0.02f)
        {
            return CreateAetheria_New(profile);
        }
        else
        {
            return null;
        }
    }

    public static WorldObject CreateRandomLootObjects(
        TreasureDeath profile,
        bool isMagical,
        LootBias lootBias = LootBias.UnBiased
    )
    {
        WorldObject wo = null;

        ChanceTable<TreasureItemType> treasureItemTypeChances;

        treasureItemTypeChances = isMagical
            ? TreasureItemTypeChances.TimelineDefaultMagical
            : TreasureItemTypeChances.TimelineDefaultNonMagical;

        switch (lootBias)
        {
            case LootBias.Armor:
                treasureItemTypeChances = TreasureItemTypeChances.Armor;
                break;
            case LootBias.Weapons:
                treasureItemTypeChances = TreasureItemTypeChances.Weapons;
                break;
            case LootBias.Jewelry:
                treasureItemTypeChances = TreasureItemTypeChances.Jewelry;
                break;

            case LootBias.MagicEquipment:
            case LootBias.MixedEquipment:
                treasureItemTypeChances = TreasureItemTypeChances.MixedMagicEquipment;
                break;
        }

        var treasureItemType = treasureItemTypeChances.Roll();
        int type;

        switch (treasureItemType)
        {
            case TreasureItemType.Gem:
                wo = CreateGem(profile, isMagical);
                break;

            case TreasureItemType.Armor:
                wo = CreateArmor(profile, isMagical, true, TreasureItemType.Undef, lootBias);
                break;

            case TreasureItemType.Clothing:
                wo = CreateArmor(profile, isMagical, false, TreasureItemType.Undef, lootBias);
                break;

            case TreasureItemType.Cloak:
                wo = CreateCloak(profile);
                break;

            case TreasureItemType.Weapon:
                wo = CreateWeapon(profile, isMagical);
                break;

            case TreasureItemType.Jewelry:
                wo = CreateJewelry(profile, isMagical);
                break;

            case TreasureItemType.Dinnerware:
                // Added Dinnerware at tail end of distribution, as
                // they are mutable loot drops that don't belong with the non-mutable drops
                // TODO: Will likely need some adjustment/fine tuning
                wo = CreateDinnerware(profile, isMagical);
                break;

            case TreasureItemType.WeaponWarrior:
                type = ThreadSafeRandom.Next(0, 5);

                switch (type)
                {
                    default:
                    case 0:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.Axe);
                        break;
                    case 1:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.Mace);
                        break;
                    case 2:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.Spear);
                        break;
                    case 3:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.Staff);
                        break;
                    case 4:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.Sword);
                        break;
                    case 5:
                        wo = CreateMissileWeapon(profile, isMagical);
                        break;
                }
                break;

            case TreasureItemType.WeaponRogue:
                type = ThreadSafeRandom.Next(0, 2);

                switch (type)
                {
                    default:
                    case 0:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.Dagger);
                        break;
                    case 1:
                        wo = CreateMeleeWeapon(profile, isMagical, MeleeWeaponSkill.UnarmedCombat);
                        break;
                    case 2:
                        wo = CreateMissileWeapon(profile, isMagical);
                        break;
                }
                break;

            case TreasureItemType.WeaponCaster:
                CreateCaster(profile, isMagical);
                break;

            case TreasureItemType.ArmorWarrior:
                wo = CreateArmor(profile, isMagical, true, TreasureItemType.ArmorWarrior);
                break;

            case TreasureItemType.ArmorRogue:
                wo = CreateArmor(profile, isMagical, true, TreasureItemType.ArmorRogue);
                break;

            case TreasureItemType.ArmorCaster:
                wo = CreateArmor(profile, isMagical, true, TreasureItemType.ArmorCaster);
                break;

            case TreasureItemType.AnimalParts:
                wo = CreateAnimalParts(profile);
                break;

            case TreasureItemType.SigilTrinketWarrior:
                SigilTrinketType[] warriorSigilTrinkets = [SigilTrinketType.Compass, SigilTrinketType.Top, SigilTrinketType.PocketWatch];
                var sigilRng = ThreadSafeRandom.Next(0, 2);
                wo = CreateSigilTrinket(profile, warriorSigilTrinkets[sigilRng]);
                break;

            case TreasureItemType.SigilTrinketRogue:
                SigilTrinketType[] rogueSigilTrinkets = [SigilTrinketType.PuzzleBox, SigilTrinketType.Goggles, SigilTrinketType.PocketWatch];
                sigilRng = ThreadSafeRandom.Next(0, 2);
                wo = CreateSigilTrinket(profile, rogueSigilTrinkets[sigilRng]);
                break;

            case TreasureItemType.SigilTrinketCaster:
                SigilTrinketType[] casterSigilTrinkets = [SigilTrinketType.Scarab, SigilTrinketType.Top, SigilTrinketType.Goggles];
                sigilRng = ThreadSafeRandom.Next(0, 2);
                wo = CreateSigilTrinket(profile, casterSigilTrinkets[sigilRng]);
                break;
        }
        return wo;
    }

    public static bool MutateItem(WorldObject item, TreasureDeath profile, bool isMagical)
    {
        // should ideally be split up between getting the item type,
        // and getting the specific mutate function parameters
        // however, with the way the current loot tables are set up, this is not ideal...

        // this function does a bunch of o(n) lookups through the loot tables,
        // and is only used for the /lootgen dev command currently
        // if this needs to be used in high performance scenarios, the collections for the loot tables will
        // will need to be updated to support o(1) queries

        // update: most of the o(n) lookup issues have been fixed,
        // however this is still looking into more hashtables than necessary.
        // ideally there should only be 1 hashtable that gets the roll.ItemType,
        // and any other necessary info (armorType / weaponType)
        // then just call the existing mutation method

        var roll = new TreasureRoll();

        roll.Wcid = (WeenieClassName)item.WeenieClassId;
        roll.BaseArmorLevel = item.ArmorLevel ?? 0;
        roll.BaseWardLevel = item.WardLevel ?? 0;

        if (roll.Wcid == WeenieClassName.coinstack)
        {
            roll.ItemType = TreasureItemType_Orig.Pyreal;
            MutateCoins(item, profile);
        }
        else if (GemMaterialChance.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.Gem;
            MutateGem(item, profile, isMagical, roll);
        }
        else if (JewelryWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.Jewelry;

            if (!roll.HasArmorLevel(item))
            {
                MutateJewelry(item, profile, isMagical, roll);
            }
            else
            {
                // crowns, coronets, diadems, etc.
                MutateArmor(item, profile, isMagical, LootTables.ArmorType.MiscClothing, roll);
            }
        }
        else if (GenericWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.ArtObject;
            MutateDinnerware(item, profile, isMagical, roll);
        }
        else if (
            HeavyWeaponWcids.TryGetValue(roll.Wcid, out var weaponType)
            || LightWeaponWcids.TryGetValue(roll.Wcid, out weaponType)
            || FinesseWeaponWcids.TryGetValue(roll.Wcid, out weaponType)
            || TwoHandedWeaponWcids.TryGetValue(roll.Wcid, out weaponType)
            || ThrownWcids.TryGetValue(roll.Wcid, out weaponType)
        )
        {
            roll.ItemType = TreasureItemType_Orig.Weapon;
            roll.WeaponType = weaponType;
            MutateMeleeWeapon(item, profile, isMagical, roll);
        }
        else if (
            BowWcids.TryGetValue(roll.Wcid, out weaponType)
            || CrossbowWcids.TryGetValue(roll.Wcid, out weaponType)
            || AtlatlWcids.TryGetValue(roll.Wcid, out weaponType)
        )
        {
            roll.ItemType = TreasureItemType_Orig.Weapon;
            roll.WeaponType = weaponType;
            MutateMissileWeapon(item, profile, isMagical, null, roll);
        }
        else if (CasterWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.Weapon;
            roll.WeaponType = TreasureWeaponType.Caster;
            MutateCaster(item, profile, isMagical, null, roll);
        }
        else if (ArmorWcids.TryGetValue(roll.Wcid, out var armorType))
        {
            roll.ItemType = TreasureItemType_Orig.Armor;
            roll.ArmorType = armorType;
            var legacyArmorType = roll.ArmorType.ToACE();
            MutateArmor(item, profile, isMagical, legacyArmorType, roll);
        }
        else if (SocietyArmorWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.SocietyArmor; // collapsed for mutation
            roll.ArmorType = TreasureArmorType.Society;
            var legacyArmorType = roll.ArmorType.ToACE();
            MutateArmor(item, profile, isMagical, legacyArmorType, roll);
        }
        else if (ClothingWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.Clothing;
            MutateArmor(item, profile, isMagical, LootTables.ArmorType.MiscClothing, roll);
        }
        // scrolls don't really get mutated, even though they are in the main mutation method still
        else if (CloakWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.Cloak;
            MutateCloak(item, profile, roll);
        }
        else if (PetDeviceWcids.Contains(roll.Wcid))
        {
            roll.ItemType = TreasureItemType_Orig.PetDevice;
            MutatePetDevice(item, profile.Tier);
        }
        else if (AetheriaWcids.Contains(roll.Wcid))
        {
            // mundane add-on
            MutateAetheria_New(item, profile);
        }
        else if (SigilTrinketWcids.Contains(roll.Wcid))
        {
            // mundane add-on
            MutateSigilTrinket(item, profile);
        }
        else if (item.TrophyQuality != null)
        {
            // mutate trophy quality
            MutateTrophy(item, profile.Tier);
        }
        // other mundane items (mana stones, food/drink, healing kits, lockpicks, and spell components/peas) don't get mutated
        // it should be safe to return false here, for the 1 caller that currently uses this method
        // since it's not this function's responsibility to determine if an item is a lootgen item,
        // and only returns true if the item has been mutated.
        else
        {
            return false;
        }

        return true;
    }

    private static readonly float WeaponModMaxBonus = 0.1f;
    private static readonly float MaxMiscBonus = 0.5f;

    public static void MutateQuestItem(WorldObject wo)
    {
        wo.SetProperty(PropertyBool.MutableQuestItem, false);
        //wo.SetProperty(PropertyBool.UpgradeableQuestItem, true);

        var tier = Math.Clamp((GetTierFromWieldDifficulty(wo.WieldDifficulty ?? 50) - 1), 0, 7);
        var lootQuality = (float)(wo.LootQualityMod ?? 0.0f);
        var armorSlots = wo.ArmorSlots ?? 1;

        // Weapon Stats
        if (wo.ItemType is ItemType.Weapon or ItemType.MissileWeapon or ItemType.MeleeWeapon or ItemType.Caster)
        {
            if (wo.WeaponSubtype == null)
            {
                _log.Error($"MutateQuestItem() - WeaponSubType is null for ({wo.Name})");
                return;
            }

            if (wo.Damage != null)
            {
                var baseStat = wo.Damage.Value;
                var maxBonus = LootTables.GetMeleeSubtypeDamageRange((LootTables.WeaponSubtype)wo.WeaponSubtype, tier);
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = maxBonus * roll;
                var final = (int)Math.Round(baseStat + bonus);

                wo.SetProperty(PropertyInt.Damage, final);
            }

            if (wo.DamageMod != null)
            {
                var baseStat = wo.DamageMod.Value;
                var bonusRange = LootTables.GetMissileCasterSubtypeDamageRange(
                    (LootTables.WeaponSubtype)wo.WeaponSubtype,
                    tier
                );
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.DamageMod, final);
            }

            if (wo is {ElementalDamageMod: not null, WeaponRestorationSpellsMod: not null})
            {
                var magicSkill = wo.WieldSkillType2 == 34 ? Skill.WarMagic : Skill.LifeMagic;

                var baseStat = magicSkill == Skill.WarMagic ? wo.ElementalDamageMod.Value : wo.WeaponRestorationSpellsMod.Value;
                var bonusRange = LootTables.GetMissileCasterSubtypeDamageRange((LootTables.WeaponSubtype)wo.WeaponSubtype, tier);
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                if (magicSkill == Skill.WarMagic)
                {
                    wo.SetProperty(PropertyFloat.ElementalDamageMod, final);
                    wo.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, 1 + (final - 1) / 2);
                }
                else
                {
                    wo.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, final);
                    wo.SetProperty(PropertyFloat.ElementalDamageMod, 1 + (final - 1) / 2);
                }
            }
            else if (wo is { WeaponRestorationSpellsMod: not null, ElementalDamageMod: null})
            {
                var baseStat = wo.WeaponRestorationSpellsMod.Value;
                var bonusRange = LootTables.GetMissileCasterSubtypeDamageRange((LootTables.WeaponSubtype)wo.WeaponSubtype, tier);
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, final);
            }
            else if (wo is { ElementalDamageMod: not null, WeaponRestorationSpellsMod: null})
            {
                var baseStat = wo.ElementalDamageMod.Value;
                var bonusRange = LootTables.GetMissileCasterSubtypeDamageRange((LootTables.WeaponSubtype)wo.WeaponSubtype, tier);
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ElementalDamageMod, final);
            }

            if (wo.WeaponOffense != null)
            {
                var baseStat = wo.WeaponOffense.Value;
                var bonusRange = WeaponModMaxBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.WeaponOffense, final);
            }

            if (wo.WeaponPhysicalDefense != null)
            {
                var baseStat = wo.WeaponPhysicalDefense.Value;
                var bonusRange = WeaponModMaxBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.WeaponPhysicalDefense, final);
            }

            if (wo.WeaponMagicalDefense != null)
            {
                var baseStat = wo.WeaponMagicalDefense.Value;
                var bonusRange = WeaponModMaxBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.WeaponMagicalDefense, final);
            }

            if (wo.WeaponLifeMagicMod != null)
            {
                var baseStat = wo.WeaponLifeMagicMod.Value;
                var bonusRange = WeaponModMaxBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.WeaponLifeMagicMod, final);
            }

            if (wo.WeaponWarMagicMod != null)
            {
                var baseStat = wo.WeaponWarMagicMod.Value;
                var bonusRange = WeaponModMaxBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.WeaponWarMagicMod, final);
            }

            //if (wo.WeaponRestorationSpellsMod != null)
            //{
            //    var baseStat = wo.WeaponRestorationSpellsMod.Value;
            //    var bonusRange = WeaponModMaxBonus;
            //    var roll =GetDiminishingRoll(null, lootQuality);
            //    var bonus = bonusRange * roll;
            //    var final = baseStat + bonus;

            //    wo.SetProperty(PropertyFloat.WeaponRestorationSpellsMod, final);
            //}

            if (wo.CriticalFrequency != null)
            {
                var baseStat = wo.CriticalFrequency.Value;
                var bonusRange = baseStat * MaxMiscBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.CriticalFrequency, final);
            }

            if (wo.GetProperty(PropertyFloat.CriticalMultiplier) != null)
            {
                var baseStat = wo.GetProperty(PropertyFloat.CriticalMultiplier) ?? 1.0f;
                var bonusRange = (baseStat - 1) * MaxMiscBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.CriticalMultiplier, final);
            }

            if (wo.IgnoreArmor != null)
            {
                var baseStat = wo.IgnoreArmor.Value;
                var bonusRange = baseStat * MaxMiscBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.IgnoreArmor, final);
            }

            if (wo.IgnoreWard != null)
            {
                var baseStat = wo.IgnoreWard.Value;
                var bonusRange = baseStat * MaxMiscBonus;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.IgnoreWard, final);
            }
        }

        // Armor Base Stats
        if (wo.ItemType is ItemType.Armor or ItemType.Clothing or ItemType.Jewelry)
        {
            if (wo.ArmorLevel != null)
            {
                var baseStat = wo.ArmorLevel.Value;
                var baseStatPerTier = baseStat;

                if (tier > 0)
                {
                    baseStatPerTier /= tier;
                }

                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = baseStatPerTier * roll;
                var final = (int)Math.Round(baseStat + bonus);

                wo.SetProperty(PropertyInt.ArmorLevel, final);
            }

            if (wo.WardLevel != null)
            {
                var baseStat = wo.WardLevel.Value;
                var baseStatPerTier = baseStat;

                if (tier > 0)
                {
                    baseStatPerTier /= tier;
                }

                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = baseStatPerTier * roll;
                var final = (int)Math.Round(baseStat + bonus);

                wo.SetProperty(PropertyInt.WardLevel, final);
            }

            if (wo.ArmorModVsAcid != null)
            {
                var baseStat = wo.ArmorModVsAcid.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsAcid, final);
            }

            if (wo.ArmorModVsBludgeon != null)
            {
                var baseStat = wo.ArmorModVsBludgeon.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsBludgeon, final);
            }

            if (wo.ArmorModVsCold != null)
            {
                var baseStat = wo.ArmorModVsCold.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsCold, final);
            }

            if (wo.ArmorModVsElectric != null)
            {
                var baseStat = wo.ArmorModVsElectric.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsElectric, final);
            }

            if (wo.ArmorModVsFire != null)
            {
                var baseStat = wo.ArmorModVsFire.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsFire, final);
            }

            if (wo.ArmorModVsPierce != null)
            {
                var baseStat = wo.ArmorModVsPierce.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsPierce, final);
            }

            if (wo.ArmorModVsSlash != null)
            {
                var baseStat = wo.ArmorModVsSlash.Value;
                var bonusRange = (baseStat * 1.1f) - baseStat;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorModVsSlash, final);
            }

            // Armor Mods
            if (wo.ArmorAttackMod != null)
            {
                var baseStat = wo.ArmorAttackMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorAttackMod, final);
            }

            if (wo.ArmorDeceptionMod != null)
            {
                var baseStat = wo.ArmorDeceptionMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorDeceptionMod, final);
            }

            if (wo.ArmorDualWieldMod != null)
            {
                var baseStat = wo.ArmorDualWieldMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorDualWieldMod, final);
            }

            if (wo.ArmorHealthMod != null)
            {
                var baseStat = wo.ArmorHealthMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorHealthMod, final);
            }

            if (wo.ArmorHealthRegenMod != null)
            {
                var baseStat = wo.ArmorHealthRegenMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorHealthRegenMod, final);
            }

            if (wo.ArmorLifeMagicMod != null)
            {
                var baseStat = wo.ArmorLifeMagicMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorLifeMagicMod, final);
            }

            if (wo.ArmorMagicDefMod != null)
            {
                var baseStat = wo.ArmorMagicDefMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorMagicDefMod, final);
            }

            if (wo.ArmorManaMod != null)
            {
                var baseStat = wo.ArmorManaMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorManaMod, final);
            }

            if (wo.ArmorManaRegenMod != null)
            {
                var baseStat = wo.ArmorManaRegenMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorManaRegenMod, final);
            }

            if (wo.ArmorPerceptionMod != null)
            {
                var baseStat = wo.ArmorPerceptionMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorPerceptionMod, final);
            }

            if (wo.ArmorPhysicalDefMod != null)
            {
                var baseStat = wo.ArmorPhysicalDefMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorPhysicalDefMod, final);
            }

            if (wo.ArmorRunMod != null)
            {
                var baseStat = wo.ArmorRunMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorRunMod, final);
            }

            if (wo.ArmorShieldMod != null)
            {
                var baseStat = wo.ArmorShieldMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorShieldMod, final);
            }

            if (wo.ArmorStaminaMod != null)
            {
                var baseStat = wo.ArmorStaminaMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorStaminaMod, final);
            }

            if (wo.ArmorStaminaRegenMod != null)
            {
                var baseStat = wo.ArmorStaminaRegenMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorStaminaRegenMod, final);
            }

            if (wo.ArmorThieveryMod != null)
            {
                var baseStat = wo.ArmorThieveryMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorThieveryMod, final);
            }

            if (wo.ArmorTwohandedCombatMod != null)
            {
                var baseStat = wo.ArmorTwohandedCombatMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorTwohandedCombatMod, final);
            }

            if (wo.ArmorWarMagicMod != null)
            {
                var baseStat = wo.ArmorWarMagicMod.Value;
                var bonusRange = 0.1f / armorSlots;
                var roll = GetDiminishingRoll(null, lootQuality);
                var bonus = bonusRange * roll;
                var final = baseStat + bonus;

                wo.SetProperty(PropertyFloat.ArmorWarMagicMod, final);
            }
        }
    }

    public static List<WorldObject> CreateRandomObjectsOfType(WeenieType type, int count)
    {
        var weenies = DatabaseManager.World.GetRandomWeeniesOfType((int)type, count);

        var worldObjects = new List<WorldObject>();

        foreach (var weenie in weenies)
        {
            var wo = WorldObjectFactory.CreateNewWorldObject(weenie.WeenieClassId);
            worldObjects.Add(wo);
        }

        return worldObjects;
    }

    /// <summary>
    /// Returns an appropriate material type for the World Object based on its loot tier.
    /// </summary>
    private static MaterialType GetMaterialType(WorldObject wo, int tier)
    {
        return GetDefaultMaterialType(wo);
    }

    /// <summary>
    /// Gets a randomized default material type for when a weenie does not have TsysMutationData
    /// </summary>
    private static MaterialType GetDefaultMaterialType(WorldObject wo)
    {
        if (wo == null)
        {
            return MaterialType.Unknown;
        }

        var material = MaterialType.Unknown;

        var materialWoods = LootTables.DefaultMaterial[0];
        var materialMetals = LootTables.DefaultMaterial[1];
        var materialGems = LootTables.DefaultMaterial[2];
        var materialStones = LootTables.DefaultMaterial[3];
        var materialCloth = LootTables.DefaultMaterial[4];
        var materialLeather = LootTables.DefaultMaterial[5];
        var materialImbueGems = LootTables.DefaultMaterial[6];

        var imbueGemMaterialChance = 0.25f;
        var weenieType = wo.WeenieType;

        switch (weenieType)
        {
            case WeenieType.Caster:

                if (ThreadSafeRandom.Next(0.0f, 1.0f) < imbueGemMaterialChance)
                {
                    var roll = ThreadSafeRandom.Next(0, materialImbueGems.Length - 1);
                    material = (MaterialType)materialImbueGems[roll];
                }
                else
                {
                    uint[] orbs = { 2366, 1050101, 1050102, 1050103, 1050104, 1050105, 1050106, 1050107 };
                    if (orbs.Contains(wo.WeenieClassId))
                    {
                        var roll = ThreadSafeRandom.Next(0, materialGems.Length - 1);
                        material = (MaterialType)materialGems[roll];
                    }
                    else
                    {
                        if (ThreadSafeRandom.Next(0, 1) == 0)
                        {
                            var roll = ThreadSafeRandom.Next(0, materialWoods.Length - 1);
                            material = (MaterialType)materialWoods[roll];
                        }
                        else
                        {
                            var roll = ThreadSafeRandom.Next(0, materialMetals.Length - 1);
                            material = (MaterialType)materialMetals[roll];
                        }
                    }
                }

                break;
            case WeenieType.Clothing:
                {
                    if (wo.ArmorWeightClass == (int)ArmorWeightClass.Cloth)
                    {
                        var roll = ThreadSafeRandom.Next(0, materialCloth.Length - 1);
                        material = (MaterialType)materialCloth[roll];
                    }
                    else if (wo.ArmorWeightClass == (int)ArmorWeightClass.Light)
                    {
                        var roll = ThreadSafeRandom.Next(0, materialLeather.Length - 1);
                        material = (MaterialType)materialLeather[roll];
                    }
                    else if (wo.ArmorWeightClass == (int)ArmorWeightClass.Heavy)
                    {
                        var roll = ThreadSafeRandom.Next(0, materialMetals.Length - 1);
                        material = (MaterialType)materialMetals[roll];
                    }
                    else if (wo.ItemType is ItemType.Clothing)
                    {
                        var roll = ThreadSafeRandom.Next(0, materialCloth.Length - 1);
                        material = (MaterialType)materialCloth[roll];
                    }
                }
                break;
            case WeenieType.MissileLauncher:
            case WeenieType.Missile:
                {
                    uint[] metalThrownWeapons = { };
                    if (metalThrownWeapons.Contains(wo.WeenieClassId))
                    {
                        var roll = ThreadSafeRandom.Next(0, materialMetals.Length - 1);
                        material = (MaterialType)materialMetals[roll];
                    }
                    else
                    {
                        var roll = ThreadSafeRandom.Next(0, materialWoods.Length - 1);
                        material = (MaterialType)materialWoods[roll];
                    }
                }
                break;
            case WeenieType.MeleeWeapon:
                {
                    uint[] woodMeleeWeapons = { 309, 3766, 3767, 3768, 3769, 7768, 7787, 7788, 7789, 7790 };
                    if (
                        wo.WeaponSkill == Skill.Staff
                        || wo.WeaponSkill == Skill.Spear
                        || woodMeleeWeapons.Contains(wo.WeenieClassId)
                    )
                    {
                        var roll = ThreadSafeRandom.Next(0, materialWoods.Length - 1);
                        material = (MaterialType)materialWoods[roll];
                    }
                    else
                    {
                        var roll = ThreadSafeRandom.Next(0, materialMetals.Length - 1);
                        material = (MaterialType)materialMetals[roll];
                    }
                }
                break;
            case WeenieType.Generic:
                if (wo.ItemType == ItemType.Jewelry)
                {
                    if (ThreadSafeRandom.Next(0.0f, 1.0f) < imbueGemMaterialChance)
                    {
                        var roll = ThreadSafeRandom.Next(0, materialImbueGems.Length - 1);
                        material = (MaterialType)materialImbueGems[roll];
                    }
                    else
                    {
                        var roll = ThreadSafeRandom.Next(0, materialMetals.Length - 1);
                        material = (MaterialType)materialMetals[roll];
                    }
                }
                else if (wo.ValidLocations is EquipMask.Shield)
                {
                    var roll = ThreadSafeRandom.Next(0, materialMetals.Length - 1);
                    material = (MaterialType)materialMetals[roll];
                }
                else
                {
                    material = MaterialType.Unknown;
                }

                break;
            default:
                material = MaterialType.Unknown;
                _log.Error("Material type is unknown for: {WorldObject}", wo.Name);
                break;
        }

        return material;
    }

    /// <summary>
    /// This will assign a completely random, valid color to the item in question. It will also randomize the shade and set the appropriate icon.
    ///
    /// This was a temporary function to give some color to loot until further work was put in for "proper" color handling. Leave it here as an option for future potential use (perhaps config option?)
    /// </summary>
    private static WorldObject RandomizeColorTotallyRandom(WorldObject wo)
    {
        // Make sure the item has a ClothingBase...otherwise we can't properly randomize the colors.
        if (wo.ClothingBase != null)
        {
            var item = DatLoader.DatManager.PortalDat.ReadFromDat<DatLoader.FileTypes.ClothingTable>(
                (uint)wo.ClothingBase
            );

            // Get a random PaletteTemplate index from the ClothingBase entry
            // But make sure there's some valid ClothingSubPalEffects (in a valid loot/clothingbase item, there always SHOULD be)
            if (item.ClothingSubPalEffects.Count > 0)
            {
                var randIndex = ThreadSafeRandom.Next(0, item.ClothingSubPalEffects.Count - 1);
                var cloSubPal = item.ClothingSubPalEffects.ElementAt(randIndex);

                // Make sure this entry has a valid icon, otherwise there's likely something wrong with the ClothingBase value for this WorldObject (e.g. not supposed to be a loot item)
                if (cloSubPal.Value.Icon > 0)
                {
                    // Assign the appropriate Icon and PaletteTemplate
                    wo.IconId = cloSubPal.Value.Icon;
                    wo.PaletteTemplate = (int)cloSubPal.Key;

                    // Throw some shade, at random
                    wo.Shade = ThreadSafeRandom.Next(0.0f, 1.0f);
                }
            }
        }
        return wo;
    }

    public static readonly List<TreasureMaterialColor> clothingColors = new List<TreasureMaterialColor>();

    public static void InitClothingColors()
    {
        for (uint i = 1; i < 19; i++)
        {
            var tmc = new TreasureMaterialColor { PaletteTemplate = i, Probability = 1 };
            clothingColors.Add(tmc);
        }
    }

    /// <summary>
    /// Assign a random color (Int.PaletteTemplate and Float.Shade) to a World Object based on the material assigned to it.
    /// </summary>
    /// <returns>WorldObject with a random applicable PaletteTemplate and Shade applied, if available</returns>
    private static void MutateColor(WorldObject wo)
    {
        if (wo.MaterialType > 0 && wo.TsysMutationData != null && wo.ClothingBase != null)
        {
            var colorCode = (byte)(wo.TsysMutationData.Value >> 16);

            // BYTE spellCode = (tsysMutationData >> 24) & 0xFF;
            // BYTE colorCode = (tsysMutationData >> 16) & 0xFF;
            // BYTE gemCode = (tsysMutationData >> 8) & 0xFF;
            // BYTE materialCode = (tsysMutationData >> 0) & 0xFF;

            var colors = DatabaseManager.World.GetCachedTreasureMaterialColors((int)wo.MaterialType, colorCode);

            if (colors == null)
            {
                // legacy support for hardcoded colorCode 0 table
                if (colorCode == 0 && (uint)wo.MaterialType > 0)
                {
                    // This is a unique situation that typically applies to Under Clothes.
                    // If the Color Code is 0, they can be PaletteTemplate 1-18, assuming there is a MaterialType
                    // (gems have ColorCode of 0, but also no MaterialCode as they are defined by the weenie)

                    // this can be removed after all servers have upgraded to latest db
                    colors = clothingColors;
                }
                else
                {
                    return;
                }
            }

            // Load the clothingBase associated with the WorldObject
            var clothingBase = DatLoader.DatManager.PortalDat.ReadFromDat<DatLoader.FileTypes.ClothingTable>(
                (uint)wo.ClothingBase
            );

            // TODO : Probably better to use an intersect() function here. I defer to someone who knows how these work better than I - Optim
            // Compare the colors list and the clothingBase PaletteTemplates and remove any invalid items
            var colorsValid = new List<TreasureMaterialColor>();
            foreach (var e in colors)
            {
                if (clothingBase.ClothingSubPalEffects.ContainsKey(e.PaletteTemplate))
                {
                    colorsValid.Add(e);
                }
            }

            colors = colorsValid;

            var totalProbability = GetTotalProbability(colors);
            // If there's zero chance to get a random color, no point in continuing.
            if (totalProbability == 0)
            {
                return;
            }

            var rng = ThreadSafeRandom.Next(0.0f, totalProbability);

            uint paletteTemplate = 0;
            var probability = 0.0f;
            // Loop through the colors until we've reach our target value
            foreach (var color in colors)
            {
                probability += color.Probability;
                if (rng < probability)
                {
                    paletteTemplate = color.PaletteTemplate;
                    break;
                }
            }

            if (paletteTemplate > 0)
            {
                var cloSubPal = clothingBase.ClothingSubPalEffects[paletteTemplate];
                // Make sure this entry has a valid icon, otherwise there's likely something wrong with the ClothingBase value for this WorldObject (e.g. not supposed to be a loot item)
                if (cloSubPal.Icon > 0)
                {
                    // Assign the appropriate Icon and PaletteTemplate
                    wo.IconId = cloSubPal.Icon;
                    wo.PaletteTemplate = (int)paletteTemplate;

                    // Throw some shade, at random
                    wo.Shade = ThreadSafeRandom.Next(0.0f, 1.0f);

                    // Some debug info...
                    // log.Info($"Color success for {wo.MaterialType}({(int)wo.MaterialType}) - {wo.WeenieClassId} - {wo.Name}. PaletteTemplate {paletteTemplate} applied.");
                }
            }
            else
            {
                _log.Warning(
                    $"[LOOT] Color looked failed for {wo.MaterialType} ({(int)wo.MaterialType}) - {wo.WeenieClassId} - {wo.Name}."
                );
            }
        }
    }

    /// <summary>
    /// Some helper functions to get Probablity from different list types
    /// </summary>
    private static float GetTotalProbability(List<TreasureMaterialColor> colors)
    {
        return colors != null ? colors.Sum(i => i.Probability) : 0.0f;
    }

    private static float GetTotalProbability(List<TreasureMaterialBase> list)
    {
        return list != null ? list.Sum(i => i.Probability) : 0.0f;
    }

    private static float GetTotalProbability(List<TreasureMaterialGroups> list)
    {
        return list != null ? list.Sum(i => i.Probability) : 0.0f;
    }

    public static MaterialType RollGemType(int tier)
    {
        // previous formula
        //return (MaterialType)ThreadSafeRandom.Next(10, 50);

        // the gem class value can be further utilized for determining the item's monetary value
        //var gemClass = GemClassChance.Roll(tier);

        var gemResult = GemMaterialChance.Roll(1);

        return gemResult.MaterialType;
    }

    public const float WeaponBulk = 0.50f;
    public const float ArmorBulk = 0.25f;

    private static bool MutateBurden(WorldObject wo, TreasureDeath treasureDeath, bool isWeapon)
    {
        // ensure item has burden
        if (wo.EncumbranceVal == null)
        {
            return false;
        }

        var qualityInterval = QualityChance.RollInterval(treasureDeath);

        // only continue if the initial roll to modify the quality succeeded
        if (qualityInterval == 0.0f)
        {
            return false;
        }

        // only continue if initial roll succeeded?
        var bulk = isWeapon ? WeaponBulk : ArmorBulk;
        bulk *= (float)(wo.BulkMod ?? 1.0f);

        var maxBurdenMod = 1.0f - bulk;

        var burdenMod = 1.0f - (qualityInterval * maxBurdenMod);

        // modify burden
        var prevBurden = wo.EncumbranceVal.Value;
        wo.EncumbranceVal = (int)Math.Round(prevBurden * burdenMod);

        if (wo.EncumbranceVal < 1)
        {
            wo.EncumbranceVal = 1;
        }

        //Console.WriteLine($"Modified burden from {prevBurden} to {wo.EncumbranceVal} for {wo.Name} ({wo.WeenieClassId})");

        return true;
    }

    private static List<(int min, int max)> itemValue_RandomRange = new List<(int min, int max)>()
    {
        (50, 1000), // T1
        (100, 1500), // T2
        (200, 2000), // T3
        (300, 2500), // T4
        (400, 3000), // T5
        (500, 3500), // T6
        (600, 4000), // T7
        (700, 4500), // T8
    };

    private static int Roll_ItemValue(WorldObject wo, int tier)
    {
        // This is just a placeholder. This doesnt return a final value used retail, just a quick value for now.
        // Will use, tier, material type, amount of gems set into item, type of gems, spells on item

        var materialMod = LootTables.getMaterialValueModifier(wo);
        var gemMod = LootTables.getGemMaterialValueModifier(wo);

        var rngRange = itemValue_RandomRange[tier - 1];

        var rng = ThreadSafeRandom.Next(rngRange.min, rngRange.max);

        return (int)(rng * gemMod * materialMod * Math.Ceiling(tier / 2.0f));
    }

    private static void MutateValue(WorldObject wo, int tier, TreasureRoll roll)
    {
        if (roll == null)
        {
            // use old method
            wo.Value = Roll_ItemValue(wo, tier);
            return;
        }

        if (wo.Value == null)
        {
            wo.Value = 0; // fixme: data
        }

        var baseValue = wo.Value;
        var itemWorkmanship = wo.ItemWorkmanship ?? 1;
        var workmanshipMod = Math.Pow(itemWorkmanship, 2.0);
        var rng = ThreadSafeRandom.Next(0.7f, 1.3f);

        wo.Value = (int)(baseValue * workmanshipMod * rng);
    }

    // increase for a wider variance in item value ranges
    private const float valueFactor = 1.0f / 3.0f;

    private const float valueNonFactor = 1.0f - valueFactor;

    private static void MutateValue_Generic(WorldObject wo, int tier)
    {
        // confirmed from retail magloot logs, matches up relatively closely

        var rng = (float)ThreadSafeRandom.Next(0.7f, 1.25f);

        var workmanshipMod = WorkmanshipChance.GetModifier(wo.ItemWorkmanship);

        var materialMod = MaterialTable.GetValueMod(wo.MaterialType);
        var gemValue = GemMaterialChance.GemValue(wo.GemType);

        var tierMod = ItemValue_TierMod[Math.Clamp(tier, 1, 8) - 1];

        var newValue = 0.0f;
        newValue = (int)wo.Value * materialMod * (int)wo.Tier + gemValue;
        newValue *= workmanshipMod * rng;

        var iValue = (int)Math.Ceiling(newValue);

        //Console.WriteLine($"\n\nMutateValue_Generic()\n" +
        //    $" -BaseValue: {wo.Value}, ValueFactor {valueFactor}\n" +
        //    $" -materialMod: {materialMod}, tierMod: {wo.Tier}\n" +
        //    $" -workmanshipMod: {workmanshipMod}, gemValue: {gemValue}\n" +
        //    $" -rng: {rng}\n" +
        //    $" --NewValue: {newValue} --iValue: {iValue}\n\n" +
        //    $" -Formula: (({wo.Value} * {materialMod} * {(wo.Tier)}) + {gemValue}) * ({workmanshipMod} * {rng})\n" +
        //    $" -Formula: (({wo.Value * materialMod * wo.Tier + gemValue}) * ({workmanshipMod * rng})");

        // only raise value?
        if (iValue > wo.Value)
        {
            wo.Value = iValue;
        }
    }

    private static void MutateValue_Spells(WorldObject wo)
    {
        if (wo.ItemMaxMana != null)
        {
            wo.Value += wo.ItemMaxMana * 2;
        }

        var spellLevelSum = 0;

        if (wo.SpellDID != null)
        {
            var spell = new Server.Entity.Spell(wo.SpellDID.Value);
            spellLevelSum += (int)spell.Level;
        }

        if (wo.Biota.PropertiesSpellBook != null)
        {
            foreach (var spellId in wo.Biota.PropertiesSpellBook.Keys)
            {
                var spell = new Server.Entity.Spell(spellId);
                spellLevelSum += (int)spell.Level;
            }
        }
        wo.Value += spellLevelSum * 10;
    }

    private static readonly List<int> ItemValue_TierMod = new List<int>()
    {
        25, // T1
        50, // T2
        100, // T3
        250, // T4
        500, // T5
        1000, // T6
        2000, // T7
        3000, // T8
    };

    /// <summary>
    /// Set the AppraisalLongDescDecoration of the item, which controls the full descriptive text shown in the client on appraisal
    /// </summary>
    private static WorldObject SetAppraisalLongDescDecoration(WorldObject wo)
    {
        var appraisalLongDescDecoration = AppraisalLongDescDecorations.None;

        if (wo.ItemWorkmanship > 0)
        {
            appraisalLongDescDecoration |= AppraisalLongDescDecorations.PrependWorkmanship;
        }

        if (wo.MaterialType > 0)
        {
            appraisalLongDescDecoration |= AppraisalLongDescDecorations.PrependMaterial;
        }

        if (wo.GemType > 0 && wo.GemCount > 0)
        {
            appraisalLongDescDecoration |= AppraisalLongDescDecorations.AppendGemInfo;
        }

        if (appraisalLongDescDecoration > 0)
        {
            wo.AppraisalLongDescDecoration = appraisalLongDescDecoration;
        }
        else
        {
            wo.AppraisalLongDescDecoration = null;
        }

        return wo;
    }

    // new methods

    public static TreasureRoll RollWcid(TreasureDeath treasureDeath, TreasureItemCategory category)
    {
        var treasureDeathExtended = treasureDeath as TreasureDeathExtended;

        var treasureItemType =
            treasureDeathExtended != null ? treasureDeathExtended.ForceTreasureItemType : TreasureItemType_Orig.Undef;

        if (treasureItemType == TreasureItemType_Orig.Undef)
        {
            treasureItemType = RollItemType(treasureDeath, category);
        }

        if (treasureItemType == TreasureItemType_Orig.Undef)
        {
            _log.Error(
                $"LootGenerationFactory.RollWcid({treasureDeath.TreasureType}, {category}): treasureItemType == Undef"
            );
            return null;
        }

        var treasureRoll = new TreasureRoll(treasureItemType);

        if (treasureDeathExtended != null)
        {
            treasureRoll.ArmorType = treasureDeathExtended.ForceArmorType;
            treasureRoll.WeaponType = treasureDeathExtended.ForceWeaponType;
            treasureRoll.Heritage = treasureDeathExtended.ForceHeritage;
        }

        WeenieClassName wcid;
        TreasureHeritageGroup heritage;
        TreasureWeaponType weaponType;
        int rng;

        switch (treasureItemType)
        {
            case TreasureItemType_Orig.Pyreal:

                treasureRoll.Wcid = WeenieClassName.coinstack;
                break;

            case TreasureItemType_Orig.Gem:

                //var gemClass = GemClassChance.Roll(treasureDeath.Tier);
                var gemResult = GemMaterialChance.Roll(1);

                treasureRoll.Wcid = gemResult.ClassName;
                break;

            case TreasureItemType_Orig.Jewelry:

                treasureRoll.Wcid = JewelryWcids.Roll(treasureDeath.Tier);
                break;

            case TreasureItemType_Orig.ArtObject:

                treasureRoll.Wcid = GenericWcids.Roll(treasureDeath.Tier);
                break;

            case TreasureItemType_Orig.Weapon:

                if (treasureRoll.WeaponType == TreasureWeaponType.Undef)
                {
                    treasureRoll.WeaponType = WeaponTypeChance.Roll(treasureDeath.Tier);
                }
                else if (
                    treasureRoll.WeaponType == TreasureWeaponType.MeleeWeapon
                    || treasureRoll.WeaponType == TreasureWeaponType.MissileWeapon
                )
                {
                    treasureRoll.WeaponType = WeaponTypeChance.Roll(treasureDeath.Tier, treasureRoll.WeaponType);
                }

                treasureRoll.Wcid = WeaponWcids.Roll(treasureDeath, treasureRoll);
                break;

            case TreasureItemType_Orig.Armor:

                if (treasureRoll.ArmorType == TreasureArmorType.Undef)
                {
                    treasureRoll.ArmorType = ArmorTypeChance.Roll(treasureDeath.Tier);
                }

                treasureRoll.Wcid = ArmorWcids.Roll(treasureDeath, treasureRoll);
                break;

            case TreasureItemType_Orig.Clothing:

                treasureRoll.Wcid = ClothingWcids.Roll(treasureDeath, treasureRoll);
                //Console.WriteLine($"Clothing WCID: {treasureRoll.Wcid}");
                break;

            case TreasureItemType_Orig.Scroll:

                treasureRoll.Wcid = ScrollWcids.Roll();
                break;

            case TreasureItemType_Orig.Caster:

                // only called if TreasureItemType.Caster was specified directly
                treasureRoll.WeaponType = TreasureWeaponType.Caster;
                treasureRoll.Wcid = CasterWcids.Roll(treasureDeath.Tier);
                break;

            case TreasureItemType_Orig.ManaStone:

                treasureRoll.Wcid = ManaStoneWcids.Roll(treasureDeath);
                break;

            case TreasureItemType_Orig.Consumable:

                treasureRoll.Wcid = ConsumeWcids.Roll(treasureDeath);
                break;

            case TreasureItemType_Orig.HealKit:

                treasureRoll.Wcid = HealKitWcids.Roll(treasureDeath);
                break;

            case TreasureItemType_Orig.Lockpick:

                treasureRoll.Wcid = LockpickWcids.Roll(treasureDeath);
                break;

            case TreasureItemType_Orig.SpellComponent:

                treasureRoll.Wcid = SpellComponentWcids.Roll(treasureDeath);
                break;

            case TreasureItemType_Orig.SocietyArmor:
            case TreasureItemType_Orig.SocietyBreastplate:
            case TreasureItemType_Orig.SocietyGauntlets:
            case TreasureItemType_Orig.SocietyGirth:
            case TreasureItemType_Orig.SocietyGreaves:
            case TreasureItemType_Orig.SocietyHelm:
            case TreasureItemType_Orig.SocietyPauldrons:
            case TreasureItemType_Orig.SocietyTassets:
            case TreasureItemType_Orig.SocietyVambraces:
            case TreasureItemType_Orig.SocietySollerets:

                treasureRoll.ItemType = TreasureItemType_Orig.SocietyArmor; // collapse for mutation
                treasureRoll.ArmorType = TreasureArmorType.Society;

                treasureRoll.Wcid = SocietyArmorWcids.Roll(treasureDeath, treasureItemType, treasureRoll);
                break;

            case TreasureItemType_Orig.Cloak:

                treasureRoll.Wcid = CloakWcids.Roll();
                break;

            case TreasureItemType_Orig.PetDevice:

                treasureRoll.Wcid = PetDeviceWcids.Roll(treasureDeath);
                break;

            case TreasureItemType_Orig.EncapsulatedSpirit:

                treasureRoll.Wcid = WeenieClassName.ace49485_encapsulatedspirit;
                break;

            case TreasureItemType_Orig.ArmorWarrior:

                if (treasureRoll.ArmorType == TreasureArmorType.Undef)
                {
                    treasureRoll.ArmorType = ArmorTypeChance.RollWarrior(treasureDeath.Tier);
                }

                treasureRoll.Wcid = ArmorWcids.Roll(treasureDeath, treasureRoll);
                break;

            case TreasureItemType_Orig.ArmorRogue:

                if (treasureRoll.ArmorType == TreasureArmorType.Undef)
                {
                    treasureRoll.ArmorType = ArmorTypeChance.RollRogue(treasureDeath.Tier);
                }

                treasureRoll.Wcid = ArmorWcids.Roll(treasureDeath, treasureRoll);
                break;

            case TreasureItemType_Orig.ArmorCaster:

                if (treasureRoll.ArmorType == TreasureArmorType.Undef)
                {
                    treasureRoll.ArmorType = ArmorTypeChance.RollCaster(treasureDeath.Tier);
                }

                treasureRoll.Wcid = ArmorWcids.Roll(treasureDeath, treasureRoll);
                break;

            case TreasureItemType_Orig.WeaponWarrior:

                heritage = (TreasureHeritageGroup)ThreadSafeRandom.Next(1, 3);
                rng = ThreadSafeRandom.Next(0, 7);

                if (treasureRoll.WeaponType == TreasureWeaponType.Undef)
                {
                    treasureRoll.WeaponType = WeaponTypeChance.Roll(TreasureItemType_Orig.WeaponWarrior);
                }

                switch (treasureRoll.WeaponType)
                {
                    default:
                    case TreasureWeaponType.Axe:
                        wcid = AxeWcids.Roll(heritage, treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Mace:
                        wcid = MaceWcids.Roll(heritage, treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Spear:
                        wcid = SpearWcids.Roll(heritage, treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Sword:
                        wcid = SwordWcids.Roll(heritage, treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Thrown:
                        wcid = ThrownWcids.Roll(treasureDeath.Tier, out weaponType);
                        break;
                }

                treasureRoll.Wcid = wcid;
                break;

            case TreasureItemType_Orig.WeaponRogue:

                heritage = (TreasureHeritageGroup)ThreadSafeRandom.Next(1, 3);

                if (treasureRoll.WeaponType == TreasureWeaponType.Undef)
                {
                    treasureRoll.WeaponType = WeaponTypeChance.Roll(TreasureItemType_Orig.WeaponRogue);
                }

                switch (treasureRoll.WeaponType)
                {
                    default:
                    case TreasureWeaponType.Unarmed:
                        wcid = UnarmedWcids.Roll(heritage, treasureDeath.Tier);
                        break;
                    case TreasureWeaponType.Dagger:
                        wcid = DaggerWcids.Roll(heritage, treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Staff:
                        wcid = StaffWcids.Roll(heritage, treasureDeath.Tier);
                        break;
                    case TreasureWeaponType.Atlatl:
                        wcid = AtlatlWcids.Roll(treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Crossbow:
                        wcid = CrossbowWcids.Roll(treasureDeath.Tier, out weaponType);
                        break;
                    case TreasureWeaponType.Bow:
                        wcid = BowWcids.Roll(heritage, treasureDeath.Tier, out weaponType);
                        break;
                }

                treasureRoll.Wcid = wcid;
                break;

            case TreasureItemType_Orig.WeaponCaster:

                treasureRoll.WeaponType = TreasureWeaponType.Caster;
                treasureRoll.Wcid = CasterWcids.Roll(treasureDeath.Tier);
                break;

            case TreasureItemType_Orig.AnimalParts:

                treasureRoll.Wcid = AnimalPartsWcids.Roll(treasureDeath.Tier);
                break;

            case TreasureItemType_Orig.SigilTrinketWarrior:
                SigilTrinketType[] warriorSigilTrinkets = [SigilTrinketType.Compass, SigilTrinketType.Top, SigilTrinketType.PocketWatch];
                var sigilRng = ThreadSafeRandom.Next(0, 2);

                treasureRoll.Wcid = SigilTrinketWcids.Roll(treasureDeath.Tier, warriorSigilTrinkets[sigilRng]);
                if (treasureRoll.Wcid == WeenieClassName.undef)
                {
                    treasureRoll = null;
                }

                break;

            case TreasureItemType_Orig.SigilTrinketRogue:
                SigilTrinketType[] rogueSigilTrinkets = [SigilTrinketType.Goggles, SigilTrinketType.PuzzleBox, SigilTrinketType.PocketWatch];
                sigilRng = ThreadSafeRandom.Next(0, 2);

                treasureRoll.Wcid = SigilTrinketWcids.Roll(treasureDeath.Tier, rogueSigilTrinkets[sigilRng]);
                if (treasureRoll.Wcid == WeenieClassName.undef)
                {
                    treasureRoll = null;
                }

                break;

            case TreasureItemType_Orig.SigilTrinketCaster:
                SigilTrinketType[] casterSigilTrinkets = [SigilTrinketType.Scarab, SigilTrinketType.PuzzleBox, SigilTrinketType.Top];
                sigilRng = ThreadSafeRandom.Next(0, 2);

                treasureRoll.Wcid = SigilTrinketWcids.Roll(treasureDeath.Tier, casterSigilTrinkets[sigilRng]);
                if (treasureRoll.Wcid == WeenieClassName.undef)
                {
                    treasureRoll = null;
                }

                break;
        }

        return treasureRoll;
    }

    /// <summary>
    /// Rolls for an overall item type, based on the *_Chances columns in the treasure_death profile
    /// </summary>
    public static TreasureItemType_Orig RollItemType(TreasureDeath treasureDeath, TreasureItemCategory category)
    {
        switch (category)
        {
            case TreasureItemCategory.Item:
                return TreasureProfile_Item.Roll(treasureDeath.ItemTreasureTypeSelectionChances);

            case TreasureItemCategory.MagicItem:
                return TreasureProfile_MagicItem.Roll(treasureDeath.MagicItemTreasureTypeSelectionChances);

            case TreasureItemCategory.MundaneItem:
                return TreasureProfile_Mundane.Roll(treasureDeath.MundaneItemTypeSelectionChances);
        }
        return TreasureItemType_Orig.Undef;
    }

    public static WorldObject CreateRandomLootObjects_New(
        int tier,
        TreasureItemCategory category,
        TreasureItemType_Orig treasureItemType = TreasureItemType_Orig.Undef,
        TreasureArmorType armorType = TreasureArmorType.Undef,
        TreasureWeaponType weaponType = TreasureWeaponType.Undef
    )
    {
        return CreateRandomLootObjects_New(tier, 0.0f, category, treasureItemType, armorType, weaponType);
    }

    public static WorldObject CreateRandomLootObjects_New(
        int tier,
        float lootQualityMod,
        TreasureItemCategory category,
        TreasureItemType_Orig treasureItemType = TreasureItemType_Orig.Undef,
        TreasureArmorType armorType = TreasureArmorType.Undef,
        TreasureWeaponType weaponType = TreasureWeaponType.Undef,
        TreasureHeritageGroup heritageGroup = TreasureHeritageGroup.Invalid
    )
    {
        var treasureDeath = new TreasureDeathExtended()
        {
            Tier = tier,
            LootQualityMod = lootQualityMod,
            ForceTreasureItemType = treasureItemType,
            ForceArmorType = armorType,
            ForceWeaponType = weaponType,
            ForceHeritage = heritageGroup,

            ItemChance = 100,
            ItemMinAmount = 1,
            ItemMaxAmount = 1,
            ItemTreasureTypeSelectionChances = 8,

            MagicItemChance = 100,
            MagicItemMinAmount = 1,
            MagicItemMaxAmount = 1,
            MagicItemTreasureTypeSelectionChances = 8,

            MundaneItemChance = 100,
            MundaneItemMinAmount = 1,
            MundaneItemMaxAmount = 1,
            MundaneItemTypeSelectionChances = 7,

            UnknownChances = 21
        };

        return CreateRandomLootObjects_New(treasureDeath, category);
    }

    public static WorldObject CreateRandomLootObjects_New(TreasureDeath treasureDeath, TreasureItemCategory category)
    {
        var treasureRoll = RollWcid(treasureDeath, category);

        if (treasureRoll == null)
        {
            return null;
        }

        var wo = CreateAndMutateWcid(treasureDeath, treasureRoll, category == TreasureItemCategory.MagicItem);

        return wo;
    }

    public static WorldObject CreateAndMutateWcid(
        TreasureDeath treasureDeath,
        TreasureRoll treasureRoll,
        bool isMagical
    )
    {
        WorldObject wo = null;

        if (treasureRoll.ItemType != TreasureItemType_Orig.Scroll)
        {
            wo = WorldObjectFactory.CreateNewWorldObject((uint)treasureRoll.Wcid);
            if (wo != null)
            {
                // ADD wo.Tier = Monster.Tier
                wo.Tier = treasureDeath.Tier;
            }

            if (wo == null)
            {
                _log.Error(
                    $"CreateAndMutateWcid({treasureDeath.TreasureType}, {(int)treasureRoll.Wcid} - {treasureRoll.Wcid}, {treasureRoll.GetItemType()}, {isMagical}) - failed to create item"
                );
                return null;
            }

            if (wo.MaxStackSize > 1)
            {
                if (treasureRoll.WeaponType == TreasureWeaponType.Thrown)
                {
                    wo.SetStackSize(Math.Min(30, (int)(wo.MaxStackSize ?? 1)));
                }
                else if (treasureRoll.ItemType == TreasureItemType_Orig.ArtObject)
                {
                    wo.SetStackSize(Math.Min(10, (int)(wo.MaxStackSize ?? 1)));
                }
                else if (treasureRoll.ItemType == TreasureItemType_Orig.Consumable)
                {
                    wo.SetStackSize(Math.Min(5, (int)(wo.MaxStackSize ?? 1)));
                }
                else if (wo.ItemType == ItemType.SpellComponents)
                {
                    var componentId = wo.GetProperty(PropertyDataId.SpellComponent) ?? 0;
                    if ((componentId > 6 && componentId < 49) || (componentId > 62 && componentId < 75)) // herbs, powders, potions and tapers
                    {
                        wo.SetStackSize(Math.Min(2 * treasureDeath.Tier, wo.MaxStackSize ?? 1));
                    }
                    else if ((wo.GetProperty(PropertyDataId.SpellComponent) ?? 0) < 63) // scarabs and talismans
                    {
                        wo.SetStackSize(Math.Min(treasureDeath.Tier, wo.MaxStackSize ?? 1));
                    }
                }
            }

            treasureRoll.BaseArmorLevel = wo.ArmorLevel ?? 0;
            treasureRoll.BaseWardLevel = wo.WardLevel ?? 0;
        }

        var armorType = treasureRoll.ArmorType.ToACE();

        switch (treasureRoll.ItemType)
        {
            case TreasureItemType_Orig.Pyreal:
                MutateCoins(wo, treasureDeath);
                break;
            case TreasureItemType_Orig.Gem:
                MutateGem(wo, treasureDeath, isMagical, treasureRoll);
                break;
            case TreasureItemType_Orig.Jewelry:

                if (!treasureRoll.HasArmorLevel(wo))
                {
                    MutateJewelry(wo, treasureDeath, isMagical, treasureRoll);
                }
                else
                {
                    // crowns, coronets, diadems, etc.
                    MutateArmor(wo, treasureDeath, isMagical, LootTables.ArmorType.MiscClothing, treasureRoll);
                }
                break;
            case TreasureItemType_Orig.ArtObject:
                if (wo is { WeenieType: WeenieType.Generic, ItemType: ItemType.MissileWeapon })
                {
                    MutateDinnerware(wo, treasureDeath, isMagical, treasureRoll);
                }

                break;

            case TreasureItemType_Orig.Weapon:

                switch (treasureRoll.WeaponType)
                {
                    case TreasureWeaponType.Axe:
                    case TreasureWeaponType.Dagger:
                    case TreasureWeaponType.DaggerMS:
                    case TreasureWeaponType.Mace:
                    case TreasureWeaponType.MaceJitte:
                    case TreasureWeaponType.Spear:
                    case TreasureWeaponType.Staff:
                    case TreasureWeaponType.Sword:
                    case TreasureWeaponType.SwordMS:
                    case TreasureWeaponType.Unarmed:
                    case TreasureWeaponType.Thrown:

                    case TreasureWeaponType.TwoHandedAxe:
                    case TreasureWeaponType.TwoHandedMace:
                    case TreasureWeaponType.TwoHandedSpear:
                    case TreasureWeaponType.TwoHandedSword:

                        MutateMeleeWeapon(wo, treasureDeath, isMagical, treasureRoll);
                        break;

                    case TreasureWeaponType.Caster:

                        MutateCaster(wo, treasureDeath, isMagical, null, treasureRoll);
                        break;

                    case TreasureWeaponType.Bow:
                    case TreasureWeaponType.BowShort:
                    case TreasureWeaponType.Crossbow:
                    case TreasureWeaponType.CrossbowLight:
                    case TreasureWeaponType.Atlatl:
                    case TreasureWeaponType.AtlatlRegular:

                        MutateMissileWeapon(wo, treasureDeath, isMagical, null, treasureRoll);
                        break;

                    default:
                        _log.Error(
                            $"CreateAndMutateWcid({treasureDeath.TreasureType}, {(int)treasureRoll.Wcid} - {treasureRoll.Wcid}, {treasureRoll.GetItemType()}, {isMagical}) - unknown weapon type"
                        );
                        break;
                }
                break;

            case TreasureItemType_Orig.Caster:

                // alternate path -- only called if TreasureItemType.Caster was specified directly
                MutateCaster(wo, treasureDeath, isMagical, null, treasureRoll);
                break;

            case TreasureItemType_Orig.Armor:
            case TreasureItemType_Orig.SocietyArmor: // collapsed, after rolling for initial wcid

                MutateArmor(wo, treasureDeath, isMagical, armorType, treasureRoll);
                break;

            case TreasureItemType_Orig.Clothing:
                MutateArmor(wo, treasureDeath, isMagical, LootTables.ArmorType.MiscClothing, treasureRoll);
                break;

            case TreasureItemType_Orig.Scroll:
                wo = CreateRandomScroll(treasureDeath, treasureRoll); // using original method
                break;

            case TreasureItemType_Orig.Cloak:
                MutateCloak(wo, treasureDeath, treasureRoll);
                break;

            case TreasureItemType_Orig.PetDevice:
                MutatePetDevice(wo, treasureDeath.Tier);
                break;

            case TreasureItemType_Orig.ArmorWarrior:
                MutateArmor(wo, treasureDeath, isMagical, armorType, treasureRoll);
                break;

            case TreasureItemType_Orig.ArmorRogue:
                MutateArmor(wo, treasureDeath, isMagical, armorType, treasureRoll);
                break;

            case TreasureItemType_Orig.ArmorCaster:
                MutateArmor(wo, treasureDeath, isMagical, armorType, treasureRoll);
                break;

            case TreasureItemType_Orig.WeaponWarrior:
                switch (treasureRoll.WeaponType)
                {
                    case TreasureWeaponType.Axe:
                    case TreasureWeaponType.Mace:
                    case TreasureWeaponType.Spear:
                    case TreasureWeaponType.Sword:
                    case TreasureWeaponType.Thrown:
                        MutateMeleeWeapon(wo, treasureDeath, isMagical, treasureRoll);
                        break;

                    case TreasureWeaponType.Atlatl:
                    case TreasureWeaponType.Bow:
                    case TreasureWeaponType.Crossbow:
                        MutateMissileWeapon(
                            wo,
                            treasureDeath,
                            isMagical,
                            GetWieldDifficultyPerTier(treasureDeath.Tier),
                            treasureRoll
                        );
                        break;
                }
                break;

            case TreasureItemType_Orig.WeaponRogue:
                switch (treasureRoll.WeaponType)
                {
                    case TreasureWeaponType.Dagger:
                    case TreasureWeaponType.Unarmed:
                    case TreasureWeaponType.Staff:
                        MutateMeleeWeapon(wo, treasureDeath, isMagical, treasureRoll);
                        break;

                    case TreasureWeaponType.Atlatl:
                    case TreasureWeaponType.Bow:
                    case TreasureWeaponType.Crossbow:
                        MutateMissileWeapon(
                            wo,
                            treasureDeath,
                            isMagical,
                            GetWieldDifficultyPerTier(treasureDeath.Tier),
                            treasureRoll
                        );
                        break;
                }
                break;

            case TreasureItemType_Orig.WeaponCaster:
                MutateCaster(wo, treasureDeath, isMagical, GetWieldDifficultyPerTier(treasureDeath.Tier), treasureRoll);
                break;

            case TreasureItemType_Orig.SigilTrinketWarrior:
                MutateSigilTrinket(wo, treasureDeath);
                break;

            case TreasureItemType_Orig.SigilTrinketRogue:
                MutateSigilTrinket(wo, treasureDeath);
                break;

            case TreasureItemType_Orig.SigilTrinketCaster:
                MutateSigilTrinket(wo, treasureDeath);
                break;

            // other mundane items (mana stones, food/drink, healing kits, lockpicks, and spell components/peas) don't get mutated
        }

        if (wo != null)
        {
            if (wo.WieldRequirements != WieldRequirement.RawAttrib)
            {
                if (wo.WieldSkillType.HasValue)
                {
                    wo.WieldSkillType = (int)wo.ConvertToMoASkill((Skill)wo.WieldSkillType);
                }

                if (wo.WieldSkillType2.HasValue)
                {
                    wo.WieldSkillType2 = (int)wo.ConvertToMoASkill((Skill)wo.WieldSkillType2);
                }

                if (wo.WieldSkillType3.HasValue)
                {
                    wo.WieldSkillType3 = (int)wo.ConvertToMoASkill((Skill)wo.WieldSkillType3);
                }

                if (wo.WieldSkillType4.HasValue)
                {
                    wo.WieldSkillType4 = (int)wo.ConvertToMoASkill((Skill)wo.WieldSkillType4);
                }
            }
        }

        return wo;
    }

    /// <summary>
    /// The min/max amount of pyreals that can be rolled per tier, from magloot corpse logs
    /// </summary>
    private static readonly List<(int min, int max)> coinRanges = new List<(int, int)>()
    {
        (5, 50), // T1
        (10, 200), // T2
        (10, 500), // T3
        (25, 1000), // T4
        (50, 5000), // T5
        (250, 5000), // T6
        (250, 5000), // T7
        (250, 5000), // T8
    };

    private static void MutateCoins(WorldObject wo, TreasureDeath profile)
    {
        var tierRange = coinRanges[profile.Tier - 1];

        // flat rng range, according to magloot corpse logs
        var rng = ThreadSafeRandom.Next(tierRange.min, tierRange.max);

        wo.SetStackSize(rng);
    }

    public static string GetLongDesc(WorldObject wo)
    {
        if (wo.SpellDID != null)
        {
            var longDesc = TryGetLongDesc(wo, (SpellId)wo.SpellDID);

            if (longDesc != null)
            {
                return longDesc;
            }
        }

        if (wo.Biota.PropertiesSpellBook != null)
        {
            foreach (var spellId in wo.Biota.PropertiesSpellBook.Keys)
            {
                var longDesc = TryGetLongDesc(wo, (SpellId)spellId);

                if (longDesc != null)
                {
                    return longDesc;
                }
            }
        }
        return wo.Name;
    }

    private static string TryGetLongDesc(WorldObject wo, SpellId spellId)
    {
        var spellLevels = SpellLevelProgression.GetSpellLevels(spellId);

        if (spellLevels != null && CasterSlotSpells.descriptors.TryGetValue(spellLevels[0], out var descriptor))
        {
            return $"{wo.Name} of {descriptor}";
        }
        else
        {
            return null;
        }
    }

    private static void RollWieldLevelReq_T7_T8(WorldObject wo, TreasureDeath profile)
    {
        if (profile.Tier < 7)
        {
            return;
        }

        var wieldLevelReq = 150;

        if (profile.Tier == 8)
        {
            // t8 had a 90% chance for 180
            // loot quality mod?
            var rng = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (rng < 0.9f)
            {
                wieldLevelReq = 180;
            }
        }

        wo.WieldRequirements = WieldRequirement.Level;
        wo.WieldDifficulty = wieldLevelReq;

        // as per retail pcaps, must be set to appear in client
        wo.WieldSkillType = 1;
    }

    private static int GetTierValue(TreasureDeath treasureDeath)
    {
        return treasureDeath.Tier;
    }

    private static int GetArmorLevelReq(int armorTier)
    {
        switch (armorTier)
        {
            case 2:
                return 10;
            case 3:
                return 20;
            case 4:
                return 30;
            case 5:
                return 40;
            case 6:
                return 50;
            case 7:
                return 75;
            case 8:
                return 100;
            default:
                return 1;
        }
    }

    public static float GetDiminishingRoll(TreasureDeath treasureDeath = null, float lootQualityMod = 0.0f)
    {
        if (treasureDeath != null)
        {
            lootQualityMod = treasureDeath.LootQualityMod > 0 ? treasureDeath.LootQualityMod : 0;
        }

        var minimumRoll = (float)(1 - Math.Exp(-1 * lootQualityMod));

        var roll = (float)ThreadSafeRandom.Next(minimumRoll, 1.0f);

        roll *= roll;

        //Console.WriteLine($"GetDiminishingRoll - LQ: {lootQualityMod}, minRoll: {minimumRoll}, roll: {roll}");

        return roll;
    }

    private static int GetMaxWardOfTier(int tier)
    {
        switch (tier)
        {
            case 1:
            case 2:
                return 7;
            case 3:
                return 14;
            case 4:
                return 21;
            case 5:
                return 28;
            case 6:
                return 35;
            case 7:
                return 50;
            case 8:
                return 75;
            default:
                return 7;
        }
    }

    public static int GetWieldDifficultyPerTier(int tier)
    {
        switch (tier)
        {
            case 1:
                return 50;
            case 2:
                return 125;
            case 3:
                return 175;
            case 4:
                return 200;
            case 5:
                return 215;
            case 6:
                return 230;
            case 7:
                return 250;
            case 8:
                return 270;
            default:
                return 50;
        }
    }

    public static int GetTierFromWieldDifficulty(int wieldDifficulty)
    {
        switch (wieldDifficulty)
        {
            case 50:
                return 1;
            case 125:
                return 2;
            case 175:
                return 3;
            case 200:
                return 4;
            case 215:
                return 5;
            case 230:
                return 6;
            case 250:
                return 7;
            case 270:
                return 8;
            default:
                return 1;
        }
    }

    public static int GetRequiredLevelPerTier(int tier)
    {
        switch (tier)
        {
            case 1:
                return 1;
            case 2:
                return 10;
            case 3:
                return 20;
            case 4:
                return 30;
            case 5:
                return 40;
            case 6:
                return 50;
            case 7:
                return 75;
            case 8:
                return 100;
            default:
                return 1;
        }
    }

    public static int GetTierFromRequiredLevel(int requiredLevel)
    {
        switch (requiredLevel)
        {
            case 10:
                return 2;
            case 20:
                return 3;
            case 30:
                return 4;
            case 40:
                return 5;
            case 50:
                return 6;
            case 75:
                return 7;
            case 100:
                return 8;
            default:
                return 1;
        }
    }

    /// <summary>
    /// Items have a chance to roll with sockets. Maximum number of sockets per item is equal to the number of
    /// equipment slots the item uses. 10% chance to roll a socket for each potential socket slot.
    /// </summary>
    private static void AssignJewelSlots(WorldObject wo)
    {
        if (wo.Tier < 2)
        {
            return;
        }
        const float socketChance = 0.1f;

        var maximumSockets = ItemSocketLimit(wo);
        wo.JewelSockets = 0;

        for (var i = 0; i < maximumSockets; i++)
        {
            if (ThreadSafeRandom.Next(0.0f, 1.0f) < socketChance)
            {
                wo.JewelSockets++;
            }
        }
    }

    public static int ItemSocketLimit(WorldObject wo)
    {
        var maximumSockets = wo.ArmorSlots ?? 1;

        // Two-handed weapons, missile launchers, and casters cannot be equipped with an off-hand, so can have 2 sockets.
        if (wo.IsTwoHanded
            || wo.WeenieType is WeenieType.MissileLauncher or WeenieType.Caster
            || wo.ItemType is ItemType.Jewelry)
        {
            maximumSockets = 2;
        }

        return maximumSockets;
    }

    public static string GetTrophyQualityName(int trophyQuality)
    {
        return trophyQuality switch
        {
            2 => "Inferior",
            3 => "Poor",
            4 => "Crude",
            5 => "Ordinary",
            6 => "Good",
            7 => "Great",
            8 => "Excellent",
            9 => "Superb",
            10 => "Peerless",
            _ => "Damaged"
        };
    }

    public static void MutateTrophy(WorldObject wo, int tier)
    {
        if (wo.TrophyQuality == null)
        {
            return;
        }

        var trophyQuality = WorkmanshipChance.Roll(tier);
        wo.SetProperty(PropertyInt.TrophyQuality, trophyQuality);

        var name = GetTrophyQualityName(trophyQuality);
        wo.SetProperty(PropertyString.Name, name + " " + wo.Name);

        var multiplier = trophyQuality * trophyQuality;
        var value = (wo.Value ?? 0) * multiplier;
        wo.SetProperty(PropertyInt.Value, value);
        wo.SetProperty(PropertyInt.StackUnitValue, value);
    }
}
