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
        var stride = new Vector2(
            Fix64.Max(Fix64.Epsilon, tilemap.cellSize.x + tilemap.cellGap.x),
            Fix64.Max(Fix64.Epsilon, tilemap.cellSize.y + tilemap.cellGap.y));
        var left = (long)minimum.X;
        var right = (long)maximum.X + 1;
        var bottom = (long)minimum.Y;
        var top = (long)maximum.Y + 1;

        foreach (var x in GridCoordinates(left, right))
            Gizmos.DrawLine(
                tilemap.transform.TransformPoint(LocalPoint(x, bottom, stride)),
                tilemap.transform.TransformPoint(LocalPoint(x, top, stride)));
        foreach (var y in GridCoordinates(bottom, top))
            Gizmos.DrawLine(
                tilemap.transform.TransformPoint(LocalPoint(left, y, stride)),
                tilemap.transform.TransformPoint(LocalPoint(right, y, stride)));
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
