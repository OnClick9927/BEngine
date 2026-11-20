using BEngine.Instrumentation;
using BEngine.Diagnostics;

namespace BEngine.Editor;

// Own one captured view, so caller-owned arrays cannot invalidate its indexes.
// Retain only one search result and one object's bounded reference lists.
internal sealed class MemoryProfilerSnapshotView
{
    internal const int MaximumRows = 10_000;
    internal const int MaximumReferences = 1_000;
    private readonly Dictionary<int, EngineObjectHeapNode> _objects = [];
    private string? _search;
    private EngineObjectHeapNode[] _rows = [];
    private int? _referenceId;
    private EngineObjectReference[] _outgoing = [], _incoming = [];

    internal MemoryProfilerSnapshotView(EngineObjectHeapSnapshot snapshot)
    {
        Snapshot = snapshot with
        {
            Objects = [.. snapshot.Objects], References = [.. snapshot.References],
            Types = [.. snapshot.Types], NativeAllocations = [.. snapshot.NativeAllocations]
        };
        foreach (var node in Snapshot.Objects) _objects.TryAdd(node.SnapshotId, node);
        NativeBytes = Snapshot.NativeAllocations.Sum(item => item.Bytes);
    }

    internal EngineObjectHeapSnapshot Snapshot { get; }
    internal long NativeBytes { get; }
    internal EngineObjectHeapNode? FindObject(int id) => _objects.GetValueOrDefault(id);

    internal EngineObjectHeapNode[] GetRows(string search)
    {
        if (string.Equals(_search, search, StringComparison.Ordinal)) return _rows;
        var rows = new List<EngineObjectHeapNode>();
        var all = string.IsNullOrWhiteSpace(search);
        foreach (var node in Snapshot.Objects)
        {
            if (!all && !node.TypeName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !node.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !node.InstanceId.ToString().Contains(search, StringComparison.Ordinal)) continue;
            rows.Add(node);
            if (rows.Count == MaximumRows) break;
        }
        _rows = rows.ToArray();
        _search = search;
        return _rows;
    }

    internal EngineObjectReference[] GetReferences(int id, bool outgoing)
    {
        if (_referenceId != id)
        {
            var from = new List<EngineObjectReference>();
            var to = new List<EngineObjectReference>();
            foreach (var reference in Snapshot.References)
            {
                if (reference.SourceSnapshotId == id && from.Count < MaximumReferences) from.Add(reference);
                if (reference.TargetSnapshotId == id && to.Count < MaximumReferences) to.Add(reference);
                if (from.Count == MaximumReferences && to.Count == MaximumReferences) break;
            }
            _outgoing = from.ToArray(); _incoming = to.ToArray();
            _referenceId = id;
        }
        return outgoing ? _outgoing : _incoming;
    }
}
