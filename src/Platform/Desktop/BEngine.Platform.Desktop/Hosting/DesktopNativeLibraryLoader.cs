namespace BEngine.Platform;

/// <summary>Preloads the native windowing library before Silk creates its first window.</summary>
public sealed class DesktopNativeLibraryLoader : SystemNativeLibraryLoader
{
    public override void Initialize(string nativeLibraryDirectory, bool reportDiagnostics = false)
    {
        base.Initialize(nativeLibraryDirectory);
        var glfwName = OperatingSystem.IsWindows() ? "glfw3.dll" :
            OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";
        _ = LoadFromSearchDirectories([glfwName], out var loadedPath);
        if (reportDiagnostics) ReportDiagnostics($"|glfw={loadedPath ?? "not-loaded"}");
    }
}
