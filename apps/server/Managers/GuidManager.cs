using System;
using System.Collections.Generic;
using System.Threading;
using ACE.Entity;
using Serilog;

namespace ACE.Server.Managers;

/// <summary>
/// Used to assign global guids and ensure they are unique to server.
/// </summary>
public static class GuidManager
{
    private static readonly ILogger _log = Log.ForContext(typeof(GuidManager));

    // Running server is guid master - database only read as startup to get current max per range.
    // weenie class templates Max 65,535 - took Turbine 17 years to get to 10K
    // these will be added by developers and not in game.
    // Nothing in this range is persisted by the game.   Only developers or content creators can create them to be persisted.
    // this is only here for documentation purposes.
    // Fragmentation: None

    /// <summary>
    /// Is equal to uint.MaxValue
    /// </summary>
    public static uint InvalidGuid { get; } = uint.MaxValue;

    private const uint LowIdLimit = 0x1000;

    private class PlayerGuidAllocator
    {
        private readonly uint max;
        private uint current;
        private readonly string name;

        public PlayerGuidAllocator(uint min, uint max, string name)
        {
            this.max = max;

            // Read current value out of ShardDatabase
            lock (this)
            {
                var done = false;
                Database.DatabaseManager.Shard.GetMaxGuidFoundInRange(
                    min,
                    max,
                    dbVal =>
                    {
                        lock (this)
                        {
                            current = dbVal;
                            done = true;
                            Monitor.Pulse(this);
                        }
                    }
                );

                while (!done)
                {
                    Monitor.Wait(this);
                }

                if (current == InvalidGuid)
                {
                    current = min;
                }
                else
                {
                    // Need to start allocating at current value in db +1
                    current++;
                }

                _log.Debug("{Name} GUID Allocator current is now {Current:X8} of {Max:X8}", name, current, max);

                if ((max - current) < LowIdLimit)
                {
                    _log.Warning("Dangerously low on {Name} GUIDs: {Current:X8} of {Max:X8}", name, current, max);
                }
            }

            this.name = name;
        }

        public uint Alloc()
        {
            lock (this)
            {
                if (current == max)
                {
                    _log.Fatal("Out of {Name} GUIDs!", name);
                    return InvalidGuid;
                }

                if (current == max - LowIdLimit)
                {
                    _log.Warning("Running dangerously low on {Name} GUIDs, need to defrag", name);
                }

                var ret = current;
                current += 1;

                return ret;
            }
        }

        /// <summary>
        /// For information purposes only, do not use the result. Use Alloc() instead
        /// This value represents the current DbMax + 1
        /// </summary>
        public uint Current()
        {
            return current;
        }
    }

    /// <summary>
    /// On a server with ~500 players, about 10,000,000 dynamic GUID's will be requested every 24hr period.
    /// </summary>
    private class DynamicGuidAllocator
    {
        private readonly uint max;
        private uint current;
        private readonly string name;

        private static readonly TimeSpan recycleTime = TimeSpan.FromMinutes(360);

        private readonly Queue<Tuple<DateTime, uint>> recycledGuids = new Queue<Tuple<DateTime, uint>>();

        /// <summary>
        /// The value here is the result of two factors:
        /// - A: The total number of GUIDs that are generated during a period of recycledTime (defined above)
        /// - B: The total number of GUIDs that are consumed and saved to the shard between server resets
        /// A safe value might be (2 * A) + (2 * B)
        /// On a shard with severe id fragmentation, this can end up eating more memory to store all the smaller gaps
        /// Once sequence gaps are depleted and there are no available id's in the recycle queue, DB Max + 1 is used
        /// You can monitor the amount of available id's using /serverstatus
        /// </summary>
        private const int limitAvailableIDsReturnedInGetSequenceGaps = 10000000;
        private bool useSequenceGapExhaustedMessageDisplayed;
        private LinkedList<(uint start, uint end)> availableIDs = new LinkedList<(uint start, uint end)>();

