using BEngine.Rendering.Rhi;

namespace BEngine;

public static class SystemInfo
{
    public static string operatingSystem => RuntimeServiceAccess.PlatformService.OperatingSystemDescription;
    public static string operatingSystemFamily => RuntimeServiceAccess.PlatformService.IsWindows ? "Windows" :
        RuntimeServiceAccess.PlatformService.IsMacOS ? "macOS" : "Linux";
    public static string deviceName => RuntimeServiceAccess.PlatformService.MachineName;
    public static string deviceModel => RuntimeServiceAccess.PlatformService.OperatingSystemArchitecture.ToString();
    public static string processorType => RuntimeServiceAccess.PlatformService.ProcessArchitecture.ToString();
    // Runtime gameplay is deliberately single-threaded. Platform hosts may still
    // use all hardware resources behind their native service boundary.
    public static int processorCount => 1;
    public static int systemMemorySize => RuntimeServiceAccess.PlatformService.AvailableMemoryMegabytes;
    public static DeviceType deviceType => DeviceType.Desktop;
    public static bool supportsLocationService => false;
    public static bool supportsVibration => false;
    private static GraphicsDeviceCapabilities? Graphics => GraphicsUtility.Current.ActiveCapabilities;
    public static string graphicsDeviceName => Graphics?.DeviceName ?? "Unknown";
    public static string graphicsDeviceVendor => Graphics?.DeviceVendor ?? "Unknown";
    public static string graphicsDeviceVersion => Graphics?.ApiVersion ?? "Unknown";
    public static string graphicsDeviceType => Graphics?.Backend.ToString() ?? "Unknown";
    public static bool supportsComputeShaders => Graphics?.SupportsComputeShaders ?? false;
    public static bool supportsInstancing => Graphics?.SupportsInstancing ?? false;
    public static bool supportsRenderTextures => Graphics?.Supports(GraphicsDeviceFeatures.RenderTargets) ?? false;
    public static int maxTextureSize => Graphics?.MaxTextureSize ?? 0;
}
