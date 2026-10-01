using ACE.Server.Commands.DeveloperCommands.ContentCommands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class ContentCommandInstanceTests
{
    [TestMethod]
    public void ContentCommands_ThatChangeTheWorldDatabaseWorkInThePersistentWorld()
    {
        Assert.IsNull(ContentCommandUtilities.WhyNotInAnInstance(0));
    }

    [TestMethod]
    public void ContentCommands_ThatChangeTheWorldDatabaseAreRefusedInAnyInstance()
    {
        // a landblock in an instance has the coordinates of the real one, so what /createinst, /addenc and /removeenc did there
        // would be written to the real landblock's rows and to the Content folder
        foreach (var instanceId in new uint[] { 1, 3, 4000 })
        {
            var reason = ContentCommandUtilities.WhyNotInAnInstance(instanceId);

            StringAssert.Contains(reason, $"instance {instanceId}");
            StringAssert.Contains(reason, "/instance leave");
        }
    }
}
