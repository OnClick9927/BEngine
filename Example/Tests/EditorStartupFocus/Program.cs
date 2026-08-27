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

            var canQueryNativeFocusState = nativeWindowType.GetMethod("CanQueryNativeFocusState",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null, [typeof(bool), typeof(bool), typeof(bool)], modifiers: null) ??
                throw new InvalidOperationException(
                    "The native focus guard cannot stop GLFW state queries before initialization.");
            Require(!CanQueryNativeFocusState(canQueryNativeFocusState, disposed: false,
                    initialized: false, focused: false),
                "Layout restoration can query GLFW state before the native window is initialized.");
            Require(CanQueryNativeFocusState(canQueryNativeFocusState, disposed: false,
                    initialized: true, focused: false),
                "A live background editor window cannot query the native focus state.");

            var canInvokeNativeFocus = nativeWindowType.GetMethod("CanInvokeNativeFocus",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null, [typeof(bool), typeof(bool), typeof(bool), typeof(bool)], modifiers: null) ??
                throw new InvalidOperationException(
                    "The native focus guard does not account for initialization, shutdown, and current focus.");
            Require(!CanInvokeNativeFocus(canInvokeNativeFocus, disposed: true, initialized: true,
                    closing: false, focused: false),
                "A disposed editor window can still invoke GLFW focus.");
            Require(!CanInvokeNativeFocus(canInvokeNativeFocus, disposed: false, initialized: false,
                    closing: false, focused: false),
                "Layout restoration can invoke GLFW focus before the native window is initialized.");
            Require(!CanInvokeNativeFocus(canInvokeNativeFocus, disposed: false, initialized: true,
                    closing: true, focused: false),
                "A closing editor window can still invoke GLFW focus.");
            Require(!CanInvokeNativeFocus(canInvokeNativeFocus, disposed: false, initialized: true,
                    closing: false, focused: true),
                "An already focused editor window redundantly invokes GLFW focus.");
            Require(CanInvokeNativeFocus(canInvokeNativeFocus, disposed: false, initialized: true,
                    closing: false, focused: false),
                "A live background editor window cannot request native focus.");
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
                "EDITOR_STARTUP_FOCUS_OK|launcher-permission,first-frame-focus,native-focus-events," +
                "preinitialize-focus-guard,closing-focus-guard,already-focused-guard,modal-focus");
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

    private static bool CanInvokeNativeFocus(MethodInfo method, bool disposed, bool initialized,
        bool closing, bool focused) =>
        method.Invoke(null, [disposed, initialized, closing, focused]) is true;

    private static bool CanQueryNativeFocusState(MethodInfo method, bool disposed, bool initialized,
        bool focused) =>
        method.Invoke(null, [disposed, initialized, focused]) is true;
}
