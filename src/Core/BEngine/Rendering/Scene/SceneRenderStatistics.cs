namespace BEngine.Rendering;

/// <summary>Statistics for one complete scene or Game View render operation.</summary>
public readonly record struct SceneRenderStatistics(
    int CameraCount,
    int VisibleSubmissionCount,
    int BatchCount,
    long DrawCallCount,
    long VertexCount,
    long TriangleCount,
    long LineCount,
    int TargetWidth,
    int TargetHeight,
    bool HasCompleteDrawStatistics)
{
    /// <summary>Opt-in CPU phase samples; null during ordinary play. Inclusive samples may overlap.</summary>
    public SceneRenderDiagnosticSnapshot? RenderBreakdown { get; init; }
    public double CollectMilliseconds { get; init; }
    public double CoreCollectMilliseconds { get; init; }
    public double ExtensionCollectMilliseconds { get; init; }
    public double SortMilliseconds { get; init; }
    /// <summary>Core geometry generation and upload; excludes extension-owned meshes.</summary>
    public double GeometryMilliseconds { get; init; }
    /// <summary>CPU draw submission, including extension rendering; excludes Present.</summary>
    public double SubmitMilliseconds { get; init; }
    public long CoreGeometryUploadCount { get; init; }
    public long CoreGeometryUploadBytes { get; init; }
    public long CoreGeometryBuiltQuadCount { get; init; }
    public long CoreGeometryReusedQuadCount { get; init; }
    public long CoreSpriteWorldGeometryBuiltCount { get; init; }
    public long CoreSpriteWorldGeometryReusedCount { get; init; }
    public long CoreSpriteCullingTestCount { get; init; }
    public long CoreSpriteCullingReuseCount { get; init; }
    public long CoreSpriteCulledCount { get; init; }
    /// <summary>Full sorts versus linear verification/permutation passes.</summary>
    public long SortFullPassCount { get; init; }
    public long SortReusePassCount { get; init; }
    /// <summary>Adjacent Core logical batches folded into a single draw without reordering.</summary>
    public long CoreDrawRangeMergeCount { get; init; }
    /// <summary>Unmasked, untextured Core draws with alpha-one corners that bypass blending.</summary>
    public long CoreOpaqueSolidDrawCount { get; init; }
}
