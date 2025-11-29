using System.Linq;
using ACE.Server.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class ShroudZoneConfigTests
{
    [TestMethod]
    public void ParsesValidEntries()
    {
        var raw = "0xD2A80024 [100.684067 87.626068 20.004999] 0.015540 0.000000 0.000000 0.999879|10|40";
        var config = new ShroudZoneConfig(raw);

        Assert.AreEqual(1, config.Zones.Count);

        var zone = config.Zones.Single();
        Assert.AreEqual((uint)0xD2A8, zone.Landblock);
        Assert.AreEqual(10f, zone.Radius);
        Assert.AreEqual(40f, zone.MaxDistance);
        Assert.AreEqual(100.684067f, zone.Center.PositionX, 0.0001f);
        Assert.AreEqual(87.626068f, zone.Center.PositionY, 0.0001f);
        Assert.AreEqual(20.004999f, zone.Center.PositionZ, 0.0001f);
    }

    [TestMethod]
    public void IgnoresInvalidEntries()
    {
        var raw = "invalid entry that should be skipped";
        var config = new ShroudZoneConfig(raw);

        Assert.AreEqual(0, config.Zones.Count);
    }
}
