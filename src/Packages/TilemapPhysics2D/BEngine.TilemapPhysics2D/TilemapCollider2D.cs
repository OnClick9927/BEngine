using BEngine.Physics2D;

namespace BEngine.TiledMap;

[AddComponentMenu("Physics 2D/Tilemap Collider 2D")]
[RequireComponent(typeof(Tilemap))]
[DisallowMultipleComponent]
public sealed class TilemapCollider2D : CompositeCollider2D
{
    private readonly List<ColliderPath2D> _generated = [];
    private int _revision = -1;
    private int[] _colliderIds = [];
    private Vector2 _size;
    private Vector2 _gap;
    private Vector2 _anchor;
    private TilemapLayout _layout;
    public bool mergeAdjacentCells { get; set; } = true;
    private bool _merged;
    private int _geometryHash;

    public override IReadOnlyList<ColliderPath2D> GetPaths()
    {
        var map = GetComponent<Tilemap>();
        if (map is null || !map.enabled || map.cellCount == 0 || !map.TryGetPalette(out var palette))
        { _generated.Clear(); _revision = -1; return _generated; }
        var ids = palette.Tiles.Where(tile => tile.HasCollider).Select(tile => tile.Id).Order().ToArray();
        var definitions = palette.Tiles.ToDictionary(tile => tile.Id);
        var geometryHash = new HashCode();
        foreach (var tile in palette.Tiles.Where(tile => tile.HasCollider))
        {
            geometryHash.Add(tile.Id); geometryHash.Add(tile.CollisionPaths is null);
            foreach (var path in tile.CollisionPaths ?? [])
            { geometryHash.Add(path.Closed); geometryHash.Add(path.IsHole); foreach (var point in path.Points) geometryHash.Add(point); }
        }
        var currentGeometryHash = geometryHash.ToHashCode();
        if (_revision == map.revision && _size == map.cellSize && _gap == map.cellGap && _anchor == map.tileAnchor &&
            _layout == map.cellLayout && _merged == mergeAdjacentCells && _geometryHash == currentGeometryHash && ids.SequenceEqual(_colliderIds)) return _generated;
        var colliding = ids.ToHashSet();
        var cells = map.GetTiles().Where(cell => colliding.Contains(cell.TileId)).ToArray();
        var pending = new List<ColliderPath2D>();
        Span<Vector2> outline = stackalloc Vector2[6];
        using var budget = LoopExecutionGuard.AllowIterations(cells.Length * 8 + 1);
        for (var index = 0; index < cells.Length; index++)
        {
            var first = cells[index]; var last = first;
            if (definitions[first.TileId].CollisionPaths is { } authored)
            {
                var authoredCenter = map.CellToLocal(first.Position);
                foreach (var path in authored)
                {
                    var points = path.TransformPoints(map.cellSize, authoredCenter, first.Transform);
                    if (path.Closed) pending.Add(new() { points = points, isHole = path.IsHole });
                    else for (var segment = 1; segment < points.Count; segment++)
                    {
                        var delta = points[segment] - points[segment - 1];
                        if (delta.sqrMagnitude == 0) continue;
                        var normal = new Vector2(-delta.y, delta.x).normalized * (Fix64).01;
                        pending.Add(new() { points = [points[segment - 1] - normal, points[segment] - normal, points[segment] + normal, points[segment - 1] + normal] });
                    }
                }
                continue;
            }
            if (mergeAdjacentCells && map.cellLayout == TilemapLayout.Rectangle && map.cellGap == Vector2.zero && first.Transform == TileTransformFlags.None)
                while (index + 1 < cells.Length && definitions[cells[index + 1].TileId].CollisionPaths is null && cells[index + 1].Transform == TileTransformFlags.None &&
                       cells[index + 1].Position.Y == first.Position.Y && cells[index + 1].Position.X == last.Position.X + 1)
                    last = cells[++index];
            var center = (map.CellToLocal(first.Position) + map.CellToLocal(last.Position)) * Fix64.Half;
            var size = new Vector2(map.cellSize.x * (last.Position.X - first.Position.X + 1), map.cellSize.y);
            var angle = (first.Transform.HasFlag(TileTransformFlags.Rotate90) ? (Fix64)90 : Fix64.Zero) +
                        (first.Transform.HasFlag(TileTransformFlags.Rotate180) ? (Fix64)180 : Fix64.Zero);
            var count = TilemapGeometry.GetOutline(map.cellLayout, size, outline, map.hexSideRatio);
            pending.Add(new ColliderPath2D { points = outline[..count].ToArray()
                .Select(point => center + RotateQuarterTurns(point, angle)).ToList() });
        }
        _generated.Clear(); _generated.AddRange(pending);
        _revision = map.revision; _size = map.cellSize; _gap = map.cellGap; _anchor = map.tileAnchor;
        _colliderIds = ids; _merged = mergeAdjacentCells; _layout = map.cellLayout;
        _geometryHash = currentGeometryHash;
        return _generated;
    }

    [ContextMenu("Rebuild Tilemap Collision")]
    public void Rebuild() { _revision = -1; _ = GetPaths(); }
    private static Vector2 RotateQuarterTurns(Vector2 point, Fix64 angle) => (int)angle switch
    { 90 => new(-point.y, point.x), 180 => -point, 270 => new(point.y, -point.x), _ => point };
}
