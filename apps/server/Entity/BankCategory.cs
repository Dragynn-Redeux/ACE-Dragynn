using System;
using System.Collections.Generic;
using ACE.Entity.Enum;
using ACE.Server.Factories;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity;

/// <summary>
/// The groups of items the /bank command deposits, sorts and searches by.
/// A side pack whose inscription names one of these collects those items.
/// </summary>
[Flags]
public enum BankCategory : long
{
    None = 0,
    Salvage = 1L << 0,
    Weapons = 1L << 1,
    Armor = 1L << 2,
    Jewelry = 1L << 3,
    Trinkets = 1L << 4,
    Ammo = 1L << 5,
    Components = 1L << 6,
    Consumables = 1L << 7,
    Gems = 1L << 8,
    Keys = 1L << 9,
    ManaStones = 1L << 10,
    Trophies = 1L << 11,
    Animal = 1L << 12,

    // Pyreals and trade notes, and everything no other category takes (scrolls, quest items, ...). Numbered after the
    // tiers only because they came later; they are ordinary categories.
    Currency = 1L << 47,
    Misc = 1L << 48,

    // The kinds below are never an item's own category, only narrower tags inside one, and a pack tagged for a kind
    // beats a pack tagged for its whole category ("swords" before "weapons", "imbue" before "salvage").

    // Salvage by what uses it: the tinkering skill for its material (Salvage.TinkeringTarget), or imbuing.
    Blacksmithing = 1L << 13,
    Tailoring = 1L << 14,
    Spellcrafting = 1L << 15,
    Woodworking = 1L << 16,
    Jewelcrafting = 1L << 17,
    Imbue = 1L << 18,

    // Weapons by type (BankSearch.GetWeaponClass, so a two-handed sword is both Swords and TwoHanded), and casters.
    Swords = 1L << 19,
    Maces = 1L << 20,
    Axes = 1L << 21,
    Spears = 1L << 22,
    Daggers = 1L << 23,
    Staffs = 1L << 24,
    Unarmed = 1L << 25,
    TwoHanded = 1L << 26,
    Bows = 1L << 27,
    Crossbows = 1L << 28,
    Atlatls = 1L << 29,
    Thrown = 1L << 30,
    Casters = 1L << 31,

    // Armor by weight class, and clothing (ItemType.Clothing: shirts, pants, robes, ...).
    HeavyArmor = 1L << 32,
    LightArmor = 1L << 33,
    ClothArmor = 1L << 34,
    Clothing = 1L << 35,

    // Jewelry by slot.
    Necklaces = 1L << 36,
    Rings = 1L << 37,
    Bracelets = 1L << 38,

    // The loot tier a piece of gear dropped at, shown to players as t0 to t7 for the server's tiers 1 to 8 (see TierOf).
    // Unlike every other tag, a tier narrows a pack instead of widening it: a pack tagged "swords t6" takes swords of
    // tier t6 only, and one tagged "t6" any gear of that tier.
    Tier0 = 1L << 39,
    Tier1 = 1L << 40,
    Tier2 = 1L << 41,
    Tier3 = 1L << 42,
    Tier4 = 1L << 43,
    Tier5 = 1L << 44,
    Tier6 = 1L << 45,
    Tier7 = 1L << 46,

    Gear = Weapons | Armor | Jewelry | Trinkets,
    SalvageKinds = Blacksmithing | Tailoring | Spellcrafting | Woodworking | Jewelcrafting | Imbue,
    WeaponKinds = Swords | Maces | Axes | Spears | Daggers | Staffs | Unarmed | TwoHanded | Bows | Crossbows | Atlatls | Thrown | Casters,
    ArmorKinds = HeavyArmor | LightArmor | ClothArmor | Clothing,
    JewelryKinds = Necklaces | Rings | Bracelets,
    Kinds = SalvageKinds | WeaponKinds | ArmorKinds | JewelryKinds,
    Tiers = Tier0 | Tier1 | Tier2 | Tier3 | Tier4 | Tier5 | Tier6 | Tier7,

    // What a tier can narrow: gear, the only items given one.
    Tiered = Gear | WeaponKinds | ArmorKinds | JewelryKinds,
}

/// <summary>
/// What a side pack's inscription asks of the bank commands.
/// Categories: the pack collects those items. Keep: a pack you carry is left alone by /bank deposit and /sort.
/// Deposit: opening the bank offers to deposit everything in a pack you carry.
/// </summary>
public readonly record struct BankPackTags(BankCategory Categories, bool Keep, bool Deposit = false)
{
    public bool IsEmpty => Categories == BankCategory.None && !Keep && !Deposit;
}

