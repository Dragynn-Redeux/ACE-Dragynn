using ACE.Entity.Enum;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class BankCategoryTests
{
    [TestMethod]
    public void Classify_PutsEachKindOfItemInItsCategory()
    {
        Assert.AreEqual(BankCategory.Salvage, BankCategories.Classify(WeenieType.Salvage, ItemType.TinkeringMaterial, EquipMask.None));

        Assert.AreEqual(BankCategory.Weapons, BankCategories.Classify(WeenieType.MeleeWeapon, ItemType.MeleeWeapon, EquipMask.MeleeWeapon));
        Assert.AreEqual(BankCategory.Weapons, BankCategories.Classify(WeenieType.MissileLauncher, ItemType.MissileWeapon, EquipMask.MissileWeapon));
        Assert.AreEqual(BankCategory.Weapons, BankCategories.Classify(WeenieType.Caster, ItemType.Caster, EquipMask.Held));

        Assert.AreEqual(BankCategory.Armor, BankCategories.Classify(WeenieType.Clothing, ItemType.Armor, EquipMask.ChestArmor));
        Assert.AreEqual(BankCategory.Armor, BankCategories.Classify(WeenieType.Clothing, ItemType.Armor, EquipMask.Shield));
        Assert.AreEqual(BankCategory.Armor, BankCategories.Classify(WeenieType.Clothing, ItemType.Clothing, EquipMask.ChestWear));
        Assert.AreEqual(BankCategory.Armor, BankCategories.Classify(WeenieType.Clothing, ItemType.Clothing, EquipMask.Cloak));

        Assert.AreEqual(BankCategory.Jewelry, BankCategories.Classify(WeenieType.Generic, ItemType.Jewelry, EquipMask.NeckWear));
    }

    [TestMethod]
    public void Classify_TrinketsAreNeverJewelryOrWeapons()
    {
        // However a trinket's weenie sets ItemType, it is a trinket.
        Assert.AreEqual(BankCategory.Trinkets, BankCategories.Classify(WeenieType.SigilTrinket, ItemType.Jewelry, EquipMask.TrinketOne));
        Assert.AreEqual(BankCategory.Trinkets, BankCategories.Classify(WeenieType.SigilTrinket, ItemType.Misc, EquipMask.None));
        Assert.AreEqual(BankCategory.Trinkets, BankCategories.Classify(WeenieType.Generic, ItemType.Jewelry, EquipMask.TrinketOne));
    }

    [TestMethod]
    public void Classify_PutsSuppliesInTheirOwnCategories()
    {
        // Arrows are ItemType.MissileWeapon but are ammo, not weapons.
        Assert.AreEqual(BankCategory.Ammo, BankCategories.Classify(WeenieType.Ammunition, ItemType.MissileWeapon, EquipMask.MissileAmmo));

        Assert.AreEqual(BankCategory.Components, BankCategories.Classify(WeenieType.SpellComponent, ItemType.SpellComponents, EquipMask.None));
        Assert.AreEqual(BankCategory.ManaStones, BankCategories.Classify(WeenieType.ManaStone, ItemType.ManaStone, EquipMask.None));

        Assert.AreEqual(BankCategory.Keys, BankCategories.Classify(WeenieType.Key, ItemType.Key, EquipMask.None));
        Assert.AreEqual(BankCategory.Keys, BankCategories.Classify(WeenieType.Lockpick, ItemType.Misc, EquipMask.None));

        Assert.AreEqual(BankCategory.Consumables, BankCategories.Classify(WeenieType.Food, ItemType.Food, EquipMask.None));
        Assert.AreEqual(BankCategory.Consumables, BankCategories.Classify(WeenieType.Food, ItemType.Misc, EquipMask.None));
        Assert.AreEqual(BankCategory.Consumables, BankCategories.Classify(WeenieType.Healer, ItemType.Misc, EquipMask.None));

        Assert.AreEqual(BankCategory.Gems, BankCategories.Classify(WeenieType.Gem, ItemType.Gem, EquipMask.None));
        Assert.AreEqual(BankCategory.Gems, BankCategories.Classify(WeenieType.Jewel, ItemType.Misc, EquipMask.None));
    }

    [TestMethod]
    public void Classify_TrophiesAreAlwaysTrophies()
    {
        Assert.AreEqual(BankCategory.Trophies, BankCategories.Classify(WeenieType.Generic, ItemType.Misc, EquipMask.None, isTrophy: true));
        Assert.AreEqual(BankCategory.Trophies, BankCategories.Classify(WeenieType.Gem, ItemType.Gem, EquipMask.None, isTrophy: true));

        // Salvage stays salvage whatever else is set on it.
        Assert.AreEqual(BankCategory.Salvage, BankCategories.Classify(WeenieType.Salvage, ItemType.TinkeringMaterial, EquipMask.None, isTrophy: true));
    }

    [TestMethod]
    public void Classify_PutsMoneyInCurrencyAndTheRestInMisc()
    {
        Assert.AreEqual(BankCategory.Currency, BankCategories.Classify(WeenieType.Coin, ItemType.Money, EquipMask.None));
        Assert.AreEqual(BankCategory.Currency, BankCategories.Classify(WeenieType.Stackable, ItemType.PromissoryNote, EquipMask.None));
        Assert.AreEqual(BankCategory.Misc, BankCategories.Classify(WeenieType.Scroll, ItemType.Writable, EquipMask.None));

        // An item that can go anywhere is not a trinket just because TrinketOne is one of its slots.
        Assert.AreEqual(BankCategory.Misc, BankCategories.Classify(WeenieType.Generic, ItemType.Misc, EquipMask.All));
    }

    [TestMethod]
    public void TryParse_AcceptsSingularsPluralsAndSpellings()
    {
        Assert.IsTrue(BankCategories.TryParse("Weapon", out var weapons));
        Assert.AreEqual(BankCategory.Weapons, weapons);

        Assert.IsTrue(BankCategories.TryParse("armour", out var armor));
        Assert.AreEqual(BankCategory.Armor, armor);

        Assert.IsTrue(BankCategories.TryParse(" jewellery ", out var jewelry));
        Assert.AreEqual(BankCategory.Jewelry, jewelry);

        Assert.IsTrue(BankCategories.TryParse("GEAR", out var gear));
        Assert.AreEqual(BankCategory.Gear, gear);

        // "jewel" is a socketable jewel, which goes with gems, not jewelry.
        Assert.IsTrue(BankCategories.TryParse("jewels", out var jewels));
        Assert.AreEqual(BankCategory.Gems, jewels);

        Assert.IsTrue(BankCategories.TryParse("comps", out var comps));
        Assert.AreEqual(BankCategory.Components, comps);

        Assert.IsTrue(BankCategories.TryParse("Trophies", out var trophies));
        Assert.AreEqual(BankCategory.Trophies, trophies);

        Assert.IsTrue(BankCategories.TryParse("arrows", out var ammo));
        Assert.AreEqual(BankCategory.Ammo, ammo);

        Assert.IsFalse(BankCategories.TryParse("all", out _));
        Assert.IsFalse(BankCategories.TryParse("stones", out _));
        Assert.IsFalse(BankCategories.TryParse("", out _));
        Assert.IsFalse(BankCategories.TryParse(null, out _));
    }

    [TestMethod]
    public void EveryCategoryHasAWordThatParsesBackToIt()
    {
        foreach (var category in BankCategories.Singles)
        {
            var word = BankCategories.Describe(category);

            Assert.IsTrue(BankCategories.TryParse(word, out var parsed), $"\"{word}\" does not parse");
            Assert.AreEqual(category, parsed);
        }
    }

    [TestMethod]
    public void TryParseItemType_ReadsTypeNamesButNotNumbers()
    {
        Assert.IsTrue(BankCategories.TryParseItemType("gem", out var gem));
        Assert.AreEqual(ItemType.Gem, gem);

        Assert.IsTrue(BankCategories.TryParseItemType("Keys", out var key));
        Assert.AreEqual(ItemType.Key, key);

        Assert.IsTrue(BankCategories.TryParseItemType("manastones", out var manaStone));
        Assert.AreEqual(ItemType.ManaStone, manaStone);

        Assert.IsTrue(BankCategories.TryParseItemType("clothing", out var clothing));
        Assert.AreEqual(ItemType.Clothing, clothing);

        Assert.IsFalse(BankCategories.TryParseItemType("64", out _));
        Assert.IsFalse(BankCategories.TryParseItemType("None", out _));
        Assert.IsFalse(BankCategories.TryParseItemType("pyreal", out _));
        Assert.IsFalse(BankCategories.TryParseItemType("mana stone", out _));
        Assert.IsFalse(BankCategories.TryParseItemType("", out _));
    }

    [TestMethod]
    public void ParseInscription_CollectsEveryCategoryWordAndIgnoresTheRest()
    {
        Assert.AreEqual(
            new BankPackTags(BankCategory.Weapons | BankCategory.Armor, false),
            BankCategories.ParseInscription("Weapons + Armor"));

        Assert.AreEqual(
            new BankPackTags(BankCategory.Weapons | BankCategory.Armor, false),
            BankCategories.ParseInscription("my armor & weapons"));

        Assert.AreEqual(
            new BankPackTags(BankCategory.Salvage, false),
            BankCategories.ParseInscription("salvage,salvage;SALVAGE"));

        Assert.AreEqual(
            new BankPackTags(BankCategory.Gear, false),
            BankCategories.ParseInscription("gear"));

        // "Mana stones" is two words; "mana" carries it and "stones" is ignored.
        Assert.AreEqual(
            new BankPackTags(BankCategory.ManaStones | BankCategory.Consumables, false),
            BankCategories.ParseInscription("Mana stones & potions"));

        Assert.IsTrue(BankCategories.ParseInscription("Bob's pack").IsEmpty);
        Assert.IsTrue(BankCategories.ParseInscription("").IsEmpty);
        Assert.IsTrue(BankCategories.ParseInscription(null).IsEmpty);
    }

    [TestMethod]
    public void ParseInscription_ReadsKeepOnItsOwnAndWithCategories()
    {
        Assert.AreEqual(new BankPackTags(BankCategory.None, true), BankCategories.ParseInscription("Keep"));
        Assert.AreEqual(new BankPackTags(BankCategory.Trinkets, true), BankCategories.ParseInscription("keep, trinkets"));

        // Only the whole word counts.
        Assert.IsFalse(BankCategories.ParseInscription("keeper").Keep);
        Assert.AreEqual(BankCategory.None, BankCategories.ParseInscription("gearbox").Categories);
    }

    [TestMethod]
    public void SalvageKindOf_IsTheTinkeringSkillForTheMaterialOrImbue()
    {
        Assert.AreEqual(BankCategory.Blacksmithing, BankCategories.SalvageKindOf(MaterialType.Iron));
        Assert.AreEqual(BankCategory.Tailoring, BankCategories.SalvageKindOf(MaterialType.Linen));
        Assert.AreEqual(BankCategory.Woodworking, BankCategories.SalvageKindOf(MaterialType.Oak));
        Assert.AreEqual(BankCategory.Spellcrafting, BankCategories.SalvageKindOf(MaterialType.Amethyst));
        Assert.AreEqual(BankCategory.Jewelcrafting, BankCategories.SalvageKindOf(MaterialType.Zircon));

        // Imbue gems are spellcrafting materials too, but their skill depends on what is imbued, so they are imbue.
        Assert.AreEqual(BankCategory.Imbue, BankCategories.SalvageKindOf(MaterialType.RedGarnet));
        Assert.AreEqual(BankCategory.Imbue, BankCategories.SalvageKindOf(MaterialType.Sunstone));

        Assert.AreEqual(BankCategory.None, BankCategories.SalvageKindOf(MaterialType.Granite));
        Assert.AreEqual(BankCategory.None, BankCategories.SalvageKindOf(null));
    }

    [TestMethod]
    public void AnimalParts_AreTheHideBoneAndMeatLoot()
    {
        Assert.IsTrue(BankCategories.TryGetAnimalPart(1052500, out var kind, out var quality));
        Assert.AreEqual((0, 0), (kind, quality));

        Assert.IsTrue(BankCategories.TryGetAnimalPart(1052507, out kind, out quality));
        Assert.AreEqual((1, 3), (kind, quality));

        Assert.IsTrue(BankCategories.TryGetAnimalPart(1052511, out kind, out quality));
        Assert.AreEqual((2, 3), (kind, quality));

        Assert.IsFalse(BankCategories.IsAnimalPart(1052512));
        Assert.IsFalse(BankCategories.IsAnimalPart(273));

        // Meat is food, but an animal part is an animal part.
        Assert.AreEqual(BankCategory.Animal, BankCategories.Classify(WeenieType.Food, ItemType.Food, EquipMask.None, isAnimalPart: true));
        Assert.AreEqual(BankCategory.Consumables, BankCategories.Classify(WeenieType.Food, ItemType.Food, EquipMask.None));
    }

    [TestMethod]
    public void PackFit_PrefersASalvageKindTagOverSalvage()
    {
        var redGarnetBag = BankCategory.Salvage | BankCategory.Imbue;

        Assert.AreEqual(8, BankCategories.PackFit(BankCategory.Imbue, redGarnetBag));
        Assert.AreEqual(6, BankCategories.PackFit(BankCategory.Imbue | BankCategory.Keys, redGarnetBag));
        Assert.AreEqual(4, BankCategories.PackFit(BankCategory.Salvage, redGarnetBag));
        Assert.AreEqual(2, BankCategories.PackFit(BankCategory.Salvage | BankCategory.Keys, redGarnetBag));

        // a pack tagged for another kind of salvage doesn't take it
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Blacksmithing, redGarnetBag));

        // and a salvage kind tag doesn't take anything that isn't that salvage
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Imbue, BankCategory.Gems));
    }

    [TestMethod]
    public void ParseInscription_ReadsTheNewTags()
    {
        Assert.AreEqual(
            new BankPackTags(BankCategory.Blacksmithing | BankCategory.Tailoring, false),
            BankCategories.ParseInscription("blacksmith + tailor"));

        Assert.AreEqual(
            new BankPackTags(BankCategory.Imbue | BankCategory.Spellcrafting, false),
            BankCategories.ParseInscription("Imbue, spellcraft"));

        Assert.AreEqual(
            new BankPackTags(BankCategory.Animal | BankCategory.Consumables, false),
            BankCategories.ParseInscription("animal parts & consumables"));
    }

    [TestMethod]
    public void ParseInscription_ReadsDepositOnItsOwnAndWithOtherTags()
    {
        Assert.AreEqual(new BankPackTags(BankCategory.None, false, true), BankCategories.ParseInscription("Deposit"));
        Assert.AreEqual(new BankPackTags(BankCategory.Weapons, false, true), BankCategories.ParseInscription("weapons + deposit"));
        Assert.AreEqual(new BankPackTags(BankCategory.None, true, true), BankCategories.ParseInscription("keep, deposit"));

        Assert.IsFalse(BankCategories.ParseInscription("Deposit").IsEmpty);
        Assert.IsFalse(BankCategories.ParseInscription("deposits").Deposit);

        Assert.AreEqual("deposit", BankCategories.Describe(new BankPackTags(BankCategory.None, false, true)));
        Assert.AreEqual("weapons, deposit", BankCategories.Describe(new BankPackTags(BankCategory.Weapons, false, true)));
    }

    [TestMethod]
    public void PackFit_PrefersTheExactPackOverAWiderOne()
    {
        Assert.AreEqual(4, BankCategories.PackFit(BankCategory.Weapons, BankCategory.Weapons));
        Assert.AreEqual(2, BankCategories.PackFit(BankCategory.Gear, BankCategory.Weapons));
        Assert.AreEqual(2, BankCategories.PackFit(BankCategory.Weapons | BankCategory.Salvage, BankCategory.Salvage));
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Armor, BankCategory.Weapons));
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.None, BankCategory.Weapons));

        // Items with no category never claim a tagged pack.
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Gear, BankCategory.None));
    }

    [TestMethod]
    public void SortOrder_GroupsGearFirstAndUncategorizedLast()
    {
        Assert.IsTrue(BankCategories.SortOrder(BankCategory.Weapons) < BankCategories.SortOrder(BankCategory.Armor));
        Assert.IsTrue(BankCategories.SortOrder(BankCategory.Trinkets) < BankCategories.SortOrder(BankCategory.Salvage));
        Assert.IsTrue(BankCategories.SortOrder(BankCategory.Salvage) < BankCategories.SortOrder(BankCategory.Trophies));
        Assert.IsTrue(BankCategories.SortOrder(BankCategory.Trophies) < BankCategories.SortOrder(BankCategory.None));
    }

    [TestMethod]
    public void Describe_NamesCategoriesAndTags()
    {
        Assert.AreEqual("gear", BankCategories.Describe(BankCategory.Gear));
        Assert.AreEqual("weapons, salvage", BankCategories.Describe(BankCategory.Weapons | BankCategory.Salvage));
        Assert.AreEqual("untagged", BankCategories.Describe(default(BankPackTags)));
        Assert.AreEqual("keep", BankCategories.Describe(new BankPackTags(BankCategory.None, true)));
        Assert.AreEqual("armor, keep", BankCategories.Describe(new BankPackTags(BankCategory.Armor, true)));
    }

    [TestMethod]
    public void TryParse_ReadsWeaponArmorAndJewelryTypes()
    {
        (string Word, BankCategory Expected)[] cases =
        [
            ("sword", BankCategory.Swords),
            ("Maces", BankCategory.Maces),
            ("axe", BankCategory.Axes),
            ("spear", BankCategory.Spears),
            ("dagger", BankCategory.Daggers),
            ("staff", BankCategory.Staffs),
            ("ua", BankCategory.Unarmed),
            ("two-hand", BankCategory.TwoHanded),
            ("Two Handed", BankCategory.TwoHanded),
            ("2h", BankCategory.TwoHanded),
            ("bow", BankCategory.Bows),
            ("crossbow", BankCategory.Crossbows),
            ("atlatl", BankCategory.Atlatls),
            ("thrown", BankCategory.Thrown),
            ("caster", BankCategory.Casters),
            ("heavy", BankCategory.HeavyArmor),
            ("light", BankCategory.LightArmor),
            ("cloth", BankCategory.ClothArmor),
            ("clothing", BankCategory.Clothing),
            ("necklace", BankCategory.Necklaces),
            ("ring", BankCategory.Rings),
            ("bracelet", BankCategory.Bracelets),
        ];

        foreach (var (word, expected) in cases)
        {
            Assert.IsTrue(BankCategories.TryParse(word, out var parsed), $"\"{word}\" does not parse");
            Assert.AreEqual(expected, parsed, word);
        }
    }

    [TestMethod]
    public void ParseInscription_ReadsTypesAndTypePhrases()
    {
        // A type followed by its own category is just the type...
        Assert.AreEqual(BankCategory.HeavyArmor, BankCategories.ParseInscription("heavy armor").Categories);
        Assert.AreEqual(BankCategory.LightArmor, BankCategories.ParseInscription("Light Armour").Categories);
        Assert.AreEqual(BankCategory.TwoHanded, BankCategories.ParseInscription("two-handed weapons").Categories);

        // ...but a list with both keeps both.
        Assert.AreEqual(BankCategory.HeavyArmor | BankCategory.Armor, BankCategories.ParseInscription("heavy, armor").Categories);

        Assert.AreEqual(BankCategory.Swords | BankCategory.TwoHanded, BankCategories.ParseInscription("swords 2h").Categories);
        Assert.AreEqual(BankCategory.Rings | BankCategory.Necklaces, BankCategories.ParseInscription("rings & necklaces").Categories);
        Assert.AreEqual(BankCategory.Weapons | BankCategory.Armor, BankCategories.ParseInscription("weapons-armor").Categories);

        Assert.AreEqual(BankCategory.TwoHanded, BankCategories.ParseInscription("Two Handed").Categories);
        Assert.AreEqual(BankCategory.TwoHanded, BankCategories.ParseInscription("two handed weapons").Categories);

        // AC's weapon skill names: the weight class only describes the next word, so no armor is tagged.
        Assert.AreEqual(BankCategory.Weapons, BankCategories.ParseInscription("Heavy Weapons").Categories);
        Assert.AreEqual(BankCategory.Weapons, BankCategories.ParseInscription("light weapons").Categories);
        Assert.AreEqual(BankCategory.Crossbows, BankCategories.ParseInscription("light crossbows").Categories);
        Assert.AreEqual(BankCategory.HeavyArmor | BankCategory.Weapons, BankCategories.ParseInscription("heavy, weapons").Categories);

        var keepRings = BankCategories.ParseInscription("rings, keep");
        Assert.AreEqual(BankCategory.Rings, keepRings.Categories);
        Assert.IsTrue(keepRings.Keep);
    }

    [TestMethod]
    public void Kinds_ComeFromWeaponClassWeightClassAndSlot()
    {
        Assert.AreEqual(
            BankCategory.Swords | BankCategory.TwoHanded,
            BankCategories.WeaponKindsOf(WeaponClass.Sword | WeaponClass.TwoHanded, ItemType.MeleeWeapon));
        Assert.AreEqual(BankCategory.Bows, BankCategories.WeaponKindsOf(WeaponClass.Bow, ItemType.MissileWeapon));
        Assert.AreEqual(BankCategory.Casters, BankCategories.WeaponKindsOf(WeaponClass.None, ItemType.Caster));

        Assert.AreEqual(BankCategory.HeavyArmor, BankCategories.ArmorKindsOf(ArmorWeightClass.Heavy, ItemType.Armor));
        Assert.AreEqual(BankCategory.ClothArmor | BankCategory.Clothing, BankCategories.ArmorKindsOf(ArmorWeightClass.Cloth, ItemType.Clothing));
        Assert.AreEqual(BankCategory.Clothing, BankCategories.ArmorKindsOf(null, ItemType.Clothing));
        Assert.AreEqual(BankCategory.None, BankCategories.ArmorKindsOf(null, ItemType.Armor));

        Assert.AreEqual(BankCategory.Necklaces, BankCategories.JewelryKindsOf(EquipMask.NeckWear));
        Assert.AreEqual(BankCategory.Rings, BankCategories.JewelryKindsOf(EquipMask.FingerWear));
        Assert.AreEqual(BankCategory.Bracelets, BankCategories.JewelryKindsOf(EquipMask.WristWear));
    }

    [TestMethod]
    public void PackFit_PrefersATypeTagOverItsCategory()
    {
        var twoHandedSword = BankCategory.Weapons | BankCategory.Swords | BankCategory.TwoHanded;

        Assert.AreEqual(8, BankCategories.PackFit(BankCategory.Swords, twoHandedSword));
        Assert.AreEqual(8, BankCategories.PackFit(BankCategory.TwoHanded, twoHandedSword));
        Assert.AreEqual(6, BankCategories.PackFit(BankCategory.Swords | BankCategory.Maces, twoHandedSword));
        Assert.AreEqual(4, BankCategories.PackFit(BankCategory.Weapons, twoHandedSword));
        Assert.AreEqual(2, BankCategories.PackFit(BankCategory.Gear, twoHandedSword));
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Maces, twoHandedSword));

        var plate = BankCategory.Armor | BankCategory.HeavyArmor;

        Assert.AreEqual(8, BankCategories.PackFit(BankCategory.HeavyArmor, plate));
        Assert.AreEqual(4, BankCategories.PackFit(BankCategory.Armor, plate));
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.LightArmor, plate));
    }

    [TestMethod]
    public void Tiers_AreTheLootTierShownFromT0()
    {
        // the tier loot stores on the item: server tier 1 is t0, 8 is t7
        Assert.AreEqual(BankCategory.Tier0, BankCategories.TierOf(1, WieldRequirement.Invalid, null, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.Tier6, BankCategories.TierOf(7, WieldRequirement.Invalid, null, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.Tier7, BankCategories.TierOf(8, WieldRequirement.Level, 10, WieldRequirement.Invalid, null));

        // without it, the attribute or level wield requirement loot gives each tier, from either requirement
        Assert.AreEqual(BankCategory.Tier6, BankCategories.TierOf(null, WieldRequirement.RawAttrib, 250, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.Tier0, BankCategories.TierOf(null, WieldRequirement.RawAttrib, 50, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.Tier3, BankCategories.TierOf(null, WieldRequirement.Level, 30, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.Tier7, BankCategories.TierOf(null, WieldRequirement.RawSkill, 300, WieldRequirement.Level, 100));

        // anything else is no tier
        Assert.AreEqual(BankCategory.None, BankCategories.TierOf(null, WieldRequirement.RawAttrib, 260, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.None, BankCategories.TierOf(null, WieldRequirement.RawSkill, 250, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.None, BankCategories.TierOf(9, WieldRequirement.Invalid, null, WieldRequirement.Invalid, null));
        Assert.AreEqual(BankCategory.None, BankCategories.TierOf(null, WieldRequirement.Invalid, null, WieldRequirement.Invalid, null));
    }

    [TestMethod]
    public void ParseInscription_ReadsTiersAlongsideCategories()
    {
        Assert.AreEqual(BankCategory.Tier6, BankCategories.ParseInscription("t6").Categories);
        Assert.AreEqual(BankCategory.Swords | BankCategory.Tier6 | BankCategory.Tier7, BankCategories.ParseInscription("swords t6, t7").Categories);
        Assert.AreEqual(BankCategory.Tier5, BankCategories.ParseInscription("Tier 5").Categories);
        Assert.AreEqual(BankCategory.Tier3, BankCategories.ParseInscription("tier3").Categories);

        // a weight class before a tier is still armor
        Assert.AreEqual(BankCategory.HeavyArmor | BankCategory.Tier6, BankCategories.ParseInscription("heavy t6").Categories);

        // the old wield requirement numbers are no longer tags
        Assert.IsTrue(BankCategories.ParseInscription("250").IsEmpty);

        Assert.IsTrue(BankCategories.TryParse("t7", out var tier));
        Assert.AreEqual(BankCategory.Tier7, tier);
        Assert.IsTrue(BankCategories.TryParse("tier 0", out var tierZero));
        Assert.AreEqual(BankCategory.Tier0, tierZero);
        Assert.AreEqual("t7", BankCategories.Describe(BankCategory.Tier7));
    }

    [TestMethod]
    public void PackFit_TiersNarrowAPackAndBeatTheSamePackWithout()
    {
        var swordT6 = BankCategory.Weapons | BankCategory.Swords | BankCategory.Tier6;
        var swordT7 = BankCategory.Weapons | BankCategory.Swords | BankCategory.Tier7;

        Assert.AreEqual(9, BankCategories.PackFit(BankCategory.Swords | BankCategory.Tier6, swordT6));
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Swords | BankCategory.Tier6, swordT7));
        Assert.AreEqual(8, BankCategories.PackFit(BankCategory.Swords, swordT6));
        Assert.AreEqual(5, BankCategories.PackFit(BankCategory.Weapons | BankCategory.Tier6, swordT6));
        Assert.AreEqual(3, BankCategories.PackFit(BankCategory.Tier6, swordT6));
        Assert.AreEqual(0, BankCategories.PackFit(BankCategory.Tier6, BankCategory.Gems));
        Assert.AreEqual(BankCategories.MaxPackFit, BankCategories.PackFit(BankCategory.Swords | BankCategory.Tier6, swordT6));
    }

    [TestMethod]
    public void Matches_NeedsACategoryAndATierWhenTheFilterNamesBoth()
    {
        var swordT6 = BankCategory.Weapons | BankCategory.Swords | BankCategory.Tier6;

        Assert.IsTrue(BankCategories.Matches(swordT6, BankCategory.Swords | BankCategory.Tier6));
        Assert.IsTrue(BankCategories.Matches(swordT6, BankCategory.Tier6 | BankCategory.Tier7));
        Assert.IsTrue(BankCategories.Matches(swordT6, BankCategory.Swords | BankCategory.Maces));
        Assert.IsFalse(BankCategories.Matches(swordT6, BankCategory.Swords | BankCategory.Tier7));
        Assert.IsFalse(BankCategories.Matches(swordT6, BankCategory.Maces | BankCategory.Tier6));
        Assert.IsFalse(BankCategories.Matches(swordT6, BankCategory.None));
    }

    [TestMethod]
    public void CouldCollect_KnowsWhichPacksATieredSortTouches()
    {
        // a "weapons" pack takes weapons of any tier, a "250" pack gear of tier 250, a "gems" pack no tiered gear
        Assert.IsTrue(BankCategories.CouldCollect(BankCategory.Weapons, BankCategory.Tier6));
        Assert.IsTrue(BankCategories.CouldCollect(BankCategory.Tier6, BankCategory.Swords));
        Assert.IsTrue(BankCategories.CouldCollect(BankCategory.Swords | BankCategory.Tier6, BankCategory.Swords | BankCategory.Tier6));
        Assert.IsFalse(BankCategories.CouldCollect(BankCategory.Gems, BankCategory.Tier6));
        Assert.IsFalse(BankCategories.CouldCollect(BankCategory.Tier7, BankCategory.Tier6));
        Assert.IsFalse(BankCategories.CouldCollect(BankCategory.Maces, BankCategory.Swords));
    }

    [TestMethod]
    public void CurrencyAndMisc_AreWordsAndPackTags()
    {
        Assert.IsTrue(BankCategories.TryParse("money", out var currency));
        Assert.AreEqual(BankCategory.Currency, currency);
        Assert.AreEqual(BankCategory.Misc | BankCategory.Currency, BankCategories.ParseInscription("misc, currency").Categories);
        Assert.AreEqual(4, BankCategories.PackFit(BankCategory.Misc, BankCategory.Misc));
    }

    [TestMethod]
    public void ParentsAndKinds_LinkEachTypeToItsCategory()
    {
        Assert.AreEqual(BankCategory.Weapons | BankCategory.Jewelry, BankCategories.ParentsOf(BankCategory.Swords | BankCategory.Rings));
        Assert.AreEqual(BankCategory.Salvage, BankCategories.ParentsOf(BankCategory.Imbue));
        Assert.AreEqual(BankCategory.None, BankCategories.ParentsOf(BankCategory.Gems));

        Assert.AreEqual(BankCategory.WeaponKinds, BankCategories.KindsWithin(BankCategory.Weapons));
        Assert.AreEqual(BankCategory.ArmorKinds | BankCategory.JewelryKinds, BankCategories.KindsWithin(BankCategory.Armor | BankCategory.Jewelry));
    }

    [TestMethod]
    public void Describe_NamesTypesSoTheyReadAsItemsAndParseBack()
    {
        Assert.AreEqual("swords, two-handed weapons", BankCategories.Describe(BankCategory.Swords | BankCategory.TwoHanded));
        Assert.AreEqual("heavy armor", BankCategories.Describe(BankCategory.HeavyArmor));

        // the "Inscribe one with ..." hint in /bank sort uses these names, so they must tag a pack with just that type
        Assert.AreEqual(BankCategory.HeavyArmor, BankCategories.ParseInscription("heavy armor").Categories);
        Assert.AreEqual(BankCategory.Thrown, BankCategories.ParseInscription("thrown weapons").Categories);
        Assert.AreEqual(BankCategory.Unarmed, BankCategories.ParseInscription("unarmed weapons").Categories);
    }
}
