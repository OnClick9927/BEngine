namespace BEngine.Rendering.Rhi;

public static class GraphicsBackendSettings
{
    private static GraphicsBackend _preferredBackend = GraphicsBackendDefaults.Default;
    private static GraphicsBackend _activeBackend = GraphicsBackendDefaults.Default;
    private static bool _hasActiveDevice;

    public static GraphicsBackend PreferredBackend
    {
        get => _preferredBackend;
        set => _preferredBackend = value;
    }

    public static GraphicsBackend ActiveBackend => _activeBackend;

    internal static void ReportActive(GraphicsDeviceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (_hasActiveDevice && _activeBackend == PreferredBackend &&
            capabilities.Backend != PreferredBackend) return;
        _activeBackend = capabilities.Backend;
        _hasActiveDevice = true;
        SystemInfo.graphicsDeviceType = capabilities.Backend.ToString();
        SystemInfo.graphicsDeviceName = capabilities.DeviceName;
        SystemInfo.graphicsDeviceVersion = capabilities.ApiVersion;
        SystemInfo.supportsComputeShaders = false;
        SystemInfo.supportsRenderTextures = capabilities.Supports(GraphicsDeviceFeatures.RenderTargets);
    }
}
