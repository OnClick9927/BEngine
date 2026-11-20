using System.Runtime.CompilerServices;

namespace BEngine.Diagnostics;

public sealed record ManagedHeapObject(ulong Address, ulong Size, string Type);
public sealed record ManagedHeapReference(ulong Source, ulong Target);
public sealed record ManagedHeapRoot(ulong Address, string Kind);

/// <summary>CLR heap sizes, object references and GC roots read from a frozen snapshot or dump.</summary>
public sealed record ManagedHeapSnapshot(DateTimeOffset CapturedAtUtc, ManagedHeapObject[] Objects,
    ManagedHeapReference[] References, ManagedHeapRoot[] Roots, bool Truncated)
{
    public static ManagedHeapSnapshot CaptureProcess(int processId, int maximumObjects = 1_000_000, CancellationToken cancellationToken = default) =>
        RuntimeServices.GetRequiredService<IManagedHeapReader>().CaptureProcess(processId, maximumObjects, cancellationToken);

    public static ManagedHeapSnapshot ReadDump(string path, int maximumObjects = 1_000_000, CancellationToken cancellationToken = default) =>
        RuntimeServices.GetRequiredService<IManagedHeapReader>().ReadDump(path, maximumObjects, cancellationToken);

    private static readonly ConditionalWeakTable<ManagedHeapSnapshot, ManagedHeapAnalysis> Analyses = new();
    public ManagedHeapAnalysis Analyze(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Analyses.GetValue(this, snapshot => new ManagedHeapAnalysis(snapshot, cancellationToken));
    }
    /// <summary>Returns a shortest retaining chain beginning at a GC root. Addresses belong to this snapshot only.</summary>
    public ulong[] PathToRoot(ulong address) => Analyze().PathToRoot(address);
}
