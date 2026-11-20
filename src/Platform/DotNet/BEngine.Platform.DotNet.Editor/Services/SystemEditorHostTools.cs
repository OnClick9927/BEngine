namespace BEngine.Platform;

public sealed class SystemEditorHostTools : IEditorHostTools
{
    public static SystemEditorHostTools Shared { get; } = new();

    public string? FindChromiumBrowser()
    {
        string[] candidates = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            "/usr/bin/chromium", "/usr/bin/google-chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public string? FindAndroidDebugBridge()
    {
        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        foreach (var variable in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var sdk = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(sdk) && Find(sdk) is { } found) return found;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var sdk in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
            Path.Combine(home, "Library", "Android", "sdk"), Path.Combine(home, "Android", "Sdk")
        })
            if (Find(sdk) is { } found) return found;
        return null;

        string? Find(string sdk)
        {
            var path = Path.Combine(sdk, "platform-tools", executable);
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
    }
}
