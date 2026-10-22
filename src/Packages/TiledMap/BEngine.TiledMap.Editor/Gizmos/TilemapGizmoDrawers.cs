using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

internal static class TilemapGizmoDrawers
{
    private const int MaximumGridSegmentsPerAxis = 128;
    private const GizmoType DrawModes = GizmoType.Selected;
    private static readonly Color SelectedColor = new(
        Fix64.Parse("0.35"), Fix64.Parse("0.9"), Fix64.One, Fix64.One);
    private static readonly Color NormalColor = new(
        Fix64.Parse("0.25"), Fix64.Parse("0.7"), Fix64.Parse("0.85"), Fix64.Parse("0.55"));

    [DrawGizmo(DrawModes)]
    private static void DrawTilemap(Tilemap tilemap, GizmoType gizmoType)
    {
        if (!tilemap.enabled || !tilemap.gameObject.activeInHierarchy) return;
        Gizmos.color = gizmoType.HasFlag(GizmoType.Selected) ? SelectedColor : NormalColor;

        var bounds = tilemap.GetBounds();
        var minimum = bounds.IsEmpty ? TileCoordinate.zero : bounds.Min;
        var maximum = bounds.IsEmpty ? TileCoordinate.zero : bounds.Max;
        if (tilemap.cellLayout != TilemapLayout.Rectangle)
        {
            Span<Vector2> outline = stackalloc Vector2[6];
            var step = Math.Max(1L, (long)Math.Ceiling(Math.Sqrt(((double)maximum.X - minimum.X + 1) * ((double)maximum.Y - minimum.Y + 1) / 1600)));
            for (long y = minimum.Y; y <= maximum.Y; y += step)
            for (long x = minimum.X; x <= maximum.X; x += step)
            {
                var count = tilemap.GetCellLocalOutline(new((int)x, (int)y), outline);
                for (var index = 0; index < count; index++)
                    Gizmos.DrawLine(tilemap.transform.TransformPoint(outline[index]), tilemap.transform.TransformPoint(outline[(index + 1) % count]));
            }
            return;
        }
        var stride = new Vector2(
            Fix64.Max(Fix64.Epsilon, tilemap.cellSize.x + tilemap.cellGap.x),
            Fix64.Max(Fix64.Epsilon, tilemap.cellSize.y + tilemap.cellGap.y));
        var left = (long)minimum.X;
        var right = (long)maximum.X + 1;
        var bottom = (long)minimum.Y;
        var top = (long)maximum.Y + 1;
        var origin = tilemap.CellToLocal(TileCoordinate.zero) - tilemap.cellSize * Fix64.Half;

        foreach (var x in GridCoordinates(left, right))
            Gizmos.DrawLine(
                tilemap.transform.TransformPoint(LocalPoint(x, bottom, stride) + origin),
                tilemap.transform.TransformPoint(LocalPoint(x, top, stride) + origin));
        foreach (var y in GridCoordinates(bottom, top))
            Gizmos.DrawLine(
                tilemap.transform.TransformPoint(LocalPoint(left, y, stride) + origin),
                tilemap.transform.TransformPoint(LocalPoint(right, y, stride) + origin));
    }

    private static IEnumerable<long> GridCoordinates(long minimum, long maximum)
    {
        var span = Math.Max(0L, maximum - minimum);
        var step = Math.Max(1L, (span + MaximumGridSegmentsPerAxis - 1) /
                                MaximumGridSegmentsPerAxis);
        var last = minimum;
        for (var coordinate = minimum; coordinate <= maximum; coordinate += step)
        {
            last = coordinate;
            yield return coordinate;
            if (coordinate > long.MaxValue - step) break;
        }
        if (last != maximum) yield return maximum;
    }

    private static Vector2 LocalPoint(long x, long y, Vector2 stride) => new(
        Fix64.FromDecimal(x) * stride.x,
        Fix64.FromDecimal(y) * stride.y);
}
