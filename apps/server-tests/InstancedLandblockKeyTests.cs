using System.Collections.Generic;
using ACE.Entity;
using ACE.Server.Managers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class InstancedLandblockKeyTests
{
    [TestMethod]
    public void InstancedLandblockKey_TheSameLandblockInDifferentInstancesIsDistinct()
    {
        var id = new LandblockId(0x01E3FFFF);

        var a = new LandblockManager.InstancedLandblockKey(1, id);
        var b = new LandblockManager.InstancedLandblockKey(2, id);

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void InstancedLandblockKey_IgnoresTheCellBitsOfTheLandblockId()
    {
        // callers hand the manager whatever id they have, sometimes with a cell in the low bits, sometimes normalized
        var withCell = new LandblockManager.InstancedLandblockKey(7, new LandblockId(0x01E303B9));
        var normalized = new LandblockManager.InstancedLandblockKey(7, new LandblockId(0x01E3FFFF));

        Assert.AreEqual(withCell, normalized);
        Assert.AreEqual(withCell.GetHashCode(), normalized.GetHashCode());
    }

    [TestMethod]
    public void InstancedLandblockKey_DifferentLandblocksInTheSameInstanceAreDistinct()
    {
        var a = new LandblockManager.InstancedLandblockKey(7, new LandblockId(0x01E3FFFF));
        var b = new LandblockManager.InstancedLandblockKey(7, new LandblockId(0x01E4FFFF));

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void InstancedLandblockKey_WorksAsADictionaryKey()
    {
        var loaded = new Dictionary<LandblockManager.InstancedLandblockKey, string>
        {
            { new LandblockManager.InstancedLandblockKey(1, new LandblockId(0x01E3FFFF)), "first copy" },
            { new LandblockManager.InstancedLandblockKey(2, new LandblockId(0x01E3FFFF)), "second copy" },
        };

        Assert.AreEqual(
            "first copy",
            loaded[new LandblockManager.InstancedLandblockKey(1, new LandblockId(0x01E30001))]
        );
        Assert.AreEqual(
            "second copy",
            loaded[new LandblockManager.InstancedLandblockKey(2, new LandblockId(0x01E30001))]
        );
        Assert.IsFalse(loaded.ContainsKey(new LandblockManager.InstancedLandblockKey(3, new LandblockId(0x01E3FFFF))));
    }
}
