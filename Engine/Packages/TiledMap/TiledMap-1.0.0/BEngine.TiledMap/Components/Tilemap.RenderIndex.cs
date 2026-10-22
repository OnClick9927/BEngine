namespace BEngine.TiledMap;

public sealed partial class Tilemap
{
    private const int RenderChunkSize = 32;
    private readonly Dictionary<(int X, int Y), RenderChunk> _renderChunks = [];
    private readonly List<RenderChunk> _queryChunks = [];
    private readonly List<(TileCell Cell, Vector2 Center)> _sortCandidates = [];
    private static readonly Comparison<(TileCell Cell, Vector2 Center)> BottomLeftComparison = static (a, b) => CompareCandidates(a, b, false, false);
    private static readonly Comparison<(TileCell Cell, Vector2 Center)> BottomRightComparison = static (a, b) => CompareCandidates(a, b, false, true);
    private static readonly Comparison<(TileCell Cell, Vector2 Center)> TopLeftComparison = static (a, b) => CompareCandidates(a, b, true, false);
    private static readonly Comparison<(TileCell Cell, Vector2 Center)> TopRightComparison = static (a, b) => CompareCandidates(a, b, true, true);
    public int lastRenderChunkVisitCount { get; private set; }
    private bool _renderIndexReady;
    private long _renderBoundsVersion;

    private sealed class RenderChunk
    {
        internal readonly Dictionary<TileCoordinate, TileCell> Cells = [];
        internal readonly List<(TileCell Cell, Vector2 Center)> Prepared = [];
        internal Vector2 Min, Max;
        internal long BoundsVersion = -1;
    }

    private static (int X, int Y) RenderChunkKey(TileCoordinate position)
    {
        // Floor division also keeps negative coordinates in contiguous chunks.
        static int Divide(int value) => (int)Math.Floor((double)value / RenderChunkSize);
        return (Divide(position.X), Divide(position.Y));
    }

    private void StoreCell(TileCell cell)
    {
        _cells[cell.Position] = cell;
        if (_renderIndexReady) IndexCell(cell);
    }
    private void IndexCell(TileCell cell)
    {
        var key = RenderChunkKey(cell.Position);
        if (!_renderChunks.TryGetValue(key, out var chunk)) _renderChunks.Add(key, chunk = new());
        chunk.Cells[cell.Position] = cell; chunk.BoundsVersion = -1;
    }
    private bool RemoveCell(TileCoordinate position)
    {
        if (!_cells.Remove(position)) return false;
        if (_renderIndexReady && _renderChunks.TryGetValue(RenderChunkKey(position), out var chunk))
        {
            chunk.Cells.Remove(position); chunk.BoundsVersion = -1;
            if (chunk.Cells.Count == 0) _renderChunks.Remove(RenderChunkKey(position));
        }
        return true;
    }
    private void ClearCells()
    {
        _cells.Clear(); _renderChunks.Clear();
    }

