using System;
using System.Collections.Generic;
using System.Numerics;
using ACE.Entity;
using Serilog;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using System.Linq;

namespace ACE.Server.WorldObjects.Managers;


public class ResonanceZoneService
{
    private static readonly ILogger _log = Log.ForContext(typeof(ResonanceZoneService));
    private static readonly Random _random = new();
    // Shroud Zone tuning (live-updated from server properties)
    private double _outerWarnMessageIntervalSeconds;
    private double _shroudedMessageIntervalSeconds;
    private double _outerWarnSwirlIntervalSeconds;
    // PortalStorm tuning (live-updated from server properties)
    private readonly ResonanceZoneConfig _config;
    private readonly Dictionary<uint, List<ResonanceZoneEntry>> _zonesByLandblock;
    private readonly Dictionary<uint, double> _shroudedNextSwirl = new();
    private readonly Dictionary<uint, double> _nextTeleportAllowed = new();
    private readonly Dictionary<uint, double> _outerWarnNextSwirl = new();
    private readonly Dictionary<uint, double> _shroudedNextMessage = new();
    private readonly Dictionary<uint, double> _outerWarnNextMessage = new();
    private readonly Dictionary<uint, double> _psNextTeleportForLandblock = new();
    private readonly Dictionary<uint, double> _psPlayerNextEligible = new();
    private readonly Dictionary<uint, double> _psPendingTeleportAtForLandblock = new();
    private readonly Dictionary<uint, ResonanceZoneEntry> _psPendingZoneForLandblock = new();
    // Portal Storm warning throttles
    private readonly Dictionary<uint, double> _psWarnNextSwirlForPlayer = new();
    private readonly Dictionary<uint, double> _psWarnNextMessageForPlayer = new();
    // Attackable-off (admin) hint throttles
    private readonly Dictionary<uint, double> _psImmuneNextMessageForPlayer = new();
    private readonly Dictionary<uint, double> _shroudImmuneNextMessageForPlayer = new();
    // Prune per-player PortalStorm eligibility cache periodically
    private double _psNextEligibilityPruneAt;
    private const double PsEligibilityPruneIntervalSeconds = 300; // 5 minutes
    private const double ImmuneMsgIntervalSeconds = 30.0;


