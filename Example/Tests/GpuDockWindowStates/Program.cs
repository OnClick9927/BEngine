using System.Reflection;
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
            VerifyNarrowTitleStability();
            Console.WriteLine(
                "GPU_DOCK_WINDOW_STATES_OK|normal,pop,modal,aux,in-process-layer,z-order,input-gating,popup-dismiss,drag-out,dock-back,title-context-menu,narrow-title-stability,narrow-title-ellipsis");
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
        var maximizePoint = Center(commands.Single(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == "[]").Rect);
        var closePoint = Center(commands.Single(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == "x").Rect);

        foreach (var point in new[] { morePoint, maximizePoint, closePoint })
        {
            RenderDock(dock, new Event(EventType.Repaint) { mousePosition = point });
            Require(TooltipCandidate() is null,
                "A docked window options, maximize, or close button still registered a tooltip.");
        }

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

    private static void VerifyNarrowTitleStability()
    {
        VerifyNarrowTitleStability(multipleTabs: false);
        VerifyNarrowTitleStability(multipleTabs: true);
    }

    private static void VerifyNarrowTitleStability(bool multipleTabs)
    {
        const string title = "Hierarchy Window With A Deliberately Long Title";
        const string titleIcon = "Icons/Windows/Hierarchy.png";
        var window = new ProbeWindow(title, titleIcon);
        var companion = multipleTabs ? new ProbeWindow("Companion") : null;
        window.OpenInternal();
        companion?.OpenInternal();
        var dock = new ImGuiDockWorkspace();
        if (companion is not null) dock.Add("Companion", companion, DockArea.Center, false);
        dock.Add("NarrowTitle", window, DockArea.Center, true);
        Require(dock.ToggleMaximize(window), "Could not maximize the narrow-title test window.");
        var bounds = new Rect(0, 0, 152, 280);
        var mode = multipleTabs ? "multiple tabs" : "single tab";

        try
        {
            var baseline = CaptureTitleFrame(dock, bounds, titleIcon, new Vector2(8, 12), false);
            Require(baseline.Content.Length < title.Length && baseline.Content.EndsWith(".",
                        StringComparison.Ordinal),
                "The narrow dock title was not replaced with a stable ellipsis label.");
            Require(baseline.ClipRect.Width > 0 && baseline.ImageClipRect.Width > 0,
                "The narrow-title regression did not exercise a visible dock viewport.");

            AssertTitleStable(baseline,
                CaptureTitleFrame(dock, bounds, titleIcon, new Vector2(8, 12), false),
                $"{mode} consecutive native frame");

            Vector2[] pointerPositions =
            [
                new(12, 12),
                new(74, 12),
                new(28, 90),
                new(145, 12),
                new(90, 190)
            ];
            foreach (var pointer in pointerPositions)
            {
                AssertTitleStable(baseline, CaptureTitleFrame(dock, bounds, titleIcon, pointer, true),
                    $"{mode} mouse move at {pointer}");
            }
        }
        finally
        {
            companion?.CloseInternal();
            window.CloseInternal();
        }
    }

    private static TitleSnapshot CaptureTitleFrame(ImGuiDockWorkspace dock, Rect bounds, string titleIcon,
        Vector2 pointer, bool movePointer)
    {
        RenderDock(dock, new Event(EventType.Layout) { mousePosition = pointer }, bounds: bounds);
        if (movePointer)
            RenderDock(dock, new Event(EventType.MouseMove) { mousePosition = pointer }, bounds: bounds);
        var commands = new List<GpuCanvasCommand>();
        RenderDock(dock, new Event(EventType.Repaint) { mousePosition = pointer }, commands, bounds);
        var imageCommand = commands.Single(command =>
            command.Type == GpuCanvasCommandType.Image && command.Content == titleIcon);
        var titleCommand = commands.Single(command =>
            command.Type == GpuCanvasCommandType.Text && command.ClipRect == imageCommand.ClipRect &&
            command.Rect.X > imageCommand.Rect.X);
        return new TitleSnapshot(titleCommand.Content, titleCommand.Rect, titleCommand.ClipRect,
            imageCommand.Rect, imageCommand.ClipRect);
    }

    private static void AssertTitleStable(TitleSnapshot expected, TitleSnapshot actual, string phase)
    {
        Require(actual.Content == expected.Content,
            $"The narrow dock title content changed during {phase}.");
        Require(actual.Rect == expected.Rect,
            $"The narrow dock title rectangle moved during {phase}: {expected.Rect} -> {actual.Rect}.");
        Require(actual.ClipRect == expected.ClipRect,
            $"The narrow dock title clip changed during {phase}: {expected.ClipRect} -> {actual.ClipRect}.");
        Require(actual.ImageRect == expected.ImageRect,
            $"The narrow dock title icon moved during {phase}: {expected.ImageRect} -> {actual.ImageRect}.");
        Require(actual.ImageClipRect == expected.ImageClipRect,
            $"The narrow dock title icon clip changed during {phase}: " +
            $"{expected.ImageClipRect} -> {actual.ImageClipRect}.");
    }

    private static void RenderDock(ImGuiDockWorkspace dock, Event evt,
        List<GpuCanvasCommand>? commands = null, Rect? bounds = null)
    {
        var area = bounds ?? new Rect(0, 0, 1000, 700);
        GUI.BeginFrame(evt, (int)area.width, (int)area.height, commands ?? []);
        try { dock.OnGUI(area); }
        finally { GUI.EndFrame(); }
    }

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));

    private static string MenuSignature(IEnumerable<GenericMenuItem> items) => string.Join('|',
        items.Select(item => $"{item.Path}:{item.On}:{item.Enabled}:{item.Separator}"));

    private static string? TooltipCandidate() => typeof(GUI).GetField("_tooltipCandidate",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) as string;

    private readonly record struct TitleSnapshot(
        string Content,
        GpuCanvasRect Rect,
        GpuCanvasRect ClipRect,
        GpuCanvasRect ImageRect,
        GpuCanvasRect ImageClipRect);

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
