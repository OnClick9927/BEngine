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
    /// <summary>SDF text commands whose prepared CPU vertices were reused.</summary>
    public int TextGeometryCacheHits { get; init; }
    /// <summary>SDF glyphs regenerated this submission, including outlined glyphs only once.</summary>
    public int TextGeometryRebuiltGlyphs { get; init; }
}