        public DynamicGuidAllocator(uint min, uint max, string name)
        {
            this.max = max;

            // Read current value out of ShardDatabase
            lock (this)
            {
                var done = false;
                Database.DatabaseManager.Shard.GetMaxGuidFoundInRange(
                    min,
                    max,
                    dbVal =>
                    {
                        lock (this)
                        {
                            current = dbVal;
                            done = true;
                            Monitor.Pulse(this);
                        }
                    }
                );

                while (!done)
                {
                    Monitor.Wait(this);
                }

                if (current == InvalidGuid)
                {
                    current = min;
                }
                else
                {
                    // Need to start allocating at current value in db +1
                    current++;
                }

                _log.Debug("{Name} GUID Allocator current is now {Current:X8} of {Max:X8}", name, current, max);

                if ((max - current) < LowIdLimit)
                {
                    _log.Warning("Dangerously low on {Name} GUIDs: {Current:X8} of {Max:X8}", name, current, max);
                }
            }

            // Get available ids in the form of sequence gaps
            lock (this)
            {
                var done = false;
                Database.DatabaseManager.Shard.GetSequenceGaps(
                    ObjectGuid.DynamicMin,
                    limitAvailableIDsReturnedInGetSequenceGaps,
                    gaps =>
                    {
                        lock (this)
                        {
                            availableIDs = new LinkedList<(uint start, uint end)>(gaps);
                            uint total = 0;
                            foreach (var pair in availableIDs)
                            {
                                total += (pair.end - pair.start) + 1;
                            }

                            _log.Debug(
                                "{Name} GUID Sequence gaps initialized with total availableIDs of {Total:N0}",
                                name,
                                total
                            );

                            done = true;
                            Monitor.Pulse(this);
                        }
                    }
                );

                while (!done)
                {
                    Monitor.Wait(this);
                }
            }

            this.name = name;
        }

        public uint Alloc()
        {
            lock (this)
            {
                // First, try to use a recycled Guid
                if (recycledGuids.TryPeek(out var result) && DateTime.UtcNow - result.Item1 > recycleTime)
                {
                    recycledGuids.Dequeue();
                    return result.Item2;
                }

                // Second, try to use a known available Guid
                if (availableIDs.First != null)
                {
                    var id = availableIDs.First.Value.start;

                    if (availableIDs.First.Value.start == availableIDs.First.Value.end)
                    {
                        availableIDs.RemoveFirst();

                        //if (availableIDs.First == null)
                        //    _log.Warning($"Sequence gap GUIDs depleted on {name}");
                    }
                    else
                    {
                        availableIDs.First.Value = (availableIDs.First.Value.start + 1, availableIDs.First.Value.end);
                    }

                    return id;
                }
                else
                {
                    if (!useSequenceGapExhaustedMessageDisplayed)
                    {
                        _log.Debug(
                            "{Name} GUID Sequence gaps exhausted. Any new, non-recycled GUID will be current + 1. current is now {Current:X8}",
                            name,
                            current
                        );

                        useSequenceGapExhaustedMessageDisplayed = true;
                    }
                }

                // Lastly, use an id that increments our max
                if (current == max)
                {
                    _log.Fatal("Out of {Name} GUIDs!", name);

                    return InvalidGuid;
                }

                if (current == max - LowIdLimit)
                {
                    _log.Warning("Running dangerously low on {Name} GUIDs, need to defrag", name);
                }

                var ret = current;
                current += 1;

                return ret;
            }
        }

        /// <summary>
        /// For information purposes only, do not use the result. Use Alloc() instead
        /// This is the value that might be used in the event that there are no recycled guid available and sequence gap guids have been exhausted
        /// This value represents the current DbMax + 1
        /// </summary>
        public uint Current()
        {
            return current;
        }

        public void Recycle(uint guid)
        {
            lock (this)
            {
                recycledGuids.Enqueue(new Tuple<DateTime, uint>(DateTime.UtcNow, guid));
            }
        }

        public override string ToString()
        {
            lock (this)
            {
                uint total = 0;
                foreach (var pair in availableIDs)
                {
                    total += (pair.end - pair.start) + 1;
                }

                return $"DynamicGuidAllocator: {name}, current: 0x{current:X8}, max: 0x{max:X8}, sequence gap GUIDs available: {total:N0}, recycled GUIDs available: {recycledGuids.Count:N0}";
            }
        }

        public (
            DateTime nextRecycleTime,
            int totalPendingRecycledGuids,
            uint totalSequenceGapGuids
        ) GetRecycleDebugInfo()
        {
            var nextRecycleTime = DateTime.MinValue;
            int totalPendingRecycledGuids;
            uint totalSequenceGapGuids = 0;

            lock (this)
            {
                if (recycledGuids.TryPeek(out var firstRecycledGuid))
                {
                    nextRecycleTime = firstRecycledGuid.Item1 + recycleTime;
                }

                totalPendingRecycledGuids = recycledGuids.Count;

                foreach (var pair in availableIDs)
                {
                    totalSequenceGapGuids += (pair.end - pair.start) + 1;
                }
            }

            return (nextRecycleTime, totalPendingRecycledGuids, totalSequenceGapGuids);
        }
    }

