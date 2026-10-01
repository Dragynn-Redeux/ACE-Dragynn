using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class InstanceTemplateTests
{
    private static Position At(uint cell)
    {
        return new Position(cell, 10f, 10f, 0f, 0f, 0f, 0f, 1f);
    }

    private static LandblockId Block(byte x, byte y)
    {
        return new LandblockId(x, y);
    }

    private static InstanceTemplate Island(LandblockId inside, int ring)
    {
        var edge = InstanceTemplate.Ring(new[] { inside }, ring);

        return new InstanceTemplate("island", new[] { inside }.Concat(edge), At(0x50500100), null, true, edge);
    }

    #region Ring

    [TestMethod]
    public void Ring_IsEverythingWithinItsWidthThatIsNotInside()
    {
        var ring = InstanceTemplate.Ring(new[] { Block(0x50, 0x50) }, 1);

        Assert.AreEqual(8, ring.Count);
        Assert.IsFalse(ring.Contains(Block(0x50, 0x50)), "what is inside is not in the ring");
        Assert.IsTrue(ring.Contains(Block(0x4F, 0x4F)));
        Assert.IsTrue(ring.Contains(Block(0x51, 0x51)), "corners are next to it too");
        Assert.IsFalse(ring.Contains(Block(0x52, 0x50)), "two away is not within a width of one");
    }

    [TestMethod]
    public void Ring_OfSeveralLandblocksHasNoLandblockTwiceAndNoneOfTheOnesInside()
    {
        var inside = new[] { Block(0x50, 0x50), Block(0x51, 0x50) };

        var ring = InstanceTemplate.Ring(inside, 1);

        // 4 x 3 landblocks around and including the two
        Assert.AreEqual(10, ring.Count);
        Assert.AreEqual(ring.Count, ring.Distinct().Count());
        Assert.IsFalse(ring.Contains(inside[0]));
        Assert.IsFalse(ring.Contains(inside[1]));
    }

    [TestMethod]
    public void Ring_IsWiderWhenItIsAskedToBe()
    {
        Assert.AreEqual(24, InstanceTemplate.Ring(new[] { Block(0x50, 0x50) }, 2).Count);
        Assert.AreEqual(0, InstanceTemplate.Ring(new[] { Block(0x50, 0x50) }, 0).Count);
        Assert.ThrowsException<ArgumentOutOfRangeException>(
            () => InstanceTemplate.Ring(new[] { Block(0x50, 0x50) }, -1)
        );
    }

    [TestMethod]
    public void Ring_StopsAtTheEdgeOfTheMap()
    {
        var corner = InstanceTemplate.Ring(new[] { Block(0, 0) }, 1);
        var farCorner = InstanceTemplate.Ring(new[] { Block(254, 254) }, 1);

        Assert.AreEqual(3, corner.Count);
        Assert.AreEqual(3, farCorner.Count);
        Assert.IsTrue(farCorner.All(l => l.LandblockX <= 254 && l.LandblockY <= 254), "255 is not a landblock");
    }

    #endregion

    #region Boundary

    [TestMethod]
    public void InstanceTemplate_TheBoundaryIsPartOfTheFootprintButNotPartOfThePlace()
    {
        var template = Island(Block(0x50, 0x50), 1);

        Assert.AreEqual(9, template.Footprint.Count);
        Assert.AreEqual(8, template.Boundary.Count);
        Assert.IsTrue(template.HasBoundary);

        Assert.IsTrue(template.Contains(Block(0x51, 0x51)));
        Assert.IsTrue(template.IsBoundary(Block(0x51, 0x51)));
        Assert.IsTrue(template.Contains(Block(0x50, 0x50)));
        Assert.IsFalse(template.IsBoundary(Block(0x50, 0x50)));
    }

    [TestMethod]
    public void InstanceTemplate_WhatIsOutsideTheFootprintIsNotBoundaryEither()
    {
        // it is not in the instance at all, which is a different thing that the instance has to deal with
        var template = Island(Block(0x50, 0x50), 1);

        Assert.IsFalse(template.Contains(Block(0x60, 0x60)));
        Assert.IsFalse(template.IsBoundary(Block(0x60, 0x60)));
    }

    [TestMethod]
    public void InstanceTemplate_WithoutABoundaryNobodyIsTurnedBack()
    {
        var template = new InstanceTemplate("dungeon", new[] { Block(0x01, 0xE3) }, At(0x01E30100));

        Assert.IsFalse(template.HasBoundary);
        Assert.AreEqual(0, template.Boundary.Count);
        Assert.IsFalse(template.IsBoundary(Block(0x01, 0xE3)));
    }

    [TestMethod]
    public void InstanceTemplate_TheBoundaryOnlyHasLandblocksOfTheFootprint()
    {
        Assert.ThrowsException<ArgumentException>(
            () =>
                new InstanceTemplate(
                    "x",
                    new[] { Block(0x50, 0x50) },
                    At(0x50500100),
                    null,
                    false,
                    new[] { Block(0x60, 0x60) }
                )
        );
    }

    [TestMethod]
    public void InstanceTemplate_CantBeNothingButBoundary()
    {
        var both = new[] { Block(0x50, 0x50), Block(0x51, 0x50) };

        Assert.ThrowsException<ArgumentException>(
            () => new InstanceTemplate("x", both, At(0x50500100), null, false, both)
        );
    }

    #endregion
}

