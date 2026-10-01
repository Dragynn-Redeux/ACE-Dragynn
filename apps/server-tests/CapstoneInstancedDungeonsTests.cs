using System.Linq;
using ACE.Server.Commands.AdminCommands;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class CapstoneInstancedDungeonsTests
{
    private const string Dungeon = "Glenden Wood Dungeon";

    // what the property held before it was *: every capstone dungeon by name
    private const string EveryName =
        "Glenden Wood Dungeon,Green Mire Grave,Sand Shallow,Manse of Panderlou,Smugglers Hideaway,Halls of the Helm,Colier Mine,Empyrean Garrison,Grievous Vault,Folthid Cellar,Mines of Despair,Beyond the Mines,Gredaline Consulate,Mage Academy,Lugian Mines,Lugian Mines2,Mountain Fortress,Olthoi Queen's Lair";

    [TestMethod]
    public void CapstoneInstancing_NothingIsInstancedWhenTheValueIsEmpty()
    {
        foreach (var empty in new[] { null, "", "   ", ",", " , " })
        {
            Assert.IsFalse(Landblock.IsListedAsCapstoneInstanced(empty, Dungeon), $"'{empty}'");
        }
    }

    [TestMethod]
    public void CapstoneInstancing_AStarMeansEveryDungeon()
    {
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced("*", Dungeon));
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced("*", "Olthoi Queen's Lair"));
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced("*", "A Dungeon Added Later"));
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced("  *  ", Dungeon));
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced("Sand Shallow,*", Dungeon));
    }

    [TestMethod]
    public void CapstoneInstancing_ANameIsMatchedWholeWhateverItsCaseAndSpacing()
    {
        var names = "Glenden Wood Dungeon, Green Mire Grave";

        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced(names, Dungeon));
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced(names, "Green Mire Grave"));
        Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced(names, "glenden wood dungeon"));
        Assert.IsFalse(Landblock.IsListedAsCapstoneInstanced(names, "Sand Shallow"));

        // the first word is all that a value with spaces that was not in quotes used to keep
        Assert.IsFalse(Landblock.IsListedAsCapstoneInstanced("Glenden", Dungeon));
    }

    [TestMethod]
    public void CapstoneInstancing_EveryNameOfTheOldDefaultStillWorks()
    {
        foreach (var name in EveryName.Split(','))
        {
            Assert.IsTrue(Landblock.IsListedAsCapstoneInstanced(EveryName, name), name);
        }
    }

    [TestMethod]
    public void CapstoneInstancing_IsDescribedForTheLog()
    {
        StringAssert.Contains(Landblock.DescribeCapstoneInstancing(""), "empty");
        StringAssert.Contains(Landblock.DescribeCapstoneInstancing(null), "numbered copies");
        StringAssert.Contains(Landblock.DescribeCapstoneInstancing("*"), "every capstone dungeon opens as an instance");
        StringAssert.Contains(Landblock.DescribeCapstoneInstancing("Sand Shallow, Mage Academy"), "2 name(s)");
    }

    [TestMethod]
    public void ModifyString_TakesEverythingAfterThePropertyNameAsTheValue()
    {
        Assert.AreEqual(
            "Glenden Wood Dungeon,Green Mire Grave",
            ModifyString.ValueOf(new[] { "capstone_instanced_dungeons", "Glenden", "Wood", "Dungeon,Green", "Mire", "Grave" })
        );
        Assert.AreEqual("*", ModifyString.ValueOf(new[] { "capstone_instanced_dungeons", "*" }));

        // in quotes the parser has already made it one parameter
        Assert.AreEqual(
            "Glenden Wood Dungeon,Green Mire Grave",
            ModifyString.ValueOf(new[] { "capstone_instanced_dungeons", "Glenden Wood Dungeon,Green Mire Grave" })
        );
    }

    [TestMethod]
    public void FetchString_SplitsALongValueIntoLinesWithoutLosingAnything()
    {
        var lines = FetchString.ToDisplayLines($"capstone_instanced_dungeons: {EveryName}", 200).ToList();

        Assert.IsTrue(lines.Count > 1);
        Assert.IsTrue(lines.All(line => line.Length <= 200));
        Assert.AreEqual($"capstone_instanced_dungeons: {EveryName}", string.Concat(lines));
        Assert.IsTrue(lines.Take(lines.Count - 1).All(line => line.EndsWith(",") || line.EndsWith(" ")), "cut after a comma or a space");
    }

    [TestMethod]
    public void FetchString_KeepsAShortTextInOneLineAndBreaksTextWithNothingToBreakAtWhereItMust()
    {
        CollectionAssert.AreEqual(new[] { "short" }, FetchString.ToDisplayLines("short", 200).ToList());
        CollectionAssert.AreEqual(new[] { "" }, FetchString.ToDisplayLines(null, 200).ToList());

        var noBreaks = new string('x', 450);
        var lines = FetchString.ToDisplayLines(noBreaks, 200).ToList();

        CollectionAssert.AreEqual(new[] { 200, 200, 50 }, lines.Select(line => line.Length).ToList());
        Assert.AreEqual(noBreaks, string.Concat(lines));
    }
}
