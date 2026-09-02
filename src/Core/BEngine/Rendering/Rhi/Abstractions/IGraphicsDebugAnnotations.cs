namespace BEngine.Rendering.Rhi;

/// <summary>Optional semantic annotations consumed by graphics diagnostics.</summary>
public interface IGraphicsDebugAnnotations
{
    bool DebugMarkersEnabled { get; }
    void PushDebugMarker(GraphicsDebugMarker marker);
    void PopDebugMarker();
}