[TestClass]
public class InstanceTemplateConfigTests
{
    // one island that is right, that the tests change one thing of at a time
    private const string Valid = """
        {
          "name": "hebian",
          "landblocks": [ "E74E" ],
          "bufferRing": 1,
          "instanceOnly": true,
          "entry": { "cell": "0xE74E0019", "x": 84, "y": 7.1, "z": 94 }
        }
        """;

    private static List<InstanceTemplate> Parse(string islands, out List<string> errors)
    {
        errors = new List<string>();

        return InstanceTemplateConfig.Parse("{ \"islands\": [ " + islands + " ] }", errors);
    }

    private static string With(string valid, string from, string to)
    {
        Assert.IsTrue(valid.Contains(from), $"the test's own json doesn't have {from}");

        return valid.Replace(from, to);
    }

    private static void AssertRejected(string island, string expectedInTheError)
    {
        var templates = Parse(island, out var errors);

        Assert.AreEqual(0, templates.Count, "the island should have been left out");
        Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
        StringAssert.Contains(errors[0], expectedInTheError);
    }

    [TestMethod]
    public void Config_AnIslandBecomesATemplateWithItsRing()
    {
        var templates = Parse(Valid, out var errors);

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(1, templates.Count);

        var template = templates[0];

        Assert.AreEqual("hebian", template.Name);
        Assert.IsTrue(template.InstanceOnly);
        Assert.AreEqual(9, template.Footprint.Count);
        Assert.AreEqual(8, template.Boundary.Count);
        Assert.IsFalse(template.IsBoundary(new LandblockId(0xE7, 0x4E)));
        Assert.AreEqual(0xE74E0019u, template.EntryPosition.Cell);
        Assert.AreEqual(84f, template.EntryPosition.PositionX);
        Assert.AreEqual(7.1f, template.EntryPosition.PositionY);
        Assert.AreEqual(94f, template.EntryPosition.PositionZ);
        Assert.AreEqual(1f, template.EntryPosition.RotationW, "no rotation unless one is given");
        Assert.IsNull(template.ReturnPosition);
    }

