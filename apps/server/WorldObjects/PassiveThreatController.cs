using System;
using System.Collections.Generic;
using Serilog;

namespace ACE.Server.WorldObjects;

public sealed class PassiveThreatController
{
    private const float PassiveTargetPctDefault = 0.40f;
    private const float PassivePlayerFavorDefault = 0.02f;
    private const float PassiveSmoothingDefault = 0.20f;
    private const int PassiveLossStreakThresholdDefault = 3;

    private readonly Dictionary<uint, float> _passiveThreatEma = new();
    private readonly Dictionary<uint, int> _passiveLossStreak = new();

    private static int GetMaxActiveThreat(Dictionary<Creature, int> threatLevel, int threatMinimum)
    {
        if (threatLevel == null || threatLevel.Count == 0)
        {
            return threatMinimum;
        }

        var max = threatMinimum;

        foreach (var kvp in threatLevel)
        {
            var c = kvp.Key;
            if (c == null || c.GeneratesPassiveThreat)
            {
                continue;
            }

            if (kvp.Value > max)
            {
                max = kvp.Value;
            }
        }

        return max;
    }

    public void ApplyPassiveThreatThreshold(
        Creature monster,
        List<Creature> targets,
        Dictionary<Creature, int> threatLevel,
        int threatMinimum,
        bool debugThreatSystem,
        ILogger log)
    {
        if (threatLevel == null || threatLevel.Count == 0)
        {
            return;
        }

        if (targets == null || targets.Count == 0)
        {
            return;
        }

        var targetPct = PassiveTargetPctDefault;
        var playerFavor = PassivePlayerFavorDefault;
        var smoothing = PassiveSmoothingDefault;
        var streakThreshold = PassiveLossStreakThresholdDefault;

        var maxActiveThreat = GetMaxActiveThreat(threatLevel, threatMinimum);
        if (maxActiveThreat < threatMinimum)
        {
            maxActiveThreat = threatMinimum;
        }

        foreach (var target in targets)
        {
            if (target == null || !target.GeneratesPassiveThreat)
            {
                continue;
            }

            var additiveFloor = Math.Max(target.PassiveThreatThreshold, 0);
            var minFloorThreat = threatMinimum + additiveFloor;

            var desired = (int)Math.Ceiling((0.5f * maxActiveThreat) / Math.Max(1e-3f, (1.0f - targetPct)));

            var capUnderTop = (int)Math.Floor(maxActiveThreat * (1.0f - playerFavor));
            if (desired > capUnderTop)
            {
                desired = capUnderTop;
            }

            if (desired < minFloorThreat)
            {
                desired = minFloorThreat;
            }

            var guid = target.Guid.Full;
            if (!_passiveThreatEma.TryGetValue(guid, out var ema))
            {
                ema = desired;
            }

            ema = (ema * (1.0f - smoothing)) + (desired * smoothing);
            _passiveThreatEma[guid] = ema;

            var smoothedDesired = (int)Math.Round(ema);

            threatLevel.TryAdd(target, threatMinimum);

            if (_passiveLossStreak.TryGetValue(guid, out var streak) && streak >= streakThreshold)
            {
                smoothedDesired = Math.Max(smoothedDesired - 5, minFloorThreat);
            }

            if (threatLevel[target] < smoothedDesired)
            {
                threatLevel[target] = smoothedDesired;
            }

            if (debugThreatSystem && log != null)
            {
                log.Information(
                    "PASSIVE THREAT DYN: {Monster} -> {Target} maxActive={MaxActive} desired={Desired} smoothed={Smoothed} floor={Floor} final={Final}",
                    monster?.Name, target.Name, maxActiveThreat, desired, smoothedDesired, minFloorThreat, threatLevel[target]
                );
            }
        }
    }

    public void UpdatePassiveLossStreaks(Creature selectedTarget, Dictionary<Creature, int> threatLevel)
    {
        if (selectedTarget == null || threatLevel == null || threatLevel.Count == 0)
        {
            return;
        }

        foreach (var kvp in threatLevel)
        {
            var c = kvp.Key;
            if (c == null || !c.GeneratesPassiveThreat)
            {
                continue;
            }

            var guid = c.Guid.Full;

            if (selectedTarget == c)
            {
                _passiveLossStreak[guid] =
                    (_passiveLossStreak.TryGetValue(guid, out var s) ? s : 0) + 1;
            }
            else
            {
                _passiveLossStreak[guid] = 0;
            }
        }
    }
}

