using ACE.Entity;

namespace ACE.Server.WorldObjects.Managers;

public class ResonanceZoneEntry
{
    public Position Location { get; }
    public float Radius { get; }
    public float MaxDistance { get; }

    // Friendly identifier (for listing / deleting / readability)
    public string Name { get; }

    // Group gating (event keys)
    public string ShroudEventKey { get; }
    public string StormEventKey { get; }
    
    public ResonanceZoneEntry(
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

