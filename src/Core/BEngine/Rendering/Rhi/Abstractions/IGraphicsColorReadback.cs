namespace BEngine.Rendering.Rhi;

/// <summary>
/// Optional graphics-device capability for taking a single color snapshot without changing
/// the contents of the active render target.
/// </summary>
public interface IGraphicsColorReadback
{
    GraphicsColorReadbackCapabilities ColorReadbackCapabilities { get; }

    /// <summary>
    /// Requests a snapshot of <paramref name="region"/>. The region is absolute within the active
    /// color surface and always uses a top-left origin, independent of the graphics backend.
    /// </summary>
    GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region,
        int surfaceWidth,
        int surfaceHeight);
}
