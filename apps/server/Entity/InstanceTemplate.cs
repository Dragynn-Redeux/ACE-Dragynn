using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity;

namespace ACE.Server.Entity;

/// <summary>
/// Describes something that can be instanced: which landblocks it is made of, where players arrive, and where they go when it ends.
/// </summary>
public class InstanceTemplate
{
    public string Name { get; }

    /// <summary>
    /// The landblocks an instance is made of. An instance never contains any other landblock,
    /// whatever is next to these in the world: nothing loads a landblock that isn't in here, and nothing can walk into one.
    /// </summary>
    public IReadOnlyCollection<LandblockId> Footprint { get; }

    /// <summary>
    /// The landblocks of the footprint that are a margin around the place instead of a part of it. They are loaded, so that there is
    /// always ground under the players, but the instance turns a player back as soon as they get into one.
    /// A footprint ends where nothing is loaded, and a player can travel a long way between two position updates, so without a margin
    /// someone who is knocked back, or is fast, could end up in a place that has no landblock.
    /// </summary>
    public IReadOnlyCollection<LandblockId> Boundary { get; }

    /// <summary>
    /// Where players arrive when they enter an instance made from this. This is a position in the instance, not in the persistent world.
    /// </summary>
    public Position EntryPosition { get; }

    /// <summary>
    /// Where players are sent when they leave, when the instance ends, and when they log out or log in again after a crash.
    /// If this is null they go to their sanctuary (the lifestone they are bound to).
    /// </summary>
    public Position ReturnPosition { get; }

    /// <summary>
    /// True if the landblocks only exist as instances: there is nothing at these coordinates in the persistent world.
    /// Anything that ends up there is a mistake, so a player who is saved there is moved out when they log in,
    /// and the persistent world refuses to load these landblocks.
    /// </summary>
    public bool InstanceOnly { get; }

    /// <summary>
    /// True if every player who logs in inside its landblocks gets an instance of their own, and is put in a new one whenever they log in
    /// there again, at the place where they logged out. Where they are saved when they log out inside it is where they are, not where
    /// the template sends players. The training academies, which every new character starts in, are like this.
    /// </summary>
    public bool Personal { get; }

    private readonly HashSet<LandblockId> footprintSet;
    private readonly HashSet<LandblockId> boundarySet;

    public InstanceTemplate(
        string name,
        IEnumerable<LandblockId> footprint,
        Position entryPosition,
        Position returnPosition = null,
        bool instanceOnly = false,
        IEnumerable<LandblockId> boundary = null,
        bool personal = false
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An instance template needs a name", nameof(name));
        }

        // a set, so a landblock listed twice is only there once. LandblockId compares X/Y only, whatever the cell bits are.
        var landblocks = new HashSet<LandblockId>(footprint ?? throw new ArgumentNullException(nameof(footprint)));

        if (landblocks.Count == 0)
        {
            throw new ArgumentException("An instance template needs at least one landblock", nameof(footprint));
        }

        var edge = new HashSet<LandblockId>(boundary ?? Enumerable.Empty<LandblockId>());

        if (!edge.IsSubsetOf(landblocks))
        {
            throw new ArgumentException(
                "The boundary can only be made of landblocks of the footprint",
                nameof(boundary)
            );
        }

        if (edge.Count == landblocks.Count)
        {
            throw new ArgumentException(
                "A template can't be nothing but boundary: players would be turned back from everywhere",
                nameof(boundary)
            );
        }

        Name = name;
        footprintSet = landblocks;
        boundarySet = edge;
        Footprint = landblocks.ToList().AsReadOnly();
        Boundary = edge.ToList().AsReadOnly();
        EntryPosition = entryPosition ?? throw new ArgumentNullException(nameof(entryPosition));
        ReturnPosition = returnPosition;
        InstanceOnly = instanceOnly;
        Personal = personal;
    }

    public bool Contains(LandblockId landblockId)
    {
        return footprintSet.Contains(landblockId);
    }

    public bool HasBoundary => boundarySet.Count > 0;

    /// <summary>
    /// Whether a player who is in this landblock is somewhere they should be turned back from
    /// </summary>
    public bool IsBoundary(LandblockId landblockId)
    {
        return boundarySet.Count > 0 && boundarySet.Contains(landblockId);
    }

    /// <summary>
    /// The landblocks around some landblocks: everything that is within this many landblocks of any of them, and is not one of them.
    /// Landblocks that would be outside of the map (0 to 254) are left out.
    /// </summary>
    public static List<LandblockId> Ring(IEnumerable<LandblockId> landblocks, int width)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A ring can't have a negative width");
        }

        var inside = new HashSet<LandblockId>(landblocks);
        var ring = new HashSet<LandblockId>();

        foreach (var landblock in inside)
        {
            for (var dx = -width; dx <= width; dx++)
            {
                for (var dy = -width; dy <= width; dy++)
                {
                    var x = landblock.LandblockX + dx;
                    var y = landblock.LandblockY + dy;

                    if (x < 0 || x > 254 || y < 0 || y > 254)
                    {
                        continue;
                    }

                    var id = new LandblockId((byte)x, (byte)y);

                    if (!inside.Contains(id))
                    {
                        ring.Add(id);
                    }
                }
            }
        }

        return ring.OrderBy(l => l.LandblockX).ThenBy(l => l.LandblockY).ToList();
    }

    public override string ToString()
    {
        return $"{Name} ({Footprint.Count} landblock{(Footprint.Count == 1 ? "" : "s")})";
    }
}
