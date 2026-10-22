using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Diagnostics;

public sealed record EngineObjectHeapNode(
    int SnapshotId,
    int InstanceId,
    string TypeName,
    string Name,
    long EstimatedShallowBytes,
    bool RuntimeOnly);

public sealed record EngineObjectReference(
    int SourceSnapshotId,
    int TargetSnapshotId,
    string Path);

public sealed record EngineObjectTypeSummary(
    string TypeName,
    int Count,
    long EstimatedShallowBytes);

public sealed record EngineObjectHeapSnapshot(
    DateTimeOffset CapturedAtUtc,
    long ManagedHeapBytes,
    long ManagedFragmentedBytes,
    long TotalAllocatedBytes,
    EngineObjectHeapNode[] Objects,
    EngineObjectReference[] References,
    EngineObjectTypeSummary[] Types)
{
    public const int MaximumObjects = 100_000;
    public NativeAllocation[] NativeAllocations { get; init; } = [];
    public const int MaximumReferences = 1_000_000;

    public static EngineObjectHeapSnapshot Capture(bool forceGarbageCollection = false)
    {
        if (forceGarbageCollection)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        var objects = BObject.FindObjectsByType<BObject>()
            .Where(item => item is not null && !item.isDestroyed)
            .OrderBy(item => item.GetInstanceID())
            .Take(MaximumObjects)
            .ToArray();
        var ids = new Dictionary<BObject, int>(ReferenceEqualityComparer.Instance);
        var nodes = new EngineObjectHeapNode[objects.Length];
        for (var index = 0; index < objects.Length; index++)
        {
            var value = objects[index];
            ids[value] = index + 1;
            nodes[index] = new EngineObjectHeapNode(
                index + 1,
                value.GetInstanceID(),
                value.GetType().FullName ?? value.GetType().Name,
                value.name ?? string.Empty,
                EstimateShallowBytes(value),
                value.IsRuntimeOnly);
        }

        var references = new List<EngineObjectReference>();
        for (var index = 0; index < objects.Length && references.Count < MaximumReferences; index++)
            CaptureReferences(objects[index], index + 1, ids, references);
        var types = nodes.GroupBy(item => item.TypeName, StringComparer.Ordinal)
            .Select(group => new EngineObjectTypeSummary(
                group.Key,
                group.Count(),
                group.Sum(item => item.EstimatedShallowBytes)))
            .OrderByDescending(item => item.EstimatedShallowBytes)
            .ThenBy(item => item.TypeName, StringComparer.Ordinal)
            .ToArray();
        var memory = GC.GetGCMemoryInfo();
        return new EngineObjectHeapSnapshot(
            DateTimeOffset.UtcNow,
            Math.Max(0, memory.HeapSizeBytes),
            Math.Max(0, memory.FragmentedBytes),
            Math.Max(0, GC.GetTotalAllocatedBytes(false)),
            nodes,
            references.ToArray(),
            types) { NativeAllocations = NativeMemoryTracker.Snapshot() };
    }

    private static void CaptureReferences(
        BObject source,
        int sourceId,
        IReadOnlyDictionary<BObject, int> ids,
        List<EngineObjectReference> output)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var field in InstanceFields(source.GetType()))
        {
            object? value;
            try { value = field.GetValue(source); }
            catch { continue; }
            Visit(value, field.Name, 0);
        }

        void Visit(object? value, string path, int depth)
        {
            if (value is null || output.Count >= MaximumReferences || depth > 5) return;
            if (value is BObject target)
            {
                if (ids.TryGetValue(target, out var targetId) && targetId != sourceId)
                    output.Add(new EngineObjectReference(sourceId, targetId, path));
                return;
            }
            var type = value.GetType();
            if (type.IsValueType || value is string or Type or Delegate) return;
            if (!visited.Add(value)) return;
            if (value is IDictionary dictionary)
            {
                var itemIndex = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (itemIndex >= 4096) break;
                    Visit(entry.Key, $"{path}.Keys[{itemIndex}]", depth + 1);
                    Visit(entry.Value, $"{path}[{itemIndex}]", depth + 1);
                    itemIndex++;
                }
                return;
            }
            if (value is IEnumerable enumerable)
            {
                var itemIndex = 0;
                foreach (var item in enumerable)
                {
                    if (itemIndex >= 4096) break;
                    Visit(item, $"{path}[{itemIndex}]", depth + 1);
                    itemIndex++;
                }
                return;
            }
            if (type.Namespace?.StartsWith("BEngine", StringComparison.Ordinal) != true) return;
            foreach (var field in InstanceFields(type))
            {
                object? nested;
                try { nested = field.GetValue(value); }
                catch { continue; }
                Visit(nested, $"{path}.{field.Name}", depth + 1);
            }
        }
    }

    private static long EstimateShallowBytes(object value)
    {
        var bytes = IntPtr.Size * 2L;
        foreach (var field in InstanceFields(value.GetType()))
        {
            var type = field.FieldType;
            if (!type.IsValueType) bytes += IntPtr.Size;
            else
            {
                try { bytes += Math.Max(1, System.Runtime.InteropServices.Marshal.SizeOf(type)); }
                catch { bytes += IntPtr.Size; }
            }
        }
        return (bytes + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size;
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!field.IsStatic) yield return field;
    }
}
