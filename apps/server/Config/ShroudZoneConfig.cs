using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ACE.Entity;
using ACE.Server.Managers;
using Serilog;

namespace ACE.Server.Config;

public class ShroudZoneConfig
{
    public const string PropertyKey = "shroud_zone_entries";

    private static readonly ILogger Log = Serilog.Log.ForContext<ShroudZoneConfig>();

    public ShroudZoneConfig(string rawEntries)
    {
        Zones = Parse(rawEntries);
    }

    public IReadOnlyList<ShroudZoneEntry> Zones { get; }

    public static ShroudZoneConfig FromProperties()
    {
        var property = PropertyManager.GetString(PropertyKey, string.Empty);
        return new ShroudZoneConfig(property.Item);
    }

    private static IReadOnlyList<ShroudZoneEntry> Parse(string rawEntries)
    {
        var zones = new List<ShroudZoneEntry>();

        if (string.IsNullOrWhiteSpace(rawEntries))
        {
            return zones;
        }

        var lines = rawEntries
            .Split(new[] { '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        foreach (var line in lines)
        {
            if (TryParseEntry(line, out var entry))
            {
                zones.Add(entry);
            }
        }

        return zones;
    }

    private static bool TryParseEntry(string line, out ShroudZoneEntry entry)
    {
        entry = null;

        // Expected format: "<cellHex> [<x> <y> <z>] <qx> <qy> <qz> <qw>|<radius>|<maxDistance>"
        var segments = line.Split('|', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (segments.Length < 3)
        {
            Log.Warning("Unable to parse shroud zone entry (missing segments): {Entry}", line);
            return false;
        }

        if (!TryParsePosition(segments[0], out var center))
        {
            Log.Warning("Unable to parse shroud zone position: {Entry}", line);
            return false;
        }

        if (!float.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) || radius <= 0)
        {
            Log.Warning("Unable to parse shroud zone radius: {Entry}", line);
            return false;
        }

        if (!float.TryParse(segments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDistance)
            || maxDistance <= 0)
        {
            Log.Warning("Unable to parse shroud zone ejection distance: {Entry}", line);
            return false;
        }

        entry = new ShroudZoneEntry(center, radius, maxDistance);
        return true;
    }

    private static bool TryParsePosition(string raw, out Position position)
    {
        position = null;

        var trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return false;
        }

        var cellToken = trimmed[..firstSpace];
        uint cellId;
        if (cellToken.StartsWith("0x"))
        {
            if (!uint.TryParse(cellToken[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cellId))
            {
                return false;
            }
        }
        else if (!uint.TryParse(cellToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out cellId))
        {
            return false;
        }

        var startBracket = trimmed.IndexOf('[', firstSpace);
        var endBracket = trimmed.IndexOf(']', startBracket + 1);
        if (startBracket < 0 || endBracket < 0)
        {
            return false;
        }

        var coordinateSegment = trimmed.Substring(startBracket + 1, endBracket - startBracket - 1);
        var coords = coordinateSegment.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (coords.Length != 3)
        {
            return false;
        }

        if (!TryParseVector3(coords, out var positionVec))
        {
            return false;
        }

        var rotationSegment = trimmed[(endBracket + 1)..].Trim();
        var rotationTokens = rotationSegment.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (rotationTokens.Length < 4)
        {
            return false;
        }

        if (!TryParseQuaternion(rotationTokens, out var rotation))
        {
            return false;
        }

        position = new Position(cellId, positionVec, rotation);
        return true;
    }

    private static bool TryParseVector3(string[] tokens, out Vector3 vector)
    {
        vector = Vector3.Zero;
        if (
            tokens.Length != 3
            || !float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
        )
        {
            return false;
        }

        vector = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseQuaternion(string[] tokens, out Quaternion quaternion)
    {
        quaternion = Quaternion.Identity;
        if (
            tokens.Length < 4
            || !float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var qx)
            || !float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var qy)
            || !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var qz)
            || !float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var qw)
        )
        {
            return false;
        }

        quaternion = new Quaternion(qx, qy, qz, qw);
        return true;
    }
}

public class ShroudZoneEntry
{
    public ShroudZoneEntry(Position center, float radius, float maxDistance)
    {
        Center = center;
        Radius = radius;
        MaxDistance = maxDistance;
        Landblock = center.Landblock;
        RadiusSquared = radius * radius;
    }

    public Position Center { get; }

    public float Radius { get; }

    public float MaxDistance { get; }

    public uint Landblock { get; }

    public float RadiusSquared { get; }
}
