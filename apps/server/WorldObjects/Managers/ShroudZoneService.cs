using System;
using System.Collections.Generic;
using System.Numerics;
using ACE.Entity;
using Serilog;
using ACE.Server.Config;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using System.Linq;

namespace ACE.Server.WorldObjects.Managers;


public class ShroudZoneService
{
    private static readonly ILogger _log = Log.ForContext(typeof(ShroudZoneService));
    private static readonly Random _random = new();
    // Shroud Zone tuning (live-updated from server properties)
    private double _outerWarnMessageIntervalSeconds;
    private double _shroudedMessageIntervalSeconds;
    private double _outerWarnSwirlIntervalSeconds;
    // PortalStorm tuning (live-updated from server properties)
    private double _psCap;
    private double _psInterval;
    private double _psCooldown;
    private readonly ShroudZoneConfig _config;
    private readonly Dictionary<uint, List<ShroudZoneEntry>> _zonesByLandblock;
    private readonly Dictionary<uint, double> _shroudedNextSwirl = new();
    private readonly Dictionary<uint, double> _nextTeleportAllowed = new();
    private readonly Dictionary<uint, double> _outerWarnNextSwirl = new();
    private readonly Dictionary<uint, double> _shroudedNextMessage = new();
    private readonly Dictionary<uint, double> _outerWarnNextMessage = new();
    private readonly Dictionary<uint, double> _psNextTeleportForLandblock = new();
    private readonly Dictionary<uint, double> _psPlayerNextEligible = new();
    private readonly Dictionary<uint, double> _psNextWarnForLandblock = new();
    private readonly Dictionary<uint, double> _psPendingTeleportAtForLandblock = new();
    private readonly Dictionary<uint, ShroudZoneEntry> _psPendingZoneForLandblock = new();




    public ShroudZoneService(ShroudZoneConfig config)
    {
        _config = config;
        _zonesByLandblock = BuildZonesByLandblock(config.Zones);
        _outerWarnMessageIntervalSeconds =
            PropertyManager.GetDouble("sz_warnmsg", 10).Item;

        _shroudedMessageIntervalSeconds =
            PropertyManager.GetDouble("sz_shroudmsg", 60).Item;

        _outerWarnSwirlIntervalSeconds =
            PropertyManager.GetDouble("sz_warnswirl", 2.5).Item;

        _log.Information(
            "Loaded {Count} shroud zones (WarnMsg={Warn}s, ShroudMsg={Shroud}s, WarnSwirl={Swirl}s)",
            config.Zones.Count,
            _outerWarnMessageIntervalSeconds,
            _shroudedMessageIntervalSeconds,
            _outerWarnSwirlIntervalSeconds
        );

        // PortalStorm tunables (initial read)
        _psCap = PropertyManager.GetDouble("ps_cap", 8).Item;
        _psInterval = PropertyManager.GetDouble("ps_interval", 60).Item;
        _psCooldown = PropertyManager.GetDouble("ps_cooldown", 120).Item;

        _log.Information(
            "PortalStorm config: Cap={Cap}, Interval={Interval}s, Cooldown={Cooldown}s",
            _psCap,
            _psInterval,
            _psCooldown
        );
    }
    private static bool EventIsActive(GameEventState state)
    {
        return state == GameEventState.On || state == GameEventState.Enabled;
    }

