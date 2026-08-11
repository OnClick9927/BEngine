using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BEngine;

public enum RuntimePlatform
{
    WindowsPlayer,
    LinuxPlayer,
    OSXPlayer,
    WindowsEditor,
    LinuxEditor,
    OSXEditor
}

public static class Application
{
    public static event Action? quitting;
    public static string productName { get; internal set; } = "BEngine Game";
    public static string version { get; internal set; } = "0.1.0";
    public static string companyName { get; internal set; } = "DefaultCompany";
    public static bool isEditor { get; internal set; }
    public static bool isPlaying { get; internal set; } = true;
    public static int targetFrameRate { get; set; } = -1;
    public static string dataPath { get; internal set; } = AppContext.BaseDirectory;
    public static string streamingAssetsPath => Path.Combine(dataPath, "StreamingAssets");
    public static string persistentDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), companyName, productName);
    public static string temporaryCachePath => Path.Combine(Path.GetTempPath(), companyName, productName);
    public static RuntimePlatform platform => ResolvePlatform(isEditor);

    public static void OpenURL(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void Quit()
    {
        quitting?.Invoke();
        isPlaying = false;
    }

    private static RuntimePlatform ResolvePlatform(bool editor)
    {
        if (OperatingSystem.IsWindows()) return editor ? RuntimePlatform.WindowsEditor : RuntimePlatform.WindowsPlayer;
        if (OperatingSystem.IsMacOS()) return editor ? RuntimePlatform.OSXEditor : RuntimePlatform.OSXPlayer;
        return editor ? RuntimePlatform.LinuxEditor : RuntimePlatform.LinuxPlayer;
    }
}

public static class Screen
{
    private static Resolution _currentResolution = new(1280, 720, 60);

    public static int width => _currentResolution.width;
    public static int height => _currentResolution.height;
    public static Resolution currentResolution => _currentResolution;
    public static Resolution[] resolutions { get; internal set; } = [_currentResolution];
    public static bool fullScreen { get; set; }
    public static bool lockCursor { get; set; }
    public static Fix64 dpi { get; internal set; } = 96;

    public static void SetResolution(int width, int height, bool fullscreen, int preferredRefreshRate = 60)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _currentResolution = new Resolution(width, height, Math.Max(1, preferredRefreshRate));
        fullScreen = fullscreen;
    }
}

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
    public static bool supportsComputeShaders { get; internal set; }
    public static bool supportsInstancing { get; internal set; } = true;
}
