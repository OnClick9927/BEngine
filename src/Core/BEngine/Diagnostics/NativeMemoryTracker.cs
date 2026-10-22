namespace BEngine.Diagnostics;

public sealed record NativeAllocation(long Id, string Category, string Label, long Bytes, DateTimeOffset CreatedAtUtc);

/// <summary>Tracks known native allocations. Bytes are requested sizes, excluding undocumented driver overhead.</summary>
public static class NativeMemoryTracker
{
    private static readonly object Gate = new();
    private static readonly Dictionary<long, NativeAllocation> Allocations = [];
    private static long _nextId;
    public static NativeAllocation[] Snapshot() { lock (Gate) return Allocations.Values.OrderByDescending(item => item.Bytes).ToArray(); }
    public static Allocation Register(string category, string label, long bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category); if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (Gate)
        {
            var id = ++_nextId; Allocations.Add(id, new(id, category, label, bytes, DateTimeOffset.UtcNow));
            return new Allocation(id);
        }
    }
    public sealed class Allocation : IDisposable
    {
        private long _id;
        internal Allocation(long id) => _id = id;
        public void Resize(long bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            lock (Gate) if (Allocations.TryGetValue(_id, out var allocation)) Allocations[_id] = allocation with { Bytes = bytes };
        }
        public void Dispose() { lock (Gate) { Allocations.Remove(_id); _id = 0; } }
    }
}
