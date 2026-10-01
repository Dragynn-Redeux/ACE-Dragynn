using System.Collections;
using System.Collections.Generic;

namespace ACE.Server.Entity;

class LandblockGroupSplitHelper : IEnumerable<Landblock>
{
    private readonly HashSet<Landblock> landblocks = new HashSet<Landblock>();

    public int Count => landblocks.Count;

    public void Add(Landblock landblock)
    {
        landblocks.Add(landblock);
    }

    public IEnumerator<Landblock> GetEnumerator()
    {
        return landblocks.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool ShouldBeAddedToThisLandblockGroup(Landblock landblock)
    {
        foreach (var value in landblocks)
        {
            if (LandblockGroup.CanShareGroup(value, landblock))
            {
                return true;
            }
        }

        return false;
    }
}
