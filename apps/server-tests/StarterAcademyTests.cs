using System.Linq;
using ACE.Entity;
using ACE.Server.Entity;
using ACE.Server.Managers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class StarterAcademyTests
{
    private static void AssertStartsAt(StarterAcademies.Academy academy, uint cell)
    {
        Assert.AreEqual(cell, academy.Start.Cell);
        Assert.AreEqual(29.9f, academy.Start.PositionX);
        Assert.AreEqual(-130f, academy.Start.PositionY);
        Assert.AreEqual(0.005f, academy.Start.PositionZ);
        Assert.AreEqual(0f, academy.Start.RotationX);
        Assert.AreEqual(0f, academy.Start.RotationY);
        Assert.AreEqual(0f, academy.Start.RotationZ);
        Assert.AreEqual(1f, academy.Start.RotationW);
    }

    [TestMethod]
    public void StarterAcademies_NewCharactersStartWhereTheyAlwaysDid()
    {
        // these are the places new characters started at before the academies had instances: making them instances must not move them
        AssertStartsAt(StarterAcademies.ForStarterTown("Shoushi"), 0x20FC016E);
        AssertStartsAt(StarterAcademies.ForStarterTown("Yaraq"), 0x20FD016E);
        AssertStartsAt(StarterAcademies.ForStarterTown("Holtburg"), 0x20FE016E);
    }

    [TestMethod]
    public void StarterAcademies_AnyOtherStarterTownStartsInHoltburg()
    {
        Assert.AreSame(StarterAcademies.Holtburg, StarterAcademies.ForStarterTown("Sawato"));
        Assert.AreSame(StarterAcademies.Holtburg, StarterAcademies.ForStarterTown(""));
    }

    [TestMethod]
    public void StarterAcademies_EachStartsInsideItsOwnLandblockAndLeavesSomewhereElse()
    {
        Assert.AreEqual(3, StarterAcademies.All.Count);
        Assert.AreEqual(3, StarterAcademies.All.Select(a => a.Landblock).Distinct().Count(), "three different landblocks");

        foreach (var academy in StarterAcademies.All)
        {
            Assert.AreEqual(academy.Landblock, academy.Start.LandblockId, academy.StarterTown);
            Assert.AreNotEqual(academy.Landblock, academy.Exit.LandblockId, academy.StarterTown);
            Assert.IsFalse(
                StarterAcademies.All.Any(other => other.Landblock == academy.Exit.LandblockId),
                $"{academy.StarterTown} does not leave into another academy"
            );
        }
    }

    [TestMethod]
    public void StarterAcademies_AreOnePersonalLandblockEachAndLeaveThePersistentWorldAlone()
    {
        foreach (var academy in StarterAcademies.All)
        {
            var template = academy.Template;

            Assert.IsTrue(template.Personal, academy.StarterTown);
            Assert.IsFalse(template.InstanceOnly, "the academy in the persistent world stays");
            Assert.IsFalse(template.HasBoundary);
            Assert.AreEqual(1, template.Footprint.Count);
            Assert.IsTrue(template.Contains(academy.Start.LandblockId));
            Assert.IsFalse(template.Contains(academy.Exit.LandblockId), "it is left by going somewhere that is not in it");
            Assert.IsTrue(template.Name.StartsWith("academy:"));
        }

        Assert.AreEqual(3, StarterAcademies.All.Select(a => a.Template.Name).Distinct().Count());
    }

    [TestMethod]
    public void InstanceTemplate_IsNotPersonalUnlessItSaysSo()
    {
        var landblock = new LandblockId(0x7A7CFFFF);
        var position = new Position(0x7A7C0100, 10f, 10f, 0f, 0f, 0f, 0f, 1f);

        Assert.IsFalse(new InstanceTemplate("test-" + System.Guid.NewGuid(), new[] { landblock }, position).Personal);
        Assert.IsTrue(
            new InstanceTemplate("test-" + System.Guid.NewGuid(), new[] { landblock }, position, personal: true).Personal
        );
    }

    [TestMethod]
    public void InstanceManager_KnowsTheAcademiesAfterTheyAreRegistered()
    {
        InstanceManager.RegisterStarterAcademies();
        InstanceManager.RegisterStarterAcademies(); // registering them again changes nothing

        foreach (var academy in StarterAcademies.All)
        {
            Assert.AreSame(academy.Template, InstanceManager.GetTemplate(academy.Template.Name));
        }

        Assert.AreEqual(
            3,
            InstanceManager.GetTemplates().Count(t => StarterAcademies.All.Any(a => ReferenceEquals(a.Template, t)))
        );
    }

    [TestMethod]
    public void InstanceManager_FindsThePersonalTemplateOfTheLandblockAPlayerLogsInAt()
    {
        InstanceManager.RegisterStarterAcademies();

        foreach (var academy in StarterAcademies.All)
        {
            Assert.AreSame(academy.Template, InstanceManager.GetPersonalTemplate(academy.Start.LandblockId));
            Assert.AreSame(
                academy.Template,
                InstanceManager.GetPersonalTemplate(new LandblockId(academy.Start.Cell)),
                "whatever cell they are in"
            );
        }

        // anywhere else, including the towns the academies lead to, is the persistent world
        foreach (var academy in StarterAcademies.All)
        {
            Assert.IsNull(InstanceManager.GetPersonalTemplate(academy.Exit.LandblockId), academy.StarterTown);
        }
    }

    [TestMethod]
    public void InstanceManager_AnOrdinaryTemplateIsNotPersonal()
    {
        var landblock = new LandblockId(0x7A7DFFFF);
        var template = new InstanceTemplate(
            "test-" + System.Guid.NewGuid(),
            new[] { landblock },
            new Position(0x7A7D0100, 10f, 10f, 0f, 0f, 0f, 0f, 1f)
        );

        InstanceManager.RegisterTemplate(template);

        Assert.IsNull(InstanceManager.GetPersonalTemplate(landblock), "sharing an instance is what every other template does");
    }

    [TestMethod]
    public void InstanceManager_TheAcademiesInThePersistentWorldStayOpen()
    {
        InstanceManager.RegisterStarterAcademies();

        foreach (var academy in StarterAcademies.All)
        {
            Assert.IsFalse(InstanceManager.IsInstanceOnly(academy.Landblock), academy.StarterTown);
            Assert.IsTrue(InstanceManager.CanEnter(0, academy.Start.LandblockId), academy.StarterTown);
        }
    }
}