    private void LogZoneOverlapsAtLoad()
    {
        var overlapPairs = 0;
        var landblocksWithOverlaps = new HashSet<uint>();
        var examples = new List<string>();
        const int maxExamples = 10;

        foreach (var kvp in _zonesByLandblock)
        {
            var landblock = kvp.Key;
            var zones = kvp.Value;

            if (zones == null || zones.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < zones.Count - 1; i++)
            {
                var a = zones[i];
                var aOuter = a.MaxDistance > 0 ? a.MaxDistance : a.Radius;
                var aPos = ToWorld2D(a.Location);

                for (var j = i + 1; j < zones.Count; j++)
                {
                    var b = zones[j];
                    var bOuter = b.MaxDistance > 0 ? b.MaxDistance : b.Radius;
                    var bPos = ToWorld2D(b.Location);

                    var delta = aPos - bPos;
                    var distSq = delta.LengthSquared();
                    var outerSum = aOuter + bOuter;

                    if (distSq > outerSum * outerSum)
                    {
                        continue;
                    }

                    overlapPairs++;
                    landblocksWithOverlaps.Add(landblock);

                    if (examples.Count < maxExamples)
                    {
                        var dist = MathF.Sqrt(distSq);
                        examples.Add(
                            $"lb={landblock:X4} '{a.Name}'[{a.Radius:0.#}/{aOuter:0.#}] <-> " +
                            $"'{b.Name}'[{b.Radius:0.#}/{bOuter:0.#}] dist={dist:0.##}"
                        );
                    }
                }
            }
        }

        if (overlapPairs == 0)
        {
            _log.Information("ResonanceZones overlap check: none detected.");
            return;
        }

        _log.Warning(
            "ResonanceZones overlap check: pairs={Pairs}, landblocks={Landblocks}. Examples: {Examples}",
            overlapPairs,
            landblocksWithOverlaps.Count,
            string.Join(" | ", examples)
        );
    }
    public ResonanceZoneService(ResonanceZoneConfig config)
    {
        _config = config;
        _zonesByLandblock = BuildZonesByLandblock(config.Zones);
        _outerWarnMessageIntervalSeconds =
            PropertyManager.GetDouble("sz_warnmsg", 10).Item;

        _shroudedMessageIntervalSeconds =
            PropertyManager.GetDouble("sz_shroudmsg", 60).Item;

        _outerWarnSwirlIntervalSeconds =
            PropertyManager.GetDouble("sz_warnswirl", 2.5).Item;

        // Summarize what was actually loaded (NOTE: config.Zones are ENABLED rows only right now)
        var totalZones = config.Zones.Count;

        var shroudKeyZones = 0;
        var stormKeyZones = 0;
        var ungatedZones = 0;   // neither key set → always "zone active" per IsZoneEventActive

        foreach (var z in config.Zones)
        {
            if (!string.IsNullOrWhiteSpace(z.ShroudEventKey))
            {
                shroudKeyZones++;
            }

            if (!string.IsNullOrWhiteSpace(z.StormEventKey))
            {
                stormKeyZones++;
            }

            if (string.IsNullOrWhiteSpace(z.ShroudEventKey) && string.IsNullOrWhiteSpace(z.StormEventKey))
            {
                ungatedZones++;
            }
        }

        _log.Information(
            "ResonanceZones loaded: zones={Zones}, shroudKey={ShroudKey}, stormKey={StormKey}, ungated={Ungated} " +
            "(WarnMsg={Warn}s, ShroudMsg={Shroud}s, WarnSwirl={Swirl}s, TeleportCooldown={TpCd}s, ShroudedSwirl={SwirlMin}-{SwirlMax}s)",
            totalZones,
            shroudKeyZones,
            stormKeyZones,
            ungatedZones,
            _outerWarnMessageIntervalSeconds,
            _shroudedMessageIntervalSeconds,
            _outerWarnSwirlIntervalSeconds,
            _config.TeleportCooldown.TotalSeconds,
            _config.ShroudedSwirlMin.TotalSeconds,
            _config.ShroudedSwirlMax.TotalSeconds
        );

        // Conservative warning: zones with no event keys are selection-eligible but inert
        var unflagged = config.Zones
            .Where(z => string.IsNullOrWhiteSpace(z.ShroudEventKey) &&
                        string.IsNullOrWhiteSpace(z.StormEventKey))
            .ToList();

        if (unflagged.Count > 0)
        {
            var examples = unflagged
                .Take(10)
                .Select(z =>
                    $"{z.Name}@{z.Location.LandblockId.Raw:X4} " +
                    $"({z.Location.PositionX:0.#},{z.Location.PositionY:0.#},{z.Location.PositionZ:0.#})")
                .ToArray();

            _log.Warning(
                "ResonanceZones: {Count} zone(s) have no shroud/storm event keys (selection-eligible but inert). Examples: {Examples}",
                unflagged.Count,
                string.Join(" | ", examples)
            );
        }

        // PortalStorm summary (keep it, but include the global toggle too)
        _log.Information(
            "PortalStorm config: Global={Global}, Cap={Cap}, Interval={Interval}s, Cooldown={Cooldown}s",
            PropertyManager.GetBool("ps_global", true).Item,
            PropertyManager.GetDouble("ps_cap", 25).Item,
            PropertyManager.GetDouble("ps_interval", 60).Item,
            PropertyManager.GetDouble("ps_cooldown", 120).Item
        );

        // Overlap reporting at load-time (not per-player tick)
        LogZoneOverlapsAtLoad();

    }
    private static bool EventIsActive(GameEventState state)
    {
        return state == GameEventState.On || state == GameEventState.Enabled;
    }

    private static bool IsZoneEventActive(ResonanceZoneEntry zone)
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
        
        if (currentUnixTime >= _psNextEligibilityPruneAt)
        {
            PruneTransientState(online, currentUnixTime);
            _psNextEligibilityPruneAt = currentUnixTime + PsEligibilityPruneIntervalSeconds;
        }

