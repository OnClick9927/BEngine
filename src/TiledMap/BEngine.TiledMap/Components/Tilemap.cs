using System.Globalization;
using System.Text;

namespace BEngine.TiledMap;

[AddComponentMenu("2D/Tilemap")]
[DisallowMultipleComponent]
public sealed class Tilemap : Component
{
    private readonly SortedDictionary<TileCoordinate, TileCell> _cells = [];
    private string _loadedCellData = string.Empty;
    private TilePalette? _paletteOverride;
    private TilePalette? _cachedPalette;
    private string _cachedPalettePath = string.Empty;
    private DateTime _cachedPaletteWriteTimeUtc;
    private int _revision;

    [SerializeField]
    [HideInInspector]
    private string _serializedCells = "v1|";

    public string paletteAsset { get; set; } = string.Empty;
    public Vector2 cellSize { get; set; } = Vector2.one;
    public Vector2 cellGap { get; set; } = Vector2.zero;
    public Vector2 tileAnchor { get; set; } = new(Fix64.Half, Fix64.Half);
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
            if (_cells.Remove(position)) PersistCells();
            return;
        }
        var next = new TileCell(position, tileId, Normalize(transformFlags));
        if (_cells.TryGetValue(position, out var current) && current == next) return;
        _cells[position] = next;
        PersistCells();
    }

    public bool ClearTile(TileCoordinate position)
    {
        EnsureCells();
        if (!_cells.Remove(position)) return false;
        PersistCells();
        return true;
    }

    public void ClearAllTiles()
    {
        EnsureCells();
        if (_cells.Count == 0) return;
        _cells.Clear();
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
        var normalizedFlags = Normalize(transformFlags);
        EnsureCells();
        var changed = 0;
        for (var y = bottom; y <= top; y++)
        for (var x = left; x <= right; x++)
        {
            var position = new TileCoordinate(x, y);
            if (tileId == 0)
            {
                if (_cells.Remove(position)) changed++;
                continue;
            }
            var next = new TileCell(position, tileId, normalizedFlags);
            if (_cells.TryGetValue(position, out var current) && current == next) continue;
            _cells[position] = next;
            changed++;
        }
        if (changed > 0) PersistCells();
        return changed;
    }

    public int FloodFill(TileCoordinate start, int tileId,
        TileTransformFlags transformFlags = TileTransformFlags.None)
    {
        if (tileId <= 0) throw new ArgumentOutOfRangeException(nameof(tileId));
        EnsureCells();
        if (!_cells.TryGetValue(start, out var startCell) || startCell.TileId == tileId &&
            startCell.Transform == Normalize(transformFlags)) return 0;
        var targetId = startCell.TileId;
        var bounds = GetBounds();
        var queue = new Queue<TileCoordinate>();
        var visited = new HashSet<TileCoordinate>();
        var changed = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var position = queue.Dequeue();
            if (!visited.Add(position) || !bounds.Contains(position) ||
                !_cells.TryGetValue(position, out var cell) || cell.TileId != targetId) continue;
            _cells[position] = new TileCell(position, tileId, Normalize(transformFlags));
            changed++;
            queue.Enqueue(position + new TileCoordinate(1, 0));
            queue.Enqueue(position + new TileCoordinate(-1, 0));
            queue.Enqueue(position + new TileCoordinate(0, 1));
            queue.Enqueue(position + new TileCoordinate(0, -1));
        }
        if (changed > 0) PersistCells();
        return changed;
    }

    public int SwapTile(int fromTileId, int toTileId)
    {
        if (fromTileId <= 0 || toTileId < 0) throw new ArgumentOutOfRangeException();
        EnsureCells();
        var positions = _cells.Values.Where(cell => cell.TileId == fromTileId)
            .Select(cell => cell.Position).ToArray();
        foreach (var position in positions)
        {
            if (toTileId == 0) _cells.Remove(position);
            else _cells[position] = _cells[position] with { TileId = toTileId };
        }
        if (positions.Length > 0) PersistCells();
        return positions.Length;
    }

    public TileBounds GetBounds()
    {
        EnsureCells();
        if (_cells.Count == 0) return TileBounds.Empty;
        return new TileBounds(
            new TileCoordinate(_cells.Keys.Min(position => position.X), _cells.Keys.Min(position => position.Y)),
            new TileCoordinate(_cells.Keys.Max(position => position.X), _cells.Keys.Max(position => position.Y)));
    }

    public IReadOnlyList<TileCell> GetTiles(TilemapSortOrder sortOrder = TilemapSortOrder.BottomLeft)
    {
        EnsureCells();
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
        return new Vector2(position.X * stride.x + tileAnchor.x * cellSize.x,
            position.Y * stride.y + tileAnchor.y * cellSize.y);
    }

    public Vector2 CellToWorld(TileCoordinate position) => transform.TransformPoint(CellToLocal(position));

    public TileCoordinate WorldToCell(Vector2 worldPosition)
    {
        var local = transform.InverseTransformPoint(worldPosition);
        var stride = CellStride();
        return new TileCoordinate(
            (int)Math.Floor((double)(local.x / stride.x)),
            (int)Math.Floor((double)(local.y / stride.y)));
    }

    public void SetPaletteOverride(TilePalette? palette)
    {
        palette?.Validate();
        _paletteOverride = palette;
        _revision++;
    }

    public bool TryGetPalette(out TilePalette palette)
    {
        if (_paletteOverride is not null)
        {
            palette = _paletteOverride;
            return true;
        }
        palette = null!;
        if (string.IsNullOrWhiteSpace(paletteAsset)) return false;
        try
        {
            var path = TilePalettePath.Resolve(paletteAsset);
            if (!File.Exists(path)) return false;
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (_cachedPalette is null || !_cachedPalettePath.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                _cachedPaletteWriteTimeUtc != writeTime)
            {
                _cachedPalette = TilePalette.Load(path);
                _cachedPalettePath = path;
                _cachedPaletteWriteTimeUtc = writeTime;
            }
            palette = _cachedPalette;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException or FormatException or
                                          OverflowException or YamlDotNet.Core.YamlException)
        {
            return false;
        }
    }

    public void RefreshAllTiles() => _revision++;

    private Vector2 CellStride()
    {
        var x = cellSize.x + cellGap.x;
        var y = cellSize.y + cellGap.y;
        return new Vector2(Fix64.Max(Fix64.Epsilon, x), Fix64.Max(Fix64.Epsilon, y));
    }

    private void EnsureCells()
    {
        if (_loadedCellData.Equals(_serializedCells, StringComparison.Ordinal)) return;
        _cells.Clear();
        if (!_serializedCells.StartsWith("v1|", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported Tilemap cell data version.");
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
            _cells[position] = new TileCell(position, tileId, flags);
        }
        _loadedCellData = _serializedCells;
    }

    private void PersistCells()
    {
        var text = new StringBuilder("v1|");
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
        _revision++;
    }

    private static TileTransformFlags Normalize(TileTransformFlags flags) => flags &
        (TileTransformFlags.FlipX | TileTransformFlags.FlipY |
         TileTransformFlags.Rotate90 | TileTransformFlags.Rotate180);
}
