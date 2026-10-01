using System.Collections.Generic;

namespace ACE.Server.Physics.Common;

public class CellArray
{
    public bool AddedOutside;
    public bool LoadCells;
    public Dictionary<uint, ObjCell> Cells;
    public int NumCells;

    /// <summary>
    /// The instance the cells in this array belong to. The static helpers that collect cells (find_cell_list and friends)
    /// look up neighbouring cells by id, and use this to look them up in the same instance as whoever asked.
    /// 0 is the persistent world.
    /// </summary>
    public uint Instance;

    public CellArray()
    {
        Cells = new Dictionary<uint, ObjCell>();
    }

    public void SetStatic()
    {
        AddedOutside = false;
        LoadCells = false;
        NumCells = 0;
    }

    public void SetDynamic()
    {
        AddedOutside = false;
        LoadCells = true;
        NumCells = 0;
    }

    public void add_cell(uint cellID, ObjCell cell)
    {
        if (!Cells.ContainsKey(cellID))
        {
            Cells.Add(cellID, cell);
            NumCells++;
        }
    }

    /// <summary>
    /// Set when the last search for cells found something over a place on the map where there is no outdoor cell.
    /// Every landblock that is loaded has all of its outdoor cells, so this means there is a landblock that isn't loaded:
    /// the edge of what an instance is made of, or a landblock that only exists in instances, next to the persistent world.
    /// (Being off the edge of the map itself does not set it.)
    /// </summary>
    public bool MissingOutdoorCell;

    public void remove_cell(ObjCell cell)
    {
        if (Cells.ContainsKey(cell.ID))
        {
            Cells.Remove(cell.ID);
            NumCells--;
        }
    }
}