public static class BankCategories
{
    /// <summary>
    /// The order the categories are listed in, and the order /bank sort groups items in.
    /// The kinds come last: they are never an item's own category, only narrower tags inside one.
    /// </summary>
    public static readonly BankCategory[] Singles =
    [
        BankCategory.Weapons,
        BankCategory.Armor,
        BankCategory.Jewelry,
        BankCategory.Trinkets,
        BankCategory.Ammo,
        BankCategory.Salvage,
        BankCategory.Animal,
        BankCategory.Components,
        BankCategory.ManaStones,
        BankCategory.Gems,
        BankCategory.Consumables,
        BankCategory.Keys,
        BankCategory.Trophies,
        BankCategory.Currency,
        BankCategory.Misc,
        BankCategory.Blacksmithing,
        BankCategory.Tailoring,
        BankCategory.Spellcrafting,
        BankCategory.Woodworking,
        BankCategory.Jewelcrafting,
        BankCategory.Imbue,
        BankCategory.Swords,
        BankCategory.Maces,
        BankCategory.Axes,
        BankCategory.Spears,
        BankCategory.Daggers,
        BankCategory.Staffs,
        BankCategory.Unarmed,
        BankCategory.TwoHanded,
        BankCategory.Bows,
        BankCategory.Crossbows,
        BankCategory.Atlatls,
        BankCategory.Thrown,
        BankCategory.Casters,
        BankCategory.HeavyArmor,
        BankCategory.LightArmor,
        BankCategory.ClothArmor,
        BankCategory.Clothing,
        BankCategory.Necklaces,
        BankCategory.Rings,
        BankCategory.Bracelets,
        BankCategory.Tier0,
        BankCategory.Tier1,
        BankCategory.Tier2,
        BankCategory.Tier3,
        BankCategory.Tier4,
        BankCategory.Tier5,
        BankCategory.Tier6,
        BankCategory.Tier7,
    ];

    public const string KeepTag = "keep";
    public const string DepositTag = "deposit";