    private static PlayerGuidAllocator playerAlloc;
    private static DynamicGuidAllocator dynamicAlloc;

    // Nothing here is ever saved to the database, so unlike the others this doesn't need to read anything at startup
    private static readonly EphemeralStaticGuidAllocator ephemeralStaticAlloc = new EphemeralStaticGuidAllocator(
        ObjectGuid.EphemeralStaticMin,
        ObjectGuid.EphemeralStaticMax,
        EphemeralStaticGuidAllocator.DefaultHoldTime
    );

    public static void Initialize()
    {
        playerAlloc = new PlayerGuidAllocator(ObjectGuid.PlayerMin, ObjectGuid.PlayerMax, "player");
        dynamicAlloc = new DynamicGuidAllocator(ObjectGuid.DynamicMin, ObjectGuid.DynamicMax, "dynamic");
    }

    /// <summary>
    /// Returns New Player Guid
    /// </summary>
    public static ObjectGuid NewPlayerGuid()
    {
        return new ObjectGuid(playerAlloc.Alloc());
    }

    /// <summary>
    /// These represent items are generated in the world.
    /// Some of them will be saved to the Shard db.
    /// They can be monsters, loot, etc..
    /// </summary>
    public static ObjectGuid NewDynamicGuid()
    {
        return new ObjectGuid(dynamicAlloc.Alloc());
    }

    /// <summary>
    /// A guid for a static object that only exists for as long as an instance does. The world db gives static objects a guid
    /// per landblock, so every instance of a landblock would have the same ones, and they would collide.
    /// These are static guids (they stay in the static range), but they are never saved, and they are never the same as one from the world db.
    /// </summary>
    public static ObjectGuid NewEphemeralStaticGuid()
    {
        return new ObjectGuid(ephemeralStaticAlloc.Alloc());
    }

    /// <summary>
    /// Gives back the guid of a static object that only existed for as long as an instance did
    /// </summary>
    public static void RecycleEphemeralStaticGuid(ObjectGuid guid)
    {
        ephemeralStaticAlloc.Recycle(guid.Full);
    }

    /// <summary>
    /// Guid will be added to the recycle queue, and available for use in GuidAllocator.recycleTime
    /// </summary>
    /// <param name="guid"></param>
    public static void RecycleDynamicGuid(ObjectGuid guid)
    {
        dynamicAlloc.Recycle(guid.Full);
    }

    public static string GetDynamicGuidDebugInfo()
    {
        return dynamicAlloc.ToString();
    }

    public static string GetIdListCommandOutput()
    {
        var playerGuidCurrent = playerAlloc.Current();
        var dynamicGuidCurrent = dynamicAlloc.Current();
        var dynamicDebugInfo = dynamicAlloc.GetRecycleDebugInfo();

        var message = $"The next Player GUID to be allocated is expected to be: 0x{playerGuidCurrent:X}\n";

        if (dynamicDebugInfo.nextRecycleTime == DateTime.MinValue)
        {
            message +=
                $"After {dynamicDebugInfo.totalSequenceGapGuids:N0} sequence gap ids have been consumed, and {dynamicDebugInfo.totalPendingRecycledGuids:N0} recycled ids have been consumed, the next id will be {dynamicGuidCurrent:X8}";
        }
        else
        {
            var nextDynamicIsAvailIn = dynamicDebugInfo.nextRecycleTime - DateTime.UtcNow;

            if (nextDynamicIsAvailIn.TotalSeconds <= 0)
            {
                message +=
                    $"After {dynamicDebugInfo.totalSequenceGapGuids:N0} sequence gap ids have been consumed, and {dynamicDebugInfo.totalPendingRecycledGuids:N0} recycled ids have been consumed, the next of which are available now, the next id will be: 0x{dynamicGuidCurrent:X8}";
            }
            else
            {
                message +=
                    $"After {dynamicDebugInfo.totalSequenceGapGuids:N0} sequence gap ids have been consumed, and {dynamicDebugInfo.totalPendingRecycledGuids:N0} recycled ids have been consumed, the next of which is available in {nextDynamicIsAvailIn.TotalMinutes:N1} m, the next id will be: 0x{dynamicGuidCurrent:X8}";
            }
        }

        return message;
    }
}
