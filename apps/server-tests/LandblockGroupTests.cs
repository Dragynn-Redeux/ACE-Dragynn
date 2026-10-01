using System;
using ACE.Entity;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class LandblockGroupTests
{
    private static LandblockId Id(int x, int y) => new LandblockId((byte)x, (byte)y);

    [TestMethod]
    public void LandblockGroup_NearbyLandblocksInTheSameInstanceShareAGroup()
    {
        // distance 3 is inside the minimum spacing of 4
        Assert.IsTrue(LandblockGroup.CanShareGroup(0, Id(10, 10), false, 0, Id(13, 10), false));
    }

    [TestMethod]
    public void LandblockGroup_FarLandblocksDoNotShareAGroup()
    {
        // distance 4 is not less than the minimum spacing of 4
        Assert.IsFalse(LandblockGroup.CanShareGroup(0, Id(10, 10), false, 0, Id(14, 10), false));
    }

    [TestMethod]
    public void LandblockGroup_DormantLandblocksUseTheTighterSpacing()
    {
        Assert.IsTrue(LandblockGroup.CanShareGroup(0, Id(10, 10), true, 0, Id(12, 10), false));
        Assert.IsFalse(LandblockGroup.CanShareGroup(0, Id(10, 10), true, 0, Id(13, 10), false));

        // either landblock being dormant is enough
        Assert.IsFalse(LandblockGroup.CanShareGroup(0, Id(10, 10), false, 0, Id(13, 10), true));
    }

    [TestMethod]
    public void LandblockGroup_DistanceIsMeasuredAsTheLargerAxis()
    {
        Assert.IsTrue(LandblockGroup.CanShareGroup(0, Id(10, 10), false, 0, Id(13, 13), false));
        Assert.IsFalse(LandblockGroup.CanShareGroup(0, Id(10, 10), false, 0, Id(14, 13), false));
    }

    [TestMethod]
    public void LandblockGroup_TheSameCoordinatesInDifferentInstancesNeverShareAGroup()
    {
        // an instanced copy of a landblock must not be pulled into the group that ticks the persistent world,
        // however close together they are
        Assert.IsFalse(LandblockGroup.CanShareGroup(0, Id(10, 10), false, 5, Id(10, 10), false));
        Assert.IsFalse(LandblockGroup.CanShareGroup(5, Id(10, 10), false, 6, Id(10, 11), false));
        Assert.IsFalse(LandblockGroup.CanShareGroup(5, Id(10, 10), true, 6, Id(10, 10), true));
    }

    [TestMethod]
    public void LandblockGroup_NeighboursWithinOneInstanceStillShareAGroup()
    {
        // this is what lets a whole island instance tick as one group
        Assert.IsTrue(LandblockGroup.CanShareGroup(5, Id(10, 10), false, 5, Id(11, 10), false));
        Assert.IsTrue(LandblockGroup.CanShareGroup(5, Id(10, 10), false, 5, Id(10, 10), false));
    }

    [TestMethod]
    public void LandblockGroup_PersistentWorldBehaviourMatchesTheOriginalRule()
    {
        // The rule as it was written before instances existed
        static bool Original(LandblockId a, bool dormantA, LandblockId b, bool dormantB)
        {
            var distance = Math.Max(Math.Abs(a.LandblockX - b.LandblockX), Math.Abs(a.LandblockY - b.LandblockY));

            if (dormantA || dormantB)
            {
                return distance < LandblockGroup.LandblockGroupMinSpacingWhenDormant;
            }

            return distance < LandblockGroup.LandblockGroupMinSpacing;
        }

        var origin = Id(100, 100);

        for (var dx = -6; dx <= 6; dx++)
        {
            for (var dy = -6; dy <= 6; dy++)
            {
                var other = Id(100 + dx, 100 + dy);

                foreach (var dormantA in new[] { false, true })
                {
                    foreach (var dormantB in new[] { false, true })
                    {
                        Assert.AreEqual(
                            Original(origin, dormantA, other, dormantB),
                            LandblockGroup.CanShareGroup(0, origin, dormantA, 0, other, dormantB),
                            $"dx={dx} dy={dy} dormantA={dormantA} dormantB={dormantB}"
                        );
                    }
                }
            }
        }
    }
}