    /// <summary>
    /// Tag and filter words, written as BankSearch.Normalize reads them: lowercase letters and digits only,
    /// so "two-handed", "Two Handed" and "twohanded" are all "twohanded".
    /// </summary>
    private static readonly Dictionary<string, BankCategory> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        { "salvage", BankCategory.Salvage },
        { "weapon", BankCategory.Weapons },
        { "weapons", BankCategory.Weapons },
        { "armor", BankCategory.Armor },
        { "armors", BankCategory.Armor },
        { "armour", BankCategory.Armor },
        { "armours", BankCategory.Armor },
        { "jewelry", BankCategory.Jewelry },
        { "jewellery", BankCategory.Jewelry },
        { "trinket", BankCategory.Trinkets },
        { "trinkets", BankCategory.Trinkets },
        { "gear", BankCategory.Gear },
        { "ammo", BankCategory.Ammo },
        { "ammunition", BankCategory.Ammo },
        { "arrow", BankCategory.Ammo },
        { "arrows", BankCategory.Ammo },
        { "bolt", BankCategory.Ammo },
        { "bolts", BankCategory.Ammo },
        { "component", BankCategory.Components },
        { "components", BankCategory.Components },
        { "comp", BankCategory.Components },
        { "comps", BankCategory.Components },
        { "consumable", BankCategory.Consumables },
        { "consumables", BankCategory.Consumables },
        { "food", BankCategory.Consumables },
        { "potion", BankCategory.Consumables },
        { "potions", BankCategory.Consumables },
        { "gem", BankCategory.Gems },
        { "gems", BankCategory.Gems },
        { "jewel", BankCategory.Gems },
        { "jewels", BankCategory.Gems },
        { "key", BankCategory.Keys },
        { "keys", BankCategory.Keys },
        { "mana", BankCategory.ManaStones },
        { "manastone", BankCategory.ManaStones },
        { "manastones", BankCategory.ManaStones },
        { "trophy", BankCategory.Trophies },
        { "trophies", BankCategory.Trophies },
        { "animal", BankCategory.Animal },
        { "currency", BankCategory.Currency },
        { "money", BankCategory.Currency },
        { "misc", BankCategory.Misc },
        { "miscellaneous", BankCategory.Misc },
        { "animals", BankCategory.Animal },
        { "blacksmithing", BankCategory.Blacksmithing },
        { "blacksmith", BankCategory.Blacksmithing },
        { "tailoring", BankCategory.Tailoring },
        { "tailor", BankCategory.Tailoring },
        { "spellcrafting", BankCategory.Spellcrafting },
        { "spellcraft", BankCategory.Spellcrafting },
        { "woodworking", BankCategory.Woodworking },
        { "woodwork", BankCategory.Woodworking },
        { "jewelcrafting", BankCategory.Jewelcrafting },
        { "jewelcraft", BankCategory.Jewelcrafting },
        { "imbue", BankCategory.Imbue },
        { "imbues", BankCategory.Imbue },
        { "imbuing", BankCategory.Imbue },
        { "sword", BankCategory.Swords },
        { "swords", BankCategory.Swords },
        { "mace", BankCategory.Maces },
        { "maces", BankCategory.Maces },
        { "axe", BankCategory.Axes },
        { "axes", BankCategory.Axes },
        { "spear", BankCategory.Spears },
        { "spears", BankCategory.Spears },
        { "dagger", BankCategory.Daggers },
        { "daggers", BankCategory.Daggers },
        { "staff", BankCategory.Staffs },
        { "staffs", BankCategory.Staffs },
        { "staves", BankCategory.Staffs },
        { "ua", BankCategory.Unarmed },
        { "unarmed", BankCategory.Unarmed },
        { "unarmedweapons", BankCategory.Unarmed },
        { "twohand", BankCategory.TwoHanded },
        { "twohanded", BankCategory.TwoHanded },
        { "2h", BankCategory.TwoHanded },
        { "2hand", BankCategory.TwoHanded },
        { "2handed", BankCategory.TwoHanded },
        { "twohandedweapons", BankCategory.TwoHanded },
        { "bow", BankCategory.Bows },
        { "bows", BankCategory.Bows },
        { "crossbow", BankCategory.Crossbows },
        { "crossbows", BankCategory.Crossbows },
        { "xbow", BankCategory.Crossbows },
        { "xbows", BankCategory.Crossbows },
        { "atlatl", BankCategory.Atlatls },
        { "atlatls", BankCategory.Atlatls },
        { "thrown", BankCategory.Thrown },
        { "throwing", BankCategory.Thrown },
        { "thrownweapons", BankCategory.Thrown },
        { "caster", BankCategory.Casters },
        { "casters", BankCategory.Casters },
        { "heavy", BankCategory.HeavyArmor },
        { "heavyarmor", BankCategory.HeavyArmor },
        { "light", BankCategory.LightArmor },
        { "lightarmor", BankCategory.LightArmor },
        { "cloth", BankCategory.ClothArmor },
        { "clotharmor", BankCategory.ClothArmor },
        { "clothing", BankCategory.Clothing },
        { "clothes", BankCategory.Clothing },
        { "necklace", BankCategory.Necklaces },
        { "necklaces", BankCategory.Necklaces },
        { "amulet", BankCategory.Necklaces },
        { "amulets", BankCategory.Necklaces },
        { "ring", BankCategory.Rings },
        { "rings", BankCategory.Rings },
        { "bracelet", BankCategory.Bracelets },
        { "bracelets", BankCategory.Bracelets },
        { "t0", BankCategory.Tier0 },
        { "tier0", BankCategory.Tier0 },
        { "t1", BankCategory.Tier1 },
        { "tier1", BankCategory.Tier1 },
        { "t2", BankCategory.Tier2 },
        { "tier2", BankCategory.Tier2 },
        { "t3", BankCategory.Tier3 },
        { "tier3", BankCategory.Tier3 },
        { "t4", BankCategory.Tier4 },
        { "tier4", BankCategory.Tier4 },
        { "t5", BankCategory.Tier5 },
        { "tier5", BankCategory.Tier5 },
        { "t6", BankCategory.Tier6 },
        { "tier6", BankCategory.Tier6 },
        { "t7", BankCategory.Tier7 },
        { "tier7", BankCategory.Tier7 },
    };

    /// <summary>
    /// The server's loot tier (1-8) for each attribute wield requirement loot gives weapons, casters and weight-class armor
    /// (LootGenerationFactory.GetWieldDifficultyPerTier), and for each level requirement it gives jewelry and clothing
    /// (GetArmorLevelReq), for gear that doesn't carry its Tier.
    /// </summary>
    private static readonly Dictionary<int, int> TierByAttributeRequirement = new()
    {
        { 50, 1 },
        { 125, 2 },
        { 175, 3 },
        { 200, 4 },
        { 215, 5 },
        { 230, 6 },
        { 250, 7 },
        { 270, 8 },
    };

    private static readonly Dictionary<int, int> TierByLevelRequirement = new()
    {
        { 1, 1 },
        { 10, 2 },
        { 20, 3 },
        { 30, 4 },
        { 40, 5 },
        { 50, 6 },
        { 75, 7 },
        { 100, 8 },
    };

    /// <summary>
    /// The one category an item belongs to: what /bank sort groups it under. See Tags for everything a pack tag can match.
    /// </summary>
    public static BankCategory Classify(WorldObject item)
    {
        return Classify(
            item.WeenieType,
            item.ItemType,
            item.ValidLocations ?? EquipMask.None,
            item.IsTrophy,
            IsAnimalPart(item.WeenieClassId)
        );
    }

    /// <summary>
    /// Every category an item answers to: its own (Classify), plus its kinds: what uses a salvage bag
    /// (blacksmithing, imbue, ...), a weapon's type (swords, two-handed, casters, ...), an armor piece's weight class
    /// and whether it is clothing, a piece of jewelry's slot. Plus, for gear, its tier.
    /// Deposit and sort filters, searches and pack tags all go by these.
    /// </summary>
    public static BankCategory Tags(WorldObject item)
    {
        var category = Classify(item);

        var tier = (category & BankCategory.Gear) != 0
            ? TierOf(item.Tier, item.WieldRequirements, item.WieldDifficulty, item.WieldRequirements2, item.WieldDifficulty2)
            : BankCategory.None;

        return tier | category switch
        {
            BankCategory.Salvage => category | SalvageKindOf(item.MaterialType),
            BankCategory.Weapons => category
                | WeaponKindsOf(BankSearch.GetWeaponClass(item.WeenieClassId, item.W_WeaponType), item.ItemType),
            BankCategory.Armor => category | ArmorKindsOf((ArmorWeightClass?)item.ArmorWeightClass, item.ItemType),
            BankCategory.Jewelry => category | JewelryKindsOf(item.ValidLocations ?? EquipMask.None),
            _ => category,
        };
    }

    /// <summary>
    /// A piece of gear's tier tag, t0 to t7 for the server's loot tiers 1 to 8. Loot stores the tier on the item (Tier);
    /// gear without it (from before it was stored) is placed by its wield requirement, attribute or level, from either
    /// of its two requirements. None if neither says.
    /// </summary>
    public static BankCategory TierOf(int? tier, WieldRequirement requirement, int? difficulty, WieldRequirement requirement2, int? difficulty2)
    {
        var serverTier = tier ?? TierFromRequirement(requirement, difficulty) ?? TierFromRequirement(requirement2, difficulty2);

        return serverTier is >= 1 and <= 8 ? (BankCategory)((long)BankCategory.Tier0 << (serverTier.Value - 1)) : BankCategory.None;
    }

    private static int? TierFromRequirement(WieldRequirement requirement, int? difficulty)
    {
        if (difficulty is not { } value)
        {
            return null;
        }

        return requirement switch
        {
            WieldRequirement.RawAttrib when TierByAttributeRequirement.TryGetValue(value, out var tier) => tier,
            WieldRequirement.Level when TierByLevelRequirement.TryGetValue(value, out var tier) => tier,
            _ => null,
        };
    }

    /// <summary>
    /// A weapon's kinds: its types (a two-handed axe is Axes and TwoHanded), or Casters for a wand, orb or staff caster.
    /// </summary>
    public static BankCategory WeaponKindsOf(WeaponClass weaponClass, ItemType itemType)
    {
        var kinds = (itemType & ItemType.Caster) != 0 ? BankCategory.Casters : BankCategory.None;

        foreach (var (weaponClassFlag, kind) in WeaponClassKinds)
        {
            if ((weaponClass & weaponClassFlag) != 0)
            {
                kinds |= kind;
            }
        }

        return kinds;
    }

    private static readonly (WeaponClass WeaponClass, BankCategory Kind)[] WeaponClassKinds =
    [
        (WeaponClass.Sword, BankCategory.Swords),
        (WeaponClass.Mace, BankCategory.Maces),
        (WeaponClass.Axe, BankCategory.Axes),
        (WeaponClass.Spear, BankCategory.Spears),
        (WeaponClass.Dagger, BankCategory.Daggers),
        (WeaponClass.Staff, BankCategory.Staffs),
        (WeaponClass.Unarmed, BankCategory.Unarmed),
        (WeaponClass.TwoHanded, BankCategory.TwoHanded),
        (WeaponClass.Bow, BankCategory.Bows),
        (WeaponClass.Crossbow, BankCategory.Crossbows),
        (WeaponClass.Atlatl, BankCategory.Atlatls),
        (WeaponClass.Thrown, BankCategory.Thrown),
    ];

    /// <summary>
    /// An armor piece's kinds: its weight class (heavy, light, cloth), and Clothing for shirts, pants, robes and the like.
    /// </summary>
    public static BankCategory ArmorKindsOf(ArmorWeightClass? weightClass, ItemType itemType)
    {
        var kinds = weightClass switch
        {
            ArmorWeightClass.Heavy => BankCategory.HeavyArmor,
            ArmorWeightClass.Light => BankCategory.LightArmor,
            ArmorWeightClass.Cloth => BankCategory.ClothArmor,
            _ => BankCategory.None,
        };

        return (itemType & ItemType.Clothing) != 0 ? kinds | BankCategory.Clothing : kinds;
    }

    /// <summary>
    /// A piece of jewelry's kinds, by the slots it is worn in.
    /// </summary>
    public static BankCategory JewelryKindsOf(EquipMask validLocations)
    {
        var kinds = BankCategory.None;

        if ((validLocations & EquipMask.NeckWear) != 0)
        {
            kinds |= BankCategory.Necklaces;
        }

        if ((validLocations & EquipMask.FingerWear) != 0)
        {
            kinds |= BankCategory.Rings;
        }

        if ((validLocations & EquipMask.WristWear) != 0)
        {
            kinds |= BankCategory.Bracelets;
        }

        return kinds;
    }

    /// <summary>
    /// The categories the kinds in tags belong to (Swords are Weapons, Rings are Jewelry, ...).
    /// </summary>
    public static BankCategory ParentsOf(BankCategory tags)
    {
        var parents = BankCategory.None;

        if ((tags & BankCategory.SalvageKinds) != 0)
        {
            parents |= BankCategory.Salvage;
        }

        if ((tags & BankCategory.WeaponKinds) != 0)
        {
            parents |= BankCategory.Weapons;
        }

        if ((tags & BankCategory.ArmorKinds) != 0)
        {
            parents |= BankCategory.Armor;
        }

        if ((tags & BankCategory.JewelryKinds) != 0)
        {
            parents |= BankCategory.Jewelry;
        }

        return parents;
    }

    /// <summary>
    /// Every kind inside the categories in tags (Weapons holds every weapon type, ...).
    /// </summary>
    public static BankCategory KindsWithin(BankCategory tags)
    {
        var kinds = BankCategory.None;

        if ((tags & BankCategory.Salvage) != 0)
        {
            kinds |= BankCategory.SalvageKinds;
        }

        if ((tags & BankCategory.Weapons) != 0)
        {
            kinds |= BankCategory.WeaponKinds;
        }

        if ((tags & BankCategory.Armor) != 0)
        {
            kinds |= BankCategory.ArmorKinds;
        }

        if ((tags & BankCategory.Jewelry) != 0)
        {
            kinds |= BankCategory.JewelryKinds;
        }

        return kinds;
    }

    /// <summary>
    /// True if the item is one /bank should act on. A null filter means "all".
    /// </summary>
    public static bool Matches(WorldObject item, BankCategory? filter)
    {
        return filter == null || Matches(Tags(item), filter.Value);
    }

    /// <summary>
    /// True if an item with these Tags answers to filter: one of the categories and types it names, if it names any,
    /// and one of the tiers it names, if it names any. So "swords 250" is swords of tier 250, and "250" any gear of tier 250.
    /// </summary>
    public static bool Matches(BankCategory itemTags, BankCategory filter)
    {
        var categories = filter & ~BankCategory.Tiers;
        var tiers = filter & BankCategory.Tiers;

        return filter != BankCategory.None
            && (categories == BankCategory.None || (itemTags & categories) != 0)
            && (tiers == BankCategory.None || (itemTags & tiers) != 0);
    }

    /// <summary>
    /// True if a pack that collects these (NamedPacks.Collects) can take any item filter names: for /bank sort,
    /// which packs to put in order, and whether to hint that no pack collects what was sorted.
    /// </summary>
    public static bool CouldCollect(BankCategory collects, BankCategory filter)
    {
        var packTiers = collects & BankCategory.Tiers;
        var filterTiers = filter & BankCategory.Tiers;

        if (packTiers != BankCategory.None && filterTiers != BankCategory.None && (packTiers & filterTiers) == 0)
        {
            return false;
        }

        var packCategories = collects & ~BankCategory.Tiers;
        var filterCategories = filter & ~BankCategory.Tiers;

        if (filterCategories != BankCategory.None)
        {
            // a pack for a tier alone takes gear of any kind
            return (packCategories & filterCategories) != 0
                || (packCategories == BankCategory.None && packTiers != BankCategory.None);
        }

        return packTiers != BankCategory.None
            ? (packTiers & filterTiers) != 0
            : (packCategories & BankCategory.Tiered) != 0;
    }

    /// <summary>
    /// What uses salvage of this material: imbuing for the imbue gems (whose skill depends on what is imbued), or
    /// the tinkering skill Salvage.TinkeringTarget gives the material. None if the material has neither.
    /// </summary>
    public static BankCategory SalvageKindOf(MaterialType? material)
    {
        if (material is not { } materialType)
        {
            return BankCategory.None;
        }

        if (Salvage.ImbueSalvage.Contains(materialType))
        {
            return BankCategory.Imbue;
        }

        if (!Salvage.TinkeringTarget.TryGetValue(materialType, out var skill))
        {
            return BankCategory.None;
        }

        return skill switch
        {
            Skill.Blacksmithing => BankCategory.Blacksmithing,
            Skill.Tailoring => BankCategory.Tailoring,
            Skill.Spellcrafting => BankCategory.Spellcrafting,
            Skill.Woodworking => BankCategory.Woodworking,
            Skill.Jewelcrafting => BankCategory.Jewelcrafting,
            _ => BankCategory.None,
        };
    }

    /// <summary>
    /// Animal parts are the hides, bones and meat in LootTables.AnimalPartsLootMatrix.
    /// kind: 0 hide, 1 bone, 2 meat. quality: 0 (tattered, cracked, gristly) up to 3 (rugged, pristine, choice).
    /// </summary>
    public static bool TryGetAnimalPart(uint weenieClassId, out int kind, out int quality)
    {
        var matrix = LootTables.AnimalPartsLootMatrix;

        for (kind = 0; kind < matrix.Length; kind++)
        {
            quality = Array.IndexOf(matrix[kind], (int)weenieClassId);
            if (quality >= 0)
            {
                return true;
            }
        }

        kind = -1;
        quality = -1;
        return false;
    }

    public static bool IsAnimalPart(uint weenieClassId)
    {
        return TryGetAnimalPart(weenieClassId, out _, out _);
    }

    /// <summary>
    /// The one category an item belongs to, and Misc for anything no other category takes (scrolls, quest items, ...).
    /// Earlier checks win: a trophy is only ever a trophy, animal parts are never food, a trinket is never jewelry,
    /// and arrows (ItemType.MissileWeapon) are ammo, not weapons.
    /// </summary>
    public static BankCategory Classify(
        WeenieType weenieType,
        ItemType itemType,
        EquipMask validLocations,
        bool isTrophy = false,
        bool isAnimalPart = false
    )
    {
        if (weenieType == WeenieType.Salvage)
        {
            return BankCategory.Salvage;
        }

        // pyreals and trade notes
        if (weenieType == WeenieType.Coin || (itemType & (ItemType.Money | ItemType.PromissoryNote)) != 0)
        {
            return BankCategory.Currency;
        }

        if (isTrophy)
        {
            return BankCategory.Trophies;
        }

        if (isAnimalPart)
        {
            return BankCategory.Animal;
        }

        if (weenieType == WeenieType.SigilTrinket || validLocations == EquipMask.TrinketOne)
        {
            return BankCategory.Trinkets;
        }

        if (weenieType == WeenieType.Ammunition)
        {
            return BankCategory.Ammo;
        }

        if ((itemType & ItemType.WeaponOrCaster) != 0)
        {
            return BankCategory.Weapons;
        }

        // Clothing and cloaks count as armor: they are worn and carry armor on this server.
        if ((itemType & ItemType.Vestements) != 0)
        {
            return BankCategory.Armor;
        }

        if ((itemType & ItemType.Jewelry) != 0)
        {
            return BankCategory.Jewelry;
        }

        if (weenieType == WeenieType.SpellComponent || (itemType & ItemType.SpellComponents) != 0)
        {
            return BankCategory.Components;
        }

        if (weenieType == WeenieType.ManaStone || (itemType & ItemType.ManaStone) != 0)
        {
            return BankCategory.ManaStones;
        }

        // Lockpicks open locks too, so they go with keys.
        if (weenieType == WeenieType.Key || weenieType == WeenieType.Lockpick || (itemType & ItemType.Key) != 0)
        {
            return BankCategory.Keys;
        }

        // Food covers potions and drinks as well; Healer is healing kits.
        if (weenieType == WeenieType.Food || weenieType == WeenieType.Healer)
        {
            return BankCategory.Consumables;
        }

        if (weenieType == WeenieType.Gem || weenieType == WeenieType.Jewel || (itemType & ItemType.Gem) != 0)
        {
            return BankCategory.Gems;
        }

        return BankCategory.Misc;
    }

    /// <summary>
    /// Reads a category word as typed in a command or an inscription: weapons, armor, gear, ...
    /// </summary>
    public static bool TryParse(string word, out BankCategory category)
    {
        category = BankCategory.None;

        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        return Words.TryGetValue(BankSearch.Normalize(word), out category);
    }

    /// <summary>
    /// Reads an item type name for /bank search: gem, key, food, manastone, ... A trailing "s" is allowed.
    /// Only letters count, so a number is never taken as a type.
    /// </summary>
    public static bool TryParseItemType(string word, out ItemType itemType)
    {
        itemType = ItemType.None;

        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        word = word.Trim();

        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                return false;
            }
        }

        if (Enum.TryParse(word, true, out itemType) && itemType != ItemType.None)
        {
            return true;
        }

        if (word.Length > 1 && word.EndsWith('s') && Enum.TryParse(word[..^1], true, out itemType) && itemType != ItemType.None)
        {
            return true;
        }

        itemType = ItemType.None;
        return false;
    }

    /// <summary>
    /// Reads a pack's inscription. Every tag word in it is collected; other words are ignored,
    /// so "Weapons + Armor", "weapons, armor" and "my armor & weapons" all mean the same thing.
    /// A word may hold hyphens and digits ("two-handed", "2h"), and "two handed" reads as one word. Two words with only
    /// spaces between read as a phrase: a kind followed by its own category ("heavy armor", "two-handed weapons") is
    /// just that kind, and a weight class in front of anything else ("Heavy Weapons", "light crossbows") is only an
    /// adjective, so it tags nothing itself.
    /// </summary>
    public static BankPackTags ParseInscription(string inscription)
    {
        if (string.IsNullOrWhiteSpace(inscription))
        {
            return default;
        }

        var categories = BankCategory.None;
        var keep = false;
        var deposit = false;

        var words = SplitWords(inscription);

        for (var i = 0; i < words.Count; i++)
        {
            var word = BankSearch.Normalize(words[i].Text);

            if (word == KeepTag)
            {
                keep = true;
                continue;
            }

            if (word == DepositTag)
            {
                deposit = true;
                continue;
            }

            var tag = ReadTag(words[i].Text);
            var nextTag = words[i].OnlySpaceAfter && i + 1 < words.Count ? ReadTag(words[i + 1].Text) : BankCategory.None;

            if (WeightClasses.Contains(tag) && (nextTag & ~BankCategory.Tiers) != BankCategory.None && nextTag != BankCategory.Armor)
            {
                continue;
            }

            categories |= tag;

            if ((tag & BankCategory.Kinds) != 0 && nextTag != BankCategory.None && nextTag == ParentsOf(tag))
            {
                i++;
            }
        }

        return new BankPackTags(categories, keep, deposit);
    }

    private static readonly HashSet<BankCategory> WeightClasses =
    [
        BankCategory.HeavyArmor,
        BankCategory.LightArmor,
        BankCategory.ClothArmor,
    ];

    /// <summary>
    /// The tag a word names: the whole word ("two-handed"), or else each of its hyphenated parts ("weapons-armor").
    /// </summary>
    private static BankCategory ReadTag(string word)
    {
        if (Words.TryGetValue(BankSearch.Normalize(word), out var tag))
        {
            return tag;
        }

        var parts = BankCategory.None;

        foreach (var part in word.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Words.TryGetValue(BankSearch.Normalize(part), out var partTag))
            {
                parts |= partTag;
            }
        }

        return parts;
    }

    /// <summary>
    /// The best PackFit there is: a pack tagged only for the item's types and for its tier.
    /// </summary>
    public const int MaxPackFit = 9;

    /// <summary>
    /// How well a pack tagged with packTags fits an item with these Tags. Higher is better, 0 is no fit.
    /// A tag for one of the item's kinds (swords, heavy armor, rings, imbue, ...) beats a tag for its whole category,
    /// and either way a pack tagged for just that beats a pack that also takes other things ("weapons" beats "gear").
    /// A pack with tiers only takes items of those tiers, and then beats the same pack without a tier.
    /// By category (doubled, plus 1 for a tier): 4, only kinds the item has (a two-handed sword fits "swords" and
    /// "two-handed" alike); 3, one of its kinds among other tags; 2, exactly its category; 1, its category among
    /// others, or no category at all on a pack for its tier ("250").
    /// </summary>
    public static int PackFit(BankCategory packTags, BankCategory itemTags)
    {
        var packTiers = packTags & BankCategory.Tiers;
        if (packTiers != BankCategory.None && (itemTags & packTiers) == 0)
        {
            return 0;
        }

        var packCategories = packTags & ~BankCategory.Tiers;
        var fit = packCategories == BankCategory.None
            ? packTiers != BankCategory.None ? 1 : 0
            : CategoryFit(packCategories, itemTags & ~BankCategory.Tiers);

        if (fit == 0)
        {
            return 0;
        }

        return fit * 2 + (packTiers != BankCategory.None ? 1 : 0);
    }

    private static int CategoryFit(BankCategory packCategories, BankCategory itemTags)
    {
        var kinds = itemTags & BankCategory.Kinds;
        if (kinds != BankCategory.None && (packCategories & kinds) != 0)
        {
            return (packCategories & ~kinds) == 0 ? 4 : 3;
        }

        var category = itemTags & ~BankCategory.Kinds;
        if (category != BankCategory.None && (packCategories & category) != 0)
        {
            return packCategories == category ? 2 : 1;
        }

        return 0;
    }

    /// <summary>
    /// Where a category falls in /bank sort order. Items with no category go last.
    /// </summary>
    public static int SortOrder(BankCategory category)
    {
        var index = Array.IndexOf(Singles, category);
        return index < 0 ? Singles.Length : index;
    }

    public static string Describe(BankCategory category)
    {
        if (category == BankCategory.None)
        {
            return "none";
        }

        if (category == BankCategory.Gear)
        {
            return "gear";
        }

        var names = new List<string>();
        foreach (var single in Singles)
        {
            if ((category & single) != 0)
            {
                names.Add(DisplayName(single));
            }
        }

        return string.Join(", ", names);
    }

    /// <summary>
    /// How one category reads in messages and pack tags. Each name also parses back to its category.
    /// </summary>
    private static string DisplayName(BankCategory single)
    {
        return single switch
        {
            BankCategory.TwoHanded => "two-handed weapons",
            BankCategory.Unarmed => "unarmed weapons",
            BankCategory.Thrown => "thrown weapons",
            BankCategory.HeavyArmor => "heavy armor",
            BankCategory.LightArmor => "light armor",
            BankCategory.ClothArmor => "cloth armor",
            _ when (single & BankCategory.Tiers) != 0 => $"t{single.ToString()[4..]}",
            _ => single.ToString().ToLowerInvariant(),
        };
    }

    public static string Describe(BankPackTags tags)
    {
        if (tags.IsEmpty)
        {
            return "untagged";
        }

        var text = tags.Categories == BankCategory.None ? "" : Describe(tags.Categories);

        if (tags.Keep)
        {
            text = text.Length == 0 ? KeepTag : $"{text}, {KeepTag}";
        }

        if (tags.Deposit)
        {
            text = text.Length == 0 ? DepositTag : $"{text}, {DepositTag}";
        }

        return text;
    }

    /// <summary>
    /// The words in an inscription: runs of letters, digits and hyphens. OnlySpaceAfter: the next word follows
    /// after nothing but spaces, so the two can be read as one phrase ("heavy armor", not "heavy, armor").
    /// </summary>
    private static List<(string Text, bool OnlySpaceAfter)> SplitWords(string text)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-';

        var words = new List<(string Text, bool OnlySpaceAfter)>();
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && IsWordChar(text[i]);

            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                var next = i;
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                {
                    next++;
                }

                words.Add((text[start..i], next < text.Length && IsWordChar(text[next])));
                start = -1;
            }
        }

        // "two handed" and "two hand" are one word, as "two-handed" is, and so is "tier 3" (t3)
        for (var i = words.Count - 2; i >= 0; i--)
        {
            var first = BankSearch.Normalize(words[i].Text);
            var second = BankSearch.Normalize(words[i + 1].Text);

            if (words[i].OnlySpaceAfter && ((first == "two" && second is "hand" or "handed") || (first == "tier" && second.Length == 1 && char.IsDigit(second[0]))))
            {
                words[i] = ($"{words[i].Text}-{words[i + 1].Text}", words[i + 1].OnlySpaceAfter);
                words.RemoveAt(i + 1);
            }
        }

        return words;
    }
}
