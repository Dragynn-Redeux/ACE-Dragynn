using System.Numerics;
using ACE.Server.Physics.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests.Physics;

[TestClass]
public class EnvCellTests
{
    [TestMethod]
    public void EnvCell_WithoutACellStructureContainsNothing()
    {
        // An interior cell that is missing from the cell dat is built from an empty dat record,
        // so it never gets a CellStructure. Asking it whether it contains a point used to throw.
        var cell = new EnvCell { CellStructure = null };

        Assert.IsFalse(cell.point_in_cell(new Vector3(1, 2, 3)));
    }
}
