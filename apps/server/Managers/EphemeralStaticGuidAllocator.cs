using System;
using System.Collections.Generic;
using Serilog;

namespace ACE.Server.Managers;

/// <summary>
/// Hands out guids for static objects that only exist for as long as an instance does (see ObjectGuid.EphemeralStaticMin).<para />
/// A guid that has been given back is not handed out again for a while. A client that has seen an object with a guid
/// can get confused by a different object showing up with the same guid soon afterwards.
/// </summary>
public class EphemeralStaticGuidAllocator
{
    private static readonly ILogger _log = Log.ForContext<EphemeralStaticGuidAllocator>();

    /// <summary>
    /// Dynamic guids are held back for the same amount of time
    /// </summary>
    public static readonly TimeSpan DefaultHoldTime = TimeSpan.FromHours(6);

    public const uint InvalidGuid = uint.MaxValue;

    private readonly object sync = new object();
    private readonly uint min;
    private readonly uint max;
    private readonly TimeSpan holdTime;
    private readonly Func<DateTime> utcNow;

    private readonly Queue<(DateTime RecycledAt, uint Guid)> recycled = new Queue<(DateTime, uint)>();
    private readonly HashSet<uint> recycledSet = new HashSet<uint>();

    private uint next;

    public EphemeralStaticGuidAllocator(uint min, uint max, TimeSpan holdTime, Func<DateTime> utcNow = null)
    {
        this.min = min;
        this.max = max;
        this.holdTime = holdTime;
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);

        next = min;
    }

    /// <summary>
    /// The number of guids that have been given back and are waiting to be handed out again
    /// </summary>
    public int Recycled
    {
        get
        {
            lock (sync)
            {
                return recycled.Count;
            }
        }
    }

    /// <summary>
    /// Returns a guid that nothing else is using, or InvalidGuid if there are none left
    /// </summary>
    public uint Alloc()
    {
        lock (sync)
        {
            // one that was given back long enough ago
            if (recycled.TryPeek(out var oldest) && utcNow() - oldest.RecycledAt >= holdTime)
            {
                Dequeue();
                return oldest.Guid;
            }

            // one that has never been used
            if (next <= max)
            {
                return next++;
            }

            // Every guid has been handed out at least once. Use the one that has been back the longest, even though it hasn't waited.
            if (recycled.Count > 0)
            {
                _log.Warning(
                    "Out of ephemeral static GUIDs that have never been used, reusing one that was given back less than {HoldTime} ago",
                    holdTime
                );

                return Dequeue().Guid;
            }

            _log.Fatal("Out of ephemeral static GUIDs!");
            return InvalidGuid;
        }
    }

    /// <summary>
    /// Gives a guid back. It will not be handed out again until it has been back for the hold time.
    /// Guids from outside the range, and guids that are already back, are ignored.
    /// </summary>
    public void Recycle(uint guid)
    {
        if (guid < min || guid > max)
        {
            return;
        }

        lock (sync)
        {
            // a guid can't be handed out twice, so it can't be in the queue twice, and one that was never handed out can't be given back
            if (guid >= next || !recycledSet.Add(guid))
            {
                return;
            }

            recycled.Enqueue((utcNow(), guid));
        }
    }

    private (DateTime RecycledAt, uint Guid) Dequeue()
    {
        var item = recycled.Dequeue();
        recycledSet.Remove(item.Guid);
        return item;
    }
}
