using System;
using System.Numerics;
using ACE.Entity;
using ACE.Server.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

/// <summary>
/// Unit coverage for the anti-blink path/door geometry in <see cref="AntiBlinkGeometry"/>. Pure coordinate
/// arithmetic: no landblock, no physics, no live object, no door weenie.
/// Ported from ACE-DreamWeave (https://github.com/Awful-Waffle-Rofl/ACE-DreamWeave, AGPL-3.0).
/// </summary>
[TestClass]
public class AntiBlinkGeometryTests
{
    // An OUTDOOR cell (indoors is "cell bits >= 0x100") in landblock 0x0102, so LandblockX = 0x01
    // and LandblockY = 0x02, and a position near the middle of the block stays inside it.
    private const uint BlockA = 0x01020025;

    // The landblock immediately east of BlockA: LandblockX = 0x02, same Y.
    private const uint BlockB = 0x02020025;

    private const float LandblockLength = 192.0f;

    private static Position At(uint cell, float x, float y, float z = 0f)
    {
        return new Position(cell, x, y, z, 0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// A door facing along +Y (identity rotation), so its blocking plane runs east-west.
    /// </summary>
    private static Position DoorFacingNorth(uint cell, float x, float y, float z = 0f)
    {
        return new Position(cell, x, y, z, 0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// A door rotated <paramref name="degrees"/> about Z from facing +Y.
    /// </summary>
    private static Position DoorRotated(uint cell, float x, float y, float degrees)
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(degrees * Math.PI / 180.0));

        return new Position(cell, x, y, 0f, q.X, q.Y, q.Z, q.W);
    }

    #region GetGlobalPos

    [TestMethod]
    public void GetGlobalPos_LiftsLandblockLocalIntoGlobalCoords()
    {
        var global = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 48f));

        Assert.AreEqual(0x01 * LandblockLength + 96f, global.X, 0.001f);
        Assert.AreEqual(0x02 * LandblockLength + 48f, global.Y, 0.001f);
    }

