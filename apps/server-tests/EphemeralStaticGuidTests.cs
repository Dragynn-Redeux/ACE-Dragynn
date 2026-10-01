using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity;
using ACE.Server.Managers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class EphemeralStaticGuidTests
{
    private DateTime now;

    private EphemeralStaticGuidAllocator NewAllocator(uint min, uint max, int holdMinutes = 60)
    {
        now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new EphemeralStaticGuidAllocator(min, max, TimeSpan.FromMinutes(holdMinutes), () => now);
    }

    [TestMethod]
    public void EphemeralStaticRange_IsPartOfTheStaticRangeAndNothingElse()
    {
        // 0x7XXYY000 is landblock XXYY. No landblock has an X of 0xFF, so 0x7FF00000 and up belongs to nobody.
        Assert.IsTrue(ObjectGuid.IsEphemeralStatic(ObjectGuid.EphemeralStaticMin));
        Assert.IsTrue(ObjectGuid.IsEphemeralStatic(ObjectGuid.EphemeralStaticMax));
        Assert.IsFalse(ObjectGuid.IsEphemeralStatic(ObjectGuid.EphemeralStaticMin - 1));
        Assert.IsFalse(ObjectGuid.IsEphemeralStatic(0x7E74E001)); // Hebian-To, a guid from the world db
        Assert.IsFalse(ObjectGuid.IsEphemeralStatic(ObjectGuid.DynamicMin));
        Assert.IsFalse(ObjectGuid.IsEphemeralStatic(ObjectGuid.PlayerMin));

        Assert.IsTrue(ObjectGuid.IsStatic(ObjectGuid.EphemeralStaticMin));
        Assert.IsTrue(ObjectGuid.IsStatic(ObjectGuid.EphemeralStaticMax));
    }

    [TestMethod]
    public void EphemeralStaticRange_HasNoneOfTheStaticGuidsThatTheWorldDatabaseNamesInAWeenie()
    {
        // The result of this query on ace_world (2026-09-20), 38 rows that are all ActivationTarget (property 16), 35 different guids:
        //   SELECT object_Id, type, value FROM weenie_properties_i_i_d WHERE value BETWEEN 1879048192 AND 2147483647;
        // Those are guids of statics that were written into a weenie. In an instance the statics have other guids, and what refers to the
        // old one is translated (WorldInstance.TranslateWorldGuid), which only works if no such guid could also be one made in an instance.
        uint[] activationTargets =
        {
            1918373954,
            1980014800,
            1980014600,
            1977929902,
            1985237108,
            1985237095,
            1985237070,
            1985237106,
            1985237115,
            1985237117,
            1985237118,
            1985237121,
            1985237122,
            1985237124,
            1985241226,
            1985241214,
            1985241235,
            1985241231,
            1982083307,
            1975828599,
            2045460487,
            1980010542,
            1977929729,
            1977929731,
            1984200705,
            1984200704,
            2064207873,
            1980010563,
            2015944705,
            1880039427,
            1978990654,
            1997606914,
            1978994750,
            1998655488,
            1978986558
        };

        Assert.AreEqual(35, activationTargets.Distinct().Count());

        foreach (var guid in activationTargets)
        {
            Assert.IsTrue(ObjectGuid.IsStatic(guid), $"0x{guid:X8} is a static guid");
            Assert.IsFalse(
                ObjectGuid.IsEphemeralStatic(guid),
                $"0x{guid:X8} must not be in the range for the statics of instances"
            );

            // 0x7, the landblock, and an index: none of them is in a landblock with an X of 0xFF
            Assert.IsTrue(((guid >> 20) & 0xFF) != 0xFF, $"0x{guid:X8}");
        }
    }

    [TestMethod]
    public void EphemeralStaticGuid_StaysStaticSoEverythingThatTreatsStaticsSpeciallyStillDoes()
    {
        // Decay, pickup and stacking all look at IsStatic()/IsDynamic(), so a static object in an instance must not turn into a dynamic one
        var guid = new ObjectGuid(ObjectGuid.EphemeralStaticMin + 5);

        Assert.IsTrue(guid.IsStatic());
        Assert.IsTrue(guid.IsEphemeralStatic());
        Assert.IsFalse(guid.IsDynamic());
        Assert.AreEqual(GuidType.Static, guid.Type);
        Assert.IsFalse(new ObjectGuid(0x7E74E001).IsEphemeralStatic());
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_HandsOutDifferentGuidsInsideTheRange()
    {
        var allocator = NewAllocator(100, 1000);

        var guids = Enumerable.Range(0, 50).Select(_ => allocator.Alloc()).ToList();

        Assert.AreEqual(50, guids.Distinct().Count());
        Assert.IsTrue(guids.All(g => g >= 100 && g <= 1000));
        Assert.AreEqual(100u, guids[0]);
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_DoesNotReuseAGuidUntilItHasBeenBackForTheHoldTime()
    {
        var allocator = NewAllocator(100, 1000, holdMinutes: 60);

        var first = allocator.Alloc();
        var second = allocator.Alloc();
        allocator.Recycle(first);

        now = now.AddMinutes(59);
        var third = allocator.Alloc();
        Assert.AreNotEqual(first, third, "handed out again before it had waited");
        Assert.AreNotEqual(second, third);

        now = now.AddMinutes(2);
        Assert.AreEqual(first, allocator.Alloc(), "should be handed out again once it has waited");
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_ReusesTheOneThatHasBeenBackTheLongestFirst()
    {
        var allocator = NewAllocator(100, 1000, holdMinutes: 10);
        var a = allocator.Alloc();
        var b = allocator.Alloc();

        allocator.Recycle(a);
        now = now.AddMinutes(1);
        allocator.Recycle(b);
        now = now.AddMinutes(30);

        Assert.AreEqual(a, allocator.Alloc());
        Assert.AreEqual(b, allocator.Alloc());
        Assert.AreEqual(0, allocator.Recycled);
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_CanNotHandOutTheSameGuidTwiceByBeingGivenItBackTwice()
    {
        // Destroying an object twice must not put its guid in the queue twice, or two objects would end up with it
        var allocator = NewAllocator(100, 1000, holdMinutes: 10);
        var guid = allocator.Alloc();

        allocator.Recycle(guid);
        allocator.Recycle(guid);
        Assert.AreEqual(1, allocator.Recycled);

        now = now.AddMinutes(11);
        var again = allocator.Alloc();
        var another = allocator.Alloc();

        Assert.AreEqual(guid, again);
        Assert.AreNotEqual(guid, another);
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_IgnoresGuidsThatAreNotItsToTakeBack()
    {
        var allocator = NewAllocator(100, 1000);
        allocator.Alloc(); // 100 is the only one handed out so far

        allocator.Recycle(5); // outside the range
        allocator.Recycle(2000); // outside the range
        allocator.Recycle(500); // inside the range, but never handed out

        Assert.AreEqual(0, allocator.Recycled);
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_RunsOutOfGuidsInsteadOfWrappingAround()
    {
        var allocator = NewAllocator(10, 12);

        Assert.AreEqual(10u, allocator.Alloc());
        Assert.AreEqual(11u, allocator.Alloc());
        Assert.AreEqual(12u, allocator.Alloc());
        Assert.AreEqual(EphemeralStaticGuidAllocator.InvalidGuid, allocator.Alloc());
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_WhenEveryGuidHasBeenUsedItReusesTheOldestGivenBackOne()
    {
        // Better than failing to spawn: reuse a guid even though it hasn't waited the full hold time
        var allocator = NewAllocator(10, 11, holdMinutes: 600);
        var a = allocator.Alloc();
        allocator.Alloc();
        Assert.AreEqual(EphemeralStaticGuidAllocator.InvalidGuid, allocator.Alloc());

        allocator.Recycle(a);

        Assert.AreEqual(a, allocator.Alloc());
        Assert.AreEqual(EphemeralStaticGuidAllocator.InvalidGuid, allocator.Alloc());
    }

    [TestMethod]
    public void EphemeralStaticGuidAllocator_HandsOutUniqueGuidsWhenUsedFromManyThreads()
    {
        var allocator = new EphemeralStaticGuidAllocator(1000, 1000 + 100_000, TimeSpan.FromHours(1));
        var all = new System.Collections.Concurrent.ConcurrentBag<uint>();

        System.Threading.Tasks.Parallel.For(
            0,
            8,
            _ =>
            {
                for (var i = 0; i < 5000; i++)
                {
                    all.Add(allocator.Alloc());
                }
            }
        );

        Assert.AreEqual(40_000, all.Count);
        Assert.AreEqual(40_000, new HashSet<uint>(all).Count);
    }

    [TestMethod]
    public void GuidManager_HandsOutEphemeralStaticGuidsThatAreStaticButNeverFromTheWorldDb()
    {
        // GuidManager.Initialize() needs a database, this one doesn't
        var first = GuidManager.NewEphemeralStaticGuid();
        var second = GuidManager.NewEphemeralStaticGuid();

        Assert.AreNotEqual(first, second);
        Assert.IsTrue(first.IsEphemeralStatic() && first.IsStatic() && !first.IsDynamic());
        Assert.IsTrue(second.IsEphemeralStatic());

        GuidManager.RecycleEphemeralStaticGuid(first);
        GuidManager.RecycleEphemeralStaticGuid(second);
    }
}