    [TestMethod]
    public void Config_ARectangleIsEveryLandblockBetweenItsCornersWhicheverCornersAreGiven()
    {
        var forward = Parse(
            With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"rectangles\": [ { \"from\": \"E74E\", \"to\": \"E950\" } ]"),
            out var errors
        );
        var backward = Parse(
            With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"rectangles\": [ { \"from\": \"E950\", \"to\": \"E74E\" } ]"),
            out _
        );

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));

        // 3 x 3 landblocks and a ring of one around them: 5 x 5
        Assert.AreEqual(25, forward[0].Footprint.Count);
        Assert.AreEqual(16, forward[0].Boundary.Count);
        Assert.AreEqual(25, backward[0].Footprint.Count);
    }

    [TestMethod]
    public void Config_LandblocksAndRectanglesCanBeUsedTogetherWithoutCountingOneTwice()
    {
        var templates = Parse(
            With(
                Valid,
                "\"landblocks\": [ \"E74E\" ]",
                "\"landblocks\": [ \"E74E\", \"E74E\" ], \"rectangles\": [ { \"from\": \"E74E\", \"to\": \"E74F\" } ]"
            ),
            out var errors
        );

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));

        // 1 x 2 landblocks with a ring of one: 3 x 4
        Assert.AreEqual(12, templates[0].Footprint.Count);
    }

    [TestMethod]
    public void Config_ABufferRingOfNothingLeavesNoBoundary()
    {
        var templates = Parse(With(Valid, "\"bufferRing\": 1", "\"bufferRing\": 0"), out var errors);

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(1, templates[0].Footprint.Count);
        Assert.IsFalse(templates[0].HasBoundary);
    }

    [TestMethod]
    public void Config_TheRingIsOneLandblockUnlessTheIslandSaysOtherwise()
    {
        var templates = Parse(With(Valid, "\"bufferRing\": 1,", ""), out var errors);

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(9, templates[0].Footprint.Count);
    }

    [TestMethod]
    public void Config_CommentsTrailingCommasAndTheCaseOfNamesAreAllowed()
    {
        const string island = """
            {
              // the town
              "Name": "hebian",
              "LANDBLOCKS": [ "E74E", ],
              "instanceOnly": true,
              "entry": { "cell": "E74E0019", "x": "84", "y": 7.1, "z": 94, },
            }
            """;

        var templates = Parse(island, out var errors);

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(1, templates.Count);
        Assert.AreEqual(84f, templates[0].EntryPosition.PositionX);
    }

    [TestMethod]
    public void Config_AReturnPositionOutsideTheIslandIsKept()
    {
        var templates = Parse(
            With(
                Valid,
                "\"instanceOnly\": true,",
                "\"instanceOnly\": true, \"return\": { \"cell\": \"0xA9B40019\", \"x\": 1, \"y\": 2, \"z\": 3 },"
            ),
            out var errors
        );

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(new LandblockId(0xA9, 0xB4), templates[0].ReturnPosition.LandblockId);
    }

    [TestMethod]
    public void Config_AnEmptyReturnIsTheSameAsLeavingItOut()
    {
        // "return": { } says nothing, so it is the sanctuary, like no return at all
        var templates = Parse(
            With(Valid, "\"instanceOnly\": true,", "\"instanceOnly\": true, \"return\": { },"),
            out var errors
        );

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(1, templates.Count);
        Assert.IsNull(templates[0].ReturnPosition);
    }

    [TestMethod]
    public void Config_AReturnThatHasSomethingInItButNoCellIsAMistake()
    {
        AssertRejected(
            With(
                Valid,
                "\"instanceOnly\": true,",
                "\"instanceOnly\": true, \"return\": { \"x\": 1, \"y\": 2, \"z\": 3 },"
            ),
            "return: it has no cell"
        );
    }

    [TestMethod]
    public void Config_AnEntryWithNothingInItSaysThatItHasNoCell()
    {
        AssertRejected(
            With(Valid, "\"entry\": { \"cell\": \"0xE74E0019\", \"x\": 84, \"y\": 7.1, \"z\": 94 }", "\"entry\": { }"),
            "entry: it has no cell"
        );
    }

    [TestMethod]
    public void Config_InstanceOnlyHasToBeSaidEitherWay()
    {
        // leaving it out would make islands that are in use in the persistent world stop existing there, or not, by accident
        AssertRejected(With(Valid, "\"instanceOnly\": true,", ""), "instanceOnly");

        Assert.AreEqual(1, Parse(With(Valid, "\"instanceOnly\": true", "\"instanceOnly\": false"), out _).Count);
    }

    [TestMethod]
    public void Config_AnIslandNeedsAName()
    {
        AssertRejected(With(Valid, "\"name\": \"hebian\",", ""), "no name");
        AssertRejected(With(Valid, "\"name\": \"hebian\"", "\"name\": \"  \""), "no name");
    }

    [TestMethod]
    public void Config_TheNamesOfTheCapstoneInstancesAreNotForIslands()
    {
        AssertRejected(With(Valid, "\"hebian\"", "\"capstone:Glenden Wood Dungeon\""), "capstone");
    }

    [TestMethod]
    public void Config_TwoIslandsCantShareANameWhateverItsCase()
    {
        var templates = Parse(Valid + "," + With(Valid, "\"hebian\"", "\"HEBIAN\""), out var errors);

        Assert.AreEqual(1, templates.Count, "the second one is left out");
        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains(errors[0], "same name");
    }

    [TestMethod]
    public void Config_AnIslandNeedsLandblocks()
    {
        AssertRejected(With(Valid, "\"landblocks\": [ \"E74E\" ],", ""), "no landblocks");
        AssertRejected(With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"landblocks\": [ ]"), "no landblocks");
    }

    [TestMethod]
    public void Config_ALandblockHasToBeWrittenAsALandblock()
    {
        AssertRejected(With(Valid, "\"E74E\"", "\"E7\""), "not a landblock");
        AssertRejected(With(Valid, "\"E74E\"", "\"GGGG\""), "not a landblock");
        AssertRejected(With(Valid, "\"E74E\"", "\"\""), "not a landblock");
        AssertRejected(With(Valid, "\"E74E\"", "\"E74E00\""), "not a landblock");
        AssertRejected(With(Valid, "\"E74E\"", "\"FFFF\""), "not a landblock");
    }

    [TestMethod]
    public void Config_ALandblockCanBeWrittenAsTheStartOfACell()
    {
        foreach (var text in new[] { "E74E", "e74e", "0xE74E", "E74EFFFF", "0xE74E0019" })
        {
            Assert.IsTrue(InstanceTemplateConfig.TryParseLandblock(text, out var landblock), text);
            Assert.AreEqual(0xE7, landblock.LandblockX, text);
            Assert.AreEqual(0x4E, landblock.LandblockY, text);
        }

        foreach (var text in new[] { null, "", " ", "E7", "E74E0", "xyz", "FFFF" })
        {
            Assert.IsFalse(InstanceTemplateConfig.TryParseLandblock(text, out _), text);
        }
    }

    [TestMethod]
    public void Config_ARectangleHasToBeSmallEnoughToHold()
    {
        // a typing mistake must not be able to make one instance of the whole map
        AssertRejected(
            With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"rectangles\": [ { \"from\": \"0000\", \"to\": \"FEFE\" } ]"),
            "400"
        );
        AssertRejected(
            With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"rectangles\": [ { \"from\": \"E74E\" } ]"),
            "rectangle"
        );
    }

    [TestMethod]
    public void Config_TheRingCountsTowardsTheSizeLimit()
    {
        // 20 x 20 is 400 landblocks, and the ring makes it 22 x 22. The entry is inside the rectangle, so the size is all that is wrong.
        var island = With(
            Valid,
            "\"landblocks\": [ \"E74E\" ]",
            "\"rectangles\": [ { \"from\": \"3030\", \"to\": \"4343\" } ]"
        );

        AssertRejected(With(island, "0xE74E0019", "0x30300019"), "with its ring");
    }

    [TestMethod]
    public void Config_TheBufferRingHasALimit()
    {
        AssertRejected(With(Valid, "\"bufferRing\": 1", "\"bufferRing\": 5"), "bufferRing");
        AssertRejected(With(Valid, "\"bufferRing\": 1", "\"bufferRing\": -1"), "bufferRing");
    }

    [TestMethod]
    public void Config_PlayersArriveInsideTheIslandNotInTheRingAndNotSomewhereElse()
    {
        AssertRejected(With(Valid, "0xE74E0019", "0xE74F0019"), "ring");
        AssertRejected(With(Valid, "0xE74E0019", "0xA9B40019"), "not one of the island's landblocks");
    }

    // aerlinthe as it is written in instances.json: a rectangle from B3F3 to BFE8, two opposite corners that are not the smaller one in both directions
    private static string Aerlinthe()
    {
        return With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"rectangles\": [ { \"from\": \"B3F3\", \"to\": \"BFE8\" } ]");
    }

    [TestMethod]
    public void Config_TheFourCornersOfARectangleAreInsideTheIsland()
    {
        // B3F3 is the corner that the rectangle is written from. It is an island landblock, not part of the ring
        foreach (var corner in new[] { "0xB3F3", "0xBFE8", "0xB3E8", "0xBFF3" })
        {
            var templates = Parse(With(Aerlinthe(), "0xE74E", corner), out var errors);

            Assert.AreEqual(0, errors.Count, corner + ": " + string.Join(" | ", errors));
            Assert.AreEqual(1, templates.Count, corner);
        }
    }

    [TestMethod]
    public void Config_AnEntryOneLandblockOutsideARectangleIsInTheRingAndRefused()
    {
        // one step out from the corner it is written from, from the other edges, and diagonally
        foreach (var outside in new[] { "0xB2F3", "0xB3F4", "0xB2F4", "0xC0E8", "0xBFE7", "0xC0E7" })
        {
            AssertRejected(With(Aerlinthe(), "0xE74E", outside), "not one of the island's landblocks");
        }
    }

    [TestMethod]
    public void Config_TheEntryPositionHasToBeAValidPosition()
    {
        AssertRejected(
            With(Valid, "\"entry\": { \"cell\": \"0xE74E0019\", \"x\": 84, \"y\": 7.1, \"z\": 94 }", ""),
            "entry"
        );
        AssertRejected(With(Valid, "0xE74E0019", "0xE74E"), "not a cell");
        AssertRejected(With(Valid, "0xE74E0019", "nonsense"), "not a cell");
        AssertRejected(With(Valid, "\"z\": 94", "\"z\": 94, \"qw\": 0"), "rotation");
    }

    [TestMethod]
    public void Config_LeavingCantPutPlayersBackInsideTheIsland()
    {
        AssertRejected(
            With(
                Valid,
                "\"instanceOnly\": true,",
                "\"instanceOnly\": true, \"return\": { \"cell\": \"0xE74E0019\", \"x\": 1, \"y\": 2, \"z\": 3 },"
            ),
            "inside the island"
        );

        // the ring is part of the island
        AssertRejected(
            With(
                Valid,
                "\"instanceOnly\": true,",
                "\"instanceOnly\": true, \"return\": { \"cell\": \"0xE74F0019\", \"x\": 1, \"y\": 2, \"z\": 3 },"
            ),
            "inside the island"
        );
    }

    [TestMethod]
    public void Config_LeavingCantSendPlayersToAPlaceThatOnlyExistsAsAnInstance()
    {
        var second = With(
            With(Valid, "\"hebian\"", "\"second\""),
            "\"instanceOnly\": true,",
            "\"instanceOnly\": true, \"return\": { \"cell\": \"0xE74E0019\", \"x\": 1, \"y\": 2, \"z\": 3 },"
        );

        // the second island's landblock is far from the first one, so the first island's landblock is not in its ring
        second = With(second, "\"E74E\"", "\"A9B4\"")
            .Replace("\"cell\": \"0xE74E0019\", \"x\": 84", "\"cell\": \"0xA9B40019\", \"x\": 84");

        var templates = Parse(Valid + "," + second, out var errors);

        Assert.AreEqual(1, templates.Count, "only the first island is left");
        Assert.AreEqual("hebian", templates[0].Name);
        Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
        StringAssert.Contains(errors[0], "only exists as an instance");
    }

    [TestMethod]
    public void Config_AMistakeInOneIslandDoesNotLoseTheOthers()
    {
        var broken = With(Valid, "\"name\": \"hebian\"", "\"name\": \"broken\"").Replace("E74E", "nope");
        var good = With(With(Valid, "\"hebian\"", "\"good\""), "E74E", "A9B4");

        var templates = Parse(broken + "," + good, out var errors);

        Assert.IsTrue(errors.Count > 0);
        Assert.AreEqual(1, templates.Count);
        Assert.AreEqual("good", templates[0].Name);
    }

    [TestMethod]
    public void Config_TextThatIsNotJsonIsReportedAndGivesNothing()
    {
        var errors = new List<string>();

        var templates = InstanceTemplateConfig.Parse("this is not json", errors);

        Assert.AreEqual(0, templates.Count);
        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains(errors[0], "can't be read");
    }

    [TestMethod]
    public void Config_AValueOfTheWrongKindLeavesOutOnlyThatIsland()
    {
        // "true" in quotes is text, not a boolean. The other island is fine, and has to load
        var bad = With(Valid, "\"instanceOnly\": true", "\"instanceOnly\": \"true\"");
        var good = With(Valid, "\"name\": \"hebian\"", "\"name\": \"good\"");

        var templates = Parse(bad + ", " + good, out var errors);

        Assert.AreEqual(1, templates.Count, string.Join(" | ", errors));
        Assert.AreEqual("good", templates[0].Name);
        Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
        StringAssert.Contains(errors[0], "island 'hebian'");
        StringAssert.Contains(errors[0], "instanceOnly has to be true or false");
    }

    [TestMethod]
    public void Config_TheKindOfValueThatIsNeededIsNamed()
    {
        AssertRejected(With(Valid, "\"bufferRing\": 1", "\"bufferRing\": \"lots\""), "bufferRing has to be a whole number");
        AssertRejected(With(Valid, "\"landblocks\": [ \"E74E\" ]", "\"landblocks\": \"E74E\""), "landblocks has to be a list");
        AssertRejected(
            With(Valid, "\"entry\": { \"cell\": \"0xE74E0019\", \"x\": 84, \"y\": 7.1, \"z\": 94 }", "\"entry\": \"0xE74E0019\""),
            "entry has to be an object"
        );
        AssertRejected(With(Valid, "\"name\": \"hebian\"", "\"name\": 5"), "name has to be text");
    }

    [TestMethod]
    public void Config_TheFileHasToBeAnObjectWithAListOfIslands()
    {
        foreach (var json in new[] { "[ ]", "\"islands\"", "{ \"islands\": 5 }" })
        {
            var errors = new List<string>();

            Assert.AreEqual(0, InstanceTemplateConfig.Parse(json, errors).Count, json);
            Assert.AreEqual(1, errors.Count, json + ": " + string.Join(" | ", errors));
            StringAssert.Contains(errors[0], "can't be read", json);
        }
    }

    [TestMethod]
    public void Config_TheNamesOfTheListAndOfTheIslandsDoNotCareAboutCapitals()
    {
        var errors = new List<string>();
        var templates = InstanceTemplateConfig.Parse("{ \"ISLANDS\": [ " + With(Valid, "\"name\"", "\"NAME\"") + " ] }", errors);

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));
        Assert.AreEqual(1, templates.Count);
    }

    [TestMethod]
    public void Config_NoIslandsIsNotAMistake()
    {
        foreach (var json in new[] { "{}", "{ \"islands\": [] }", "{ \"islands\": null }" })
        {
            var errors = new List<string>();

            Assert.AreEqual(0, InstanceTemplateConfig.Parse(json, errors).Count, json);
            Assert.AreEqual(0, errors.Count, json);
        }
    }

    [TestMethod]
    public void Config_TheFileThatComesWithTheServerHasNoMistakesAndNothingInstanceOnly()
    {
        // the instances.json that comes with the server (apps/server/instances.json) is copied next to the test assembly by the tests project
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "instances.json");

        if (!System.IO.File.Exists(path))
        {
            Assert.Inconclusive("instances.json is not next to the tests");
        }

        var errors = new List<string>();
        var templates = InstanceTemplateConfig.Parse(System.IO.File.ReadAllText(path), errors);

        Assert.AreEqual(0, errors.Count, string.Join(" | ", errors));

        // an island that is instance only takes its landblocks away from the persistent world, so nothing that comes with the server may be
        foreach (var template in templates)
        {
            Assert.IsFalse(template.InstanceOnly, template.Name + " is instance only");
        }
    }
}
