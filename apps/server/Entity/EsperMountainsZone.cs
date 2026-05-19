using ACE.Entity;

namespace ACE.Server.Entity;

/// <summary>
/// Defines the Northern Esper Mountains polygon zone used to gate full-strength Frigid damage.
/// Outside this zone, Frigid environmental damage is scaled to 10% of its normal value.
/// </summary>
public static class EsperMountainsZone
{
    /// <summary>
    /// Polygon vertices bounding the Northern Esper Mountains in AC map coordinates.
    ///
    /// Format: (northSouth, eastWest)
    ///   northSouth — positive = North, negative = South  (e.g.  78.7 = 78.7N,  -5.0 = 5.0S)
    ///   eastWest   — positive = East,  negative = West   (e.g.  3.2  = 3.2E,  -8.0  = 8.0W)
    ///
    /// How to read these from the game:
    ///   The in-game "@loc" or map tooltip shows coordinates like "78.7N, 8.0W".
    ///   Enter them here as  (78.7f, -8.0f).
    ///
    /// Vertices may be listed clockwise or counter-clockwise; the ray-cast algorithm handles both.
    /// Add as many vertices as needed for an accurate boundary fit.
    ///
    private static readonly (float NS, float EW)[] NsEwVertices =
    [
        // (northSouth, eastWest) — e.g. 78.7N 8.0W → (78.7f, -8.0f)
        (98.0f, 3.0f), // 12:00
        (86.0f, 32.0f), // 2:00
        (64.0f, 36.0f), // 3:00
        (41.0f, -12.0f), // 6:00
        (55.0f, -52.0f), // 8:00
        (83.0f, -46.0f), // 10:00
    ];

    // World-space vertices pre-computed once from NsEwVertices.
    // worldX = (EW + 102) * 240
    // worldY = (NS + 102) * 240
    private static readonly (float X, float Y)[] Vertices = BuildWorldVertices();

    private static (float X, float Y)[] BuildWorldVertices()
    {
        var result = new (float X, float Y)[NsEwVertices.Length];
        for (var i = 0; i < NsEwVertices.Length; i++)
        {
            result[i] = NsEwToWorld(NsEwVertices[i].NS, NsEwVertices[i].EW);
        }
        return result;
    }

    /// <summary>
    /// Converts an AC map coordinate pair to world-space (X, Y).
    /// Inverse of <c>PositionExtensions.GetMapCoords()</c>:
    ///   worldX = (eastWest  + 102) * 240
    ///   worldY = (northSouth + 102) * 240
    /// </summary>
    public static (float X, float Y) NsEwToWorld(float northSouth, float eastWest) =>
        ((eastWest + 102f) * 240f, (northSouth + 102f) * 240f);

    /// <summary>
    /// Returns <see langword="true"/> if the given world-space point lies inside
    /// the Northern Esper Mountains polygon, using the ray-casting even-odd rule.
    /// Cost is O(n) where n = number of vertices — negligible for any reasonable polygon size.
    /// </summary>
    public static bool Contains(float worldX, float worldY)
    {
        var inside = false;
        var n = Vertices.Length;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (xi, yi) = Vertices[i];
            var (xj, yj) = Vertices[j];

            if ((yi > worldY) != (yj > worldY) &&
                worldX < (xj - xi) * (worldY - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the given <see cref="Position"/> lies inside
    /// the Northern Esper Mountains polygon.
    /// </summary>
    public static bool Contains(Position position)
    {
        if (position is null)
        {
            return false;
        }

        var global = position.ToGlobal();
        return Contains(global.X, global.Y);
    }
}
