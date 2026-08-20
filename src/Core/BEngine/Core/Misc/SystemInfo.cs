using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BEngine;

public static class SystemInfo
{
    public static string operatingSystem => RuntimeInformation.OSDescription;
    public static string operatingSystemFamily => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Linux";
    public static string deviceName => Environment.MachineName;
    public static string deviceModel => RuntimeInformation.OSArchitecture.ToString();
    public static string processorType => RuntimeInformation.ProcessArchitecture.ToString();
    public static int processorCount => Environment.ProcessorCount;
    public static int systemMemorySize => (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576L, 0, int.MaxValue);
    public static string graphicsDeviceName { get; internal set; } = "Unknown";
    public static string graphicsDeviceVendor { get; internal set; } = "Unknown";
    public static string graphicsDeviceVersion { get; internal set; } = "Unknown";
    public static string graphicsDeviceType { get; internal set; } = "Unknown";
    public static bool supportsComputeShaders { get; internal set; }
    public static bool supportsInstancing { get; internal set; } = true;
}
