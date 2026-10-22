namespace BEngine.Rendering.Rhi;

/// <summary>Graphics platform SPI. Engine consumers use GraphicsUtility.</summary>
public interface IGraphicsPlatformService
{
    GraphicsBackend DefaultBackend { get; }
    GraphicsBackend PreferredBackend { get; set; }
    GraphicsDeviceCapabilities? ActiveCapabilities { get; }
    IReadOnlyList<GraphicsBackendDescriptor> Backends { get; }
    void ReportActive(GraphicsDeviceCapabilities capabilities);
    GraphicsShaderPair ResolveShaderVariant(GraphicsDeviceCapabilities capabilities, string resourceStem);
    GraphicsRect ResolveViewport(GraphicsRect target, Rect normalizedViewport, GraphicsBackend backend);
}
