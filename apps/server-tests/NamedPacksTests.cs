using ACE.Entity.Enum;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class NamedPacksTests
{
    [TestMethod]
    public void KindOf_GoesByTheNameAnywhereInItInAnyCase()
    {
        Assert.AreEqual(NamedPackKind.SalvageCrate, NamedPacks.KindOf("Salvage Crate"));
        Assert.AreEqual(NamedPackKind.SalvageCrate, NamedPacks.KindOf("Large salvage crate"));
        Assert.AreEqual(NamedPackKind.Quiver, NamedPacks.KindOf("Hunter's Quiver"));
        Assert.AreEqual(NamedPackKind.ComponentPouch, NamedPacks.KindOf("component pouch"));
        Assert.AreEqual(NamedPackKind.TrophyPack, NamedPacks.KindOf("Trophy Pack"));

        Assert.AreEqual(NamedPackKind.None, NamedPacks.KindOf("Backpack"));
        Assert.AreEqual(NamedPackKind.None, NamedPacks.KindOf("Trophy Bag"));
        Assert.AreEqual(NamedPackKind.None, NamedPacks.KindOf(""));
        Assert.AreEqual(NamedPackKind.None, NamedPacks.KindOf((string)null));
    }

    [TestMethod]
    public void Takes_MatchesWhatSortFilesIntoEachPack()
    {
        Assert.IsTrue(NamedPacks.Takes(NamedPackKind.SalvageCrate, WeenieType.Salvage, false));
        Assert.IsTrue(NamedPacks.Takes(NamedPackKind.Quiver, WeenieType.Ammunition, false));
        Assert.IsTrue(NamedPacks.Takes(NamedPackKind.ComponentPouch, WeenieType.SpellComponent, false));
        Assert.IsTrue(NamedPacks.Takes(NamedPackKind.TrophyPack, WeenieType.Generic, true));

        Assert.IsFalse(NamedPacks.Takes(NamedPackKind.Quiver, WeenieType.MissileLauncher, false));
        Assert.IsFalse(NamedPacks.Takes(NamedPackKind.TrophyPack, WeenieType.Generic, false));
        Assert.IsFalse(NamedPacks.Takes(NamedPackKind.SalvageCrate, WeenieType.SpellComponent, false));
        Assert.IsFalse(NamedPacks.Takes(NamedPackKind.None, WeenieType.Salvage, true));
    }

    [TestMethod]
    public void CategoryOf_NamesWhatEachPackCollects()
    {
        Assert.AreEqual(BankCategory.Salvage, NamedPacks.CategoryOf(NamedPackKind.SalvageCrate));
        Assert.AreEqual(BankCategory.Ammo, NamedPacks.CategoryOf(NamedPackKind.Quiver));
        Assert.AreEqual(BankCategory.Components, NamedPacks.CategoryOf(NamedPackKind.ComponentPouch));
        Assert.AreEqual(BankCategory.Trophies, NamedPacks.CategoryOf(NamedPackKind.TrophyPack));
        Assert.AreEqual(BankCategory.None, NamedPacks.CategoryOf(NamedPackKind.None));
    }
}
