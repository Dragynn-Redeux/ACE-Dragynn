using ACE.Server.WorldObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class WithdrawReportTests
{
    [TestMethod]
    public void Add_JoinsAStackTakenInPartsIntoTheSamePack()
    {
        var report = new WithdrawReport();

        // topped up a stack in the quiver, then the rest went in as a new stack there
        report.Add("Quiver", "Deadly Arrow", 50);
        report.Add("Quiver", "Deadly Arrow", 150);

        Assert.AreEqual(1, report.Taken.Count);
        Assert.AreEqual(("Quiver", "Deadly Arrow", (int?)200), report.Taken[0]);
    }

    [TestMethod]
    public void Add_KeepsDifferentPacksAndItemsApart()
    {
        var report = new WithdrawReport();

        report.Add("Quiver", "Deadly Arrow", 50);
        report.Add("your main pack", "Deadly Arrow", 150);
        report.Add("your main pack", "Prismatic Taper", 5);

        Assert.AreEqual(3, report.Taken.Count);
        Assert.AreEqual(("your main pack", "Deadly Arrow", (int?)150), report.Taken[1]);
    }

    [TestMethod]
    public void Add_ListsEachItemThatDoesNotStack()
    {
        var report = new WithdrawReport();

        report.Add("Weapons Pack", "Iron Jitte", null);
        report.Add("Weapons Pack", "Iron Jitte", null);

        Assert.AreEqual(2, report.Taken.Count);
        Assert.IsNull(report.Taken[1].Amount);
    }
}
