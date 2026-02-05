using System;
using System.Collections.Generic;
using System.Linq;

namespace ACE.Server.WorldObjects;

public sealed class MonsterPassiveThreat
{
    private readonly Dictionary<Creature, int> _threatLevel = new();
    private readonly Dictionary<Creature, float> _positiveThreat = new();
    private readonly Dictionary<Creature, float> _negativeThreat = new();

    private readonly int _threatMinimum;

    public MonsterPassiveThreat(int threatMinimum)
    {
        _threatMinimum = threatMinimum;
    }

    public void Reset()
    {
        _threatLevel.Clear();
        _positiveThreat.Clear();
        _negativeThreat.Clear();
    }

    public void ApplyPassiveBias(IEnumerable<Creature> targets)
    {
        foreach (var target in targets)
        {
            if (!target.GeneratesPassiveThreat)
            {
                continue;
            }

            _threatLevel.TryAdd(target, _threatMinimum);
        }
    }

    public Creature SelectTarget(IEnumerable<Creature> targets)
    {
        if (_threatLevel.Count == 0)
        {
            return null;
        }

        var maxThreat = _threatLevel.Values.Max();
        var minimumAggro = (int)(maxThreat * 0.5f);

        var candidates = new List<Creature>();

        foreach (var kvp in _threatLevel)
        {
            var target = kvp.Key;
            var threat = kvp.Value;

            if (!targets.Contains(target))
            {
                continue;
            }

            if (target.GeneratesPassiveThreat || threat >= minimumAggro)
            {
                candidates.Add(target);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(t => _threatLevel[t])
            .First();
    }

    public void IncreaseThreat(Creature target, int amount)
    {
        _threatLevel.TryAdd(target, _threatMinimum);
        _threatLevel[target] += amount;
    }

    public void TickDown()
    {
        foreach (var key in _threatLevel.Keys.ToList())
        {
            _threatLevel[key]--;

            if (_threatLevel[key] <= 0)
            {
                _threatLevel.Remove(key);
            }
        }
    }
}
