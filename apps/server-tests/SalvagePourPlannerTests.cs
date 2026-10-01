using System.Collections.Generic;
using System.Linq;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class SalvagePourPlannerTests
{
    private static int[] After(List<(int Units, int MaxUnits)> bags, List<SalvagePourPlanner.Pour> pours)
    {
        var units = bags.Select(b => b.Units).ToArray();

        foreach (var pour in pours)
        {
            units[pour.Source] -= pour.Amount;
            units[pour.Target] += pour.Amount;
        }

        return units;
    }

    [TestMethod]
    public void Plan_PoursSmallBagsIntoTheLargest()
    {
        var bags = new List<(int, int)> { (30, 100), (50, 100), (10, 100) };

        var pours = SalvagePourPlanner.Plan(bags);

        CollectionAssert.AreEqual(new[] { 0, 90, 0 }, After(bags, pours));
        Assert.AreEqual(2, SalvagePourPlanner.CountEmptied(bags, pours));
    }

    [TestMethod]
    public void Plan_FillsOneBagThenStartsTheNext()
    {
        var bags = new List<(int, int)> { (80, 100), (70, 100), (60, 100) };

        var pours = SalvagePourPlanner.Plan(bags);

        // 210 units need three bags of 100 at least partly: 100, 100, 10. Nothing is lost or made.
        var after = After(bags, pours);
        CollectionAssert.AreEquivalent(new[] { 100, 100, 10 }, after);
        Assert.AreEqual(210, after.Sum());
        Assert.AreEqual(0, SalvagePourPlanner.CountEmptied(bags, pours));
    }

    [TestMethod]
    public void Plan_NeverOverfillsABag()
    {
        var bags = new List<(int, int)> { (100, 100), (100, 100), (40, 100), (50, 100) };

        var pours = SalvagePourPlanner.Plan(bags);

        var after = After(bags, pours);
        Assert.IsTrue(after.All(u => u >= 0 && u <= 100));
        CollectionAssert.AreEqual(new[] { 100, 100, 0, 90 }, after);
        Assert.AreEqual(1, SalvagePourPlanner.CountEmptied(bags, pours));
        Assert.IsTrue(pours.All(p => p.Amount > 0 && p.Source != p.Target));
    }

    [TestMethod]
    public void Plan_DoesNothingWithOneBagOrFullBags()
    {
        Assert.AreEqual(0, SalvagePourPlanner.Plan(new List<(int, int)> { (40, 100) }).Count);
        Assert.AreEqual(0, SalvagePourPlanner.Plan(new List<(int, int)>()).Count);
        Assert.AreEqual(0, SalvagePourPlanner.Plan(new List<(int, int)> { (100, 100), (100, 100) }).Count);
    }

    [TestMethod]
    public void Plan_LeavesAnAlreadyEmptyBagAlone()
    {
        var bags = new List<(int, int)> { (50, 100), (0, 100), (20, 100) };

        var pours = SalvagePourPlanner.Plan(bags);

        Assert.IsTrue(pours.All(p => p.Source != 1 && p.Target != 1));
        CollectionAssert.AreEqual(new[] { 70, 0, 0 }, After(bags, pours));

        // Only bags that had something and were poured out count as emptied.
        Assert.AreEqual(1, SalvagePourPlanner.CountEmptied(bags, pours));
    }
}
