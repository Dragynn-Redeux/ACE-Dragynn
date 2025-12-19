using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ACE.Entity;
using ACE.Server.Managers;
using Serilog;

namespace ACE.Server.WorldObjects.Managers;

public class ResonanceZoneConfig
{
    public const string ZonesKey = "shroud_zone_entries";
    public const string TeleportCooldownKey = "shroud_zone_teleport_cooldown_seconds";
    public const string ShroudedSwirlMinKey = "shroud_zone_shrouded_swirl_min_seconds";
    public const string ShroudedSwirlMaxKey = "shroud_zone_shrouded_swirl_max_seconds";

    private static readonly ILogger Log = Serilog.Log.ForContext<ResonanceZoneConfig>();
    public TimeSpan ShroudedSwirlMin { get; }
    public TimeSpan ShroudedSwirlMax { get; }

   public ResonanceZoneConfig(
    string rawEntries,
    TimeSpan teleportCooldown,
    TimeSpan shroudedSwirlMin,
    TimeSpan shroudedSwirlMax)
{
    Zones = Parse(rawEntries);
    TeleportCooldown = teleportCooldown;
    ShroudedSwirlMin = shroudedSwirlMin;
    ShroudedSwirlMax = shroudedSwirlMax;
}



    public IReadOnlyList<ResonanceZoneEntry> Zones { get; }

    public TimeSpan TeleportCooldown { get; }


    public static ResonanceZoneConfig FromProperties()
    {
        var rawEntries = PropertyManager.GetString(ZonesKey, string.Empty).Item ?? string.Empty;

        var cooldownSeconds = PropertyManager.GetDouble(TeleportCooldownKey, 30).Item;
        var teleportCooldown = TimeSpan.FromSeconds(cooldownSeconds);
      
        var swirlMin = TimeSpan.FromSeconds(PropertyManager.GetDouble(ShroudedSwirlMinKey, 10).Item);
        var swirlMax = TimeSpan.FromSeconds(PropertyManager.GetDouble(ShroudedSwirlMaxKey, 20).Item);

        return new ResonanceZoneConfig(rawEntries, teleportCooldown, swirlMin, swirlMax);

    }



    private static IReadOnlyList<ResonanceZoneEntry> Parse(string rawEntries)
    {
        var zones = new List<ResonanceZoneEntry>();

        if (string.IsNullOrWhiteSpace(rawEntries))
        {
            return zones;
        }

        var lines = rawEntries
            .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
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

    private static bool TryParseEntry(string line, out ResonanceZoneEntry entry)
    {
        entry = null;

        // Expected format: "<cellHex> [<x> <y> <z>] <qx> <qy> <qz> <qw>|<radius>|<maxDistance>"
        var segments = line.Split('|', StringSplitOptions.TrimEntries);
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

        var name = segments.Length > 3 ? segments[3] : string.Empty;
        var shroudEventKey = segments.Length > 4 ? segments[4] : string.Empty;
        var stormEventKey = segments.Length > 5 ? segments[5] : string.Empty;

        entry = new ResonanceZoneEntry(center, radius, maxDistance, name, shroudEventKey, stormEventKey);


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
        var coords = coordinateSegment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (coords.Length != 3)
        {
            return false;
        }

        if (!TryParseVector3(coords, out var positionVec))
        {
            return false;
        }

        var rotationSegment = trimmed[(endBracket + 1)..].Trim();
        var rotationTokens = rotationSegment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

