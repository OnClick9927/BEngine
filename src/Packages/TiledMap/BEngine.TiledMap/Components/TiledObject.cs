namespace BEngine.TiledMap;

public enum TiledObjectShape { Rectangle, Ellipse, Point, Polygon, Polyline }

/// <summary>An authored Tiled object, useful for spawn points, zones and route geometry.</summary>
[AddComponentMenu("2D/Tiled Object")]
public sealed class TiledObject : Component
{
    [HideInInspector] public int objectId { get; set; }
    public string className { get; set; } = "";
    public uint tileGid { get; set; }
    public TiledObjectShape shape { get; set; }
    public Vector2 size { get; set; }
    public List<Vector2> points { get; set; } = [];
    public List<TiledObjectProperty> properties { get; set; } = [];
    /// <summary>Projects authored object coordinates into the map's isometric plane.</summary>
    public bool isometricProjection { get; set; }
    public Fix64 projectionAspect { get; set; } = 2;
    public string? GetProperty(string name) => properties.FirstOrDefault(value => value.name == name)?.value;

    public IReadOnlyList<Vector2> GetLocalOutline()
    {
        var outline = GetAuthoredOutline();
        return isometricProjection ? outline.Select(ProjectPoint).ToArray() : outline;
    }

    public Vector2 ProjectPoint(Vector2 point) => isometricProjection
        ? new((point.x + point.y) * projectionAspect * Fix64.Half, (point.y - point.x) * Fix64.Half)
        : point;

    private IReadOnlyList<Vector2> GetAuthoredOutline()
    {
        if (shape is TiledObjectShape.Polygon or TiledObjectShape.Polyline) return points;
        if (shape == TiledObjectShape.Point) return [Vector2.zero];
        if (shape == TiledObjectShape.Rectangle)
        {
            if (tileGid != 0 && GetComponent<Tilemap>() is { } map && map.GetCell(new(0, 0)) is { } cell)
                return new[] { new Vector2(-Fix64.Half, -Fix64.Half), new(Fix64.Half, -Fix64.Half), new(Fix64.Half, Fix64.Half), new(-Fix64.Half, Fix64.Half) }
                    .Select(point => TileCollisionPath2D.TransformPoint(point, size, map.CellToLocal(new(0, 0)), cell.Transform)).ToArray();
            var y = tileGid == 0 ? -size.y : size.y;
            return [Vector2.zero, new(size.x, 0), new(size.x, y), new(0, y)];
        }
        var outline = new Vector2[32];
        var center = new Vector2(size.x, -size.y) * Fix64.Half;
        for (var index = 0; index < outline.Length; index++)
        {
            var angle = (Fix64)index / outline.Length * Mathf.PI * 2;
            outline[index] = center + new Vector2(Mathf.Cos(angle) * size.x, Mathf.Sin(angle) * size.y) * Fix64.Half;
        }
        return outline;
    }
}

public sealed class TiledObjectProperty
{
    public string name { get; set; } = "";
    public string type { get; set; } = "string";
    public string value { get; set; } = "";
}
