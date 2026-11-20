namespace BEngine.Rendering.Rhi;

public static class GraphicsBackendSettings
{
    public static GraphicsBackend PreferredBackend
    {
        get => GraphicsUtility.Current.PreferredBackend;
        set => GraphicsUtility.Current.PreferredBackend = value;
    }
    public static GraphicsBackend ActiveBackend => GraphicsUtility.Current.ActiveCapabilities?.Backend ?? GraphicsBackendDefaults.Default;
    internal static void ReportActive(GraphicsDeviceCapabilities capabilities) => GraphicsUtility.Current.ReportActive(capabilities);
}
