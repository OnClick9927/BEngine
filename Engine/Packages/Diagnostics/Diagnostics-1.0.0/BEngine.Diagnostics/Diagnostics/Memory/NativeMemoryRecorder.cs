using System.Runtime.CompilerServices;
using BEngine.Instrumentation;

namespace BEngine.Diagnostics;

/// <summary>Detailed native memory accounting is installed only with the diagnostic implementation.</summary>
internal sealed class NativeMemoryRecorder : INativeMemoryRecorder
{
    private readonly object _gate = new();
    private readonly Dictionary<long, NativeAllocation> _allocations = [];
    private long _nextId;

    [ModuleInitializer]
    internal static void Initialize()
    {
        var registration = NativeMemoryTracker.RegisterRecorder(new NativeMemoryRecorder());
        RuntimeAssemblyCleanup.Register(assembly =>
        {
            if (assembly == typeof(NativeMemoryRecorder).Assembly) registration.Dispose();
        });
    }
    public long Register(string category, string label, long bytes)
    {
        lock (_gate)
        {
            var id = ++_nextId;
            _allocations.Add(id, new(id, category, label, bytes, DateTimeOffset.UtcNow));
            return id;
        }
    }
    public void Resize(long id, long bytes)
    {
        lock (_gate)
            if (_allocations.TryGetValue(id, out var allocation) && allocation.Bytes != bytes)
                _allocations[id] = allocation with { Bytes = bytes };
    }
    public void Release(long id) { lock (_gate) _allocations.Remove(id); }
    public NativeAllocation[] Snapshot()
    {
        lock (_gate) return _allocations.Values.OrderByDescending(item => item.Bytes).ToArray();
    }
}