        // Bucket players by landblock once (split eligible vs immune by Attackable)
        var eligibleByLandblock = new Dictionary<uint, List<Player>>();
        var immuneByLandblock = new Dictionary<uint, List<Player>>();

        foreach (var p in online)
        {
            var lb = p.Location.Landblock;

            var dict = p.Attackable ? eligibleByLandblock : immuneByLandblock;

            if (!dict.TryGetValue(lb, out var list))
            {
                list = new List<Player>();
                dict[lb] = list;
            }

            list.Add(p);
        }

        // For each landblock that has storm-capable zones
        foreach (var kvp in _zonesByLandblock)
        {
            var landblock = kvp.Key;

            if (!eligibleByLandblock.TryGetValue(landblock, out var playersInLb) || playersInLb.Count == 0)
            {
                    // cancel pending if nobody is here anymore
                    _psPendingTeleportAtForLandblock.Remove(landblock);
                    _psPendingZoneForLandblock.Remove(landblock);
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

                var inRegion = new List<Player>();
                foreach (var p in playersInLb)
                {
                    var d = (ToWorld2D(p.Location) - zoneWorld).LengthSquared();
                    if (d <= maxDistSq)
                    {
                        inRegion.Add(p);
                    }
                }

                var cap = (int)PropertyManager.GetDouble("ps_cap", 8).Item;
                if (cap <= 0)
                {    
                    continue;
                }

                var inRegionCount = inRegion.Count;

                // Warning should still occur even during landblock cooldown
                if (inRegionCount >= cap - 1 && inRegionCount > 0)
                {
            // Notify Attackable-off players that they'd be affected (immune), throttled
            if (immuneByLandblock.TryGetValue(landblock, out var immuneInLb) && immuneInLb.Count > 0)
            {
                foreach (var p in immuneInLb)
                {
                    var d = (ToWorld2D(p.Location) - zoneWorld).LengthSquared();
                    if (d > maxDistSq)
                    {
                        continue;
                    }

                    var id = p.Guid.Full;

                    if (!_psImmuneNextMessageForPlayer.TryGetValue(id, out var nextAt) || currentUnixTime >= nextAt)
                    {
                        p.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                "A portal storm builds nearby. You would be affected, but your Attackable state is OFF.",
                                ChatMessageType.System
                            )
                        );
                        _psImmuneNextMessageForPlayer[id] = currentUnixTime + ImmuneMsgIntervalSeconds;
                    }
                }
            }

                    FireStormWarning(zone, inRegion, currentUnixTime);
                }


                // If we have a pending teleport for this landblock, but pressure dropped, cancel it.
                if (_psPendingTeleportAtForLandblock.ContainsKey(landblock) && inRegionCount < cap)
                {
                    _psPendingTeleportAtForLandblock.Remove(landblock);
                    _psPendingZoneForLandblock.Remove(landblock);
                    continue;
                }

                if (inRegionCount < cap)
                {    
                    continue;
                }

                // Throttle teleport per landblock (DO NOT throttle warnings)
                if (_psNextTeleportForLandblock.TryGetValue(landblock, out var nextTime) &&
                    currentUnixTime < nextTime)
                {    
                    continue;
                }

                // hard cap reached → schedule + then teleport after delay
                var delay = PropertyManager.GetDouble("ps_delay", 2).Item;

                if (!_psPendingTeleportAtForLandblock.TryGetValue(landblock, out var fireAt))
                {
                    _psPendingTeleportAtForLandblock[landblock] = currentUnixTime + delay;
                    _psPendingZoneForLandblock[landblock] = zone;

                    foreach (var p in inRegion)
                    {
                        p.PlayParticleEffect(PlayScript.PortalStorm, p.Guid);
                    }

                    break; // stop after scheduling for this landblock
                }

                // Not ready to fire yet
                if (currentUnixTime < fireAt)
                {    
                    continue;
                }

                // Fire now
                if (!_psPendingZoneForLandblock.TryGetValue(landblock, out var pendingZone))
                {
                    pendingZone = zone;
                }

                FireStormOnce(pendingZone, landblock, inRegion, currentUnixTime);

                _psPendingTeleportAtForLandblock.Remove(landblock);
                _psPendingZoneForLandblock.Remove(landblock);

