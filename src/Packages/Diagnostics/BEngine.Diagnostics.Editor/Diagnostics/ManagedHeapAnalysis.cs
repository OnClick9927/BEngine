namespace BEngine.Editor.Diagnostics;

public sealed record ManagedHeapTypeSummary(string Type, long Count, decimal Bytes);
public sealed record ManagedHeapTypeDelta(string Type, long CountBefore, long CountAfter, decimal BytesBefore, decimal BytesAfter)
{
    public long CountDelta => CountAfter - CountBefore;
    public decimal BytesDelta => BytesAfter - BytesBefore;
}

/// <summary>Indexed retaining paths and immediate dominators. Retained sizes are exact only for complete captures.</summary>
public sealed class ManagedHeapAnalysis
{
    private readonly ManagedHeapSnapshot _snapshot;
    private readonly Dictionary<ulong, int> _indices;
    private readonly int[] _rootParents, _dominators;
    private readonly ulong[] _retained;
    public ManagedHeapTypeSummary[] Types { get; }
    public bool IsComplete => !_snapshot.Truncated;
    public decimal TotalBytes { get; }

    public ManagedHeapAnalysis(ManagedHeapSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ManagedHeapBudget.Check(snapshot.Objects.Length, snapshot.References.Length, snapshot.Roots.Length);
        _snapshot = snapshot; var count = snapshot.Objects.Length; var root = count;
        _indices = new(count); _rootParents = new int[count + 1]; Array.Fill(_rootParents, -1);
        _dominators = new int[count]; Array.Fill(_dominators, -1); _retained = new ulong[count];
        var sizes = new Dictionary<string, (long Count, decimal Bytes)>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var item = snapshot.Objects[i]; _indices.Add(item.Address, i); TotalBytes += item.Size;
            sizes.TryGetValue(item.Type, out var type); sizes[item.Type] = (type.Count + 1, type.Bytes + item.Size);
        }
        Types = sizes.Select(item => new ManagedHeapTypeSummary(item.Key, item.Value.Count, item.Value.Bytes)).OrderByDescending(item => item.Bytes).ToArray();
        var edges = new List<(int Source, int Target)>(snapshot.References.Length + snapshot.Roots.Length);
        foreach (var edge in snapshot.References)
        {
            if ((edges.Count & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (_indices.TryGetValue(edge.Source, out var source) && _indices.TryGetValue(edge.Target, out var target)) edges.Add((source, target));
        }
        foreach (var item in snapshot.Roots) if (_indices.TryGetValue(item.Address, out var index)) edges.Add((root, index));
        var offsets = new int[count + 2]; var incomingOffsets = new int[count + 2];
        foreach (var (source, target) in edges) { offsets[source + 1]++; incomingOffsets[target + 1]++; }
        for (var i = 1; i < offsets.Length; i++) { offsets[i] += offsets[i - 1]; incomingOffsets[i] += incomingOffsets[i - 1]; }
        var outgoing = new int[edges.Count]; var incoming = new int[edges.Count];
        var cursor = (int[])offsets.Clone(); var incomingCursor = (int[])incomingOffsets.Clone();
        foreach (var (source, target) in edges) { outgoing[cursor[source]++] = target; incoming[incomingCursor[target]++] = source; }
        edges.Clear();

        // Build shortest paths once, shared by every later object selection.
        var queue = new int[count + 1]; var head = 0; var tail = 1; queue[0] = root; _rootParents[root] = root;
        while (head < tail)
        {
            if ((head & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var source = queue[head++];
            for (var edge = offsets[source]; edge < offsets[source + 1]; edge++)
            { var target = outgoing[edge]; if (_rootParents[target] < 0) { _rootParents[target] = source; queue[tail++] = target; } }
        }

        // Lengauer–Tarjan, with iterative DFS and compression to support million-object chains.
        var number = new int[count + 1]; var vertex = new int[count + 2]; var parent = new int[count + 2];
        var stack = new int[count + 1]; var iterators = (int[])offsets.Clone(); var depth = 0; var visited = 1;
        number[root] = 1; vertex[1] = root; stack[0] = root;
        while (depth >= 0)
        {
            if ((visited & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var source = stack[depth];
            if (iterators[source] >= offsets[source + 1]) { depth--; continue; }
            var target = outgoing[iterators[source]++]; if (number[target] != 0) continue;
            number[target] = ++visited; vertex[visited] = target; parent[visited] = number[source]; stack[++depth] = target;
        }
        var semi = new int[visited + 1]; var ancestor = new int[visited + 1]; var label = new int[visited + 1];
        var idom = new int[visited + 1]; var bucket = new int[visited + 1]; var next = new int[visited + 1]; var path = new int[visited];
        for (var i = 1; i <= visited; i++) semi[i] = label[i] = i;
        int Eval(int node)
        {
            var length = 0; var current = node;
            while (ancestor[current] != 0 && ancestor[ancestor[current]] != 0) { path[length++] = current; current = ancestor[current]; }
            while (length > 0)
            {
                current = path[--length]; var above = ancestor[current];
                if (semi[label[above]] < semi[label[current]]) label[current] = label[above];
                ancestor[current] = ancestor[above];
            }
            return label[node];
        }
        for (var w = visited; w >= 2; w--)
        {
            if ((w & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var objectIndex = vertex[w];
            for (var edge = incomingOffsets[objectIndex]; edge < incomingOffsets[objectIndex + 1]; edge++)
            {
                var predecessor = number[incoming[edge]];
                if (predecessor != 0) semi[w] = Math.Min(semi[w], semi[Eval(predecessor)]);
            }
            next[w] = bucket[semi[w]]; bucket[semi[w]] = w; ancestor[w] = parent[w];
            for (var v = bucket[parent[w]]; v != 0; v = next[v]) { var u = Eval(v); idom[v] = semi[u] < semi[v] ? u : parent[w]; }
            bucket[parent[w]] = 0;
        }
        for (var w = 2; w <= visited; w++)
        {
            if (idom[w] != semi[w]) idom[w] = idom[idom[w]];
            var index = vertex[w]; _dominators[index] = vertex[idom[w]]; _retained[index] = snapshot.Objects[index].Size;
        }
        for (var w = visited; w >= 2; w--)
        {
            var index = vertex[w]; var dominator = _dominators[index];
            if (dominator != root) _retained[dominator] = checked(_retained[dominator] + _retained[index]);
        }
    }

    public ulong? RetainedBytes(ulong address) => _indices.TryGetValue(address, out var index) && _rootParents[index] >= 0 ? _retained[index] : null;
    public ulong? ImmediateDominator(ulong address) => _indices.TryGetValue(address, out var index) && _dominators[index] >= 0 && _dominators[index] < _snapshot.Objects.Length ? _snapshot.Objects[_dominators[index]].Address : null;
    public ulong[] PathToRoot(ulong address)
    {
        if (!_indices.TryGetValue(address, out var index) || _rootParents[index] < 0) return [];
        var result = new List<ulong>();
        while (index < _snapshot.Objects.Length) { result.Add(_snapshot.Objects[index].Address); index = _rootParents[index]; }
        result.Reverse(); return result.ToArray();
    }
    /// <summary>Compare types, because moving GC makes cross-snapshot object addresses unstable.</summary>
    public ManagedHeapTypeDelta[] CompareTo(ManagedHeapAnalysis baseline)
    {
        var before = baseline.Types.ToDictionary(item => item.Type, StringComparer.Ordinal);
        var after = Types.ToDictionary(item => item.Type, StringComparer.Ordinal);
        return before.Keys.Union(after.Keys, StringComparer.Ordinal).Select(type =>
        {
            before.TryGetValue(type, out var previous); after.TryGetValue(type, out var current);
            return new ManagedHeapTypeDelta(type, previous?.Count ?? 0, current?.Count ?? 0, previous?.Bytes ?? 0, current?.Bytes ?? 0);
        }).OrderByDescending(item => item.BytesDelta).ToArray();
    }
}
