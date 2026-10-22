namespace BEngine.Rendering.Rhi;

/// <summary>Application-facing API; the platform host supplies IGraphicsPlatformService through dependency injection.</summary>
public sealed class GraphicsUtility
{
    private readonly IGraphicsPlatformService _implementation;

    public GraphicsUtility(IGraphicsPlatformService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static GraphicsUtility Current => RuntimeServiceAccess.GetRequiredService<GraphicsUtility>();

    public GraphicsBackend DefaultBackend => _implementation.DefaultBackend;

    public GraphicsBackend PreferredBackend { get => _implementation.PreferredBackend; set => _implementation.PreferredBackend = value; }

    public GraphicsDeviceCapabilities? ActiveCapabilities => _implementation.ActiveCapabilities;

    public IReadOnlyList<GraphicsBackendDescriptor> Backends => _implementation.Backends;

    public void ReportActive(GraphicsDeviceCapabilities capabilities) =>
        _implementation.ReportActive(capabilities);

    public GraphicsShaderPair ResolveShaderVariant(GraphicsDeviceCapabilities capabilities, string resourceStem) =>
        _implementation.ResolveShaderVariant(capabilities, resourceStem);

    public GraphicsRect ResolveViewport(GraphicsRect target, Rect normalizedViewport, GraphicsBackend backend) =>
        _implementation.ResolveViewport(target, normalizedViewport, backend);
}
