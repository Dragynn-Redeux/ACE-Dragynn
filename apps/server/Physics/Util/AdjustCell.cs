using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using Serilog;

namespace ACE.Server.Physics.Util;

public class AdjustCell
{
    private static readonly ILogger _log = Log.ForContext<AdjustCell>();

    public List<Common.EnvCell> EnvCells;

    /// <summary>
    /// Kept per instance, because the cells in the list are the ones that belong to the landblock of the instance it was built for
    /// </summary>
    public static ConcurrentDictionary<(uint Instance, uint DungeonID), AdjustCell> AdjustCells =
        new ConcurrentDictionary<(uint Instance, uint DungeonID), AdjustCell>();

    public AdjustCell(uint dungeonID)
        : this(dungeonID, Common.LScape.PersistentInstance) { }

    public AdjustCell(uint dungeonID, uint instance)
    {
        var blockInfoID = dungeonID << 16 | 0xFFFE;
        var blockinfo = DatManager.CellDat.ReadFromDat<LandblockInfo>(blockInfoID);
        var numCells = blockinfo.NumCells;

        BuildEnv(dungeonID, numCells, instance);
    }

    public void BuildEnv(uint dungeonID, uint numCells)
    {
        BuildEnv(dungeonID, numCells, Common.LScape.PersistentInstance);
    }

    public void BuildEnv(uint dungeonID, uint numCells, uint instance)
    {
        EnvCells = new List<Common.EnvCell>();
        uint firstCellID = 0x100;
        var missingCells = 0;
        for (uint i = 0; i < numCells; i++)
        {
            var cellID = firstCellID + i;
            var blockCell = dungeonID << 16 | cellID;

            var objCell = Common.LScape.get_landcell(blockCell, instance);
            var envCell = objCell as Common.EnvCell;

            // A cell that isn't in the cell dat still comes back as an EnvCell, just an empty one without a CellStructure.
            // It can never contain a point, so it is left out.
            if (envCell?.CellStructure != null)
            {
                EnvCells.Add(envCell);
            }
            else
            {
                missingCells++;
            }
        }

        if (missingCells > 0)
        {
            _log.Warning(
                "AdjustCell: landblock {Landblock:X4} lists {NumCells} interior cells in its LandblockInfo, but {MissingCells} of them are not in the cell dat (stale LandblockInfo.NumCells?). Those cells are ignored.",
                dungeonID,
                numCells,
                missingCells
            );
        }
    }

    public uint? GetCell(Vector3 point)
    {
        foreach (var envCell in EnvCells)
        {
            if (envCell.point_in_cell(point))
            {
                return envCell.ID;
            }
        }

        return null;
    }

    public static AdjustCell Get(uint dungeonID)
    {
        return Get(dungeonID, Common.LScape.PersistentInstance);
    }

    /// <summary>
    /// Returns null if the landblock is not loaded in the instance. Nothing is loaded from here for an instance,
    /// and nothing is cached for it either, so it can be asked again once the landblock is there.
    /// </summary>
    public static AdjustCell Get(uint dungeonID, uint instance)
    {
        var key = (instance, dungeonID);

        AdjustCells.TryGetValue(key, out var adjustCell);
        if (adjustCell == null)
        {
            if (
                instance != Common.LScape.PersistentInstance
                && Common.LScape.get_landblock(dungeonID << 16, instance) == null
            )
            {
                return null;
            }

            adjustCell = new AdjustCell(dungeonID, instance);
            AdjustCells.TryAdd(key, adjustCell);
        }
        return adjustCell;
    }

    /// <summary>
    /// Forgets the cells for a landblock in an instance, when that landblock unloads
    /// </summary>
    public static void Remove(uint dungeonID, uint instance)
    {
        AdjustCells.TryRemove((instance, dungeonID), out _);
    }
}