                var interval = PropertyManager.GetDouble("ps_interval", 60).Item;
                _psNextTeleportForLandblock[landblock] = currentUnixTime + interval;

                break; // stop after firing for this landblock
            }

        }
    }

    private void PruneTransientState(List<Player> online, double now)
    {
        var onlineIds = new HashSet<uint>(online.Select(p => p.Guid.Full));

        void PrunePlayerDict(Dictionary<uint, double> dict)
        {
            if (dict.Count == 0)
            {
                return;
            }

            var remove = new List<uint>();
            foreach (var kvp in dict)
            {
                if (kvp.Value <= now || !onlineIds.Contains(kvp.Key))
                {
                    remove.Add(kvp.Key);
                }
            }

            foreach (var id in remove)
            {
                dict.Remove(id);
            }
        }

        // ── PortalStorm (per-player)
        PrunePlayerDict(_psPlayerNextEligible);
        PrunePlayerDict(_psWarnNextSwirlForPlayer);
        PrunePlayerDict(_psWarnNextMessageForPlayer);
        PrunePlayerDict(_psImmuneNextMessageForPlayer);

        // ── Shroud (per-player)
        PrunePlayerDict(_nextTeleportAllowed);
        PrunePlayerDict(_shroudedNextSwirl);
        PrunePlayerDict(_shroudedNextMessage);
        PrunePlayerDict(_outerWarnNextSwirl);
        PrunePlayerDict(_outerWarnNextMessage);
        PrunePlayerDict(_shroudImmuneNextMessageForPlayer);

        // ── PortalStorm (per-landblock)
        var landblocksWithPlayers =
            new HashSet<uint>(online.Select(p => p.Location.Landblock));

        var lbRemove = new List<uint>();

        foreach (var lb in _psNextTeleportForLandblock.Keys)
        {
            var hasPlayers = landblocksWithPlayers.Contains(lb);
            var hasPending = _psPendingTeleportAtForLandblock.ContainsKey(lb);

            if (!hasPlayers && !hasPending)
            {
                lbRemove.Add(lb);
            }
        }

        foreach (var lb in lbRemove)
        {
            _psNextTeleportForLandblock.Remove(lb);
            _psPendingTeleportAtForLandblock.Remove(lb);
            _psPendingZoneForLandblock.Remove(lb);
        }
    }


        private void FireStormWarning(
        ResonanceZoneEntry zone,
        List<Player> playersInRegion,
        double currentUnixTime)
    {
        const double swirlIntervalSeconds = 10.0;
        const double messageIntervalSeconds = 60.0;

        foreach (var p in playersInRegion)
        {
            var id = p.Guid.Full;

            var doSwirl =
                !_psWarnNextSwirlForPlayer.TryGetValue(id, out var nextSwirl) ||
                currentUnixTime >= nextSwirl;

            var doMsg =
                !_psWarnNextMessageForPlayer.TryGetValue(id, out var nextMsg) ||
                currentUnixTime >= nextMsg;

            if (!doSwirl && !doMsg)
            {
                continue;
            }

            if (doSwirl)
            {
                p.PlayParticleEffect(PlayScript.PortalStorm, p.Guid);
                _psWarnNextSwirlForPlayer[id] = currentUnixTime + swirlIntervalSeconds;
            }

            if (doMsg)
            {
                p.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "A rising pull gathers around you, tugging at your center as if trying to draw you into a drifting current. The pressure sharpens, and you feel moments away from being pulled away.",
                        ChatMessageType.System
                    )
                );
                _psWarnNextMessageForPlayer[id] = currentUnixTime + messageIntervalSeconds;
            }
        }
    }

    private void FireStormOnce(
        ResonanceZoneEntry zone,
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

    private static Dictionary<uint, List<ResonanceZoneEntry>> BuildZonesByLandblock(
        IReadOnlyList<ResonanceZoneEntry> zones)
    {
        var result = new Dictionary<uint, List<ResonanceZoneEntry>>();

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
        Dictionary<uint, List<ResonanceZoneEntry>> dict,
        uint landblockId,
        ResonanceZoneEntry zone
    )
    {
        if (!dict.TryGetValue(landblockId, out var list))
        {
            list = new List<ResonanceZoneEntry>();
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



        private static bool IsShroudZoneActive(ResonanceZoneEntry zone)
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

    private static bool IsPortalStormZoneActive(ResonanceZoneEntry zone)
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



    public static ResonanceZoneService CreateFromConfig()
    {
        var config = ResonanceZoneConfig.FromProperties();
        return new ResonanceZoneService(config);
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

        ResonanceZoneEntry chosenZone = null;
        var chosenDistSq = float.MaxValue;

        foreach (var zone in zones)
        {
            if (!IsZoneEventActive(zone))
            {
                continue;
            }

            var zoneWorld = ToWorld2D(zone.Location);
            var diff = playerWorld - zoneWorld;
            var distanceSq = diff.LengthSquared();

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
        }

        if (chosenZone == null)
        {
            // In same landblock but outside all zones
            ClearAllStateFor(player);
            return;
        }


        // Attackable-off players are immune to shroud effects; show a throttled hint ONLY if a shroud is active here
        if (!player.Attackable)
        {
            var shroudActiveHere = IsShroudZoneActive(chosenZone);
            if (shroudActiveHere)
            {
                var id = player.Guid.Full;

                if (!_shroudImmuneNextMessageForPlayer.TryGetValue(id, out var nextAt) || currentUnixTime >= nextAt)
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            "A resonance shroud presses in around you. You would be affected, but your Attackable state is OFF.",
                            ChatMessageType.System
                        )
                    );
                    _shroudImmuneNextMessageForPlayer[id] = currentUnixTime + ImmuneMsgIntervalSeconds;
                }
            }

            ClearAllStateFor(player);
            return;
        }
       
        // Existing behavior, applied to chosen zone only
        var chosenInnerSq = chosenZone.Radius * chosenZone.Radius;
        var insideInner = chosenDistSq <= chosenInnerSq;

        var shroudActive = IsShroudZoneActive(chosenZone);
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

    private void HandleTeleport(Player player, ResonanceZoneEntry zone, double currentUnixTime)
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
    private Position BuildDestination(ResonanceZoneEntry zone, Player player)
    {
        // Outer radius for this zone (storm/shroud): MaxDistance if set, else Radius
        var outer = zone.MaxDistance > 0 ? zone.MaxDistance : zone.Radius;

        // “Just outside” band
        const float bufferMin = 5f;
        const float bufferMax = 15f;

        var minDistance = outer + bufferMin;
        var maxDistance = outer + bufferMax;

        // Tunables (live)
        var clearance = (float)PropertyManager.GetDouble("rz_teleport_clearance", 0.5).Item;
        var maxfall   = (float)PropertyManager.GetDouble("rz_maxfall", 0.5).Item;
        
        // Zone center in world space
        var zoneWorld = ToWorld2D(zone.Location);

        const int attempts = 12;

        for (var i = 0; i < attempts; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            var distance = (float)(_random.NextDouble() * (maxDistance - minDistance) + minDistance);

            var worldX = zoneWorld.X + (direction.X * distance);
            var worldY = zoneWorld.Y + (direction.Y * distance);

            if (!WorldManager.TryBuildGroundedPosition(worldX, worldY, clearance, out var grounded, out var groundZ))
            {
                continue;
            }
            // rz_maxfall = requested fall height
            // minForcedFall = absolute minimum allowed drop height
            const float minForcedFall = 0.5f;

            // Enforce absolute floor at use-time
            var zOffset = Math.Max(clearance, Math.Max(maxfall, minForcedFall));

            var destZ = groundZ + zOffset;

            // Keep player's rotation so they arrive upright
            return new Position(
                grounded.LandblockId.Raw,
                grounded.PositionX,
                grounded.PositionY,
                destZ,
                player.Location.RotationX,
                player.Location.RotationY,
                player.Location.RotationZ,
                player.Location.RotationW
            );
        }

        // Absolute fallback: old behavior
        return new Position(
            zone.Location.LandblockId.Raw,
            zone.Location.PositionX,
            zone.Location.PositionY,
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
}