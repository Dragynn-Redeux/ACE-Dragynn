using System;
using System.Collections.Generic;
using System.Linq;

namespace ACE.Server.Entity;

/// <summary>
/// Plans pouring salvage bags of the same material and workmanship into as few bags as possible.
/// </summary>
public static class SalvagePourPlanner
{
    /// <summary>
    /// Pour Amount units from bag Source into bag Target (indexes into the list given to Plan).
    /// </summary>
    public readonly record struct Pour(int Source, int Target, int Amount);

    /// <summary>
    /// The fullest bags are topped up first, from the emptiest, the same way /sort combines stacks.
    /// A bag ends up empty only if every unit it had was poured out.
    /// </summary>
    /// <param name="bags">Each bag's units and the most it can hold.</param>
    public static List<Pour> Plan(IReadOnlyList<(int Units, int MaxUnits)> bags)
    {
        var order = Enumerable.Range(0, bags.Count)
            .OrderByDescending(i => bags[i].Units)
            .ThenBy(i => i)
            .ToList();

        var units = bags.Select(b => b.Units).ToArray();
        var pours = new List<Pour>();

        var targetIdx = 0;
        var sourceIdx = order.Count - 1;

        while (targetIdx < sourceIdx)
        {
            var target = order[targetIdx];
            var source = order[sourceIdx];

            var room = bags[target].MaxUnits - units[target];
            if (room <= 0)
            {
                targetIdx++;
                continue;
            }

            if (units[source] <= 0)
            {
                sourceIdx--;
                continue;
            }

            var amount = Math.Min(room, units[source]);

            pours.Add(new Pour(source, target, amount));
            units[target] += amount;
            units[source] -= amount;

            if (units[source] == 0)
            {
                sourceIdx--;
            }
        }

        return pours;
    }

    /// <summary>
    /// How many bags the pours leave empty.
    /// </summary>
    public static int CountEmptied(IReadOnlyList<(int Units, int MaxUnits)> bags, IEnumerable<Pour> pours)
    {
        var units = bags.Select(b => b.Units).ToArray();

        foreach (var pour in pours)
        {
            units[pour.Source] -= pour.Amount;
            units[pour.Target] += pour.Amount;
        }

        return units.Where((u, i) => u == 0 && bags[i].Units > 0).Count();
    }
}
