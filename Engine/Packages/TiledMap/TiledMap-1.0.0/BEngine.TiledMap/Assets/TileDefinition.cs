using YamlDotNet.Serialization;

namespace BEngine.TiledMap;

public sealed class TileDefinition
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Tile 1";
    public string Texture { get; set; } = string.Empty;
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public float UvX { get; set; }
    public float UvY { get; set; }
    public float UvWidth { get; set; } = 1;
    public float UvHeight { get; set; } = 1;
    public byte Red { get; set; } = byte.MaxValue;
    public byte Green { get; set; } = byte.MaxValue;
    public byte Blue { get; set; } = byte.MaxValue;
    public byte Alpha { get; set; } = byte.MaxValue;
    public bool HasCollider { get; set; }
    /// <summary>Normalized geometry about the tile center. Null preserves legacy whole-cell collision.</summary>
    public List<TileCollisionPath2D>? CollisionPaths { get; set; }
    public List<TileRule> Rules { get; set; } = [];
    public List<TileAnimationFrame> Animation { get; set; } = [];

    [YamlIgnore]
    public Rect uv => new((Fix64)(double)UvX, (Fix64)(double)UvY,
        (Fix64)(double)UvWidth, (Fix64)(double)UvHeight);

    [YamlIgnore]
    public Color tint => new(
        (Fix64)Red / byte.MaxValue,
        (Fix64)Green / byte.MaxValue,
        (Fix64)Blue / byte.MaxValue,
        (Fix64)Alpha / byte.MaxValue);

    internal void Validate()
    {
        if (CollisionPaths is { } paths && (paths.Count > 1024 || paths.Any(path => path is null || path.Points is null ||
            path.Points.Count < (path.Closed ? 3 : 2) || path.Points.Count > 65536 ||
            path.Points.Any(point => Fix64.Abs(point.x) > 10000 || Fix64.Abs(point.y) > 10000))))
            throw new InvalidDataException($"Tile {Id} has invalid collision geometry.");
        if (Rules is null || Animation is null || Rules.Count > 256 || Animation.Count > 1024 ||
            Rules.Any(rule => rule is null || rule.OutputTileId <= 0 || rule.Neighbors is null || rule.Neighbors.Count > 64 ||
                rule.Neighbors.Any(neighbor => neighbor is null || !Enum.IsDefined(neighbor.Match) ||
                    neighbor.X is < -1024 or > 1024 || neighbor.Y is < -1024 or > 1024 ||
                    neighbor.Match == TileNeighborMatch.TileId && neighbor.TileId <= 0)) ||
            Animation.Any(frame => frame is null || frame.TileId <= 0 || !float.IsFinite(frame.Duration) || frame.Duration < 0.001f || frame.Duration > 86400))
            throw new InvalidDataException($"Tile {Id} has invalid rule or animation data.");
        if (Id <= 0) throw new InvalidDataException("Tile IDs must be positive; zero is reserved for empty cells.");
        if (string.IsNullOrWhiteSpace(Name)) Name = $"Tile {Id}";
        if (!float.IsFinite(UvX) || !float.IsFinite(UvY) || !float.IsFinite(UvWidth) ||
            !float.IsFinite(UvHeight) || UvWidth <= 0 || UvHeight <= 0 ||
            UvX < 0 || UvY < 0 || UvX + UvWidth > 1.0001f || UvY + UvHeight > 1.0001f)
            throw new InvalidDataException($"Tile {Id} has invalid normalized UV coordinates.");
    }
}

public sealed class TileCollisionPath2D
{
    [YamlIgnore]
    public List<Vector2> Points { get; set; } = [];
    [YamlMember(Alias = "points", ApplyNamingConventions = false)]
    public List<TileCollisionVertex2D> SerializedPoints
    {
        get => Points.Select(point => new TileCollisionVertex2D { X = (double)point.x, Y = (double)point.y }).ToList();
        set => Points = value.Select(point => new Vector2((Fix64)point.X, (Fix64)point.Y)).ToList();
    }
    public bool Closed { get; set; } = true;
    public bool IsHole { get; set; }

    public List<Vector2> TransformPoints(Vector2 size, Vector2 center, TileTransformFlags flags) =>
        Points.Select(point => TransformPoint(point, size, center, flags)).ToList();

    public static Vector2 TransformPoint(Vector2 point, Vector2 size, Vector2 center, TileTransformFlags flags)
    {
        point = new(point.x * size.x * (flags.HasFlag(TileTransformFlags.FlipX) ? -1 : 1),
            point.y * size.y * (flags.HasFlag(TileTransformFlags.FlipY) ? -1 : 1));
        var turns = (flags.HasFlag(TileTransformFlags.Rotate90) ? 1 : 0) +
            (flags.HasFlag(TileTransformFlags.Rotate180) ? 2 : 0);
        return center + (turns switch { 1 => new(-point.y, point.x), 2 => -point, 3 => new(point.y, -point.x), _ => point });
    }
}
public sealed class TileCollisionVertex2D
{
    public double X { get; set; }
    public double Y { get; set; }
}
