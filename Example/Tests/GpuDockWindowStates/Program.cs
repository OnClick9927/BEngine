using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.GpuDockWindowStates;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyWindowStates();
            EditorWindowLayerRegressionTests.Run();
            VerifyDragOutAndDockBack();
            VerifyTitleContextMenu();
            Console.WriteLine(
                "GPU_DOCK_WINDOW_STATES_OK|normal,pop,modal,aux,in-process-layer,z-order,input-gating,popup-dismiss,drag-out,dock-back,title-context-menu");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GPU_DOCK_WINDOW_STATES_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyWindowStates()
    {
        Require(Enum.GetNames<EditorWindowState>().SequenceEqual(["Normal", "Pop", "Modal", "Aux"]),
            "EditorWindow does not expose the Normal/Pop/Modal/Aux states.");
        var window = new ProbeWindow("States");

        window.Show();
        Require(window.ConsumeRequestedState() == EditorWindowState.Normal, "Show did not request Normal.");
        window.ShowPopup();
        Require(window.ConsumeRequestedState() == EditorWindowState.Pop, "ShowPopup did not request Pop.");
        window.ShowModal();
        Require(window.ConsumeRequestedState() == EditorWindowState.Modal, "ShowModal did not request Modal.");
        window.ShowModalUtility();
        Require(window.ConsumeRequestedState() == EditorWindowState.Modal,
            "ShowModalUtility did not request Modal.");
        window.ShowUtility();
        Require(window.ConsumeRequestedState() == EditorWindowState.Aux, "ShowUtility did not request Aux.");
        window.ShowAuxWindow();
        Require(window.ConsumeRequestedState() == EditorWindowState.Aux, "ShowAuxWindow did not request Aux.");

        var buttonRect = new Rect(12, 24, 80, 20);
        var dropDownSize = new Vector2(320, 240);
        window.ShowAsDropDown(buttonRect, dropDownSize);
        Require(window.ConsumeRequestedState() == EditorWindowState.Pop,
            "ShowAsDropDown did not request an in-editor popup.");
        Require(window.position.Equals(new Rect(12, 44, 320, 240)),
            "ShowAsDropDown did not anchor its overlay below the requested editor rectangle.");
    }

    private static void VerifyDragOutAndDockBack()
    {
        var anchor = new ProbeWindow("Anchor");
        var dragged = new ProbeWindow("Dragged");
        anchor.OpenInternal();
        dragged.OpenInternal();
        var dock = new ImGuiDockWorkspace();
        var anchorPanel = dock.Add("Anchor", anchor, DockArea.Center, true);
        var draggedPanel = dock.Add("Dragged", dragged, DockArea.Center, true);
        ImGuiDockPanel? undocked = null;
        dock.UndockRequested += (panel, _) => undocked = panel;

        RenderDock(dock, new Event(EventType.Layout));
        RenderDock(dock, new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(310, 12), button = 0
        });
        RenderDock(dock, new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(310, -30), button = 0
        });
        RenderDock(dock, new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(310, -30), button = 0
        });
        Require(ReferenceEquals(undocked, draggedPanel),
            "Releasing a dragged tab outside the workspace did not request a floating window.");

        dock.Remove(draggedPanel.Id);
        var dockedAgain = dock.DockExternal(draggedPanel.Id, dragged, new Vector2(430, 220));
        Require(ReferenceEquals(anchorPanel.Group, dockedAgain.Group) && dock.IsSelected(dragged),
            "A floating Normal window could not return to the target dock tab group.");
        anchor.CloseInternal();
        dragged.CloseInternal();
    }

    private static void VerifyTitleContextMenu()
    {
        var first = new ContextMenuProbeWindow("Context A");
        var second = new ContextMenuProbeWindow("Context B");
        first.OpenInternal();
        second.OpenInternal();
        var dock = new ImGuiDockWorkspace();
        dock.Add("Context A", first, DockArea.Center, false);
        dock.Add("Context B", second, DockArea.Center, true);

        var commands = new List<GpuCanvasCommand>();
        RenderDock(dock, new Event(EventType.Repaint), commands);
        var firstTitle = commands.Single(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == "Context A");
        var moreButton = commands.Single(command =>
            command.Type == GpuCanvasCommandType.Image &&
            command.Content.EndsWith("More.png", StringComparison.Ordinal));
        var titlePoint = Center(firstTitle.Rect);
        var morePoint = Center(moreButton.Rect);

        IReadOnlyList<GenericMenuItem>? captured = null;
        GenericMenuDispatcher.Handler = items => captured = items.ToArray();
        try
        {
            var contextClick = new Event(EventType.ContextClick)
            {
                mousePosition = titlePoint,
                button = 1
            };
            RenderDock(dock, contextClick);
            Require(contextClick.type == EventType.Used,
                "Right-clicking an EditorWindow title did not consume the context event.");
            Require(dock.IsSelected(first) && first.hasFocus,
                "Right-clicking a non-selected EditorWindow title did not select and focus its window.");
            var titleMenu = captured?.ToArray() ??
                            throw new InvalidOperationException("EditorWindow title right-click did not open a menu.");
            Require(titleMenu.Any(item => item.Path == "Probe/Context A"),
                "EditorWindow title right-click opened the menu for the wrong window.");
            Require(titleMenu.Any(item => item.Path == "Float") &&
                    titleMenu.Any(item => item.Path == "Lock") &&
                    titleMenu.Any(item => item.Path == "Close Tab"),
                "EditorWindow title menu is missing Float, Lock, or Close Tab.");

            captured = null;
            RenderDock(dock, new Event(EventType.MouseDown) { mousePosition = morePoint, button = 0 });
            RenderDock(dock, new Event(EventType.MouseUp) { mousePosition = morePoint, button = 0 });
            var optionsMenu = captured?.ToArray() ??
                              throw new InvalidOperationException("Three-dot window options did not open a menu.");
            Require(MenuSignature(titleMenu) == MenuSignature(optionsMenu),
                "Title right-click and three-dot options produced different EditorWindow menus.");
        }
        finally
        {
            GenericMenuDispatcher.Handler = null;
            first.CloseInternal();
            second.CloseInternal();
        }
    }

    private static void RenderDock(ImGuiDockWorkspace dock, Event evt,
        List<GpuCanvasCommand>? commands = null)
    {
        GUI.BeginFrame(evt, 1000, 700, commands ?? []);
        try { dock.OnGUI(new Rect(0, 0, 1000, 700)); }
        finally { GUI.EndFrame(); }
    }

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));

    private static string MenuSignature(IEnumerable<GenericMenuItem> items) => string.Join('|',
        items.Select(item => $"{item.Path}:{item.On}:{item.Enabled}:{item.Separator}"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ContextMenuProbeWindow(string title) : EditorWindow
    {
        public override void AddItemsToMenu(GenericMenu menu) =>
            menu.AddItem(new GUIContent($"Probe/{title}"), false, () => { });

        protected override void OnEnable() => titleContent = new GUIContent(title);
    }
}
