namespace BEngine.TiledMap;

public sealed partial class Tilemap
{
    private static readonly (int X, int Y)[] RectangleNeighbors = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly (int X, int Y)[] HexNeighbors = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
    public TileCoordinate LocalToCell(Vector2 local)
    {
        var anchorOffset = Vector2.Scale(tileAnchor - new Vector2(Fix64.Half, Fix64.Half), cellSize);
        local -= anchorOffset;
        var stride = CellStride();
        var x = local.x / stride.x; var y = local.y / stride.y;
        var step = (1 + hexSideRatio) * Fix64.Half;
        var result = cellLayout switch
        {
            TilemapLayout.Rectangle => new(Mathf.FloorToInt(x), Mathf.FloorToInt(y)),
            TilemapLayout.Isometric => new(Mathf.FloorToInt(x + y + Fix64.Half), Mathf.FloorToInt(y - x + Fix64.Half)),
            TilemapLayout.HexagonPointTop => RoundHex(x - y / step * Fix64.Half, y / step),
            TilemapLayout.HexagonFlatTop => RoundHex(x / step, y - x / step * Fix64.Half),
            _ => throw new InvalidOperationException("Unknown Tilemap layout.")
        };
        if (cellLayout is TilemapLayout.HexagonPointTop or TilemapLayout.HexagonFlatTop)
        {
            if (Contains(result)) return result;
            foreach (var neighbor in GetNeighbors(result)) if (Contains(neighbor)) return neighbor;
        }
        return result;
        bool Contains(TileCoordinate cell) => TilemapGeometry.Contains(cellLayout, cellSize,
            local - (CellToLocal(cell) - anchorOffset), hexSideRatio);
    }

    private static TileCoordinate RoundHex(Fix64 q, Fix64 r)
    {
        var s = -q - r;
        var iq = Mathf.RoundToInt(q); var ir = Mathf.RoundToInt(r); var @is = Mathf.RoundToInt(s);
        var dq = Fix64.Abs(iq - q); var dr = Fix64.Abs(ir - r); var ds = Fix64.Abs(@is - s);
        if (dq > dr && dq > ds) iq = checked(-ir - @is);
        else if (dr > ds) ir = checked(-iq - @is);
        return new(iq, ir);
    }

    public int GetCellLocalOutline(TileCoordinate position, Span<Vector2> points)
    {
        var count = TilemapGeometry.GetOutline(cellLayout, cellSize, points, hexSideRatio);
        var center = CellToLocal(position);
        for (var index = 0; index < count; index++) points[index] += center;
        return count;
    }

    /// <summary>Axial neighbors for hexagons; four edge neighbors for rectangles and diamonds.</summary>
    public IEnumerable<TileCoordinate> GetNeighbors(TileCoordinate cell)
    {
        var offsets = cellLayout is TilemapLayout.HexagonPointTop or TilemapLayout.HexagonFlatTop ? HexNeighbors : RectangleNeighbors;
        foreach (var (dx, dy) in offsets)
        {
            var x = (long)cell.X + dx; var y = (long)cell.Y + dy;
            if (x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue)
                yield return new TileCoordinate((int)x, (int)y);
        }
    }
}
