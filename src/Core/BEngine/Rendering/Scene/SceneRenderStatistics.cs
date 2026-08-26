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
    bool HasCompleteDrawStatistics);
