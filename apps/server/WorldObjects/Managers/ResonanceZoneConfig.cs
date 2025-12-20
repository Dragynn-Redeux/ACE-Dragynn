using System;
using System.Collections.Generic;
using ACE.Server.Managers;
using ACE.Database;
using System.Linq;
using ACE.Entity;



namespace ACE.Server.WorldObjects.Managers;

public class ResonanceZoneConfig
{
    public const string TeleportCooldownKey = "shroud_zone_teleport_cooldown_seconds";
    public const string ShroudedSwirlMinKey = "shroud_zone_shrouded_swirl_min_seconds";
    public const string ShroudedSwirlMaxKey = "shroud_zone_shrouded_swirl_max_seconds";

    public TimeSpan ShroudedSwirlMin { get; }
    public TimeSpan ShroudedSwirlMax { get; }

 public ResonanceZoneConfig(
    IReadOnlyList<ResonanceZoneEntry> zones,
    TimeSpan teleportCooldown,
    TimeSpan shroudedSwirlMin,
    TimeSpan shroudedSwirlMax)
    {
    Zones = zones ?? Array.Empty<ResonanceZoneEntry>();
    TeleportCooldown = teleportCooldown;
    ShroudedSwirlMin = shroudedSwirlMin;
    ShroudedSwirlMax = shroudedSwirlMax;
    }
    




    public IReadOnlyList<ResonanceZoneEntry> Zones { get; }

    public TimeSpan TeleportCooldown { get; }


    public static ResonanceZoneConfig FromProperties()
    {
        var cooldownSeconds =
            PropertyManager.GetDouble(TeleportCooldownKey, 30).Item;

        var teleportCooldown =
            TimeSpan.FromSeconds(cooldownSeconds);

        var swirlMin =
            TimeSpan.FromSeconds(
                PropertyManager.GetDouble(ShroudedSwirlMinKey, 10).Item);

        var swirlMax =
            TimeSpan.FromSeconds(
                PropertyManager.GetDouble(ShroudedSwirlMaxKey, 20).Item);

        // DB rows (EF entities)
        var rows =
            DatabaseManager.ShardConfig
                .GetResonanceZoneEntriesEnabled();

        // Map DB rows -> runtime zones
        var zones = rows.Select(r => new ResonanceZoneEntry(
            new Position(
                r.CellId,
                r.X, r.Y, r.Z,
                r.Qx, r.Qy, r.Qz, r.Qw),
            r.Radius,
            r.MaxDistance,
            r.Name,
            r.ShroudEventKey,
            r.StormEventKey
        )).ToList();

        return new ResonanceZoneConfig(
            zones,
            teleportCooldown,
            swirlMin,
            swirlMax
        );
    }

}



