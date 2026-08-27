using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.EditorWindowChromeCursor;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyRuntimeScreenAndCursor();
            VerifyEditorCursorRect();
            VerifyDockChromeColors();
            Console.WriteLine("EDITOR_WINDOW_CHROME_CURSOR_OK|title-content-colors,resize-cursors,screen,cursor");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyRuntimeScreenAndCursor()
    {
        Screen.SetResolution(1920, 1080, FullScreenMode.Windowed, 144);
        Require(Screen.width == 1920 && Screen.height == 1080, "Screen size was not updated.");
        Require(Screen.currentResolution.refreshRate == 144, "Screen refresh rate was not updated.");
        Require(Screen.safeArea.Equals(new Rect(0, 0, 1920, 1080)), "Screen safe area is invalid.");
        Screen.fullScreen = true;
        Require(Screen.fullScreenMode == FullScreenMode.FullScreenWindow, "Fullscreen mode was not synchronized.");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.SetCursor(null, new Vector2(3, 5), CursorMode.ForceSoftware);
        Require(!Cursor.visible && Cursor.lockState == CursorLockMode.Confined,
            "Cursor visibility or lock state was not retained.");
        Require(Cursor.hotspot == new Vector2(3, 5) && Cursor.mode == CursorMode.ForceSoftware,
            "Cursor presentation state was not retained.");
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private static void VerifyEditorCursorRect()
    {
        var commands = new List<GpuCanvasCommand>();
        var begin = typeof(GUI).GetMethod("BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
        var end = typeof(GUI).GetMethod("EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
        var requested = typeof(GUI).GetProperty("requestedMouseCursor",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        begin.Invoke(null, [new Event(EventType.MouseMove) { mousePosition = new Vector2(12, 12) },
            200, 120, commands]);
        try
        {
            EditorGUIUtility.AddCursorRect(new Rect(8, 8, 20, 20), MouseCursor.SplitResizeLeftRight);
            Require((MouseCursor)requested.GetValue(null)! == MouseCursor.SplitResizeLeftRight,
                "A hovered resize area did not request the horizontal resize cursor.");
        }
        finally
        {
            end.Invoke(null, null);
        }

        var nativeWindow = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.ImGuiNativeWindow", true)!;
        Require(nativeWindow.GetMethod("ApplyMouseCursor", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "The native editor window does not apply requested mouse cursors.");
    }

    private static void VerifyDockChromeColors()
    {
        EditorAppearance.Apply(new EditorPreferencesDocument());
        Require(!EditorStyles.toolbar.normal.backgroundColor.Equals(
                    GUI.skin.viewBackground.normal.backgroundColor),
            "EditorWindow title bar and content colors must remain visually distinct.");
        Require(EditorStyles.dockTab.normal.backgroundColor.Equals(
                    EditorStyles.windowTitle.normal.backgroundColor),
            "Inactive dock tabs do not use the title bar color.");
        Require(!EditorStyles.dockTabActive.normal.backgroundColor.Equals(
                    EditorStyles.dockTab.normal.backgroundColor) &&
                EditorStyles.dockTabActive.normal.backgroundColor.a > 0,
            "The active dock tab is not visually distinct from an inactive tab.");

        var dock = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.ImGuiDockWorkspace", true)!;
        Require(dock.GetMethod("DrawSplit", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Dock split interaction is missing.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
