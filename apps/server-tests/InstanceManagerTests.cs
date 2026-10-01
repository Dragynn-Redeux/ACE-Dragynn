using System;
using System.Linq;
using ACE.Entity;
using ACE.Server.Entity;
using ACE.Server.Managers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class InstanceManagerTests
{
    private Func<DateTime> previousClock;
    private Func<TimeSpan> previousTimeout;
    private DateTime now;

    private static readonly LandblockId Dungeon = new LandblockId(0x01E3FFFF);
    private static readonly LandblockId Neighbour = new LandblockId(0x01E4FFFF);

    [TestInitialize]
    public void UseAFakeClock()
    {
        previousClock = InstanceManager.UtcNow;
        previousTimeout = InstanceManager.EmptyTimeout;

        now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        InstanceManager.UtcNow = () => now;
        InstanceManager.EmptyTimeout = () => TimeSpan.FromMinutes(15);
    }

    [TestCleanup]
    public void RestoreTheClock()
    {
        InstanceManager.UtcNow = previousClock;
        InstanceManager.EmptyTimeout = previousTimeout;
    }

    private static Position At(uint cell)
    {
        return new Position(cell, 10f, 10f, 0f, 0f, 0f, 0f, 1f);
    }

    // The instance manager is static and shared by every test, so every test makes its own templates and only looks at its own instances
    private static InstanceTemplate NewTemplate(bool instanceOnly = false, params LandblockId[] footprint)
    {
        return new InstanceTemplate(
            "test-" + Guid.NewGuid(),
            footprint.Length > 0 ? footprint : new[] { Dungeon },
            At(0x01E30100),
            At(0xA9B40019),
            instanceOnly
        );
    }

    [TestMethod]
    public void InstanceTemplate_NeedsANameLandblocksAndAnEntryPosition()
    {
        Assert.ThrowsException<ArgumentException>(() => new InstanceTemplate(" ", new[] { Dungeon }, At(0x01E30100)));
        Assert.ThrowsException<ArgumentException>(() => new InstanceTemplate("x", new LandblockId[0], At(0x01E30100)));
        Assert.ThrowsException<ArgumentNullException>(() => new InstanceTemplate("x", new[] { Dungeon }, null));
        Assert.ThrowsException<ArgumentNullException>(() => new InstanceTemplate("x", null, At(0x01E30100)));
    }

    [TestMethod]
    public void InstanceTemplate_ListsEachLandblockOnceWhateverItsCellBits()
    {
        var template = new InstanceTemplate(
            "x",
            new[] { new LandblockId(0x01E30001), new LandblockId(0x01E3FFFF), new LandblockId(0x01E303B9) },
            At(0x01E30100)
        );

        Assert.AreEqual(1, template.Footprint.Count);
        Assert.IsTrue(template.Contains(new LandblockId(0x01E30100)));
        Assert.IsFalse(template.Contains(Neighbour));
    }

    [TestMethod]
    public void InstanceManager_GivesEveryInstanceItsOwnIdAndNeverTheIdOfThePersistentWorld()
    {
        var template = NewTemplate();

        var first = InstanceManager.Register(template);
        var second = InstanceManager.Register(template);

        Assert.AreNotEqual(Landblock.PersistentInstance, first.Id);
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.IsTrue(second.Id > first.Id);
        Assert.AreSame(first, InstanceManager.Get(first.Id));
    }

    [TestMethod]
    public void InstanceManager_AnInstanceContainsOnlyTheLandblocksOfItsTemplate()
    {
        var instance = InstanceManager.Register(NewTemplate());

        Assert.IsTrue(InstanceManager.IsInFootprint(instance.Id, Dungeon));
        Assert.IsTrue(
            InstanceManager.IsInFootprint(instance.Id, new LandblockId(0x01E303B9)),
            "cell bits don't matter"
        );
        Assert.IsFalse(
            InstanceManager.IsInFootprint(instance.Id, Neighbour),
            "what is next to it in the world is not part of it"
        );
        Assert.IsFalse(
            InstanceManager.IsInFootprint(instance.Id + 1000, Dungeon),
            "an instance that doesn't exist contains nothing"
        );

        // the persistent world contains everything
        Assert.IsTrue(InstanceManager.IsInFootprint(Landblock.PersistentInstance, Neighbour));
    }

    [TestMethod]
    public void LandblockManager_NeverLoadsALandblockForAnInstanceThatDoesNotExistOrForAPlaceOutsideIt()
    {
        var instance = InstanceManager.Register(NewTemplate());

        Assert.IsNull(LandblockManager.GetLandblock(Dungeon, instance.Id + 1000, false), "no such instance");
        Assert.IsNull(LandblockManager.GetLandblock(Neighbour, instance.Id, false), "not part of the instance");
        Assert.IsFalse(LandblockManager.IsLoaded(Neighbour, instance.Id));
    }

    [TestMethod]
    public void InstanceManager_NobodyCanBeSentIntoAnInstanceThatIsShuttingDown()
    {
        var instance = InstanceManager.Register(NewTemplate());
        Assert.IsTrue(InstanceManager.CanEnter(instance.Id, Dungeon));
        Assert.IsFalse(InstanceManager.CanEnter(instance.Id, Neighbour));
        Assert.IsFalse(InstanceManager.CanEnter(instance.Id + 1000, Dungeon));
        Assert.IsTrue(InstanceManager.CanEnter(Landblock.PersistentInstance, Neighbour));

        Assert.IsTrue(InstanceManager.Close(instance.Id));

        Assert.IsTrue(instance.IsClosing);
        Assert.IsFalse(InstanceManager.CanEnter(instance.Id, Dungeon));
    }

    [TestMethod]
    public void InstanceManager_FindsTheInstanceOfATemplateThatBelongsToAnOwner()
    {
        var template = NewTemplate();
        var ownerA = new object();
        var ownerB = new object();

        var instance = InstanceManager.Register(template, ownerA);

        Assert.AreSame(instance, InstanceManager.Find(template, ownerA));
        Assert.IsNull(InstanceManager.Find(template, ownerB));

        InstanceManager.Close(instance.Id);
        Assert.IsNull(InstanceManager.Find(template, ownerA), "one that is shutting down can't be handed out again");
    }

    [TestMethod]
    public void InstanceManager_ATeleportStaysInTheInstanceOnlyIfItIsToSomewhereInsideIt()
    {
        var instance = InstanceManager.Register(NewTemplate());

        // a portal inside a dungeon
        Assert.AreEqual(instance.Id, InstanceManager.ResolveDestinationInstance(instance.Id, At(0x01E30200)));

        // a recall, a lifestone
        Assert.AreEqual(
            Landblock.PersistentInstance,
            InstanceManager.ResolveDestinationInstance(instance.Id, At(0xA9B40019))
        );

        // nobody gets into an instance from the persistent world by accident, even to the same coordinates
        Assert.AreEqual(
            Landblock.PersistentInstance,
            InstanceManager.ResolveDestinationInstance(Landblock.PersistentInstance, At(0x01E30200))
        );
    }

    [TestMethod]
    public void InstanceManager_FirstEnterablePositionSkipsNullsAndUnreachableOnes()
    {
        var island = new LandblockId(0x8484FFFF);
        InstanceManager.RegisterTemplate(NewTemplate(true, island));

        var onTheIsland = At(0x84840100);
        var ordinary = At(0xA9B40019);

        Assert.AreSame(ordinary, InstanceManager.FirstEnterablePosition(ordinary), "the first one that is fine is used as is");
        Assert.AreSame(
            ordinary,
            InstanceManager.FirstEnterablePosition(null, onTheIsland, ordinary),
            "a null, then one that has become instance only, are both skipped"
        );
    }

    [TestMethod]
    public void InstanceManager_FirstEnterablePositionFallsBackWhenNothingIsReachable()
    {
        var island = new LandblockId(0x8585FFFF);
        InstanceManager.RegisterTemplate(NewTemplate(true, island));

        var fallback = InstanceManager.FirstEnterablePosition();
        Assert.AreEqual(WorldManager.DefaultFallbackPosition.LandblockId, fallback.LandblockId, "no candidates at all");

        var onlyBadOne = InstanceManager.FirstEnterablePosition(null, At(0x85850100));
        Assert.AreEqual(WorldManager.DefaultFallbackPosition.LandblockId, onlyBadOne.LandblockId, "a null and an instance-only one");
    }

    [TestMethod]
    public void InstanceManager_AnInstanceNobodyEntersIsDeletedAfterTheTimeout()
    {
        var instance = InstanceManager.Register(NewTemplate());

        InstanceManager.Tick(now.AddMinutes(14), TimeSpan.FromMinutes(15));
        Assert.IsNotNull(InstanceManager.Get(instance.Id), "not yet");

        InstanceManager.Tick(now.AddMinutes(16), TimeSpan.FromMinutes(15));
        Assert.IsNull(InstanceManager.Get(instance.Id));
    }

    [TestMethod]
    public void InstanceManager_TheTimeoutOnlyStartsOnceTheLastPlayerHasLeft()
    {
        var instance = InstanceManager.Register(NewTemplate());

        InstanceManager.OnMemberEntered(instance.Id, 0x50000001);
        InstanceManager.OnMemberEntered(instance.Id, 0x50000002);
        Assert.AreEqual(2, instance.MemberCount);

        now = now.AddHours(3);
        InstanceManager.Tick(now, TimeSpan.FromMinutes(15));
        Assert.IsNotNull(InstanceManager.Get(instance.Id), "there are players in it, however long it has been open");

        InstanceManager.OnMemberLeft(instance.Id, 0x50000001);
        now = now.AddMinutes(20);
        InstanceManager.Tick(now, TimeSpan.FromMinutes(15));
        Assert.IsNotNull(InstanceManager.Get(instance.Id), "one is still in it");

        InstanceManager.OnMemberLeft(instance.Id, 0x50000002);
        now = now.AddMinutes(14);
        InstanceManager.Tick(now, TimeSpan.FromMinutes(15));
        Assert.IsNotNull(InstanceManager.Get(instance.Id), "the last one left 14 minutes ago");

        now = now.AddMinutes(2);
        InstanceManager.Tick(now, TimeSpan.FromMinutes(15));
        Assert.IsNull(InstanceManager.Get(instance.Id));
    }

    [TestMethod]
    public void InstanceManager_SomeoneComingBackRestartsTheTimeout()
    {
        var instance = InstanceManager.Register(NewTemplate());

        InstanceManager.OnMemberEntered(instance.Id, 0x50000001);
        InstanceManager.OnMemberLeft(instance.Id, 0x50000001);

        now = now.AddMinutes(10);
        InstanceManager.OnMemberEntered(instance.Id, 0x50000001);
        InstanceManager.OnMemberLeft(instance.Id, 0x50000001);

        now = now.AddMinutes(10);
        InstanceManager.Tick(now, TimeSpan.FromMinutes(15));
        Assert.IsNotNull(
            InstanceManager.Get(instance.Id),
            "20 minutes since it opened, but only 10 since the last player left"
        );

        now = now.AddMinutes(6);
        InstanceManager.Tick(now, TimeSpan.FromMinutes(15));
        Assert.IsNull(InstanceManager.Get(instance.Id));
    }

    [TestMethod]
    public void InstanceManager_AShutDownInstanceIsDeletedAsSoonAsItIsEmpty()
    {
        var instance = InstanceManager.Register(NewTemplate());

        InstanceManager.Close(instance.Id);

        // nobody is in it, so there is nothing to wait for and no reason to wait for the timeout
        InstanceManager.Tick(now.AddSeconds(1), TimeSpan.FromMinutes(15));
        Assert.IsNull(InstanceManager.Get(instance.Id));
    }

    [TestMethod]
    public void InstanceManager_TheGuidsOfPlayersWhoAreNotOnlineDoNotKeepAShutDownInstanceOpen()
    {
        var instance = InstanceManager.Register(NewTemplate());
        InstanceManager.OnMemberEntered(instance.Id, 0x5FFFFFF0); // nobody is online with this guid

        InstanceManager.Close(instance.Id);

        Assert.AreEqual(0, instance.MemberCount);

        InstanceManager.Tick(now.AddSeconds(1), TimeSpan.FromMinutes(15));
        Assert.IsNull(InstanceManager.Get(instance.Id));
    }

    [TestMethod]
    public void InstanceManager_MembersOfThePersistentWorldAndUnknownInstancesAreIgnored()
    {
        var instance = InstanceManager.Register(NewTemplate());

        InstanceManager.OnMemberEntered(Landblock.PersistentInstance, 0x50000001);
        InstanceManager.OnMemberEntered(instance.Id + 1000, 0x50000001);
        InstanceManager.OnMemberLeft(Landblock.PersistentInstance, 0x50000001);
        InstanceManager.OnMemberLeft(instance.Id + 1000, 0x50000001);

        Assert.AreEqual(0, instance.MemberCount);
    }

    [TestMethod]
    public void InstanceManager_OnlyTemplatesThatAreInstanceOnlyReserveTheirLandblocks()
    {
        var reservedBlock = new LandblockId(0x7A7AFFFF);
        var ordinaryBlock = new LandblockId(0x7B7BFFFF);

        InstanceManager.RegisterTemplate(NewTemplate(true, reservedBlock));
        InstanceManager.RegisterTemplate(NewTemplate(false, ordinaryBlock));

        Assert.IsNotNull(InstanceManager.GetInstanceOnlyTemplate(reservedBlock));
        Assert.IsNull(InstanceManager.GetInstanceOnlyTemplate(ordinaryBlock));
        Assert.IsNull(InstanceManager.GetInstanceOnlyTemplate(new LandblockId(0x7C7CFFFF)));
    }

    [TestMethod]
    public void InstanceManager_ATemplateCanBeFoundByNameWhateverTheCase()
    {
        var template = NewTemplate();
        InstanceManager.RegisterTemplate(template);

        Assert.AreSame(template, InstanceManager.GetTemplate(template.Name.ToUpperInvariant()));
        Assert.IsNull(InstanceManager.GetTemplate("there is no such template"));
    }

    [TestMethod]
    public void InstanceManager_TheTimeoutIsFifteenMinutesUnlessTheServerPropertyIsChanged()
    {
        // the server property is read through the database, so what is checked here is the default it falls back to
        Assert.AreEqual(15L, DefaultPropertyManager.DefaultLongProperties["instance_empty_timeout_minutes"].Item);
    }

    [TestMethod]
    public void InstanceManager_ALandblockThatOnlyExistsAsAnInstanceIsNotThereInThePersistentWorld()
    {
        var island = new LandblockId(0x7D7DFFFF);
        var elsewhere = new LandblockId(0x7E7EFFFF);
        var template = NewTemplate(true, island);

        Assert.IsFalse(InstanceManager.IsInstanceOnly(island), "nothing says so before the template is registered");

        InstanceManager.RegisterTemplate(template);
        var instance = InstanceManager.Register(template);

        Assert.IsTrue(InstanceManager.IsInstanceOnly(island));
        Assert.IsTrue(InstanceManager.IsInstanceOnly(new LandblockId(0x7D7D0100)), "cell bits don't matter");
        Assert.IsFalse(InstanceManager.IsInstanceOnly(elsewhere));

        // nobody can be sent there in the persistent world, but they can be sent to an instance of it
        Assert.IsFalse(InstanceManager.CanEnter(Landblock.PersistentInstance, island));
        Assert.IsTrue(InstanceManager.CanEnter(Landblock.PersistentInstance, elsewhere));
        Assert.IsTrue(InstanceManager.CanEnter(instance.Id, island));
    }

    [TestMethod]
    public void InstanceManager_ATemplateThatIsNotInstanceOnlyLeavesItsLandblocksInThePersistentWorld()
    {
        var landblock = new LandblockId(0x7F7FFFFF);

        InstanceManager.RegisterTemplate(NewTemplate(false, landblock));

        Assert.IsFalse(InstanceManager.IsInstanceOnly(landblock));
        Assert.IsTrue(InstanceManager.CanEnter(Landblock.PersistentInstance, landblock));
    }

    [TestMethod]
    public void InstanceManager_ReplacingATemplateChangesWhichLandblocksAreInstanceOnly()
    {
        var name = "test-" + Guid.NewGuid();
        var first = new LandblockId(0x8080FFFF);
        var second = new LandblockId(0x8081FFFF);

        InstanceManager.RegisterTemplate(new InstanceTemplate(name, new[] { first }, At(0x80800100), null, true));
        Assert.IsTrue(InstanceManager.IsInstanceOnly(first));

        InstanceManager.RegisterTemplate(new InstanceTemplate(name, new[] { second }, At(0x80810100), null, true));
        Assert.IsFalse(InstanceManager.IsInstanceOnly(first), "the earlier template is gone");
        Assert.IsTrue(InstanceManager.IsInstanceOnly(second));
    }

    [TestMethod]
    public void LandblockManager_WillNotLoadALandblockThatOnlyExistsAsAnInstanceInThePersistentWorld()
    {
        // Nothing has to be set up for this: the landblock is refused before there is anything to load
        var island = new LandblockId(0x8181FFFF);

        InstanceManager.RegisterTemplate(NewTemplate(true, island));

        Assert.IsNull(LandblockManager.GetLandblock(island, false));
        Assert.IsNull(LandblockManager.GetLandblock(island, true, true));
        Assert.IsFalse(LandblockManager.IsLoaded(island));
    }

    [TestMethod]
    public void WorldInstance_FindsTheGuidAStaticHasInTheInstanceFromTheGuidTheWorldDatabaseGaveIt()
    {
        // 0x76545074 is a guid from the world database. In an instance the object has one of its own, and a weenie that says
        // "activate 0x76545074" (its ActivationTarget) has to find the copy that is in the instance
        var instance = InstanceManager.Register(NewTemplate());

        instance.MapWorldGuid(0x76545074, 0x7FF00010);

        Assert.AreEqual(0x7FF00010u, instance.TranslateWorldGuid(0x76545074));
        Assert.AreEqual(0x7FF00010u, InstanceManager.TranslateWorldGuid(instance.Id, 0x76545074));
    }

    [TestMethod]
    public void WorldInstance_LeavesAGuidThatIsNotFromTheWorldDatabaseAsItIs()
    {
        // what is made in the instance has the same guid wherever it is looked up: the statics themselves,
        // the activation target a linked parent gives its children, and anything that is not static
        var instance = InstanceManager.Register(NewTemplate());

        instance.MapWorldGuid(0x76545074, 0x7FF00010);

        Assert.AreEqual(0x7FF00010u, instance.TranslateWorldGuid(0x7FF00010));
        Assert.AreEqual(0x80000123u, instance.TranslateWorldGuid(0x80000123));
        Assert.AreEqual(
            0x76545075u,
            instance.TranslateWorldGuid(0x76545075),
            "a guid the instance has no copy for is left alone"
        );
    }

    [TestMethod]
    public void WorldInstance_TwoInstancesOfTheSameLandblockDoNotShareTheirGuids()
    {
        var template = NewTemplate();
        var first = InstanceManager.Register(template);
        var second = InstanceManager.Register(template);

        first.MapWorldGuid(0x76545074, 0x7FF00010);
        second.MapWorldGuid(0x76545074, 0x7FF00020);

        Assert.AreEqual(0x7FF00010u, InstanceManager.TranslateWorldGuid(first.Id, 0x76545074));
        Assert.AreEqual(0x7FF00020u, InstanceManager.TranslateWorldGuid(second.Id, 0x76545074));
    }

    [TestMethod]
    public void InstanceManager_TheGuidsOfThePersistentWorldAreNeverTranslated()
    {
        var instance = InstanceManager.Register(NewTemplate());

        instance.MapWorldGuid(0x76545074, 0x7FF00010);

        Assert.AreEqual(0x76545074u, InstanceManager.TranslateWorldGuid(Landblock.PersistentInstance, 0x76545074));
        Assert.AreEqual(
            0x76545074u,
            InstanceManager.TranslateWorldGuid(instance.Id + 1000, 0x76545074),
            "an instance that doesn't exist has nothing to translate with"
        );
    }

    [TestMethod]
    public void InstanceManager_DescribesAnInstanceTheWayAPersonReadsIt()
    {
        var template = NewTemplate();
        var instance = InstanceManager.Register(template);

        StringAssert.Contains(InstanceManager.Describe(Landblock.PersistentInstance), "persistent world");
        StringAssert.Contains(InstanceManager.Describe(instance.Id), $"{instance.Id} ({template.Name}, 0 players)");

        InstanceManager.OnMemberEntered(instance.Id, 0x50000001);
        StringAssert.Contains(InstanceManager.Describe(instance.Id), "1 player)");

        InstanceManager.OnMemberEntered(instance.Id, 0x50000002);
        StringAssert.Contains(InstanceManager.Describe(instance.Id), "2 players)");

        InstanceManager.Close(instance.Id);
        StringAssert.Contains(InstanceManager.Describe(instance.Id), "shutting down");

        StringAssert.Contains(InstanceManager.Describe(instance.Id + 1000), "does not exist");
    }

    [TestMethod]
    public void InstanceManager_SaysWhereALandblockIsInRelationToTheInstanceItIsLookedAtFrom()
    {
        var inside = new LandblockId(0x50, 0x50);
        var ring = new LandblockId(0x51, 0x50);
        var instance = InstanceManager.Register(
            new InstanceTemplate(
                "test-" + Guid.NewGuid(),
                new[] { inside, ring },
                At(0x50500100),
                null,
                false,
                new[] { ring }
            )
        );

        StringAssert.Contains(InstanceManager.DescribePlace(instance.Id, inside), "inside the instance");
        StringAssert.Contains(InstanceManager.DescribePlace(instance.Id, ring), "ring");
        StringAssert.Contains(InstanceManager.DescribePlace(instance.Id, new LandblockId(0x60, 0x60)), "OUTSIDE");
        StringAssert.Contains(InstanceManager.DescribePlace(instance.Id + 1000, inside), "does not exist");
        StringAssert.Contains(InstanceManager.DescribePlace(Landblock.PersistentInstance, inside), "persistent world");

        var islandOnly = new LandblockId(0x8383FFFF);
        InstanceManager.RegisterTemplate(NewTemplate(true, islandOnly));

        StringAssert.Contains(
            InstanceManager.DescribePlace(Landblock.PersistentInstance, islandOnly),
            "only exists in instances"
        );
    }

    [TestMethod]
    public void InstanceManager_OnlyObjectsMadeWhileTheServerRanAndLyingOnTheGroundCanBeMovedBetweenInstances()
    {
        var dynamicGuid = new ObjectGuid(ObjectGuid.DynamicMin + 5);

        Assert.IsNull(InstanceManager.WhyNotMovable(dynamicGuid, false, true, false, false));

        StringAssert.Contains(InstanceManager.WhyNotMovable(dynamicGuid, true, true, false, false), "/instance enter");
        StringAssert.Contains(
            InstanceManager.WhyNotMovable(dynamicGuid, false, false, false, false),
            "not on the ground"
        );
        StringAssert.Contains(InstanceManager.WhyNotMovable(dynamicGuid, false, true, true, false), "generator");
        StringAssert.Contains(InstanceManager.WhyNotMovable(dynamicGuid, false, true, false, true), "keeps count");

        // what a landblock is made of: a guid from the world database, or the one it has in an instance
        StringAssert.Contains(
            InstanceManager.WhyNotMovable(new ObjectGuid(0x76545074), false, true, false, false),
            "static object"
        );
        StringAssert.Contains(
            InstanceManager.WhyNotMovable(new ObjectGuid(ObjectGuid.EphemeralStaticMin + 3), false, true, false, false),
            "static object"
        );
    }

    [TestMethod]
    public void InstanceManager_AnObjectCantBeMovedToTheInstanceItIsAlreadyIn()
    {
        // a move to where it already is takes it out of its landblock and back into the same one, and it is left invisible
        StringAssert.Contains(InstanceManager.WhyNotMovableTo(0, 0), "persistent world");
        StringAssert.Contains(InstanceManager.WhyNotMovableTo(3, 3), "instance 3");

        Assert.IsNull(InstanceManager.WhyNotMovableTo(0, 3));
        Assert.IsNull(InstanceManager.WhyNotMovableTo(3, 0));
        Assert.IsNull(InstanceManager.WhyNotMovableTo(3, 4));
    }

    [TestMethod]
    public void InstanceManager_TwoPlayersWhoArriveAtTheSameMomentGetTheSameInstance()
    {
        // the two members of a fellowship who go through a portal together must not make one instance each
        var template = NewTemplate();
        var owner = new object();

        var results = Enumerable
            .Range(0, 16)
            .AsParallel()
            .WithDegreeOfParallelism(16)
            .Select(_ =>
            {
                var instance = InstanceManager.FindOrRegister(template, owner, out var created);
                return (instance, created);
            })
            .ToList();

        Assert.AreEqual(1, results.Count(r => r.created), "only one of them makes it");
        Assert.AreEqual(1, results.Select(r => r.instance.Id).Distinct().Count());
    }

    [TestMethod]
    public void InstanceManager_AnInstanceThatIsShuttingDownIsNotFoundAgain()
    {
        var template = NewTemplate();
        var owner = new object();

        var first = InstanceManager.FindOrRegister(template, owner, out var firstCreated);
        first.IsClosing = true;
        var second = InstanceManager.FindOrRegister(template, owner, out var secondCreated);

        Assert.IsTrue(firstCreated);
        Assert.IsTrue(secondCreated, "a new one is made instead of going into one that is about to be deleted");
        Assert.AreNotEqual(first.Id, second.Id);
    }
}
