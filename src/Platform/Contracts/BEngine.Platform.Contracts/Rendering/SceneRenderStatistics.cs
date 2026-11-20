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
    public double SortMilliseconds { get; init; }
    /// <summary>Geometry generation and upload reported by the renderer.</summary>
    public double GeometryMilliseconds { get; init; }
    /// <summary>CPU draw submission, including extension rendering; excludes Present.</summary>
    public double SubmitMilliseconds { get; init; }
    public SceneRenderCounters RendererCounters { get; init; }
}
