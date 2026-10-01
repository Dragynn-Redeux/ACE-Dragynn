using System;
using System.Collections.Generic;
using ACE.Entity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class LandblockIdTests
{
    [TestMethod]
    public void LandblockId_EqualityIgnoresCellBits()
    {
        var withCell = new LandblockId(0x01E30001);
        var normalized = new LandblockId(0x01E3FFFF);

        Assert.IsTrue(withCell == normalized);
        Assert.IsFalse(withCell != normalized);
        Assert.IsTrue(withCell.Equals(normalized));
    }

    [TestMethod]
    public void LandblockId_HashCodeAgreesWithEquality()
    {
        var withCell = new LandblockId(0x01E30001);
        var normalized = new LandblockId(0x01E3FFFF);

        Assert.AreEqual(withCell.GetHashCode(), normalized.GetHashCode());
    }

    [TestMethod]
    public void LandblockId_DictionaryLookupIgnoresCellBits()
    {
        var lookup = new Dictionary<LandblockId, string> { { new LandblockId(0x01E3FFFF), "capstone" } };

        Assert.IsTrue(lookup.ContainsKey(new LandblockId(0x01E303B9)));
        Assert.IsTrue(lookup.TryGetValue(new LandblockId(0x01E30001), out var value));
        Assert.AreEqual("capstone", value);
    }

    [TestMethod]
    public void LandblockId_IsComparedWithoutBoxing()
    {
        // sets and dictionaries of landblocks use this instead of Equals(object), which boxes both sides on every comparison
        IEquatable<LandblockId> withCell = new LandblockId(0x01E30001);

        Assert.IsTrue(withCell.Equals(new LandblockId(0x01E3FFFF)));
        Assert.IsFalse(withCell.Equals(new LandblockId(0x01E4FFFF)));
    }

    [TestMethod]
    public void LandblockId_DifferentLandblocksAreNotEqual()
    {
        var a = new LandblockId(0x01E3FFFF);
        var b = new LandblockId(0x01E4FFFF);
        var c = new LandblockId(0x02E3FFFF);

        Assert.IsFalse(a == b);
        Assert.IsFalse(a == c);
        Assert.IsFalse(new Dictionary<LandblockId, string> { { a, "a" } }.ContainsKey(b));
    }
}
