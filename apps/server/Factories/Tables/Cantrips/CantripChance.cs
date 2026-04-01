using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database.Models.World;
using ACE.Server.Factories.Entity;
using ACE.Server.Managers;
using Serilog;

namespace ACE.Server.Factories.Tables.Cantrips;

public static class CantripChance
{
    private static readonly ILogger _log = Log.ForContext(typeof(CantripChance));

    private static ChanceTable<int> T1_NumCantrips = new ChanceTable<int>() { (0, 1.0f), };

    private static ChanceTable<int> T2_NumCantrips = new ChanceTable<int>() { (1, 1.0f), };

    private static ChanceTable<int> T3_NumCantrips = new ChanceTable<int>() { (1, 0.90f), (2, 0.10f), };

    private static ChanceTable<int> T4_NumCantrips = new ChanceTable<int>() { (1, 0.85f), (2, 0.10f), (3, 0.05f), };

    private static ChanceTable<int> T5_NumCantrips = new ChanceTable<int>() { (1, 0.85f), (2, 0.10f), (3, 0.05f), };

    private static ChanceTable<int> T6_NumCantrips = new ChanceTable<int>() { (1, 0.85f), (2, 0.10f), (3, 0.05f), };

    private static ChanceTable<int> T7_T8_NumCantrips = new ChanceTable<int>()
    {
        (1, 0.7f),
        (2, 0.15f),
        (3, 0.1f),
        (4, 0.05f),
    };

    private static readonly List<ChanceTable<int>> _numCantrips = new List<ChanceTable<int>>()
    {
        T1_NumCantrips,
        T2_NumCantrips,
        T3_NumCantrips,
        T4_NumCantrips,
        T5_NumCantrips,
        T6_NumCantrips,
        T7_T8_NumCantrips,
        T7_T8_NumCantrips,
    };

    public static int RollNumCantrips(TreasureDeath profile)
    {
        return _numCantrips[profile.Tier - 1].Roll(profile.LootQualityMod);
    }

    private static ChanceTable<int> T1_CantripLevel = new ChanceTable<int>() { (1, 1.0f) };

    private static ChanceTable<int> T2_CantripLevel = new ChanceTable<int>() { (1, 1.0f) };

    private static ChanceTable<int> T3_CantripLevel = new ChanceTable<int>() { (1, 1.0f), };

    private static ChanceTable<int> T4_CantripLevel = new ChanceTable<int>() { (1, 0.75f), (2, 0.25f), };

    private static ChanceTable<int> T5_CantripLevel = new ChanceTable<int>() { (1, 0.50f), (2, 0.50f), };

    private static ChanceTable<int> T6_CantripLevel = new ChanceTable<int>() { (1, 0.25f), (2, 0.74f), (3, 0.01f), };

    private static ChanceTable<int> T7_CantripLevel = new ChanceTable<int>() { (2, 0.75f), (3, 0.25f), };

    private static ChanceTable<int> T8_CantripLevel = new ChanceTable<int>() { (2, 0.50f), (3, 0.49f), (4, 0.01f) };

    private static readonly List<ChanceTable<int>> _cantripLevels = new List<ChanceTable<int>>()
    {
        T1_CantripLevel,
        T2_CantripLevel,
        T3_CantripLevel,
        T4_CantripLevel,
        T5_CantripLevel,
        T6_CantripLevel,
        T7_CantripLevel,
        T8_CantripLevel,
    };

    public static int RollCantripLevel(TreasureDeath profile)
    {
        return cantripLevels[profile.Tier - 1].Roll(profile.LootQualityMod);
    }

    public static int RollCantripLevelForTier(int tier, float qualityMod = 0.0f)
    {
        tier = Math.Clamp(tier, 1, cantripLevels.Count);
        return cantripLevels[tier - 1].Roll(qualityMod);
    }

    public static int GetQuestCantripLevelForTier(int tier)
    {
        return tier switch
        {
            3 => 2,
            4 => 2,
            5 => 3,
            6 => 3,
            7 => 4,
            _ => 0,
        };
    }

    private static List<ChanceTable<int>> numCantrips = _numCantrips;

    public static void ApplyNumCantripsMod(bool showResults = true)
    {
        // scales NumCantrips, no chance vs. chance, relative to each other
        var cantrip_drop_rate = (float)Math.Max(0.0f, PropertyManager.GetDouble("cantrip_drop_rate").Item);

        if (cantrip_drop_rate != 1.0f)
        {
            var newTable = new List<ChanceTable<int>>();

            foreach (var entry in _numCantrips)
            {
                var newEntry = ScaleNumCantrips(entry, cantrip_drop_rate);
                newTable.Add(newEntry);
            }
            numCantrips = newTable;
        }
        else
        {
            numCantrips = _numCantrips;
        }

        if (showResults)
        {
            _log.Information($"ApplyNumCantripsMod({cantrip_drop_rate})");

            ShowTables(numCantrips);
        }
    }

