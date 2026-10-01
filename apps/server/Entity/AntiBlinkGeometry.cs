using System;
using System.Numerics;

namespace ACE.Server.Entity;

/// <summary>
/// Pure path/door geometry for anti-blink door detection (see the "Anti-blink door detection" region in
/// Player_Tick.cs). Kept out of Player so it can be exercised without Player's static initializers, which
/// need the world database.
///
/// Ported from ACE-DreamWeave (https://github.com/Awful-Waffle-Rofl/ACE-DreamWeave, AGPL-3.0).
/// </summary>
public static class AntiBlinkGeometry
{
    /// <summary>
    /// A landblock is 192 units on a side. Door and player positions are landblock-local, so a path that
    /// crosses a landblock boundary is only comparable once both ends are lifted into global coordinates.
    /// </summary>
    public const float LandblockLength = 192.0f;

    /// <summary>
    /// How close to a door's plane a player has to already be for that door to be ignored, in units.
    /// </summary>
    public const float DoorPlaneTolerance = 0.25f;

    public const float DoorPlaneToleranceSq = DoorPlaneTolerance * DoorPlaneTolerance;

    public static Vector2 GetGlobalPos(ACE.Entity.Position pos)
    {
        return new Vector2(
            pos.LandblockX * LandblockLength + pos.PositionX,
            pos.LandblockY * LandblockLength + pos.PositionY
        );
    }

    /// <summary>
    /// The door's blocking plane as a 2D segment: its width axis, centered on its origin, in global coords.
    /// UnitY transformed by the door's rotation is the direction it faces; the perpendicular of that is the
    /// axis the door spans, and therefore the line a player has to cross to get through it.
    /// </summary>
    public static (Vector2 Start, Vector2 End) GetDoorSegment(ACE.Entity.Position doorPos, float doorWidth)
    {
        var facing = Vector3.Transform(Vector3.UnitY, doorPos.Rotation);

        var facing2d = new Vector2(facing.X, facing.Y);

        // a door lying flat has no vertical plane to cross - no segment to test against
        if (facing2d.LengthSquared() < 1e-6f)
        {
            return (Vector2.Zero, Vector2.Zero);
        }

        facing2d = Vector2.Normalize(facing2d);

        var widthAxis = new Vector2(-facing2d.Y, facing2d.X);
        var center = GetGlobalPos(doorPos);
        var halfWidth = doorWidth * 0.5f;

        return (center - widthAxis * halfWidth, center + widthAxis * halfWidth);
    }

    /// <summary>
    /// The point where segments p1-p2 and p3-p4 cross, or null if they do not.
    /// </summary>
    public static Vector2? GetSegmentIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        var d1 = p2 - p1;
        var d2 = p4 - p3;

        var denominator = d1.X * d2.Y - d1.Y * d2.X;

        // parallel, or one of the segments is degenerate
        if (Math.Abs(denominator) < 1e-10f)
        {
            return null;
        }

        var delta = p3 - p1;

        var t = (delta.X * d2.Y - delta.Y * d2.X) / denominator;
        var u = (delta.X * d1.Y - delta.Y * d1.X) / denominator;

        if (t < 0.0f || t > 1.0f || u < 0.0f || u > 1.0f)
        {
            return null;
        }

        return p1 + d1 * t;
    }

    /// <summary>
    /// Squared distance from a point to the nearest point on segment a-b.
    /// </summary>
    public static float DistanceToSegmentSq(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSq = ab.LengthSquared();

        if (lengthSq < 1e-10f)
        {
            return (point - a).LengthSquared();
        }

        var t = Vector2.Dot(point - a, ab) / lengthSq;

        t = Math.Max(0.0f, Math.Min(1.0f, t));

        return (point - (a + ab * t)).LengthSquared();
    }

    /// <summary>
    /// TRUE when a door's Z is within limit of the LOWER endpoint of the path. Using the lower end keeps a
    /// door on another storey skipped while a jump apex above the limit can no longer hide a door on the
    /// landing segment (the takeoff or landing end of every arc segment is at floor level).
    /// </summary>
    public static bool IsDoorWithinZ(float doorZ, float oldZ, float newZ, float zHeightLimit) =>
        Math.Abs(doorZ - Math.Min(oldZ, newZ)) <= zHeightLimit;

    /// <summary>
    /// A player already on a closed door's plane is exempt from that door only if they got there before it
    /// closed - the "open doorway swung shut on me" case. Arriving after CloseTimestamp means the player walked
    /// onto the plane of a door that was already closed, which only a client with the door deleted can do.
    /// A door never closed since startup has CloseTimestamp 0 and is therefore always enforced.
    /// </summary>
    public static bool IsLegitimatelyOnDoorPlane(
        uint recordedDoorGuid,
        double onPlaneSince,
        uint doorGuid,
        double closeTimestamp
    ) => recordedDoorGuid == doorGuid && onPlaneSince < closeTimestamp;

    /// <summary>
    /// Pure transition for the on-door-plane record: 0 clears it; a new guid stamps `now`; the same guid
    /// leaves `since` untouched so a player who never left the plane keeps their original arrival time.
    /// </summary>
    public static (uint Guid, double Since) NextOnDoorPlaneRecord(
        uint recordedGuid,
        double recordedSince,
        uint candidateGuid,
        double now
    )
    {
        if (candidateGuid == 0)
        {
            return (0, recordedSince);
        }

        if (candidateGuid != recordedGuid)
        {
            return (candidateGuid, now);
        }

        return (recordedGuid, recordedSince);
    }
}
