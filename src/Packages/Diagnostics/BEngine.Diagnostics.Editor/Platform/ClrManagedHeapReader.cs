using BEngine.Instrumentation;
using BEngine.Diagnostics;
using Microsoft.Diagnostics.Runtime;

namespace BEngine.Platform;

public sealed class ClrManagedHeapReader : IManagedHeapReader
{
    public ManagedHeapSnapshot CaptureProcess(int processId, int maximumObjects = 1_000_000, CancellationToken cancellationToken = default)
    {
        using var target = DataTarget.CreateSnapshotAndAttach(processId);
        return Read(target, maximumObjects, cancellationToken);
    }
    public ManagedHeapSnapshot ReadDump(string path, int maximumObjects = 1_000_000, CancellationToken cancellationToken = default)
    {
        using var target = DataTarget.LoadDump(path);
        return Read(target, maximumObjects, cancellationToken);
    }
    private static ManagedHeapSnapshot Read(DataTarget target, int maximum, CancellationToken cancellationToken)
    {
        if (maximum < 1 || maximum > 5_000_000) throw new ArgumentOutOfRangeException(nameof(maximum));
        var captureBudget = ManagedHeapBudget.effectiveEstimatedAnalysisBytes;
        maximum = (int)Math.Min(maximum, Math.Max(1, captureBudget / 512));
        var maximumReferences = (int)Math.Min(10_000_000, Math.Max(1, captureBudget / 128));
        var maximumRoots = (int)Math.Min(1_000_000, Math.Max(1, captureBudget / 128));
        if (target.ClrVersions.Length != 1) throw new InvalidDataException("Choose a dump containing one CLR runtime.");
        using var runtime = target.ClrVersions[0].CreateRuntime(); var heap = runtime.Heap;
        if (!heap.CanWalkHeap) throw new InvalidOperationException("The CLR heap in this snapshot cannot be walked.");
        var objects = new List<ManagedHeapObject>(); var references = new List<ManagedHeapReference>(); var truncated = false;
        foreach (var item in heap.EnumerateObjects())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (objects.Count == maximum || references.Count >= maximumReferences) { truncated = true; break; }
            if (!item.IsValid || item.IsFree) continue;
            objects.Add(new(item.Address, item.Size, item.Type?.Name ?? "Unknown"));
            foreach (var reference in item.EnumerateReferences())
            {
                if (references.Count >= maximumReferences) { truncated = true; break; }
                references.Add(new(item.Address, reference.Address));
            }
        }
        var rootList = new List<ManagedHeapRoot>();
        foreach (var root in heap.EnumerateRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rootList.Count == maximumRoots) { truncated = true; break; }
            rootList.Add(new(root.Object.Address, root.RootKind.ToString()));
        }
        var roots = rootList.ToArray();
        return new(DateTimeOffset.UtcNow, objects.ToArray(), references.ToArray(), roots, truncated);
    }

}
