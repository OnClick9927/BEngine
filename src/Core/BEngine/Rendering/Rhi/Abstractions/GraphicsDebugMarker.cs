namespace BEngine.Rendering.Rhi;

/// <summary>Allocation-free metadata describing a camera, batch, or source render group.</summary>
public readonly record struct GraphicsDebugMarker(
    string Group = "",
    string BatchName = "",
    Guid? Material = null,
    string Shader = "",
    string Atlas = "",
    string SourceName = "",
    int? SourceInstanceId = null);
