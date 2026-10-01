using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Factories.Enum;
using WeenieClassName = ACE.Server.Factories.Enum.WeenieClassName;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class BankSearchTests
{
    [TestMethod]
    public void NameMatches_IgnoresSpacesPunctuationAndCase()
    {
        Assert.IsTrue(BankSearch.NameMatches("Acid Long Sword", "longsword"));
        Assert.IsTrue(BankSearch.NameMatches("Longsword", "long sword"));
        Assert.IsTrue(BankSearch.NameMatches("Warhammer", "war-hammer"));
        Assert.IsTrue(BankSearch.NameMatches("Iron Jitte", "iron jitte"));
        Assert.IsTrue(BankSearch.NameMatches("Khanda-handled Mace", "khanda handled"));
        Assert.IsTrue(BankSearch.NameMatches("Pyreal", "PYREAL"));

        Assert.IsFalse(BankSearch.NameMatches("Jitte", "mace"));
        Assert.IsFalse(BankSearch.NameMatches("Long Sword", "---"));
        Assert.IsFalse(BankSearch.NameMatches("Long Sword", " "));
        Assert.IsFalse(BankSearch.NameMatches(null, "sword"));
    }

    [TestMethod]
    public void TryParseWeaponClass_ReadsClassesHoweverTheyAreWritten()
    {
        Assert.IsTrue(BankSearch.TryParseWeaponClass("Maces", out var mace));
        Assert.AreEqual(WeaponClass.Mace, mace);

        Assert.IsTrue(BankSearch.TryParseWeaponClass("staves", out var staff));
        Assert.AreEqual(WeaponClass.Staff, staff);

        foreach (var twoHanded in new[] { "two-handed", "Two Handed", "twohanded", "2H" })
        {
            Assert.IsTrue(BankSearch.TryParseWeaponClass(twoHanded, out var parsed), twoHanded);
            Assert.AreEqual(WeaponClass.TwoHanded, parsed);
        }

        // A particular weapon is found by name, not as a class.
        Assert.IsFalse(BankSearch.TryParseWeaponClass("longsword", out _));
        Assert.IsFalse(BankSearch.TryParseWeaponClass("jitte", out _));
        Assert.IsFalse(BankSearch.TryParseWeaponClass("", out _));
    }

    [TestMethod]
    public void GetWeaponClass_UsesTheLootTableAWeaponIsRolledFrom()
    {
        // Maces that aren't called maces
        Assert.AreEqual(WeaponClass.Mace, BankSearch.GetWeaponClass((uint)WeenieClassName.jitte, WeaponType.Undef));
        Assert.AreEqual(WeaponClass.Mace, BankSearch.GetWeaponClass((uint)WeenieClassName.jittefire, WeaponType.Undef));
        Assert.AreEqual(WeaponClass.Mace, BankSearch.GetWeaponClass((uint)WeenieClassName.dabus, WeaponType.Undef));
        Assert.AreEqual(WeaponClass.Mace, BankSearch.GetWeaponClass((uint)WeenieClassName.club, WeaponType.Undef));
        Assert.AreEqual(WeaponClass.Mace, BankSearch.GetWeaponClass((uint)WeenieClassName.morningstar, WeaponType.Undef));

        Assert.AreEqual(WeaponClass.Sword, BankSearch.GetWeaponClass((uint)WeenieClassName.swordlong, WeaponType.Undef));

        // War hammers roll from the axe table, as they use axe skill.
        Assert.AreEqual(WeaponClass.Axe, BankSearch.GetWeaponClass((uint)WeenieClassName.warhammer, WeaponType.Undef));

        // A two-handed sword is both.
        Assert.AreEqual(
            WeaponClass.Sword | WeaponClass.TwoHanded,
            BankSearch.GetWeaponClass((uint)WeenieClassName.ace40760_nodachi, WeaponType.TwoHanded)
        );
    }

    [TestMethod]
    public void GetWeaponClass_FallsBackToWeaponTypeForWeaponsNotInTheLootTables()
    {
        const uint questWeapon = 999_999_000;

        Assert.AreEqual(WeaponClass.Spear, BankSearch.GetWeaponClass(questWeapon, WeaponType.Spear));
        Assert.AreEqual(WeaponClass.TwoHanded, BankSearch.GetWeaponClass(questWeapon, WeaponType.TwoHanded));
        Assert.AreEqual(WeaponClass.None, BankSearch.GetWeaponClass(questWeapon, WeaponType.Magic));
        Assert.AreEqual(WeaponClass.None, BankSearch.GetWeaponClass(questWeapon, WeaponType.Undef));
    }

    [TestMethod]
    public void ClassOf_CoversEveryTreasureWeaponTypeTheLootTablesUse()
    {
        foreach (var type in new[]
        {
            TreasureWeaponType.Axe, TreasureWeaponType.Dagger, TreasureWeaponType.DaggerMS, TreasureWeaponType.Mace,
            TreasureWeaponType.MaceJitte, TreasureWeaponType.Spear, TreasureWeaponType.Staff, TreasureWeaponType.Sword,
            TreasureWeaponType.SwordMS, TreasureWeaponType.Unarmed, TreasureWeaponType.Bow, TreasureWeaponType.BowShort,
            TreasureWeaponType.Crossbow, TreasureWeaponType.CrossbowLight, TreasureWeaponType.Atlatl,
            TreasureWeaponType.AtlatlRegular, TreasureWeaponType.Thrown, TreasureWeaponType.TwoHandedAxe,
            TreasureWeaponType.TwoHandedMace, TreasureWeaponType.TwoHandedSpear, TreasureWeaponType.TwoHandedSword,
        })
        {
            Assert.AreNotEqual(WeaponClass.None, BankSearch.ClassOf(type), type.ToString());
        }
    }
}
