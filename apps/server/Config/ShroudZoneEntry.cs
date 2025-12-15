using ACE.Entity;

namespace ACE.Server.Config;

public enum ShroudZoneKind
{
    ShroudOnly,   // just shroud/teleport logic
    StormOnly,    // just portal-storm logic
    Both          // both systems active
}

public class ShroudZoneEntry
{
    public Position Location { get; }
    public float Radius { get; }
    public float MaxDistance { get; }

    // Friendly identifier (for listing / deleting / readability)
    public string Name { get; }

    // Group gating (event keys)
    public string ShroudEventKey { get; }
    public string StormEventKey { get; }

    // Optional per-zone storm cap override (null => use global ps_cap)
    public int? StormCap { get; }

    public uint Landblock => Location.LandblockId.Raw;

    public ShroudZoneEntry(
        Position location,
        float radius,
        float maxDistance,
        string name,
        string shroudEventKey,
        string stormEventKey,
        int? stormCap)
    {
        Location = location;
        Radius = radius;
        MaxDistance = maxDistance;

        Name = name ?? string.Empty;
        ShroudEventKey = shroudEventKey ?? string.Empty;
        StormEventKey = stormEventKey ?? string.Empty;
        StormCap = stormCap;
    }
}