    private static bool IsZoneEventActive(ShroudZoneEntry zone)
    {
        // If neither shroud nor storm has an event key, zone is always considered active.
        if (string.IsNullOrWhiteSpace(zone.ShroudEventKey) &&
            string.IsNullOrWhiteSpace(zone.StormEventKey))
        {
            return true;
        }

        // If either event is active, the zone is “on” for at least one effect.
        if (!string.IsNullOrWhiteSpace(zone.ShroudEventKey) &&
            EventIsActive(EventManager.GetEventStatus(zone.ShroudEventKey)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(zone.StormEventKey) &&
            EventIsActive(EventManager.GetEventStatus(zone.StormEventKey)))
        {
            return true;
        }

        return false;
    }

    public void Tick(double currentUnixTime)
    {
        TickPortalStorm(currentUnixTime);
    }

    private void TickPortalStorm(double currentUnixTime)
    {
        // Global on/off
        if (!PropertyManager.GetBool("ps_global", true).Item)
        {
            return;
        }

        // Get online players once
        var online = PlayerManager.GetAllOnline();

        // Bucket players by landblock once
        var byLandblock = new Dictionary<uint, List<Player>>();
        foreach (var p in online)
        {
            var lb = p.Location.Landblock;
            if (!byLandblock.TryGetValue(lb, out var list))
            {
                list = new List<Player>();
                byLandblock[lb] = list;
            }
            list.Add(p);
        }

        // For each landblock that has storm-capable zones
        foreach (var kvp in _zonesByLandblock)
        {
            var landblock = kvp.Key;

            if (!byLandblock.TryGetValue(landblock, out var playersInLb) || playersInLb.Count == 0)
            {
                    continue;
            }

            // Throttle per landblock
            if (_psNextTeleportForLandblock.TryGetValue(landblock, out var nextTime) &&
                currentUnixTime < nextTime)
            {
                    continue;
            }

            foreach (var zone in kvp.Value)
            {
                // Only zones with an active storm event
                if (!IsPortalStormZoneActive(zone))
                {
                    continue;
                }

                var zoneWorld = ToWorld2D(zone.Location);
                var maxDist = zone.MaxDistance > 0 ? zone.MaxDistance : zone.Radius;
                var maxDistSq = maxDist * maxDist;

                int inRegionCount = 0;
                var inRegion = new List<Player>();
                foreach (var p in playersInLb)
                {
                    var d = (ToWorld2D(p.Location) - zoneWorld).LengthSquared();
                    if (d <= maxDistSq)
                    {
                        inRegion.Add(p);
                        inRegionCount++;
                    }
                }

                var cap = (int)PropertyManager.GetDouble("ps_cap", 8).Item;
                if (cap <= 0)
                {
                    continue;
                }

                if (inRegionCount == cap - 1)
                {
                    // warning tier (no teleport)
                    FireStormWarning(zone, landblock, inRegion, currentUnixTime);
                    continue;
                }

                if (inRegionCount < cap)
                {
                    continue;
                }

                // hard cap reached → schedule + then teleport after delay
                var delay = PropertyManager.GetDouble("ps_delay", 2).Item; // seconds

                // If storm pressure drops, cancel pending teleport
                if (inRegionCount < cap)
                {
                    _psPendingTeleportAtForLandblock.Remove(landblock);
                    _psPendingZoneForLandblock.Remove(landblock);
                    continue;
                }

                // Schedule if not already pending
                if (!_psPendingTeleportAtForLandblock.TryGetValue(landblock, out var fireAt) ||
                    !_psPendingZoneForLandblock.TryGetValue(landblock, out var pendingZone) ||
                    pendingZone != zone)
                {
                    _psPendingTeleportAtForLandblock[landblock] = currentUnixTime + delay;
                    _psPendingZoneForLandblock[landblock] = zone;

                    // Optional: one-time “storm about to snap” feedback at scheduling time
                    foreach (var p in inRegion)
                    {
                        p.PlayParticleEffect(PlayScript.PortalStorm, p.Guid);
                    }

                    break; // only one landblock storm per tick
                }

                // Fire when delay expires
                if (currentUnixTime >= fireAt)
                {
                    FireStormOnce(zone, landblock, inRegion, currentUnixTime);

                    _psPendingTeleportAtForLandblock.Remove(landblock);
                    _psPendingZoneForLandblock.Remove(landblock);

                    var interval = PropertyManager.GetDouble("ps_interval", 60).Item;
                    _psNextTeleportForLandblock[landblock] = currentUnixTime + interval;

                    break;
                }

                // still waiting
                break;

            }
        }
    }
    private void FireStormWarning(
        ShroudZoneEntry zone,
        uint landblock,
        List<Player> playersInRegion,
        double currentUnixTime)
    {
        // throttle warning per landblock 
        if (_psNextWarnForLandblock.TryGetValue(landblock, out var nextWarn) &&
            currentUnixTime < nextWarn)
        {
            return;
        }

        foreach (var p in playersInRegion)
        {
            p.PlayParticleEffect(PlayScript.PortalStorm, p.Guid);
            p.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "A rising pull gathers around you, tugging at your center as if trying to draw you into a drifting current.",
                    ChatMessageType.System
                )
            );
        }

