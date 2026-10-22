namespace BEngine.TiledMap;

public enum TilemapLayout { Rectangle, Isometric, HexagonPointTop, HexagonFlatTop }

/// <summary>Counterclockwise cell outlines, shared by rendering, picking, gizmos and physics.</summary>
public static class TilemapGeometry
{
    public static int GetOutline(TilemapLayout layout, Vector2 size, Span<Vector2> points)
        => GetOutline(layout, size, points, Fix64.Half);

    public static int GetOutline(TilemapLayout layout, Vector2 size, Span<Vector2> points, Fix64 hexSideRatio)
    {
        if (hexSideRatio < 0 || hexSideRatio > 1) throw new ArgumentOutOfRangeException(nameof(hexSideRatio));
        var count = layout is TilemapLayout.HexagonPointTop or TilemapLayout.HexagonFlatTop ? 6 : 4;
        if (points.Length < count) throw new ArgumentException("Cell outline buffer is too small.", nameof(points));
        var x = size.x * Fix64.Half; var y = size.y * Fix64.Half;
        switch (layout)
        {
            case TilemapLayout.Rectangle:
                points[0] = new(-x, -y); points[1] = new(x, -y); points[2] = new(x, y); points[3] = new(-x, y); break;
            case TilemapLayout.Isometric:
                points[0] = new(0, -y); points[1] = new(x, 0); points[2] = new(0, y); points[3] = new(-x, 0); break;
            case TilemapLayout.HexagonPointTop:
                points[0] = new(0, -y); points[1] = new(x, -y * hexSideRatio); points[2] = new(x, y * hexSideRatio);
                points[3] = new(0, y); points[4] = new(-x, y * hexSideRatio); points[5] = new(-x, -y * hexSideRatio); break;
            case TilemapLayout.HexagonFlatTop:
                points[0] = new(-x * hexSideRatio, -y); points[1] = new(x * hexSideRatio, -y); points[2] = new(x, 0);
                points[3] = new(x * hexSideRatio, y); points[4] = new(-x * hexSideRatio, y); points[5] = new(-x, 0); break;
            default: throw new ArgumentOutOfRangeException(nameof(layout));
        }
        var distinct = 0;
        for (var index = 0; index < count; index++)
            if (distinct == 0 || points[index] != points[distinct - 1]) points[distinct++] = points[index];
        return distinct;
    }

    public static bool Contains(TilemapLayout layout, Vector2 size, Vector2 point)
        => Contains(layout, size, point, Fix64.Half);

    public static bool Contains(TilemapLayout layout, Vector2 size, Vector2 point, Fix64 hexSideRatio)
    {
        Span<Vector2> outline = stackalloc Vector2[6];
        var count = GetOutline(layout, size, outline, hexSideRatio);
        for (var index = 0; index < count; index++)
        {
            var a = outline[index]; var edge = outline[(index + 1) % count] - a; var relative = point - a;
            if (edge.x * relative.y - edge.y * relative.x < -Fix64.Epsilon) return false;
        }
        return true;
    }
}
