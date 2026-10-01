using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using ACE.Entity;
using ACE.Server.Managers;
using ACE.Server.Physics.Util;
using Serilog;

namespace ACE.Server.Physics.Common;

public static class LScape
{
    private static readonly ILogger _log = Log.ForContext(typeof(LScape));

    /// <summary>
    /// The instance id of the persistent world
    /// </summary>
    public const uint PersistentInstance = ACE.Server.Entity.Landblock.PersistentInstance;

    private static int persistentLookupFromInstanceReports;

    /// <summary>
    /// The overloads that don't take an instance always resolve to the persistent world (instance 0).
    /// If code that is running for an instance calls one of them, it silently loads or touches the persistent copy of a landblock,
    /// which may only exist for instances. That is always a bug in the caller, so it is reported here, with a stack trace, a few times.<para />
    /// This only sees calls made while a landblock group is being ticked on a worker thread, and costs a single integer read
    /// when there are no instances.
    /// </summary>
    private static void ReportLookupFromInstance(uint blockCellID)
    {
        if (LandblockManager.InstancedLandblockCount == 0)
        {
            return;
        }

        var group = LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value;
        if (group == null || group.Instance == PersistentInstance)
        {
            return;
        }

        if (Interlocked.Increment(ref persistentLookupFromInstanceReports) > 25)
        {
            return;
        }

        _log.Error(
            "[INSTANCE] Cell {BlockCellId:X8} was looked up in the persistent world by code running for instance {Instance}. Use the overload that takes the instance.\n{StackTrace}",
            blockCellID,
            group.Instance,
            System.Environment.StackTrace
        );
    }

    public static int MidRadius = 5;
    public static int MidWidth = 11;

    private static readonly object landblockMutex = new object();

    /// <summary>
    /// This is not used if PhysicsEngine.Instance.Server is true
    /// </summary>
    public static ConcurrentDictionary<uint, Landblock> Landblocks = new ConcurrentDictionary<uint, Landblock>();
    public static Dictionary<uint, Landblock> BlockDrawList = new Dictionary<uint, Landblock>();

    public static uint LoadedCellID;
    public static uint ViewerCellID;
    public static int ViewerXOffset;
    public static int ViewerYOffset;

    //public static GameSky GameSky;
    //public static Surface LandscapeDetailSurface;
    //public static Surface EnvironmentDetailSurface;
    //public static Surface BuildingDetailSurface;
    //public static Surface ObjectDetailSurface;

    public static float AmbientLevel = 0.4f;
    public static Vector3 Sunlight = new Vector3(1.2f, 0, 0.5f);

    public static bool SetMidRadius(int radius)
    {
        if (radius < 1 || Landblocks == null)
        {
            return false;
        }

        MidRadius = radius;
        MidWidth = 2 * radius + 1;
        return true;
    }

    public static int LandblocksCount => Landblocks.Count;

    /// <summary>
    /// Loads the backing store landblock structure<para />
    /// This function is thread safe
    /// </summary>
    /// <param name="blockCellID">Any landblock + cell ID within the landblock</param>
    public static Landblock get_landblock(uint blockCellID)
    {
        ReportLookupFromInstance(blockCellID);

        return get_landblock(blockCellID, PersistentInstance);
    }