        // small cooldown so it doesn’t spam every tick
        _psNextWarnForLandblock[landblock] = currentUnixTime + 5;
    }

    private void FireStormOnce(
        ShroudZoneEntry zone,
        uint landblock,
        List<Player> playersInRegion,
        double currentUnixTime)
    {
        // Tunables (live)
        var cooldown = PropertyManager.GetDouble("ps_cooldown", 120).Item;

        // pick eligible players (per-player cooldown)
        var eligible = new List<Player>();
        foreach (var p in playersInRegion)
        {
            var id = p.Guid.Full;
            if (!_psPlayerNextEligible.TryGetValue(id, out var nextOk) || currentUnixTime >= nextOk)
            {
                eligible.Add(p);
            }
        }

        if (eligible.Count == 0)
            {
                return;
            }

        var target = eligible[_random.Next(eligible.Count)];

        // message to everyone in-region (once per storm fire)
        foreach (var p in playersInRegion)
        {
            p.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "The portal field fractures into a stormfront around you. Currents surge, searching for something to tear loose.",
                    ChatMessageType.System
                )
            );
        }

        // teleport chosen player
        target.PlayParticleEffect(PlayScript.PortalStorm, target.Guid);
        var destination = BuildDestination(zone, target);
        WorldManager.ThreadSafeTeleport(target, destination);

        // per-player eligibility cooldown
        _psPlayerNextEligible[target.Guid.Full] = currentUnixTime + cooldown;
    }

    private static Dictionary<uint, List<ShroudZoneEntry>> BuildZonesByLandblock(
        IReadOnlyList<ShroudZoneEntry> zones)
    {
        var result = new Dictionary<uint, List<ShroudZoneEntry>>();

        const float landblockSize = 192.0f; // local X/Y run 0..192 inside each block

        foreach (var zone in zones)
        {
            var lb = zone.Location.Landblock;

            // Decode landblock into grid coords: hi byte = X, low byte = Y
            var blockX = (int)((lb >> 8) & 0xFF);
            var blockY = (int)(lb & 0xFF);

            // Global (world) coordinates of the zone center
            var centerX = blockX * landblockSize + zone.Location.PositionX;
            var centerY = blockY * landblockSize + zone.Location.PositionY;

            // Use maxDistance as the outer relevant radius; fall back to Radius if needed
            var effectRadius = zone.MaxDistance > 0 ? zone.MaxDistance : zone.Radius;

            // If radius is bad, just register it in its own landblock
            if (effectRadius <= 0)
            {
                AddZoneToBlock(result, lb, zone);
                continue;
            }

            // Figure out which block indices the circle touches
            var minBlockX = (int)Math.Floor((centerX - effectRadius) / landblockSize);
            var maxBlockX = (int)Math.Floor((centerX + effectRadius) / landblockSize);
            var minBlockY = (int)Math.Floor((centerY - effectRadius) / landblockSize);
            var maxBlockY = (int)Math.Floor((centerY + effectRadius) / landblockSize);

            for (var bx = minBlockX; bx <= maxBlockX; bx++)
            {
                if (bx < 0 || bx > 0xFF)
                {
                    continue;
                }

                for (var by = minBlockY; by <= maxBlockY; by++)
                {
                    if (by < 0 || by > 0xFF)
                    {
                        continue;
                    }

                    var landblockId = (uint)((bx << 8) | by);
                    AddZoneToBlock(result, landblockId, zone);
                }
            }
        }

        return result;
    }

    private static void AddZoneToBlock(
        Dictionary<uint, List<ShroudZoneEntry>> dict,
        uint landblockId,
        ShroudZoneEntry zone
    )
    {
        if (!dict.TryGetValue(landblockId, out var list))
        {
            list = new List<ShroudZoneEntry>();
            dict[landblockId] = list;
        }

        list.Add(zone);
    }
    private static System.Numerics.Vector2 ToWorld2D(Position pos)
    {
        const float landblockSize = 192.0f;

        var lb = pos.Landblock;

        var blockX = (int)((lb >> 8) & 0xFF);
        var blockY = (int)(lb & 0xFF);

        var worldX = blockX * landblockSize + pos.PositionX;
        var worldY = blockY * landblockSize + pos.PositionY;

        return new System.Numerics.Vector2(worldX, worldY);
    }
    private static bool IsEventRunning(string key)
    {
        // No key → treat as “no event gate”; let globals decide.
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        var state = EventManager.GetEventStatus(key);
        return EventIsActive(state);   // On or Enabled
    }



        private static bool IsShroudZoneActive(ShroudZoneEntry zone)
    {
        if (!PropertyManager.GetBool("sz_global", true).Item)
        {
            return false;
        }

        // Empty key = no shroud effect on this zone
        if (string.IsNullOrWhiteSpace(zone.ShroudEventKey))
        {
            return false;
        }

        return IsEventRunning(zone.ShroudEventKey);
    }

    private static bool IsPortalStormZoneActive(ShroudZoneEntry zone)
    {
        if (!PropertyManager.GetBool("ps_global", true).Item)
        {
            return false;
        }

        // Empty key = no portal storm effect on this zone
        if (string.IsNullOrWhiteSpace(zone.StormEventKey))
        {
            return false;
        }

        return IsEventRunning(zone.StormEventKey);
    }



    public static ShroudZoneService CreateFromConfig()
    {
        var config = ShroudZoneConfig.FromProperties();
        return new ShroudZoneService(config);
    }
    public void TryHandlePlayer(Player player, double currentUnixTime)
    {
        var hasZones = _zonesByLandblock.TryGetValue(player.Location.Landblock, out var zones);
        if (!hasZones || zones.Count == 0)
        {
            ClearAllStateFor(player);
            return;
        }

        var playerWorld = ToWorld2D(player.Location);

        ShroudZoneEntry chosenZone = null;
        float chosenDistSq = float.MaxValue;

        // track overlaps (eligible zones only)
        List<(string name, float distSq)> overlaps = null;

        foreach (var zone in zones)
        {
            if (!IsZoneEventActive(zone))
            {
                _log.Information("Zone skipped (event inactive): player={Player} zone={Zone}", player.Name, zone.Name);
                continue;
            }

            var zoneWorld = ToWorld2D(zone.Location);
            var diff = playerWorld - zoneWorld;
            var distanceSq = diff.LengthSquared();

            var innerRadiusSq = zone.Radius * zone.Radius;

            // IMPORTANT: if MaxDistance is 0/unset, fall back to Radius
            var maxDist = (zone.MaxDistance > 0) ? zone.MaxDistance : zone.Radius;
            var maxDistSq = maxDist * maxDist;

            if (distanceSq > maxDistSq)
                {
                    continue; // not eligible
                }
            // eligible zone (within max distance + event active)
            if (chosenZone == null || distanceSq < chosenDistSq)
            {
                chosenZone = zone;
                chosenDistSq = distanceSq;
            }

            overlaps ??= new List<(string, float)>();
            overlaps.Add((zone.Name, distanceSq));
        }

        if (chosenZone == null)
        {
            // In same landblock but outside all zones
            ClearAllStateFor(player);
            return;
        }

        // Admin warning if overlap/misconfig: more than one eligible zone
        if (overlaps != null && overlaps.Count > 1)
        {
            // TODO: if you already have an admin/audit rate limiter, use it here.
            // Keep it simple for now; you can add a per-player cooldown dict later.
            _log.Warning(
                "SHROUD ZONE OVERLAP: player={Player} lb={Landblock:X4} eligibleZones={Zones}",
                player.Name,
                player.Location.Landblock,
                string.Join(", ", overlaps.OrderBy(z => z.distSq).Select(z => $"{z.name}(dist2={z.distSq:0.##})"))
            );
        }

        // Existing behavior, applied to chosen zone only
        var chosenInnerSq = chosenZone.Radius * chosenZone.Radius;
        var insideInner = chosenDistSq <= chosenInnerSq;

        var shroudActive = IsShroudZoneActive(chosenZone);
        var portalStormActive = IsPortalStormZoneActive(chosenZone);

        _log.Information(
            "ShroudZoneCheck: player={Player} zone={Zone} lb={Landblock:X4} dist2={DistSq} inner2={InnerSq} max2={MaxSq} shroudActive={Shroud} portalActive={Portal}",
            player.Name,
            chosenZone.Name,
            player.Location.Landblock,
            chosenDistSq,
            chosenInnerSq,
            ((chosenZone.MaxDistance > 0 ? chosenZone.MaxDistance : chosenZone.Radius) *
            (chosenZone.MaxDistance > 0 ? chosenZone.MaxDistance : chosenZone.Radius)),
            shroudActive,
            portalStormActive
        );

        var isShrouded = player.IsShrouded();
        
        if (isShrouded)
        {
            if (shroudActive)
            {
                    HandleShroudedPlayer(player, currentUnixTime);
            }        
        }
        else
        {
            if (shroudActive)
            {
                HandleOuterWarning(player, currentUnixTime);

                if (insideInner)
                {   
                     HandleTeleport(player, chosenZone, currentUnixTime);
                }

            }
        }
    }


    private void HandleTeleport(Player player, ShroudZoneEntry zone, double currentUnixTime)
    {
        var guid = player.Guid.Full;

        if (_nextTeleportAllowed.TryGetValue(guid, out var nextTeleport) &&
            currentUnixTime < nextTeleport)
        {
            return;
        }

        // final warning swirl + message
        player.PlayParticleEffect(PlayScript.PortalStorm, player.Guid);
        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                "The pull snaps tight. A cold surge seizes your balance, dragging at your core as the resonance rejects you. There is nothing shielding you from it. You are swept away before you can steady yourself.",
                ChatMessageType.System
            )
        );

        var destination = BuildDestination(zone, player);
        WorldManager.ThreadSafeTeleport(player, destination);

        _nextTeleportAllowed[guid] = currentUnixTime + _config.TeleportCooldown.TotalSeconds;

        _shroudedNextSwirl.Remove(guid);
        _outerWarnNextSwirl.Remove(guid);
    }

    private void ClearAllStateFor(Player player)
    {
        var guid = player.Guid.Full;
        _shroudedNextSwirl.Remove(guid);
        _nextTeleportAllowed.Remove(guid);
        _outerWarnNextSwirl.Remove(guid);
        _shroudedNextMessage.Remove(guid);
        _outerWarnNextMessage.Remove(guid);
    }

    private void HandleShroudedPlayer(Player player, double currentUnixTime)
    {
        var guid = player.Guid.Full;

        if (!_shroudedNextSwirl.TryGetValue(guid, out var nextSwirl))
        {
            // First time inside as shrouded: swirl immediately
            player.PlayParticleEffect(PlayScript.PortalStorm, player.Guid);

            // Message immediately the first time, then rate-limited
            if (!_shroudedNextMessage.TryGetValue(guid, out var nextMsg) ||
                currentUnixTime >= nextMsg)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "A faint current slides across you, testing your presence. It catches for a moment, then softens, as if something around you steadies the flow and keeps you grounded.",
                        ChatMessageType.System
                    )
                );

                _shroudedNextMessage[guid] = currentUnixTime + GetShroudedMessageInterval();
            }

            _shroudedNextSwirl[guid] = currentUnixTime + NextSwirlDelay();
            return;
        }

        if (currentUnixTime < nextSwirl)
        {
            return;
        }

        // Time for next shrouded swirl
        player.PlayParticleEffect(PlayScript.PortalStorm, player.Guid);

        if (!_shroudedNextMessage.TryGetValue(guid, out var nextMessageTime) ||
            currentUnixTime >= nextMessageTime)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "A faint current slides across you, testing your presence. It catches for a moment, then softens, as if something around you steadies the flow and keeps you grounded.",
                    ChatMessageType.System
                )
            );

            _shroudedNextMessage[guid] = currentUnixTime + GetShroudedMessageInterval();
        }

        _shroudedNextSwirl[guid] = currentUnixTime + NextSwirlDelay();
    }
    private void HandleUnshroudedPlayer(
        Player player,
        ShroudZoneEntry zone,
        double currentUnixTime,
        bool insideInner)
    {
        var guid = player.Guid.Full;

        if (insideInner)
        {
            // Inner radius → actual teleport, with cooldown
            if (_nextTeleportAllowed.TryGetValue(guid, out var nextTeleport) &&
                currentUnixTime < nextTeleport)
            {
                return;
            }

            // Final swirl before teleport
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "The pull snaps tight. A cold surge seizes your balance, dragging at your core as the resonance rejects you. There is nothing shielding you from it. You are swept away before you can steady yourself.",
                    ChatMessageType.System
                )
            );


            var destination = BuildDestination(zone, player);
            WorldManager.ThreadSafeTeleport(player, destination);

            _nextTeleportAllowed[guid] = currentUnixTime + _config.TeleportCooldown.TotalSeconds;

            // Clear any pending outer / shrouded state once we teleport
            _shroudedNextSwirl.Remove(guid);
            _outerWarnNextSwirl.Remove(guid);
            return;
        }

        // Outer ring: warning swirls only (no teleport yet)
        HandleOuterWarning(player, currentUnixTime);
    }
    private void HandleOuterWarning(Player player, double currentUnixTime)
    {
        var guid = player.Guid.Full;

        if (!_outerWarnNextSwirl.TryGetValue(guid, out var nextSwirl))
        {
            // First time inside outer radius: swirl + warning immediately
            player.PlayParticleEffect(PlayScript.PortalStorm, player.Guid);
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "A rising pull gathers around you, tugging at your center as if trying to draw you into a drifting current. The pressure sharpens, and you feel moments away from being pulled away.",
                    ChatMessageType.System
                )
            );

            _outerWarnNextSwirl[guid]   = currentUnixTime + NextOuterWarnDelay();
            _outerWarnNextMessage[guid] = currentUnixTime + GetOuterWarnMessageInterval();
            return;
        }

        if (currentUnixTime < nextSwirl)
        {
            return;
        }

        // Time for next outer warning swirl
        player.PlayParticleEffect(PlayScript.PortalStorm, player.Guid);

        // Only repeat the chat line if the message cooldown has expired
        if (!_outerWarnNextMessage.TryGetValue(guid, out var nextMsg) ||
            currentUnixTime >= nextMsg)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "A rising pull gathers around you, tugging at your center as if trying to draw you into a drifting current. The pressure sharpens, and you feel moments away from being pulled away.",
                    ChatMessageType.System
                )
            );

            _outerWarnNextMessage[guid] = currentUnixTime + GetOuterWarnMessageInterval();
        }

        _outerWarnNextSwirl[guid] = currentUnixTime + NextOuterWarnDelay();
    }
    private double NextOuterWarnDelay()
    {
        return GetOuterWarnSwirlInterval();
    }
    private Position BuildDestination(ShroudZoneEntry zone, Player player)
    {
        var angle = _random.NextDouble() * Math.PI * 2;
        var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));

        var minDistance = _config.Radius + 5f;
        var maxDistance = Math.Max(_config.EjectionDistance, minDistance + 5f);
        var distance = (float)(_random.NextDouble() * (maxDistance - minDistance) + minDistance);

        var offset = direction * distance;

        // Keep player's current rotation so they arrive upright
        return new Position(
            zone.Location.LandblockId.Raw,
            zone.Location.PositionX + offset.X,
            zone.Location.PositionY + offset.Y,
            zone.Location.PositionZ,
            player.Location.RotationX,
            player.Location.RotationY,
            player.Location.RotationZ,
            player.Location.RotationW
        );
    }
    private double NextSwirlDelay()
    {
        var rangeSeconds = _config.ShroudedSwirlMax.TotalSeconds - _config.ShroudedSwirlMin.TotalSeconds;
        var offset = _random.NextDouble() * Math.Max(rangeSeconds, 0);
        return _config.ShroudedSwirlMin.TotalSeconds + offset;
    }
    private double GetOuterWarnMessageInterval()
    {
        _outerWarnMessageIntervalSeconds =
            PropertyManager.GetDouble("sz_warnmsg", _outerWarnMessageIntervalSeconds).Item;
        return _outerWarnMessageIntervalSeconds;
    }

    private double GetShroudedMessageInterval()
    {
        _shroudedMessageIntervalSeconds =
            PropertyManager.GetDouble("sz_shroudmsg", _shroudedMessageIntervalSeconds).Item;
        return _shroudedMessageIntervalSeconds;
    }

    private double GetOuterWarnSwirlInterval()
    {
        _outerWarnSwirlIntervalSeconds =
            PropertyManager.GetDouble("sz_warnswirl", _outerWarnSwirlIntervalSeconds).Item;
        return _outerWarnSwirlIntervalSeconds;
    }
    private double GetPortalStormInterval()
    {
        _psInterval = PropertyManager.GetDouble("ps_interval", _psInterval).Item;
        return _psInterval;
    }

    private double GetPortalStormCooldown()
    {
        _psCooldown = PropertyManager.GetDouble("ps_cooldown", _psCooldown).Item;
        return _psCooldown;
    }
}