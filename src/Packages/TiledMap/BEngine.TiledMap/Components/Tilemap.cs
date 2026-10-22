using System.Globalization;
using System.Text;

namespace BEngine.TiledMap;

[AddComponentMenu("2D/Tilemap")]
[DisallowMultipleComponent]
public sealed partial class Tilemap : Component
{
    private readonly SortedDictionary<TileCoordinate, TileCell> _cells = [];
    private string _loadedCellData = string.Empty;
    private TilePalette? _paletteOverride;
    private int _revision;

    [SerializeField]
    [HideInInspector]
    private string _serializedCells = "v1|";

    [FormerlySerializedAs("paletteAsset")]
    public TilePalette? palette { get; set; }
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string paletteAsset { get => palette?.assetPath ?? string.Empty; set => palette = BAsset.Load<TilePalette>(value); }
    private Vector2 _cellSize = Vector2.one, _cellGap = Vector2.zero, _tileAnchor = new(Fix64.Half, Fix64.Half);
    private TilemapLayout _cellLayout;
    private Fix64 _hexSideRatio = Fix64.Half;
    /// <summary>Parallel hex edge length divided by cell height (point top) or width (flat top).</summary>
    [Range(0, 1)]
    public Fix64 hexSideRatio { get => _hexSideRatio; set { if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); if (_hexSideRatio == value) return; _hexSideRatio = value; RefreshLayout(); } }
    public Vector2 cellSize { get => _cellSize; set { if (_cellSize == value) return; _cellSize = value; RefreshLayout(); } }
    public Vector2 cellGap { get => _cellGap; set { if (_cellGap == value) return; _cellGap = value; RefreshLayout(); } }
    public TilemapLayout cellLayout { get => _cellLayout; set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); if (_cellLayout == value) return; _cellLayout = value; RefreshLayout(); } }
    public Vector2 tileAnchor { get => _tileAnchor; set { if (_tileAnchor == value) return; _tileAnchor = value; RefreshLayout(); } }
    public int cellCount { get { EnsureCells(); return _cells.Count; } }
    public int revision => _revision;

    public int GetTile(TileCoordinate position)
    {
        EnsureCells();
        return _cells.TryGetValue(position, out var cell) ? cell.TileId : 0;
    }

    public TileCell? GetCell(TileCoordinate position)
    {
        EnsureCells();
        return _cells.TryGetValue(position, out var cell) ? cell : null;
    }

    public bool HasTile(TileCoordinate position) => GetTile(position) != 0;

    public void SetTile(TileCoordinate position, int tileId,
        TileTransformFlags transformFlags = TileTransformFlags.None)
    {
        if (tileId < 0) throw new ArgumentOutOfRangeException(nameof(tileId));
        EnsureCells();
        if (tileId == 0)
        {
            if (RemoveCell(position)) PersistCells();
            return;
        }
        var next = new TileCell(position, tileId, Normalize(transformFlags));
        if (_cells.TryGetValue(position, out var current) && current == next) return;
        StoreCell(next);
        PersistCells();
    }

    /// <summary>Applies a bounded group of cells and rebuilds serialized data once.</summary>
    public void SetTiles(IReadOnlyList<TileCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count > 1_000_000) throw new ArgumentOutOfRangeException(nameof(cells));
        using var budget = LoopExecutionGuard.AllowIterations(cells.Count + 1);
        foreach (var cell in cells) if (cell.TileId < 0) throw new ArgumentOutOfRangeException(nameof(cells));
        EnsureCells();
        var changed = false;
        foreach (var cell in cells)
        {
            if (cell.TileId == 0) { changed |= RemoveCell(cell.Position); continue; }
            var next = cell with { Transform = Normalize(cell.Transform) };
            if (_cells.TryGetValue(cell.Position, out var previous) && previous == next) continue;
            StoreCell(next); changed = true;
        }
        if (changed) PersistCells();
    }

    public bool ClearTile(TileCoordinate position)
    {
        EnsureCells();
        if (!RemoveCell(position)) return false;
        PersistCells();
        return true;
    }

    public void ClearAllTiles()
    {
        EnsureCells();
        if (_cells.Count == 0) return;
        ClearCells();
        PersistCells();
    }

    public int BoxFill(TileCoordinate min, TileCoordinate max, int tileId,
        TileTransformFlags transformFlags = TileTransformFlags.None)
    {
        if (tileId < 0) throw new ArgumentOutOfRangeException(nameof(tileId));
        var left = Math.Min(min.X, max.X);
        var right = Math.Max(min.X, max.X);
        var bottom = Math.Min(min.Y, max.Y);
        var top = Math.Max(min.Y, max.Y);
        var width = (long)right - left + 1; var height = (long)top - bottom + 1;
        if (width > 1_000_000 || height > 1_000_000 || width * height > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(max), "A box fill is limited to one million cells.");
        using var budget = LoopExecutionGuard.AllowIterations((int)Math.Max(width, height) + 1);
        var normalizedFlags = Normalize(transformFlags);
        EnsureCells();
        var changed = 0;
        for (long y = bottom; y <= top; y++)
        for (long x = left; x <= right; x++)
        {
            var position = new TileCoordinate((int)x, (int)y);
            if (tileId == 0)
            {
                if (RemoveCell(position)) changed++;
                continue;
            }
            var next = new TileCell(position, tileId, normalizedFlags);
            if (_cells.TryGetValue(position, out var current) && current == next) continue;
            StoreCell(next);
            changed++;
        }
        if (changed > 0) PersistCells();
        return changed;
    }

    public int FloodFill(TileCoordinate start, int tileId,
        TileTransformFlags transformFlags = TileTransformFlags.None) => FloodFill(start, tileId, transformFlags, 1_000_000);

    public int FloodFill(TileCoordinate start, int tileId, TileTransformFlags transformFlags, int maximumCells)
    {
        if (maximumCells is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximumCells));
        if (tileId <= 0) throw new ArgumentOutOfRangeException(nameof(tileId));
        EnsureCells();
        var targetId = GetTile(start);
        var flags = Normalize(transformFlags);
        if (targetId == tileId && GetCell(start)?.Transform == flags) return 0;
        var bounds = GetBounds();
        // Empty space is only fillable inside the authored map extent. Never flood an infinite plane.
        if (bounds.IsEmpty || !bounds.Contains(start)) return 0;
        var queue = new Queue<TileCoordinate>();
        var visited = new HashSet<TileCoordinate>();
        var cells = new List<TileCell>();
        queue.Enqueue(start);
        using var budget = LoopExecutionGuard.AllowIterations(maximumCells * 6 + 1);
        while (queue.Count > 0)
        {
            var position = queue.Dequeue();
            if (!visited.Add(position) || !bounds.Contains(position) || GetTile(position) != targetId) continue;
            if (cells.Count >= maximumCells) throw new InvalidOperationException($"Flood fill is limited to {maximumCells} cells.");
            cells.Add(new TileCell(position, tileId, flags));
            foreach (var neighbor in GetNeighbors(position))
                if (bounds.Contains(neighbor)) queue.Enqueue(neighbor);
        }
        SetTiles(cells);
        return cells.Count;
    }

    public int SwapTile(int fromTileId, int toTileId)
    {
        if (fromTileId <= 0 || toTileId < 0) throw new ArgumentOutOfRangeException();
        EnsureCells();
        var positions = _cells.Values.Where(cell => cell.TileId == fromTileId)
            .Select(cell => cell.Position).ToArray();
        using var budget = LoopExecutionGuard.AllowIterations(positions.Length + 1);
        foreach (var position in positions)
        {
            if (toTileId == 0) RemoveCell(position);
            else StoreCell(_cells[position] with { TileId = toTileId });
        }
        if (positions.Length > 0) PersistCells();
        return positions.Length;
    }

    public TileBounds GetBounds()
    {
        EnsureCells();
        if (_cells.Count == 0) return TileBounds.Empty;
        var left = int.MaxValue; var bottom = int.MaxValue; var right = int.MinValue; var top = int.MinValue;
        foreach (var position in _cells.Keys)
        {
            left = Math.Min(left, position.X); bottom = Math.Min(bottom, position.Y);
            right = Math.Max(right, position.X); top = Math.Max(top, position.Y);
        }
        return new TileBounds(new(left, bottom), new(right, top));
    }

    public IReadOnlyList<TileCell> GetTiles(TilemapSortOrder sortOrder = TilemapSortOrder.BottomLeft)
    {
        if (!Enum.IsDefined(sortOrder)) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        EnsureCells();
        if (cellLayout != TilemapLayout.Rectangle)
        {
            var top = sortOrder is TilemapSortOrder.TopLeft or TilemapSortOrder.TopRight;
            var right = sortOrder is TilemapSortOrder.TopRight or TilemapSortOrder.BottomRight;
            var ordered = top ? _cells.Values.OrderByDescending(cell => CellToLocal(cell.Position).y)
                : _cells.Values.OrderBy(cell => CellToLocal(cell.Position).y);
            return (right ? ordered.ThenByDescending(cell => CellToLocal(cell.Position).x)
                : ordered.ThenBy(cell => CellToLocal(cell.Position).x)).ToArray();
        }
        return sortOrder switch
        {
            TilemapSortOrder.BottomLeft => _cells.Values.OrderBy(cell => cell.Position.Y)
                .ThenBy(cell => cell.Position.X).ToArray(),
            TilemapSortOrder.BottomRight => _cells.Values.OrderBy(cell => cell.Position.Y)
                .ThenByDescending(cell => cell.Position.X).ToArray(),
            TilemapSortOrder.TopLeft => _cells.Values.OrderByDescending(cell => cell.Position.Y)
                .ThenBy(cell => cell.Position.X).ToArray(),
            TilemapSortOrder.TopRight => _cells.Values.OrderByDescending(cell => cell.Position.Y)
                .ThenByDescending(cell => cell.Position.X).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(sortOrder))
        };
    }

    public Vector2 CellToLocal(TileCoordinate position)
    {
        var stride = CellStride();
        var q = (Fix64)position.X; var r = (Fix64)position.Y;
        var center = cellLayout switch
        {
            TilemapLayout.Rectangle => new Vector2(q * stride.x, r * stride.y),
            TilemapLayout.Isometric => new Vector2((q - r) * stride.x * Fix64.Half, (q + r) * stride.y * Fix64.Half),
            TilemapLayout.HexagonPointTop => new Vector2((q + r * Fix64.Half) * stride.x, r * stride.y * (1 + hexSideRatio) * Fix64.Half),
            TilemapLayout.HexagonFlatTop => new Vector2(q * stride.x * (1 + hexSideRatio) * Fix64.Half, (r + q * Fix64.Half) * stride.y),
            _ => throw new InvalidOperationException("Unknown Tilemap layout.")
        };
        var origin = cellLayout == TilemapLayout.Rectangle ? Vector2.zero : new Vector2(Fix64.Half, Fix64.Half);
        return center + Vector2.Scale(tileAnchor - origin, cellSize);
    }

    public Vector2 CellToWorld(TileCoordinate position) => transform.TransformPoint(CellToLocal(position));

    public TileCoordinate WorldToCell(Vector2 worldPosition)
    {
        return LocalToCell(transform.InverseTransformPoint(worldPosition));
    }

    public void SetPaletteOverride(TilePalette? palette)
    {
        palette?.Validate();
        _paletteOverride = palette;
        _revision++;
    }

    public bool TryGetPalette(out TilePalette palette)
    {
        if (_paletteOverride is { isDestroyed: false })
        {
            palette = _paletteOverride;
            return true;
        }
        palette = this.palette!;
        return palette is { isDestroyed: false };
    }

    public void RefreshAllTiles() { _revision++; }

    private void RefreshLayout()
    {
        _revision++; _renderBoundsVersion++;
        if (gameObject is null || isDestroyed) return;
        foreach (var child in transform.children)
            if (child.gameObject.GetComponent<TilemapPrefabInstance>() is { } marker)
                child.localPosition = CellToLocal(new(marker.cellX, marker.cellY));
    }

    private Vector2 CellStride()
    {
        var x = cellSize.x + cellGap.x;
        var y = cellSize.y + cellGap.y;
        return new Vector2(Fix64.Max(Fix64.Epsilon, x), Fix64.Max(Fix64.Epsilon, y));
    }

    private void EnsureCells()
    {
        if (_editDepth > 0) return;
        if (_loadedCellData.Equals(_serializedCells, StringComparison.Ordinal)) return;
        ClearCells();
        if (!_serializedCells.StartsWith("v1|", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported Tilemap cell data version.");
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(1, _serializedCells.Length / 7));
        foreach (var entry in _serializedCells[3..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var values = entry.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length != 4) throw new InvalidDataException($"Invalid Tilemap cell entry '{entry}'.");
            var position = new TileCoordinate(
                int.Parse(values[0], CultureInfo.InvariantCulture),
                int.Parse(values[1], CultureInfo.InvariantCulture));
            var tileId = int.Parse(values[2], CultureInfo.InvariantCulture);
            var flags = Normalize((TileTransformFlags)int.Parse(values[3], CultureInfo.InvariantCulture));
            if (tileId <= 0) throw new InvalidDataException($"Tilemap cell {position} has invalid tile ID {tileId}.");
            StoreCell(new TileCell(position, tileId, flags));
        }
        _loadedCellData = _serializedCells;
    }

    private void PersistCells(bool force = false)
    {
        if (_editDepth > 0 && !force) { _editDirty = true; return; }
        var text = new StringBuilder("v1|");
        using var budget = LoopExecutionGuard.AllowIterations(_cells.Count + 1);
        foreach (var cell in _cells.Values)
        {
            if (text.Length > 3) text.Append(';');
            text.Append(cell.Position.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.Position.Y.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.TileId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(((int)cell.Transform).ToString(CultureInfo.InvariantCulture));
        }
        _serializedCells = text.ToString();
        _loadedCellData = _serializedCells;
        _editDirty = false; serializationCount++;
        _revision++;
    }

    private static TileTransformFlags Normalize(TileTransformFlags flags) => flags &
        (TileTransformFlags.FlipX | TileTransformFlags.FlipY |
         TileTransformFlags.Rotate90 | TileTransformFlags.Rotate180);
}
