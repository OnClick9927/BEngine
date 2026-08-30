using BEngine.Editor.Rendering;

namespace BEngine.Editor;

internal static class DockHostTransferTests
{
    internal static void Run()
    {
        var window = new HostTransferProbeWindow();
        window.OpenInternal();
        window.FocusInternal();
        EditorWindow.SetMouseOverWindow(window);
        GUIUtility.hotControl = 90210;
        GUIUtility.keyboardControl = 90211;

        try
        {
            GpuEditorApplication.PrepareWindowForHostTransfer(window);
            Require(!window.hasFocus && window.LostFocusCount == 1,
                "Moving an EditorWindow between native and dock hosts did not release its old focus owner.");
            Require(GUIUtility.hotControl == 0 && GUIUtility.keyboardControl == 0,
                "Moving an EditorWindow between hosts retained controls owned by its old IMGUI frame.");
            Require(!ReferenceEquals(EditorWindow.mouseOverWindow, window),
                "Moving an EditorWindow between hosts retained stale hover ownership.");

            var dock = new ImGuiDockWorkspace();
            dock.DockExternal("HostTransferProbe", window, new Vector2(400, 220));
            window.FocusInternal();
            Require(window.hasFocus && window.FocusCount == 2,
                "The dock host could not reacquire focus for the transferred EditorWindow.");

            var commands = new List<GpuCanvasCommand>();
            RenderDock(dock, new Event(EventType.Repaint), commands);
            var action = commands.Single(command =>
                command.Type == GpuCanvasCommandType.Text && command.Content == "Dock transfer action");
            var point = new Vector2((Fix64)(action.Rect.X + action.Rect.Width / 2),
                (Fix64)(action.Rect.Y + action.Rect.Height / 2));
            RenderDock(dock, new Event(EventType.MouseDown) { mousePosition = point, button = 0 });
            RenderDock(dock, new Event(EventType.MouseUp) { mousePosition = point, button = 0 });
            Require(window.ClickCount == 1,
                "The re-docked EditorWindow was painted but did not receive IMGUI pointer events.");
        }
        finally
        {
            window.CloseInternal();
            GUIUtility.ReleaseInputFocus();
            EditorWindow.SetMouseOverWindow(null);
        }
    }

    private static void RenderDock(ImGuiDockWorkspace dock, Event current,
        List<GpuCanvasCommand>? commands = null)
    {
        GUI.BeginFrame(current, 1000, 700, commands ?? []);
        dock.HostIsInteractive = true;
        try { dock.OnGUI(new Rect(0, 0, 1000, 700)); }
        finally { GUI.EndFrame(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HostTransferProbeWindow : EditorWindow
    {
        internal int ClickCount { get; private set; }
        internal int FocusCount { get; private set; }
        internal int LostFocusCount { get; private set; }

        internal HostTransferProbeWindow() => titleContent = new GUIContent("Host Transfer Probe");

        protected override void OnFocus() => FocusCount++;
        protected override void OnLostFocus() => LostFocusCount++;

        protected override void OnGUI()
        {
            if (GUI.Button(new Rect(12, 12, 180, 24), "Dock transfer action")) ClickCount++;
        }
    }
}
