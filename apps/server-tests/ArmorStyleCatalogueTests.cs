using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class ArmorStyleCatalogueTests
{
    private const CoverageMask Chest = CoverageMask.OuterwearChest;
    private const CoverageMask LowerArms = CoverageMask.OuterwearLowerArms;
    private const CoverageMask Sleeves = CoverageMask.OuterwearUpperArms | CoverageMask.OuterwearLowerArms;
    private const CoverageMask Pants =
        CoverageMask.OuterwearAbdomen | CoverageMask.OuterwearUpperLegs | CoverageMask.OuterwearLowerLegs;

    // ClothingBase and ClothingPriority of these weenies, from the world database
    private static readonly Dictionary<uint, (uint ClothingBase, CoverageMask Coverage)> Weenies =
        new()
        {
            { 36, (0x1000000C, LowerArms) }, // Leather Bracers
            { 39, (0x10000055, Chest) }, // Leather Breastplate
            { 102, (0x1000002E, Sleeves) }, // Leather Sleeves
            { 25638, (0x100004EC, Chest) }, // Leather Vest
            { 25647, (0x100004ED, Pants) }, // Leather Pants
            { 25651, (0x100004E0, Sleeves) }, // Leather Sleeves (newer)
            { 38, (0x1000000F, LowerArms) }, // Studded Leather Bracers
            { 105, (0x1000002F, Sleeves) }, // Studded Leather Sleeves
            { 6003, (0x1000018C, Chest) }, // Koujia Breastplate
            { 6004, (0x10000189, Pants) }, // Koujia Leggings
            { 6005, (0x1000018B, Sleeves) }, // Koujia Sleeves
        };

    private static ArmorStyleCatalogue Build()
    {
        return ArmorStyleCatalogue.Build(wcid =>
            Weenies.TryGetValue(wcid, out var w) ? (w.ClothingBase, (int)w.Coverage) : (null, null)
        );
    }

    [TestMethod]
    public void Definitions_NameEachPieceOnce()
    {
        var wcids = ArmorStyleCatalogue.DefinedWcids.ToList();

        CollectionAssert.AllItemsAreUnique(wcids);
    }

    [TestMethod]
    public void Build_LeavesOutPiecesWithoutALook()
    {
        var catalogue = Build();

        Assert.IsNotNull(catalogue.Get(102));
        Assert.IsNull(catalogue.Get(47)); // Leather Coat isn't in the test data
    }

    [TestMethod]
    public void Find_KoujiaSleevesFitSleevesButNotBracers()
    {
        var catalogue = Build();
        var koujiaSleeves = catalogue.Get(6005);

        Assert.IsNull(catalogue.Find(koujiaSleeves, LowerArms));
        Assert.AreEqual(6005u, catalogue.Find(koujiaSleeves, Sleeves).Wcid);
        Assert.AreEqual(6003u, catalogue.Find(koujiaSleeves, Chest).Wcid);
        Assert.AreEqual(6004u, catalogue.Find(koujiaSleeves, Pants).Wcid);
    }

    [TestMethod]
    public void Find_StuddedBracersMakeSleevesStudded()
    {
        var catalogue = Build();
        var studdedBracers = catalogue.Get(38);

        Assert.AreEqual(38u, catalogue.Find(studdedBracers, LowerArms).Wcid);
        Assert.AreEqual(105u, catalogue.Find(studdedBracers, Sleeves).Wcid);
    }

    [TestMethod]
    public void Find_HasNoHelmsGauntletsOrBoots()
    {
        var catalogue = Build();

        Assert.IsNull(catalogue.Find(catalogue.Get(102), CoverageMask.Head));
        Assert.IsNull(catalogue.Find(catalogue.Get(102), CoverageMask.Hands));
        Assert.IsNull(catalogue.Find(catalogue.Get(102), CoverageMask.Feet));
    }

    [TestMethod]
    public void Build_LeavesOutPiecesCoveringHeadHandsOrFeet()
    {
        var catalogue = ArmorStyleCatalogue.Build(wcid =>
            wcid == 102 ? (0x1000002E, (int)(Sleeves | CoverageMask.Hands)) : (null, null)
        );

        Assert.IsNull(catalogue.Get(102));
    }

    [TestMethod]
    public void CoversExtremities_IsTrueForHelmsGauntletsAndBoots()
    {
        Assert.IsTrue(ArmorStyleCatalogue.CoversExtremities(CoverageMask.Head));
        Assert.IsTrue(ArmorStyleCatalogue.CoversExtremities(CoverageMask.Hands));
        Assert.IsTrue(ArmorStyleCatalogue.CoversExtremities(CoverageMask.Feet));
        Assert.IsTrue(ArmorStyleCatalogue.CoversExtremities(CoverageMask.Hands | LowerArms)); // long gauntlets

        Assert.IsFalse(ArmorStyleCatalogue.CoversExtremities(LowerArms));
        Assert.IsFalse(ArmorStyleCatalogue.CoversExtremities(Sleeves));
        Assert.IsFalse(ArmorStyleCatalogue.CoversExtremities(Pants));
    }

    [TestMethod]
    public void Find_PrefersTheTemplatesOwnLeatherSet()
    {
        var catalogue = Build();

        Assert.AreEqual(39u, catalogue.Find(catalogue.Get(102), Chest).Wcid);
        Assert.AreEqual(25638u, catalogue.Find(catalogue.Get(25651), Chest).Wcid);
    }

    [TestMethod]
    public void Find_FallsBackToTheOtherLeatherSetForCoverageItLacks()
    {
        var catalogue = Build();

        Assert.AreEqual(25647u, catalogue.Find(catalogue.Get(102), Pants).Wcid);
    }

    [TestMethod]
    public void Identify_GoesByTheItemsOwnPieceWhenItStillLooksLikeIt()
    {
        var catalogue = Build();

        Assert.AreEqual(102u, catalogue.Identify(102, 0x1000002E, ArmorWeightClass.Light).Wcid);
        Assert.AreEqual(ArmorStyle.Koujia, catalogue.Identify(6005, 0x1000018B, ArmorWeightClass.Light).Style);
    }

    [TestMethod]
    public void Identify_GoesByTheLookWhenTheItemWasRestyled()
    {
        var catalogue = Build();

        // Leather Sleeves tailored to look like Studded Leather Sleeves
        Assert.AreEqual(105u, catalogue.Identify(102, 0x1000002F, ArmorWeightClass.Light).Wcid);
    }

    [TestMethod]
    public void Identify_ReturnsNullForLooksOutsideAnyStyle()
    {
        var catalogue = Build();

        Assert.IsNull(catalogue.Identify(1, 0x10000999, ArmorWeightClass.Light));
        Assert.IsNull(catalogue.Identify(102, null, ArmorWeightClass.Light));
        Assert.IsNull(catalogue.Identify(1, 0x1000018B, ArmorWeightClass.Heavy)); // Koujia Sleeves' look, wrong weight class
    }

    [TestMethod]
    public void Coverages_ListsWhatAStyleCanGoOn()
    {
        var catalogue = Build();

        CollectionAssert.AreEqual(new[] { Chest, Sleeves, Pants }, catalogue.Coverages(ArmorStyle.Koujia));
    }

    [TestMethod]
    public void ChooseColors_KeepsTheTemplatesPaletteWhenThePieceHasIt()
    {
        var icons = new Dictionary<uint, uint> { { 4, 0x06001111 }, { 20, 0x06002222 } };

        Assert.AreEqual((20, 0x06002222u), ArmorStyleCatalogue.ChooseColors(20, 4, 0x06009999, icons));
    }

    [TestMethod]
    public void ChooseColors_FallsBackToThePiecesPalette()
    {
        var icons = new Dictionary<uint, uint> { { 4, 0x06001111 } };

        Assert.AreEqual((4, 0x06001111u), ArmorStyleCatalogue.ChooseColors(20, 4, 0x06009999, icons));
        Assert.AreEqual((4, 0x06001111u), ArmorStyleCatalogue.ChooseColors(null, 4, 0x06009999, icons));
        Assert.AreEqual(((int?)null, 0x06009999u), ArmorStyleCatalogue.ChooseColors(20, null, 0x06009999, icons));
    }

    [TestMethod]
    public void DescribeCoverage_NamesTheBodyParts()
    {
        Assert.AreEqual("upper arms + lower arms", ArmorStyleCatalogue.DescribeCoverage(Sleeves));
        Assert.AreEqual("abdomen + upper legs + lower legs", ArmorStyleCatalogue.DescribeCoverage(Pants));
        Assert.AreEqual("head", ArmorStyleCatalogue.DescribeCoverage(CoverageMask.Head));
    }

    [TestMethod]
    public void SlotCount_CostsOneUsePerArmorSlot()
    {
        Assert.AreEqual(1, ArmorStyleCatalogue.SlotCount(CoverageMask.Head));
        Assert.AreEqual(1, ArmorStyleCatalogue.SlotCount(LowerArms));
        Assert.AreEqual(2, ArmorStyleCatalogue.SlotCount(Sleeves));
        Assert.AreEqual(3, ArmorStyleCatalogue.SlotCount(Pants));
        Assert.AreEqual(
            4,
            ArmorStyleCatalogue.SlotCount(
                Chest | CoverageMask.OuterwearAbdomen | Sleeves // coat
            )
        );
        Assert.AreEqual(2, ArmorStyleCatalogue.SlotCount(CoverageMask.Hands | LowerArms)); // long gauntlets
    }

    [TestMethod]
    public void SlotCount_AllTheBodyArmorIsSixUses()
    {
        var suit = new[]
        {
            Chest | CoverageMask.OuterwearAbdomen,
            Sleeves,
            CoverageMask.OuterwearUpperLegs | CoverageMask.OuterwearLowerLegs
        };

        Assert.AreEqual(6, suit.Sum(ArmorStyleCatalogue.SlotCount));
    }

    [TestMethod]
    public void DescribeTargetType_NamesTheArmorAndItsCost()
    {
        Assert.AreEqual("bracers (1 use)", ArmorStyleCatalogue.DescribeTargetType(LowerArms));
        Assert.AreEqual("sleeves (upper arms + lower arms, 2 uses)", ArmorStyleCatalogue.DescribeTargetType(Sleeves));
        Assert.AreEqual(
            "leggings with girths (abdomen + upper legs + lower legs, 3 uses)",
            ArmorStyleCatalogue.DescribeTargetType(Pants)
        );
        Assert.AreEqual(
            "armor covering the chest + lower legs (2 uses)",
            ArmorStyleCatalogue.DescribeTargetType(Chest | CoverageMask.OuterwearLowerLegs)
        );
    }

    [TestMethod]
    public void DescribeTargetType_NamesEveryCoverageTheStylesUse()
    {
        var catalogue = Build();

        foreach (var style in new[] { ArmorStyle.Leather, ArmorStyle.StuddedLeather, ArmorStyle.Koujia })
        {
            foreach (var coverage in catalogue.Coverages(style))
            {
                StringAssert.DoesNotMatch(
                    ArmorStyleCatalogue.DescribeTargetType(coverage),
                    new System.Text.RegularExpressions.Regex("^armor covering")
                );
            }
        }
    }

    [TestMethod]
    public void DescribeTargetType_NamesHelmsGauntletsAndBootsWithoutCost()
    {
        Assert.AreEqual("helms", ArmorStyleCatalogue.DescribeTargetType(CoverageMask.Head, withCost: false));
        Assert.AreEqual("gauntlets", ArmorStyleCatalogue.DescribeTargetType(CoverageMask.Hands, withCost: false));
        Assert.AreEqual("boots", ArmorStyleCatalogue.DescribeTargetType(CoverageMask.Feet, withCost: false));
        Assert.AreEqual(
            "long gauntlets",
            ArmorStyleCatalogue.DescribeTargetType(CoverageMask.Hands | LowerArms, withCost: false)
        );
        Assert.AreEqual("sleeves", ArmorStyleCatalogue.DescribeTargetType(Sleeves, withCost: false));
    }

    [TestMethod]
    public void StyleName_ReadsLikeTheArmorsName()
    {
        Assert.AreEqual("Studded Leather", ArmorStyleCatalogue.StyleName(ArmorStyle.StuddedLeather));
        Assert.AreEqual("Olthoi", ArmorStyleCatalogue.StyleName(ArmorStyle.OlthoiArmor));
        Assert.AreEqual("Koujia", ArmorStyleCatalogue.StyleName(ArmorStyle.Koujia));
    }
}
