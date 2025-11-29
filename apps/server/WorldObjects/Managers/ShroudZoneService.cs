using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using ACE.Server.Config;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using ACE.Server.Network.Enum;
using Serilog;

namespace ACE.Server.WorldObjects.Managers;

public class ShroudZoneService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ShroudZoneService>();

    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TeleportCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinShroudedDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxShroudedDelay = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<ObjectGuid, ZonePlayerState> _playerStates = new();
    private readonly IReadOnlyDictionary<uint, List<ShroudZoneEntry>> _zonesByLandblock;

    public ShroudZoneService(ShroudZoneConfig config)
    {
        _zonesByLandblock = BuildLookup(config.Zones);
        Log.Information("Loaded {ZoneCount} shroud zone entries", config.Zones.Count);
    }

    public static ShroudZoneService CreateFromConfig()
    {
        var config = ShroudZoneConfig.FromProperties();
        return new ShroudZoneService(config);
    }

    public bool TryHandlePlayer(Player player)
    {
        if (_zonesByLandblock.Count == 0)
        {
            return false;
        }

        var location = player?.Location;
        if (location == null)
        {
            return false;
        }

        if (!_zonesByLandblock.TryGetValue(location.Landblock, out var zones))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var state = _playerStates.GetOrAdd(player.Guid, _ => new ZonePlayerState());

        foreach (var zone in zones)
        {
            if (!IsInside(location, zone))
            {
                continue;
            }

            if (state.NextAllowedActionUtc > now)
            {
                return false;
            }

            if (player.IsShrouded())
            {
                ScheduleOrTriggerShroudedEffect(player, zone, state, now);
                _playerStates[player.Guid] = state;
                return false;
            }

            ExecuteTeleport(player, zone, now, state);
            _playerStates[player.Guid] = state;
            return true;
        }

        if (state.PendingEffectUtc.HasValue)
        {
            state.PendingEffectUtc = null;
            _playerStates[player.Guid] = state;
        }

        return false;
    }

    private static IReadOnlyDictionary<uint, List<ShroudZoneEntry>> BuildLookup(IReadOnlyList<ShroudZoneEntry> zones)
    {
        var result = new Dictionary<uint, List<ShroudZoneEntry>>();

        foreach (var zone in zones)
        {
            if (!result.TryGetValue(zone.Landblock, out var list))
            {
                list = new List<ShroudZoneEntry>();
                result[zone.Landblock] = list;
            }

            list.Add(zone);
        }

        return result;
    }

    private static bool IsInside(ACE.Entity.Position location, ShroudZoneEntry zone)
    {
        var distanceSq = Vector3.DistanceSquared(location.Pos, zone.Center.Pos);
        return distanceSq <= zone.RadiusSquared;
    }

    private void ScheduleOrTriggerShroudedEffect(Player player, ShroudZoneEntry zone, ZonePlayerState state, DateTime now)
    {
        if (state.PendingEffectUtc.HasValue)
        {
            if (now >= state.PendingEffectUtc.Value)
            {
                player.PlayParticleEffect(PlayScript.PortalEntry, player.Guid);
                state.PendingEffectUtc = null;
                state.NextAllowedActionUtc = now + Cooldown;
                Log.Debug(
                    "Played shrouded zone swirl for {Player} in landblock {Landblock}",
                    player.Name,
                    zone.Landblock
                );
            }

            return;
        }

        var delaySeconds = Random.Shared.NextDouble() * (MaxShroudedDelay - MinShroudedDelay).TotalSeconds
            + MinShroudedDelay.TotalSeconds;
        var delay = TimeSpan.FromSeconds(delaySeconds);

        state.PendingEffectUtc = now + delay;
        state.NextAllowedActionUtc = state.PendingEffectUtc.Value;
        Log.Debug(
            "Scheduled shrouded zone swirl for {Player} in landblock {Landblock} after {Delay} seconds",
            player.Name,
            zone.Landblock,
            delay.TotalSeconds
        );
    }

    private void ExecuteTeleport(Player player, ShroudZoneEntry zone, DateTime now, ZonePlayerState state)
    {
        var destination = BuildDestination(zone, player);

        player.PlayParticleEffect(PlayScript.PortalEntry, player.Guid);
        WorldManager.ThreadSafeTeleport(player, destination);

        state.PendingEffectUtc = null;
        state.NextAllowedActionUtc = now + TeleportCooldown;
        Log.Information(
            "Teleported {Player} away from shroud zone in landblock {Landblock} after entering radius {Radius}",
            player.Name,
            zone.Landblock,
            zone.Radius
        );
    }

    private static ACE.Entity.Position BuildDestination(ShroudZoneEntry zone, Player player)
    {
        var angle = Random.Shared.NextDouble() * Math.Tau;
        var minDistance = zone.Radius;
        var maxDistance = Math.Max(minDistance + 1, zone.MaxDistance);
        var distance = minDistance + (float)(Random.Shared.NextDouble() * (maxDistance - minDistance));

        var offsetX = (float)Math.Cos(angle) * distance;
        var offsetY = (float)Math.Sin(angle) * distance;

        var newPos = new Vector3(zone.Center.PositionX + offsetX, zone.Center.PositionY + offsetY, zone.Center.PositionZ);
        var destination = new ACE.Entity.Position(zone.Center.LandblockId.Raw, newPos, player.Location.Rotation);

        WorldObject.AdjustDungeon(destination);
        return destination;
    }

    private class ZonePlayerState
    {
        public DateTime NextAllowedActionUtc { get; set; }

        public DateTime? PendingEffectUtc { get; set; }
    }
}
