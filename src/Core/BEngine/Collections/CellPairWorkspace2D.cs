namespace BEngine;

/// <summary>Indices of an ordered pair in the caller's input arrays.</summary>
public readonly struct IndexPair2D
{
    public readonly int First;
    public readonly int Second;

    public IndexPair2D(int first, int second)
    {
        First = first;
        Second = second;
    }
}

/// <summary>
/// Reuses fixed storage to select neighboring cells in ascending first/second index order.
/// The returned span is borrowed until the next Build call. Instances are not thread safe.
/// </summary>
public sealed class CellPairWorkspace2D
{
    private readonly IndexPair2D[] _pairs;
    private readonly int[] _cellHeads, _queryHeads, _cellKeysX, _cellKeysY, _next;
    private readonly uint[] _candidateWords;
    public int MaximumItems { get; }
    public int PairCapacity => _pairs.Length;

    public CellPairWorkspace2D(int maximumItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumItems);
        var pairCapacity = (long)maximumItems * (maximumItems - 1) / 2;
        if (pairCapacity > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maximumItems), "The pair capacity exceeds the array limit.");
        MaximumItems = maximumItems;
        _pairs = pairCapacity == 0 ? [] : new IndexPair2D[(int)pairCapacity];
        // Small lists retain the direct loop. The spatial table is fixed at
        // construction and is shared by all later builds in this workspace.
        if (maximumItems < 256)
        { _cellHeads = _queryHeads = _cellKeysX = _cellKeysY = _next = []; _candidateWords = []; return; }
        var buckets = 1;
        while ((long)buckets < (long)maximumItems * 2)
            buckets = checked(buckets * 2);
        _cellHeads = new int[buckets]; _queryHeads = new int[buckets]; _cellKeysX = new int[buckets]; _cellKeysY = new int[buckets];
        _next = new int[maximumItems];
        _candidateWords = new uint[(maximumItems + 31) / 32];
    }

    /// <summary>
    /// Selects each i/j pair once, with i less than j. No allocation or storage growth occurs.
    /// Cell differences use unchecked Int32 subtraction followed by Math.Abs; a difference
    /// equal to Int32.MinValue throws OverflowException, matching a direct cell rejection loop.
    /// </summary>
    public ReadOnlySpan<IndexPair2D> Build(ReadOnlySpan<int> cellX, ReadOnlySpan<int> cellY,
        int maximumCellDistance = 1)
    {
        if (cellX.Length != cellY.Length)
            throw new ArgumentException("Cell coordinate spans must have equal lengths.", nameof(cellY));
        if (cellX.Length > MaximumItems)
            throw new ArgumentOutOfRangeException(nameof(cellX), "The item count exceeds this workspace's fixed capacity.");
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCellDistance);

        if (cellX.Length >= 256 && maximumCellDistance == 1 && CanUseSpatialTable(cellX, cellY))
            return BuildSpatial(cellX, cellY);

        return BuildDirect(cellX, cellY, maximumCellDistance);
    }

    private ReadOnlySpan<IndexPair2D> BuildDirect(ReadOnlySpan<int> cellX, ReadOnlySpan<int> cellY,
        int maximumCellDistance)
    {

        var written = 0;
        for (var first = 0; first < cellX.Length; first++)
        {
            var x = cellX[first];
            var y = cellY[first];
            for (var second = first + 1; second < cellX.Length; second++)
            {
                if (Math.Abs(unchecked(x - cellX[second])) > maximumCellDistance ||
                    Math.Abs(unchecked(y - cellY[second])) > maximumCellDistance)
                    continue;
                _pairs[written++] = new IndexPair2D(first, second);
            }
        }
        return _pairs.AsSpan(0, written);
    }

    private static bool CanUseSpatialTable(ReadOnlySpan<int> cellX, ReadOnlySpan<int> cellY)
    {
        var minX = cellX[0]; var maxX = minX; var minY = cellY[0]; var maxY = minY;
        for (var i = 1; i < cellX.Length; i++)
        {
            minX = Math.Min(minX, cellX[i]); maxX = Math.Max(maxX, cellX[i]);
            minY = Math.Min(minY, cellY[i]); maxY = Math.Max(maxY, cellY[i]);
        }
        // A direct unchecked subtraction can wrap or become Int32.MinValue,
        // for which Math.Abs throws after any earlier pairs were written.
        // Keep that exact loop, including its partial output, for wide spans.
        return (long)maxX - minX < 1L << 31 && (long)maxY - minY < 1L << 31;
    }

    private ReadOnlySpan<IndexPair2D> BuildSpatial(ReadOnlySpan<int> cellX, ReadOnlySpan<int> cellY)
    {
        Array.Fill(_cellHeads, -1);
        var cells = 0;
        for (var i = cellX.Length - 1; i >= 0; i--)
        {
            var slot = FindCell(cellX[i], cellY[i]);
            if (_cellHeads[slot] < 0) cells++;
            _cellKeysX[slot] = cellX[i]; _cellKeysY[slot] = cellY[i];
            _next[i] = _cellHeads[slot]; _cellHeads[slot] = i;
        }
        // A handful of dense cells emits nearly all pairs. Direct scanning
        // avoids indexing overhead in this common contact-cluster case.
        if (cells <= 9) return BuildDirect(cellX, cellY, 1);
        // Query cursors advance independently; the hash occupancy remains immutable.
        Array.Copy(_cellHeads, _queryHeads, _cellHeads.Length);
        var written = 0;
        for (var first = 0; first < cellX.Length; first++)
        {
            Array.Clear(_candidateWords);
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    var slot = FindCell(unchecked(cellX[first] + dx), unchecked(cellY[first] + dy));
                    var head = _queryHeads[slot];
                    while (head >= 0 && head <= first) head = _next[head];
                    _queryHeads[slot] = head;
                    while (head >= 0)
                    {
                        _candidateWords[head >> 5] |= 1u << (head & 31);
                        head = _next[head];
                    }
                }
            // The bitset restores the original second-index order, regardless
            // of cell traversal order, without sorting or per-frame allocation.
            for (var word = first >> 5; word < _candidateWords.Length; word++)
            {
                var candidates = _candidateWords[word];
                while (candidates != 0)
                {
                    var second = (word << 5) + System.Numerics.BitOperations.TrailingZeroCount(candidates);
                    _pairs[written++] = new IndexPair2D(first, second);
                    candidates &= candidates - 1;
                }
            }
        }
        return _pairs.AsSpan(0, written);
    }

    private int FindCell(int x, int y)
    {
        var hash = unchecked((uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu);
        hash ^= hash >> 16;
        var slot = (int)(hash & (uint)(_cellHeads.Length - 1));
        while (_cellHeads[slot] >= 0 && (_cellKeysX[slot] != x || _cellKeysY[slot] != y))
            slot = (slot + 1) & (_cellHeads.Length - 1);
        return slot;
    }
}
