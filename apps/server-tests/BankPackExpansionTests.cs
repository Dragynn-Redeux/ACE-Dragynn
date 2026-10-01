using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class BankPackExpansionTests
{
    [TestMethod]
    public void IsEligible_PlainPacksAndTrophyPacksOnly()
    {
        Assert.IsTrue(BankPackExpansion.IsEligible(NamedPackKind.None, 0));
        Assert.IsTrue(BankPackExpansion.IsEligible(NamedPackKind.TrophyPack, 0));

        // A Trophy Pack grows even if it only takes some item types.
        Assert.IsTrue(BankPackExpansion.IsEligible(NamedPackKind.TrophyPack, 128));

        Assert.IsFalse(BankPackExpansion.IsEligible(NamedPackKind.SalvageCrate, 0));
        Assert.IsFalse(BankPackExpansion.IsEligible(NamedPackKind.Quiver, 0));
        Assert.IsFalse(BankPackExpansion.IsEligible(NamedPackKind.ComponentPouch, 0));

        // A pack that only takes some item types (a specialized pack) is not a plain pack.
        Assert.IsFalse(BankPackExpansion.IsEligible(NamedPackKind.None, 4096));
    }

    [TestMethod]
    public void CapacityInBank_IsTheConfiguredSizeWhenOn()
    {
        Assert.AreEqual(100, BankPackExpansion.CapacityInBank(true, 100, 24, 0));
        Assert.AreEqual(100, BankPackExpansion.CapacityInBank(true, 100, 24, 60));

        // never smaller than the pack itself
        Assert.AreEqual(24, BankPackExpansion.CapacityInBank(true, 10, 24, 5));

        // a pack's capacity is one byte
        Assert.AreEqual(255, BankPackExpansion.CapacityInBank(true, 1000, 24, 0));
        Assert.AreEqual(24, BankPackExpansion.CapacityInBank(true, -5, 24, 0));
    }

    [TestMethod]
    public void CapacityInBank_NeverCutsOffWhatAPackHolds()
    {
        // The feature turned off: back to the pack's own size, unless it holds more.
        Assert.AreEqual(24, BankPackExpansion.CapacityInBank(false, 100, 24, 10));
        Assert.AreEqual(60, BankPackExpansion.CapacityInBank(false, 100, 24, 60));

        // The size turned down below what a pack already holds.
        Assert.AreEqual(80, BankPackExpansion.CapacityInBank(true, 50, 24, 80));
    }

    [TestMethod]
    public void CapacityOutOfBank_IsThePacksOwnSizeUnlessItHoldsMore()
    {
        Assert.AreEqual(24, BankPackExpansion.CapacityOutOfBank(24, 0));
        Assert.AreEqual(24, BankPackExpansion.CapacityOutOfBank(24, 24));
        Assert.AreEqual(30, BankPackExpansion.CapacityOutOfBank(24, 30));
        Assert.AreEqual(255, BankPackExpansion.CapacityOutOfBank(24, 400));
    }
}
