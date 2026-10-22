namespace BEngine.Rendering;

public readonly record struct UIRenderStatistics(
    int CommandCount,
    int BatchCount,
    int DrawCalls,
    int VertexCount,
    int UnresolvedImages)
{
    /// <summary>Alpha-one SolidRect draws that overwrite without destination blending.</summary>
    public int OpaqueSolidDrawCalls { get; init; }
}