    /// <summary>
    /// The whole point of global coords: two positions with the SAME local X in ADJACENT landblocks
    /// must not collapse onto each other. This is what lets the path test survive a block boundary
    /// instead of bailing out on it.
    /// </summary>
    [TestMethod]
    public void GetGlobalPos_AdjacentLandblocksDoNotCollide()
    {
        var a = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 96f));
        var b = AntiBlinkGeometry.GetGlobalPos(At(BlockB, 96f, 96f));

        Assert.AreNotEqual(a.X, b.X);
        Assert.AreEqual(LandblockLength, b.X - a.X, 0.001f);
        Assert.AreEqual(a.Y, b.Y, 0.001f);
    }

    #endregion

    #region GetDoorSegment

    [TestMethod]
    public void GetDoorSegment_IdentityRotation_SpansEastWestCenteredOnDoor()
    {
        var segment = AntiBlinkGeometry.GetDoorSegment(DoorFacingNorth(BlockA, 96f, 96f), 3.0f);

        var center = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 96f));

        // door faces +Y, so its width axis is X
        Assert.AreEqual(center.X - 1.5f, Math.Min(segment.Start.X, segment.End.X), 0.001f);
        Assert.AreEqual(center.X + 1.5f, Math.Max(segment.Start.X, segment.End.X), 0.001f);
        Assert.AreEqual(center.Y, segment.Start.Y, 0.001f);
        Assert.AreEqual(center.Y, segment.End.Y, 0.001f);
    }

    [TestMethod]
    public void GetDoorSegment_RotatedNinetyDegrees_SpansNorthSouth()
    {
        var segment = AntiBlinkGeometry.GetDoorSegment(DoorRotated(BlockA, 96f, 96f, 90f), 3.0f);

        var center = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 96f));

        Assert.AreEqual(center.X, segment.Start.X, 0.001f);
        Assert.AreEqual(center.X, segment.End.X, 0.001f);
        Assert.AreEqual(center.Y - 1.5f, Math.Min(segment.Start.Y, segment.End.Y), 0.001f);
        Assert.AreEqual(center.Y + 1.5f, Math.Max(segment.Start.Y, segment.End.Y), 0.001f);
    }

    [TestMethod]
    public void GetDoorSegment_WidthIsHonored()
    {
        var narrow = AntiBlinkGeometry.GetDoorSegment(DoorFacingNorth(BlockA, 96f, 96f), 2.0f);
        var wide = AntiBlinkGeometry.GetDoorSegment(DoorFacingNorth(BlockA, 96f, 96f), 8.0f);

        Assert.AreEqual(2.0f, (narrow.End - narrow.Start).Length(), 0.001f);
        Assert.AreEqual(8.0f, (wide.End - wide.Start).Length(), 0.001f);
    }

    #endregion

    #region GetSegmentIntersection

    [TestMethod]
    public void GetSegmentIntersection_CrossingSegments_ReturnsCrossingPoint()
    {
        var hit = AntiBlinkGeometry.GetSegmentIntersection(
            new Vector2(0f, -1f),
            new Vector2(0f, 1f),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        Assert.IsNotNull(hit);
        Assert.AreEqual(0f, hit.Value.X, 0.001f);
        Assert.AreEqual(0f, hit.Value.Y, 0.001f);
    }

    [TestMethod]
    public void GetSegmentIntersection_ParallelSegments_ReturnsNull()
    {
        var hit = AntiBlinkGeometry.GetSegmentIntersection(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 5f),
            new Vector2(10f, 5f)
        );

        Assert.IsNull(hit);
    }

    [TestMethod]
    public void GetSegmentIntersection_CollinearSegments_ReturnsNull()
    {
        var hit = AntiBlinkGeometry.GetSegmentIntersection(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(2f, 0f),
            new Vector2(8f, 0f)
        );

        Assert.IsNull(hit);
    }

    /// <summary>
    /// The lines cross, but the crossing point is past the end of the movement segment. Walking up TO a
    /// door and stopping is not walking through it, and must not be rejected.
    /// </summary>
    [TestMethod]
    public void GetSegmentIntersection_CrossingBeyondSegmentEnd_ReturnsNull()
    {
        var hit = AntiBlinkGeometry.GetSegmentIntersection(
            new Vector2(0f, -5f),
            new Vector2(0f, -1f),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        Assert.IsNull(hit);
    }

    /// <summary>
    /// The path crosses the door's plane, but off to the side of the doorway itself - walking past the
    /// outside of a closed door along the wall it sits in.
    /// </summary>
    [TestMethod]
    public void GetSegmentIntersection_CrossesPlaneBesideDoorway_ReturnsNull()
    {
        var hit = AntiBlinkGeometry.GetSegmentIntersection(
            new Vector2(20f, -1f),
            new Vector2(20f, 1f),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        Assert.IsNull(hit);
    }

    [TestMethod]
    public void GetSegmentIntersection_DegenerateSegment_ReturnsNull()
    {
        var hit = AntiBlinkGeometry.GetSegmentIntersection(new Vector2(0f, -1f), new Vector2(0f, 1f), Vector2.Zero, Vector2.Zero);

        Assert.IsNull(hit);
    }

    #endregion

    #region DistanceToSegmentSq

    [TestMethod]
    public void DistanceToSegmentSq_PointOnSegment_IsZero()
    {
        var d = AntiBlinkGeometry.DistanceToSegmentSq(new Vector2(0f, 0f), new Vector2(-1f, 0f), new Vector2(1f, 0f));

        Assert.AreEqual(0f, d, 0.0001f);
    }

    [TestMethod]
    public void DistanceToSegmentSq_PointOffSegment_UsesPerpendicularDistance()
    {
        var d = AntiBlinkGeometry.DistanceToSegmentSq(new Vector2(0f, 3f), new Vector2(-1f, 0f), new Vector2(1f, 0f));

        Assert.AreEqual(9f, d, 0.0001f);
    }

    /// <summary>
    /// Past the end of the segment the nearest point is the endpoint, not the infinite line.
    /// </summary>
    [TestMethod]
    public void DistanceToSegmentSq_PointBeyondEnd_UsesEndpoint()
    {
        var d = AntiBlinkGeometry.DistanceToSegmentSq(new Vector2(5f, 0f), new Vector2(-1f, 0f), new Vector2(1f, 0f));

        Assert.AreEqual(16f, d, 0.0001f);
    }

    [TestMethod]
    public void DistanceToSegmentSq_DegenerateSegment_UsesPointDistance()
    {
        var d = AntiBlinkGeometry.DistanceToSegmentSq(new Vector2(3f, 4f), Vector2.Zero, Vector2.Zero);

        Assert.AreEqual(25f, d, 0.0001f);
    }

    /// <summary>
    /// The anti-wedge case: a player caught standing on a closed door's plane. Their position registers as
    /// on the segment, which is what makes CheckDoorCollision skip that door instead of rejecting every
    /// move they make - including the ones that would get them out.
    /// </summary>
    [TestMethod]
    public void DistanceToSegmentSq_PlayerStandingInDoorway_RegistersOnThePlane()
    {
        var door = DoorFacingNorth(BlockA, 96f, 96f);
        var segment = AntiBlinkGeometry.GetDoorSegment(door, 3.0f);

        var standingInIt = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 96f));

        Assert.IsTrue(AntiBlinkGeometry.DistanceToSegmentSq(standingInIt, segment.Start, segment.End) < AntiBlinkGeometry.DoorPlaneToleranceSq);
    }

    /// <summary>
    /// A player a stride back from the door is NOT on its plane, so the door is still enforced against
    /// them. This is the boundary that keeps the anti-wedge guard from becoming a bypass.
    /// </summary>
    [TestMethod]
    public void DistanceToSegmentSq_PlayerApproachingDoor_IsNotOnThePlane()
    {
        var door = DoorFacingNorth(BlockA, 96f, 96f);
        var segment = AntiBlinkGeometry.GetDoorSegment(door, 3.0f);

        var approaching = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 95f));

        Assert.IsFalse(AntiBlinkGeometry.DistanceToSegmentSq(approaching, segment.Start, segment.End) < AntiBlinkGeometry.DoorPlaneToleranceSq);
    }

    #endregion

    #region End to end: path vs door

    /// <summary>
    /// The blink case: a straight walk from one side of a closed door to the other.
    /// </summary>
    [TestMethod]
    public void PathThroughDoor_IsDetected()
    {
        var door = DoorFacingNorth(BlockA, 96f, 96f);
        var segment = AntiBlinkGeometry.GetDoorSegment(door, 3.0f);

        var start = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 94f));
        var end = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 98f));

        Assert.IsNotNull(AntiBlinkGeometry.GetSegmentIntersection(start, end, segment.Start, segment.End));
    }

    /// <summary>
    /// Approaching the door and stopping short of it is legitimate movement.
    /// </summary>
    [TestMethod]
    public void PathStoppingAtDoor_IsNotDetected()
    {
        var door = DoorFacingNorth(BlockA, 96f, 96f);
        var segment = AntiBlinkGeometry.GetDoorSegment(door, 3.0f);

        var start = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 90f));
        var end = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 96f, 95f));

        Assert.IsNull(AntiBlinkGeometry.GetSegmentIntersection(start, end, segment.Start, segment.End));
    }

    /// <summary>
    /// A door standing on a landblock boundary is still detected, rather than the check bailing out early on a
    /// cross-landblock move.
    /// </summary>
    [TestMethod]
    public void PathThroughDoorAcrossLandblockBoundary_IsDetected()
    {
        // door sits 1 unit inside BlockB's western edge, facing east (+X), so its plane runs north-south
        var door = DoorRotated(BlockB, 1f, 96f, 90f);
        var segment = AntiBlinkGeometry.GetDoorSegment(door, 3.0f);

        // walk east out of BlockA's eastern edge and into BlockB, straight through the door
        var start = AntiBlinkGeometry.GetGlobalPos(At(BlockA, 190f, 96f));
        var end = AntiBlinkGeometry.GetGlobalPos(At(BlockB, 3f, 96f));

        Assert.IsNotNull(AntiBlinkGeometry.GetSegmentIntersection(start, end, segment.Start, segment.End));
    }

    #endregion

    #region IsDoorWithinZ

    /// <summary>
    /// A jump apex above the limit must not hide a door whose plane is crossed on the way back down: the
    /// landing end of the segment is at floor level, and using the LOWER of the two endpoints catches it.
    /// </summary>
    [TestMethod]
    public void IsDoorWithinZ_ApexAboveLimitLandingAtFloor_IsWithin()
    {
        Assert.IsTrue(AntiBlinkGeometry.IsDoorWithinZ(0f, 4.3f, 0.1f, 3.5f));
    }

    [TestMethod]
    public void IsDoorWithinZ_StoreyAbove_IsNotWithin()
    {
        Assert.IsFalse(AntiBlinkGeometry.IsDoorWithinZ(6f, 0f, 0.2f, 3.5f));
    }

    [TestMethod]
    public void IsDoorWithinZ_JumpingDownToDoorLevel_IsWithin()
    {
        Assert.IsTrue(AntiBlinkGeometry.IsDoorWithinZ(0f, 5f, 0f, 3.5f));
    }

    /// <summary>
    /// The default limit (2.0) skips a ground-floor door for a player walking on the floor above it.
    /// </summary>
    [TestMethod]
    public void IsDoorWithinZ_UpperFloorAtDefaultLimit_IsNotWithin()
    {
        Assert.IsFalse(AntiBlinkGeometry.IsDoorWithinZ(0f, 2.8f, 2.8f, 2.0f));
    }

    #endregion

    #region IsLegitimatelyOnDoorPlane

    [TestMethod]
    public void IsLegitimatelyOnDoorPlane_ArrivedBeforeClose_IsExempt()
    {
        Assert.IsTrue(AntiBlinkGeometry.IsLegitimatelyOnDoorPlane(1u, 100d, 1u, 105d));
    }

    [TestMethod]
    public void IsLegitimatelyOnDoorPlane_ArrivedAfterClose_IsEnforced()
    {
        Assert.IsFalse(AntiBlinkGeometry.IsLegitimatelyOnDoorPlane(1u, 110d, 1u, 105d));
    }

    [TestMethod]
    public void IsLegitimatelyOnDoorPlane_NeverClosedDoor_IsEnforced()
    {
        Assert.IsFalse(AntiBlinkGeometry.IsLegitimatelyOnDoorPlane(1u, 50d, 1u, 0d));
    }

    [TestMethod]
    public void IsLegitimatelyOnDoorPlane_DifferentDoor_IsEnforced()
    {
        Assert.IsFalse(AntiBlinkGeometry.IsLegitimatelyOnDoorPlane(1u, 50d, 2u, 105d));
    }

    #endregion

    #region NextOnDoorPlaneRecord

    [TestMethod]
    public void NextOnDoorPlaneRecord_NoCandidate_ClearsRecord()
    {
        var next = AntiBlinkGeometry.NextOnDoorPlaneRecord(1u, 100d, 0u, 200d);

        Assert.AreEqual(0u, next.Guid);
    }

    [TestMethod]
    public void NextOnDoorPlaneRecord_NewGuid_StampsNow()
    {
        var next = AntiBlinkGeometry.NextOnDoorPlaneRecord(1u, 100d, 2u, 200d);

        Assert.AreEqual(2u, next.Guid);
        Assert.AreEqual(200d, next.Since, 0.0001d);
    }

    [TestMethod]
    public void NextOnDoorPlaneRecord_SameGuid_KeepsOriginalSince()
    {
        var next = AntiBlinkGeometry.NextOnDoorPlaneRecord(1u, 100d, 1u, 200d);

        Assert.AreEqual(1u, next.Guid);
        Assert.AreEqual(100d, next.Since, 0.0001d);
    }

    /// <summary>
    /// Leaving a door's plane and later coming back re-stamps `since` - it is not the same arrival.
    /// </summary>
    [TestMethod]
    public void NextOnDoorPlaneRecord_ClearThenRearrive_RestampsSince()
    {
        var cleared = AntiBlinkGeometry.NextOnDoorPlaneRecord(1u, 100d, 0u, 150d);
        var reArrived = AntiBlinkGeometry.NextOnDoorPlaneRecord(cleared.Guid, cleared.Since, 1u, 300d);

        Assert.AreEqual(1u, reArrived.Guid);
        Assert.AreEqual(300d, reArrived.Since, 0.0001d);
    }

    #endregion
}
