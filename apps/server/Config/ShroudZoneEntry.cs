using ACE.Entity;

namespace ACE.Server.Config;

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
    
    public ShroudZoneEntry(
        Position location,
        float radius,
        float maxDistance,
        string name,
        string shroudEventKey,
        string stormEventKey)
    {
        Location = location;
        Radius = radius;
        MaxDistance = maxDistance;

        Name = name ?? string.Empty;
        ShroudEventKey = shroudEventKey ?? string.Empty;
        StormEventKey = stormEventKey ?? string.Empty;
    }
}