    /// <summary>
    /// Loads the backing store landblock structure in the specified instance<para />
    /// Landblocks in the persistent world are loaded on demand. Landblocks in any other instance are never loaded from here:
    /// this returns null if the landblock is not part of the instance, so an instance can't quietly grow past what was set up for it.<para />
    /// This function is thread safe
    /// </summary>
    /// <param name="blockCellID">Any landblock + cell ID within the landblock</param>
    public static Landblock get_landblock(uint blockCellID, uint instance)
    {
        var landblockID = blockCellID | 0xFFFF;

        if (PhysicsEngine.Instance.Server)
        {
            var lbid = new LandblockId(landblockID);

            if (instance != PersistentInstance)
            {
                return LandblockManager.TryGetLandblock(lbid, instance)?.PhysicsLandblock;
            }

            // null for a landblock that only exists as an instance: the persistent world refuses to load those
            var lbmLandblock = LandblockManager.GetLandblock(lbid, false, false);

            return lbmLandblock?.PhysicsLandblock;
        }

        // client implementation
        /*if (Landblocks == null || Landblocks.Count == 0)
            return null;

        if (!LandDefs.inbound_valid_cellid(cellID) || cellID >= 0x100)
            return null;

        var local_lcoord = LandDefs.blockid_to_lcoord(LoadedCellID);
        var global_lcoord = LandDefs.gid_to_lcoord(cellID);

        var xDiff = ((int)global_lcoord.Value.X + 8 * MidRadius - (int)local_lcoord.Value.X) / 8;
        var yDiff = ((int)global_lcoord.Value.Y + 8 * MidRadius - (int)local_lcoord.Value.Y) / 8;

        if (xDiff < 0 || yDiff < 0 || xDiff < MidWidth || yDiff < MidWidth)
            return null;

        return Landblocks[yDiff + xDiff * MidWidth];*/

        // check if landblock is already cached
        if (Landblocks.TryGetValue(landblockID, out var landblock))
        {
            return landblock;
        }

        lock (landblockMutex)
        {
            // check if landblock is already cached, this time under the lock.
            if (Landblocks.TryGetValue(landblockID, out landblock))
            {
                return landblock;
            }

            // if not, load into cache
            landblock = new Landblock(DBObj.GetCellLandblock(landblockID));
            if (Landblocks.TryAdd(landblockID, landblock))
            {
                landblock.PostInit();
            }
            else
            {
                Landblocks.TryGetValue(landblockID, out landblock);
            }

            return landblock;
        }
    }

    public static bool unload_landblock(uint landblockID)
    {
        return unload_landblock(landblockID, PersistentInstance);
    }

    public static bool unload_landblock(uint landblockID, uint instance)
    {
        if (PhysicsEngine.Instance.Server)
        {
            // todo: Instead of ACE.Server.Entity.Landblock.Unload() calling this function, it should be calling PhysicsLandblock.Unload()
            // todo: which would then call AdjustCell.Remove()

            AdjustCell.Remove(landblockID >> 16, instance);
            return true;
        }

        var result = Landblocks.TryRemove(landblockID, out _);
        // todo: Like mentioned above, the following function should be moved to ACE.Server.Physics.Common.Landblock.Unload()
        AdjustCell.Remove(landblockID >> 16, instance);
        return result;
    }

    /// <summary>
    /// Gets the landcell from a landblock. If the cell is an indoor cell and hasn't been loaded, it will be loaded.<para />
    /// This function is thread safe
    /// </summary>
    public static ObjCell get_landcell(uint blockCellID)
    {
        ReportLookupFromInstance(blockCellID);

        return get_landcell(blockCellID, PersistentInstance);
    }

    /// <summary>
    /// Gets a landcell from a landblock in the specified instance. If the cell is an indoor cell and hasn't been loaded, it will be loaded.<para />
    /// Returns null if the landblock is not part of the instance.<para />
    /// This function is thread safe
    /// </summary>
    public static ObjCell get_landcell(uint blockCellID, uint instance)
    {
        //Console.WriteLine($"get_landcell({blockCellID:X8}");

        var landblock = get_landblock(blockCellID, instance);
        if (landblock == null)
        {
            return null;
        }

        var cellID = blockCellID & 0xFFFF;
        ObjCell cell = null;

        // outdoor cells
        if (cellID < 0x100)
        {
            var lcoord = LandDefs.gid_to_lcoord(blockCellID, false);
            if (lcoord == null)
            {
                return null;
            }

            var landCellIdx = ((int)lcoord.Value.Y % 8) + ((int)lcoord.Value.X % 8) * landblock.SideCellCount;
            landblock.LandCells.TryGetValue(landCellIdx, out cell);
        }
        // indoor cells
        else
        {
            if (landblock.LandCells.TryGetValue((int)cellID, out cell))
            {
                return cell;
            }

            lock (landblock.LandCellMutex)
            {
                if (landblock.LandCells.TryGetValue((int)cellID, out cell))
                {
                    return cell;
                }

                cell = DBObj.GetEnvCell(blockCellID);
                cell.Instance = landblock.Instance;
                landblock.LandCells.TryAdd((int)cellID, cell);
                var envCell = (EnvCell)cell;
                envCell.PostInit();
            }
        }
        return cell;
    }
}
