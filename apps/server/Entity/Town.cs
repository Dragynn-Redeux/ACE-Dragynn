using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity;

public class Town
{
    public static string GetNearestTown(WorldObject worldObject)
    {
        var objectPosition = worldObject.Location.GetMapCoords() ?? new Vector2(0.0f, 0.0f);

        var closest = TownPositions.MinBy(kv => Vector2.Distance(kv.Value, objectPosition));

        return closest.Key;
    }

    public static string GetSimplifiedTownString(string townName)
    {
        return townName.Replace(" ", "").Replace("-", "").Replace("'", "");
    }

    public static uint GetTownTier(string townName)
    {
        TownTiers.TryGetValue(townName, out var tier);

        return tier;
    }

    private static readonly Dictionary<string, Vector2> TownPositions = new ()
    {
        {"Al-Arqas", new Vector2(13.7f, -31.2f) },
        {"Al-Jalima", new Vector2(4.8f, 7.4f) },
        {"Arwic", new Vector2(56.8f, 33.6f) },
        {"Baishi", new Vector2(62.9f, -49.3f) },
        {"Bandit Castle", new Vector2(50.0f, 66.5f) },
        {"Bluespire", new Vector2(-75.4f, 39.4f) },
        {"Cragstone", new Vector2(49.1f, -26.0f) },
        {"Crater", new Vector2(13.5f, 64.9f) },
        {"Danby's Outpost", new Vector2(-28.7f, 23.4f) },
        {"Dryreach", new Vector2(73.0f, -8.1f) },
        {"Eastham", new Vector2(63.4f, 17.5f) },
        {"Fort Tethana", new Vector2(-71.8f, 1.5f) },
        {"Glenden Wood", new Vector2(27.4f, 29.9f) },
        {"Greenspire", new Vector2(-66.9f, 42.9f) },
        {"Hebian-To", new Vector2(83.2f, -39.1f) },
        {"Holtburg", new Vector2(33.6f, 42.1f) },
        {"Kara", new Vector2(47.6f, -83.5f) },
        {"Khayyaban", new Vector2(24.7f, -47.6f) },
        {"Lin", new Vector2(73.1f, -54.5f) },
        {"Lytelthorpe", new Vector2(51.1f, 0.9f) },
        {"Mayoi", new Vector2(81.9f, -61.6f) },
        {"Nanto", new Vector2(82.1f, -52.5f) },
        {"Neydisa", new Vector2(17.6f, 69.7f) },
        {"Plateau Village", new Vector2(-43.1f, 44.5f) },
        {"Qalaba'r", new Vector2(19.6f, -74.6f) },
        {"Redspire", new Vector2(-83.1f, 40.8f) },
        {"Rithwic", new Vector2(59.3f, 10.8f) },
        {"Samsur", new Vector2(19.0f, -3.2f) },
        {"Sawato", new Vector2(59.3f, -28.7f) },
        {"Shoushi", new Vector2(73.6f, -34.2f) },
        {"Stonehold", new Vector2(-21.8f, 68.7f) },
        {"Tou-Tou", new Vector2(95.8f, -28.1f) },
        {"Tufa", new Vector2(5.0f, -13.9f) },
        {"Uziz", new Vector2(28.3f, -25.2f) },
        {"Wai Jhou", new Vector2(-51.4f, -62.0f) },
        {"Xarabydun", new Vector2(16.1f, -41.9f) },
        {"Yanshi", new Vector2(46.4f, -12.7f) },
        {"Yaraq", new Vector2(-1.7f, -21.6f) },
        {"Zaikhal", new Vector2(0.7f, 13.5f) },
    };

    private static readonly Dictionary<string, uint> TownTiers = new ()
    {
        {"Holtburg", 0 },
        {"Shoushi", 0 },
        {"Yaraq", 0 },

        {"Al-Arqas", 1 },
        {"Arwic", 1 },
        {"Cragstone", 1 },
        {"Glenden Wood", 1 },
        {"Hebian-To", 1 },
        {"Sawato", 1 },
        {"Tou-Tou", 1 },
        {"Tufa", 1 },
        {"Zaikhal", 1 },

        {"Al-Jalima", 2 },
        {"Eastham", 2 },
        {"Lin", 2 },
        {"Lytelthorpe", 2 },
        {"Nanto", 2 },
        {"Rithwic", 2 },
        {"Samsur", 2 },
        {"Uziz", 2 },
        {"Yanshi", 2 },

        {"Baishi", 3 },
        {"Danby's Outpost", 3 },
        {"Dryreach", 3 },
        {"Khayyaban", 3 },
        {"Mayoi", 3},
        {"Plateau Village", 3 },
        {"Xarabydun", 3 },

        {"Bandit Castle", 4 },
        {"Crater", 4 },
        {"Kara", 4 },
        {"Neydisa", 4 },
        {"Qalaba'r", 4 },
        {"Stonehold", 4 },

        {"Ahurenga", 5 },
        {"Bluespire", 5 },
        {"Greenspire", 5 },
        {"Redspire", 5 },

        {"Ayan Baqur", 6 },
        {"Fort Tethana", 6 },
        {"Timaru", 6 },
        {"Wai Jhou", 6 },
    };

    private static uint MapCoordsToLandblockId(Vector2 mapCoords)
    {
        var globalX = (mapCoords.X + 102) * 240;
        var globalY = (mapCoords.Y + 102) * 240;
        var blockX = (uint)(globalX / 192);
        var blockY = (uint)(globalY / 192);
        return (blockX << 24) | (blockY << 16) | 0xFFFF;
    }

    /// <summary>
    /// Landblock IDs derived from each town's center coordinates.
    /// Additional IDs can be added at runtime via Town.TownLandblockIds.Add(id).
    /// </summary>
    public static readonly HashSet<uint> TownLandblockIds = BuildTownLandblockIds();

    private static HashSet<uint> BuildTownLandblockIds()
    {
        var ids = new HashSet<uint>();
        foreach (var mapCoords in TownPositions.Values)
        {
            ids.Add(MapCoordsToLandblockId(mapCoords));
        }
        return ids;
    }

    public static bool IsTownLandblock(uint landblockId)
    {
        return TownLandblockIds.Contains(landblockId);
    }
}