    /// <summary>
    /// Fills reusable render candidates whose centers overlap a local rectangle, using a chunk index.
    /// Include a conservative tile extent in localBounds; exact per-tile culling follows.
    /// Edits affect this index immediately, including edits inside BeginEdit scopes.
    /// </summary>
    public void FillRenderCandidates(Rect localBounds, TilemapSortOrder sortOrder, List<TileCell> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(sortOrder)) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        EnsureCells(); destination.Clear();
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(_cells.Count, _renderChunks.Count) + 1);
        if (!_renderIndexReady)
        {
            _renderChunks.Clear();
            foreach (var cell in _cells.Values) IndexCell(cell);
            _renderIndexReady = true;
        }
        _queryChunks.Clear(); _sortCandidates.Clear();
        var stride = CellStride();
        var origin = cellLayout == TilemapLayout.Rectangle ? Vector2.zero : new Vector2(Fix64.Half, Fix64.Half);
        var anchor = Vector2.Scale(tileAnchor - origin, cellSize);
        var step = (1 + hexSideRatio) * Fix64.Half;
        var lowX = double.PositiveInfinity; var lowY = lowX; var highX = double.NegativeInfinity; var highY = highX;
        foreach (var point in (ReadOnlySpan<Vector2>)[ new Vector2(localBounds.xMin, localBounds.yMin), new(localBounds.xMax, localBounds.yMin),
            new(localBounds.xMax, localBounds.yMax), new(localBounds.xMin, localBounds.yMax) ])
        {
            var x = (point.x - anchor.x) / stride.x; var y = (point.y - anchor.y) / stride.y;
            var coordinate = cellLayout switch
            {
                TilemapLayout.Rectangle => new Vector2(x, y),
                TilemapLayout.Isometric => new Vector2(x + y, y - x),
                TilemapLayout.HexagonPointTop => new Vector2(x - y / step * Fix64.Half, y / step),
                TilemapLayout.HexagonFlatTop => new Vector2(x / step, y - x / step * Fix64.Half),
                _ => throw new InvalidOperationException("Unknown Tilemap layout.")
            };
            lowX = Math.Min(lowX, (double)coordinate.x); highX = Math.Max(highX, (double)coordinate.x);
            lowY = Math.Min(lowY, (double)coordinate.y); highY = Math.Max(highY, (double)coordinate.y);
        }
        var minX = (long)Math.Floor(Math.Clamp(lowX - 2, int.MinValue, int.MaxValue) / RenderChunkSize);
        var minY = (long)Math.Floor(Math.Clamp(lowY - 2, int.MinValue, int.MaxValue) / RenderChunkSize);
        var maxX = (long)Math.Floor(Math.Clamp(highX + 2, int.MinValue, int.MaxValue) / RenderChunkSize);
        var maxY = (long)Math.Floor(Math.Clamp(highY + 2, int.MinValue, int.MaxValue) / RenderChunkSize);
        if ((double)(maxX - minX + 1) * (maxY - minY + 1) <= _renderChunks.Count)
        {
            for (var x = minX; x <= maxX; x++) for (var y = minY; y <= maxY; y++)
                if (_renderChunks.TryGetValue(((int)x, (int)y), out var candidate)) _queryChunks.Add(candidate);
        }
        else _queryChunks.AddRange(_renderChunks.Values);
        lastRenderChunkVisitCount = _queryChunks.Count;
        foreach (var chunk in _queryChunks)
        {
            if (chunk.BoundsVersion != _renderBoundsVersion)
            {
                var first = true;
                chunk.Prepared.Clear();
                foreach (var cell in chunk.Cells.Values)
                {
                    var point = CellToLocal(cell.Position);
                    chunk.Prepared.Add((cell, point));
                    if (first) { chunk.Min = chunk.Max = point; first = false; }
                    else
                    {
                        chunk.Min = new(Fix64.Min(chunk.Min.x, point.x), Fix64.Min(chunk.Min.y, point.y));
                        chunk.Max = new(Fix64.Max(chunk.Max.x, point.x), Fix64.Max(chunk.Max.y, point.y));
                    }
                }
                chunk.BoundsVersion = _renderBoundsVersion;
            }
            if (chunk.Max.x < localBounds.xMin || chunk.Min.x > localBounds.xMax ||
                chunk.Max.y < localBounds.yMin || chunk.Min.y > localBounds.yMax) continue;
            foreach (var item in chunk.Prepared)
                if (item.Center.x >= localBounds.xMin && item.Center.x <= localBounds.xMax &&
                    item.Center.y >= localBounds.yMin && item.Center.y <= localBounds.yMax)
                    _sortCandidates.Add((item.Cell, cellLayout == TilemapLayout.Rectangle ? new(item.Cell.Position.X, item.Cell.Position.Y) : item.Center));
        }
        _sortCandidates.Sort(sortOrder switch
        {
            TilemapSortOrder.BottomLeft => BottomLeftComparison,
            TilemapSortOrder.BottomRight => BottomRightComparison,
            TilemapSortOrder.TopLeft => TopLeftComparison,
            TilemapSortOrder.TopRight => TopRightComparison,
            _ => throw new ArgumentOutOfRangeException(nameof(sortOrder))
        });
        foreach (var candidate in _sortCandidates) destination.Add(candidate.Cell);
        _queryChunks.Clear(); _sortCandidates.Clear();
    }

    private static int CompareCandidates((TileCell Cell, Vector2 Center) a,
        (TileCell Cell, Vector2 Center) b, bool top, bool right)
    {
        var pa = a.Center; var pb = b.Center;
        var y = top ? pb.y.CompareTo(pa.y) : pa.y.CompareTo(pb.y);
        if (y != 0) return y;
        var x = right ? pb.x.CompareTo(pa.x) : pa.x.CompareTo(pb.x);
        // The stable sort keeps the underlying coordinate order for equal centers.
        return x != 0 ? x : a.Cell.Position.CompareTo(b.Cell.Position);
    }
}
