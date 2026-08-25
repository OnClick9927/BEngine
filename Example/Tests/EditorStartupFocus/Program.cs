using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.ExampleTests.EditorStartupFocus;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var editorAssembly = Assembly.Load("BEngine.Editor");
            var applicationType = editorAssembly.GetType("BEngine.Editor.GpuEditorApplication", true)!;
            var nativeWindowType = editorAssembly.GetType("BEngine.Editor.ImGuiNativeWindow", true)!;
            var windowLayerType = editorAssembly.GetType("BEngine.Editor.EditorWindowLayer", true)!;

            var run = applicationType.GetMethod("Run", BindingFlags.Instance | BindingFlags.Public,
                binder: null, [typeof(Action)], modifiers: null);
            Require(run is not null,
                "GPU editor application does not expose its first-frame-complete Run callback.");
            Require(nativeWindowType.GetEvent("firstFrameRendered", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic) is not null &&
                    nativeWindowType.GetMethod("Focus", BindingFlags.Instance | BindingFlags.Public) is not null,
                "The native GPU editor window cannot focus itself after its first rendered frame.");
            Require(nativeWindowType.GetEvent("focusChanged", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic) is not null,
                "The native GPU editor window does not publish focus lifecycle changes.");
            Require(windowLayerType.GetMethod("Focus", BindingFlags.Instance | BindingFlags.Public) is not null &&
                    windowLayerType.GetProperty("TopModalWindow", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic) is not null,
                "The in-process editor window layer cannot restore focus to its modal window.");

            var launcherAssembly = Assembly.Load("BEngine.Launcher");
            var launcherType = launcherAssembly.GetType("BEngine.Launcher.ProjectLauncherForm", true)!;
            var foregroundPermissionImport = launcherType.GetMethod("AllowSetForegroundWindow",
                    BindingFlags.Static | BindingFlags.NonPublic)?.GetCustomAttribute<DllImportAttribute>();
            Require(foregroundPermissionImport?.Value.Equals("user32.dll",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Launcher does not transfer foreground permission to the Editor process.");

            Console.WriteLine(
                "EDITOR_STARTUP_FOCUS_OK|launcher-permission,first-frame-focus,native-focus-events,modal-focus");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_STARTUP_FOCUS_FAILED|{exception}");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