    public static ChanceTable<int> ScaleNumCantrips(ChanceTable<int> numCantrips, float cantrip_drop_rate)
    {
        var newTable = new ChanceTable<int>();

        foreach (var entry in numCantrips)
        {
            if (entry.result != 0)
            {
                newTable.Add((entry.result, entry.chance * cantrip_drop_rate));
            }
        }

        var totalChance = newTable.Sum(i => i.chance);

        if (totalChance > 1.0f)
        {
            newTable = Rescale(newTable);
            totalChance = 1.0f;
        }

        var finalTable = new ChanceTable<int>();
        finalTable.Add((0, 1.0f - totalChance));
        finalTable.AddRange(newTable);

        return finalTable;
    }

    private static List<ChanceTable<int>> cantripLevels = _cantripLevels;

    public static void ApplyCantripLevelsMod(bool showResults = true)
    {
        // scales CantripLevels, relative to each other
        var minor_cantrip_drop_rate = (float)Math.Max(0.0f, PropertyManager.GetDouble("minor_cantrip_drop_rate").Item);
        var major_cantrip_drop_rate = (float)Math.Max(0.0f, PropertyManager.GetDouble("major_cantrip_drop_rate").Item);
        var epic_cantrip_drop_rate = (float)Math.Max(0.0f, PropertyManager.GetDouble("epic_cantrip_drop_rate").Item);
        var legendary_cantrip_drop_rate = (float)
            Math.Max(0.0f, PropertyManager.GetDouble("legendary_cantrip_drop_rate").Item);

        if (
            minor_cantrip_drop_rate != 1.0f
            || major_cantrip_drop_rate != 1.0f
            || epic_cantrip_drop_rate != 1.0f
            || legendary_cantrip_drop_rate != 1.0f
        )
        {
            var newTable = new List<ChanceTable<int>>();

            foreach (var entry in _cantripLevels)
            {
                var newEntry = ScaleCantripLevels(
                    entry,
                    minor_cantrip_drop_rate,
                    major_cantrip_drop_rate,
                    epic_cantrip_drop_rate,
                    legendary_cantrip_drop_rate
                );
                newTable.Add(newEntry);
            }
            cantripLevels = newTable;
        }
        else
        {
            cantripLevels = _cantripLevels;
        }

        if (showResults)
        {
            _log.Information(
                $"ApplyCantripLevelsMod({minor_cantrip_drop_rate}, {major_cantrip_drop_rate}, {epic_cantrip_drop_rate}, {legendary_cantrip_drop_rate})"
            );

            ShowTables(cantripLevels);
        }
    }

    public static ChanceTable<int> ScaleCantripLevels(
        ChanceTable<int> cantripLevel,
        float minor_cantrip_drop_rate,
        float major_cantrip_drop_rate,
        float epic_cantrip_drop_rate,
        float legendary_cantrip_drop_rate
    )
    {
        var newTable = new ChanceTable<int>();

        foreach (var entry in cantripLevel)
        {
            var modifier = 1.0f;

            switch (entry.result)
            {
                case 1:
                    modifier = minor_cantrip_drop_rate;
                    break;
                case 2:
                    modifier = major_cantrip_drop_rate;
                    break;
                case 3:
                    modifier = epic_cantrip_drop_rate;
                    break;
                case 4:
                    modifier = legendary_cantrip_drop_rate;
                    break;
            }
            newTable.Add((entry.result, entry.chance * modifier));
        }

        return Rescale(newTable);
    }

    public static ChanceTable<int> Rescale(ChanceTable<int> table, float target = 1.0f)
    {
        var total = table.Sum(i => i.chance);

        if (total == target)
        {
            return table;
        }

        // get scalar
        var scalar = target / total;

        var rescaled = new ChanceTable<int>();

        foreach (var entry in table)
        {
            rescaled.Add((entry.result, entry.chance * scalar));
        }

        return rescaled;
    }

    static CantripChance()
    {
        ApplyNumCantripsMod(false);
        ApplyCantripLevelsMod(false);
    }

    private static void ShowTables(List<ChanceTable<int>> tables)
    {
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];

            _log.Information($"Tier {i + 1}:");

            foreach (var entry in table)
            {
                _log.Information($"{entry.result}: {GetPercent(entry.chance)}");
            }
        }
    }

    private static string GetPercent(float pct)
    {
        return $"{Math.Round(pct * 100, 2, MidpointRounding.AwayFromZero)}%";
    }
}
