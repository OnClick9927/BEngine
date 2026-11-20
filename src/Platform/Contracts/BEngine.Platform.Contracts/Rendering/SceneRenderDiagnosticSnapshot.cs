namespace BEngine.Rendering;

/// <summary>Inclusive rows overlap their nested rows. Sum SelfTicks, NestedRenderTicks and
/// UnattributedTicks for the capture ledger. Diagnostic snapshot creation/serialization is excluded.</summary>
public sealed record SceneRenderDiagnosticSnapshot
{
    public bool Completed { get; init; }
    public bool BeforeCleanup { get; init; }
    public int CameraCount { get; init; }
    public int RenderDepth { get; init; }
    public long TotalTicks { get; init; }
    public double TotalMilliseconds { get; init; }
    public long TotalAllocatedBytes { get; init; }
    public long NestedRenderTicks { get; init; }
    public double NestedRenderMilliseconds { get; init; }
    public long NestedRenderAllocatedBytes { get; init; }
    public long UnattributedTicks { get; init; }
    public long UnattributedAllocatedBytes { get; init; }
    public int OverflowRendererTypeOccurrences { get; init; }
    public int OverflowPhaseOccurrences { get; init; }
    public int DroppedScopes { get; init; }
    public int OpenScopes { get; init; }
    public SceneRenderDiagnosticSample[] Samples { get; init; } = [];
}
public readonly record struct SceneRenderDiagnosticSample(
    string Name, bool RendererType, long Calls, long InclusiveTicks, long SelfTicks,
    double InclusiveMilliseconds, double SelfMilliseconds, long AllocatedBytes, long SelfAllocatedBytes);
