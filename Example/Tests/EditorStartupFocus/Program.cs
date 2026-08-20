using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.ExampleTests.EditorStartupFocus;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var editorAssembly = Assembly.Load("BEngine.Editor");
        var hostType = editorAssembly.GetType("BEngine.Editor.EditorHostApplication", true)!;
        var dockType = editorAssembly.GetType("BEngine.Editor.DockWorkspace", true)!;

        Require(hostType.GetField("_startupFocusApplied", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Editor host is missing its one-shot startup focus guard.");
        Require(hostType.GetMethod("FocusEditorAfterStartup", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Editor host does not handle startup-complete focus.");
        Require(hostType.GetMethod("ActivateMainWindow", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Editor host does not centralize foreground activation.");
        Require(dockType.GetMethod("FocusActivePanel", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Foreground activation does not hand keyboard focus to the active Dock.");

        var foregroundImport = hostType.GetMethod("SetForegroundWindow", BindingFlags.Static |
            BindingFlags.NonPublic)?.GetCustomAttribute<DllImportAttribute>();
        Require(foregroundImport?.Value.Equals("user32.dll", StringComparison.OrdinalIgnoreCase) == true,
            "Editor startup focus is not backed by the Win32 foreground API.");

        var launcherAssembly = Assembly.Load("BEngine.Launcher");
        var launcherType = launcherAssembly.GetType("BEngine.Launcher.ProjectLauncherForm", true)!;
        var foregroundPermissionImport = launcherType.GetMethod("AllowSetForegroundWindow", BindingFlags.Static |
            BindingFlags.NonPublic)?.GetCustomAttribute<DllImportAttribute>();
        Require(foregroundPermissionImport?.Value.Equals("user32.dll", StringComparison.OrdinalIgnoreCase) == true,
            "Launcher does not transfer foreground permission to the Editor process.");

        Console.WriteLine("EDITOR_STARTUP_FOCUS_OK|launcher-permission,foreground-api,one-shot,dock-focus,lifecycle-complete");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
