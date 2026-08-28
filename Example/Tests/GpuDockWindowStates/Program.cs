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
            VerifyNativeCrossMonitorDockTracking();
            VerifyNativeGeometryRecovery();
            VerifyNativeWindowFrameScheduling();
            VerifyDockHostHoverIsolation();
            VerifyTitleContextMenu();
            VerifyWindowLockChrome();
            VerifyNarrowTitleStability();
            Console.WriteLine(
                "GPU_DOCK_WINDOW_STATES_OK|normal,pop,modal,aux,native-float,cross-monitor,dpi-coordinates,cross-dpi-caption,resize-not-dock,offscreen-recovery,negative-monitor,inactive-render-throttle,repaint-wakeup,restore-wakeup,focus-wakeup,dock-host-hover-isolation,in-process-transients,z-order,input-gating,popup-dismiss,drag-out-immediate,splitter-not-float,dock-back,dock-float-lock-roundtrip,title-context-menu,window-lock,narrow-title-stability,narrow-title-ellipsis");
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
        var dragged = new LockProbeWindow();
        anchor.OpenInternal();
        dragged.OpenInternal();
        dragged.isLocked = true;
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
        Require(ReferenceEquals(undocked, draggedPanel),
            "Dragging a docked title beyond the workspace did not request Float immediately.");
        RenderDock(dock, new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(310, -30), button = 0
        });
        Require(ReferenceEquals(undocked, draggedPanel),
            "Releasing a dragged tab outside the workspace did not request a floating window.");

        dock.Remove(draggedPanel.Id);
        Require(dragged.isLocked,
            "Changing a locked docked window to Float discarded its lock state.");
        var dockedAgain = dock.DockExternal(draggedPanel.Id, dragged, new Vector2(430, 220));
        Require(ReferenceEquals(anchorPanel.Group, dockedAgain.Group) && dock.IsSelected(dragged),
            "A floating Normal window could not return to the target dock tab group.");
        Require(dragged.isLocked,
            "Docking a locked floating window discarded its lock state.");

        undocked = null;
        RenderDock(dock, new Event(EventType.Layout));
        var splitterPoint = Enumerable.Range(1, 698)
            .Select(y => new Vector2(500, y))
            .First(point => !dock.CanDockAt(point));
        RenderDock(dock, new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(310, 12), button = 0
        });
        RenderDock(dock, new Event(EventType.MouseDrag)
        {
            mousePosition = splitterPoint, button = 0
        });
        RenderDock(dock, new Event(EventType.MouseUp)
        {
            mousePosition = splitterPoint, button = 0
        });
        Require(undocked is null,
            "Releasing a dragged tab over an in-workspace splitter incorrectly requested Float.");
        anchor.CloseInternal();
        dragged.CloseInternal();
    }

    private static void VerifyNativeCrossMonitorDockTracking()
    {
        var logical = new Vector2(240, 135);
        foreach (var scale in new[] { Fix64.FromDecimal(0.5m), Fix64.One, Fix64.FromDecimal(1.8m) })
        {
            var client = ImGuiNativeWindow.GUIToClient(logical, scale);
            Require(ImGuiNativeWindow.ClientToGUI(client, scale) == logical,
                $"GUI/client coordinate conversion did not round-trip at editor scale {scale}.");
        }
        var highDpiClient = ImGuiNativeWindow.GUIToClient(logical,
            Fix64.FromDecimal(1.25m), Fix64.FromDecimal(1.5m));
        Require(ImGuiNativeWindow.ClientToGUI(highDpiClient,
                    Fix64.FromDecimal(1.25m), Fix64.FromDecimal(1.5m)) == logical,
            "GUI/client coordinate conversion did not round-trip with per-monitor DPI scaling.");

        var tracker = new NativeWindowDockTracker();
        var size = new Vector2(480, 320);
        tracker.Reset(new Vector2(1800, 120), size);
        Require(tracker.Observe(new Vector2(1800, 120), size, true, null).Phase == NativeDockDragPhase.None,
            "Pressing a native title bar without moving incorrectly started a dock operation.");
        Require(tracker.Observe(new Vector2(2420, -180), size, true, null).Phase == NativeDockDragPhase.None,
            "Moving a native float onto another monitor incorrectly clamped it to the main workspace.");
        var dockPoint = new Vector2(460, 280);
        var preview = tracker.Observe(new Vector2(-820, 90), size, true, dockPoint);
        Require(preview == new NativeDockDragUpdate(NativeDockDragPhase.Preview, dockPoint),
            "A cross-monitor native drag did not produce a dock preview after re-entering the main window.");
        var drop = tracker.Observe(new Vector2(-820, 90), size, false, dockPoint);
        Require(drop == new NativeDockDragUpdate(NativeDockDragPhase.Drop, dockPoint),
            "Releasing a cross-monitor native drag over the editor did not request docking.");

        // Win32 can hold the editor thread in its native move loop. In that case the final moved
        // position is first observed after mouse release and must still complete the dock.
        tracker.Reset(new Vector2(1700, 80), size);
        tracker.Observe(new Vector2(1700, 80), size, true, null);
        var modalMoveDrop = tracker.Observe(new Vector2(320, 150), size, false, dockPoint);
        Require(modalMoveDrop == new NativeDockDragUpdate(NativeDockDragPhase.Drop, dockPoint),
            "A native modal move loop lost the dock request when movement became visible after release.");

        tracker.Reset(new Vector2(900, 200), size);
        tracker.Observe(new Vector2(900, 200), size, true, null);
        Require(tracker.Observe(new Vector2(940, 200), new Vector2(440, 320), true, dockPoint).Phase ==
                NativeDockDragPhase.None &&
                tracker.Observe(new Vector2(940, 200), new Vector2(440, 320), false, dockPoint).Phase ==
                NativeDockDragPhase.None,
            "Resizing a native float from its left border incorrectly requested docking.");

        tracker.Reset(new Vector2(900, 200), size);
        tracker.Observe(new Vector2(900, 200), size, true, null);
        Require(tracker.Observe(new Vector2(900, 240), new Vector2(480, 280), false, dockPoint).Phase ==
                NativeDockDragPhase.None,
            "A native top-border resize observed after the modal loop incorrectly requested docking.");

        tracker.Reset(new Vector2(1900, 120), size);
        tracker.Observe(new Vector2(1900, 120), size, true, null,
            NativeWindowPointerOperation.CaptionMove);
        var dpiAdjustedSize = new Vector2(720, 480);
        Require(tracker.Observe(new Vector2(300, 140), dpiAdjustedSize, true, dockPoint).Phase ==
                NativeDockDragPhase.Preview &&
                tracker.Observe(new Vector2(300, 140), dpiAdjustedSize, false, dockPoint).Phase ==
                NativeDockDragPhase.Drop,
            "A caption drag with a per-monitor DPI size adjustment was misclassified as a resize.");

        tracker.Reset(new Vector2(900, 200), size);
        tracker.Observe(new Vector2(900, 200), size, true, null,
            NativeWindowPointerOperation.BorderResize);
        Require(tracker.Observe(new Vector2(940, 200), size, false, dockPoint).Phase ==
                NativeDockDragPhase.None,
            "An explicitly hit-tested native border resize incorrectly requested docking.");
    }

    private static void VerifyNativeGeometryRecovery()
    {
        var workAreas = new[]
        {
            new Rect(-1920, 0, 1920, 1040),
            new Rect(0, 0, 1920, 1040),
            new Rect(1920, -220, 2560, 1400)
        };
        var negativeMonitorWindow = new Rect(-1700, 80, 640, 480);
        Require(NativeFloatingWindowGeometry.RestoreToVisibleWorkArea(
                    negativeMonitorWindow, workAreas).Equals(negativeMonitorWindow),
            "A visible float on a negative-coordinate monitor was moved to another display.");

        var removedMonitorWindow = new Rect(5100, 120, 700, 500);
        var recovered = NativeFloatingWindowGeometry.RestoreToVisibleWorkArea(
            removedMonitorWindow, workAreas);
        Require(recovered.x >= 1920 && recovered.xMax <= 4480 &&
                recovered.y >= -220 && recovered.yMax <= 1180,
            $"An off-screen saved float was not recovered to the nearest remaining work area: {recovered}.");

        var oversized = NativeFloatingWindowGeometry.RestoreToVisibleWorkArea(
            new Rect(6000, 3000, 5000, 2400), workAreas);
        Require(oversized.width == 5000 && oversized.height == 2400 &&
                oversized.x == 1920 && oversized.y == -220,
            "Recovering an oversized float changed its constrained size or selected the wrong work area.");

        var constrained = NativeFloatingWindowGeometry.ConstrainSize(
            new Rect(50, 60, 1200, 90), new Vector2(300, 180), new Vector2(900, 700));
        Require(constrained.Equals(new Rect(50, 60, 900, 180)),
            "Native float geometry did not enforce EditorWindow minSize/maxSize.");
    }

    private static void VerifyNativeWindowFrameScheduling()
    {
        var interval = NativeWindowFrameScheduler.UnfocusedFrameIntervalTicks;
        var scheduler = new NativeWindowFrameScheduler();
        var initial = scheduler.Evaluate(focused: false, minimized: false, timestamp: 100);
        Require(initial.ShouldRender, "A manually pumped native window skipped its first frame.");
        scheduler.NotifyRendered(initial);

        Require(!scheduler.Evaluate(false, false, 100 + interval - 1).ShouldRender,
            "An idle unfocused native window rendered before its throttle interval elapsed.");
        var periodic = scheduler.Evaluate(false, false, 100 + interval);
        Require(periodic.ShouldRender,
            "An unfocused native window did not receive its low-frequency maintenance frame.");
        scheduler.NotifyRendered(periodic);

        scheduler.RequestRender();
        var repaint = scheduler.Evaluate(false, false, 101 + interval);
        Require(repaint.ShouldRender, "Repaint did not wake an unfocused native window immediately.");
        scheduler.RequestRender();
        scheduler.NotifyRendered(repaint);
        Require(scheduler.Evaluate(false, false, 101 + interval).ShouldRender,
            "A repaint requested during rendering was lost when that frame completed.");

        var minimizedScheduler = new NativeWindowFrameScheduler();
        Require(!minimizedScheduler.Evaluate(false, true, 200).ShouldRender,
            "A minimized native window submitted its initial GPU frame.");
        minimizedScheduler.RequestRender();
        Require(!minimizedScheduler.Evaluate(false, true, 201).ShouldRender,
            "A repaint forced GPU submission while the native window was minimized.");
        Require(minimizedScheduler.Evaluate(false, false, 202).ShouldRender,
            "Restoring a minimized native window did not render immediately.");

        var focusScheduler = new NativeWindowFrameScheduler();
        var unfocused = focusScheduler.Evaluate(false, false, 300);
        focusScheduler.NotifyRendered(unfocused);
        Require(!focusScheduler.Evaluate(false, false, 301).ShouldRender,
            "The focus scheduling probe did not enter its idle state.");
        Require(focusScheduler.Evaluate(true, false, 302).ShouldRender,
            "Refocusing a native window did not render immediately.");
    }

    private static void VerifyDockHostHoverIsolation()
    {
        var docked = new ProbeWindow("Docked hover");
        var otherHost = new ProbeWindow("Other native host");
        docked.OpenInternal();
        otherHost.OpenInternal();
        var dock = new ImGuiDockWorkspace();
        dock.Add("Docked hover", docked, DockArea.Center, true);
        var pointer = new Vector2(300, 100);

        try
        {
            RenderDock(dock, new Event(EventType.Repaint) { mousePosition = pointer },
                hostIsInteractive: true);
            Require(ReferenceEquals(EditorWindow.mouseOverWindow, docked),
                "An interactive dock host did not publish its hovered EditorWindow.");

            EditorWindow.SetMouseOverWindow(otherHost);
            RenderDock(dock, new Event(EventType.Repaint) { mousePosition = pointer },
                hostIsInteractive: false);
            Require(ReferenceEquals(EditorWindow.mouseOverWindow, otherHost),
                "An unfocused main dock stole hover ownership from another native host.");

            EditorWindow.SetMouseOverWindow(docked);
            RenderDock(dock, new Event(EventType.Repaint) { mousePosition = pointer },
                hostIsInteractive: false);
            Require(EditorWindow.mouseOverWindow is null,
                "An unfocused main dock retained stale hover ownership for one of its panels.");
        }
        finally
        {
            EditorWindow.SetMouseOverWindow(null);
            docked.CloseInternal();
            otherHost.CloseInternal();
        }
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

    private static void VerifyWindowLockChrome()
    {
        var window = new LockProbeWindow();
        window.OpenInternal();
        var dock = new ImGuiDockWorkspace();
        dock.Add("LockProbe", window, DockArea.Center, true);
        try
        {
            var commands = new List<GpuCanvasCommand>();
            RenderDock(dock, new Event(EventType.Repaint), commands);
            var unlocked = commands.Single(command => command.Type == GpuCanvasCommandType.Image &&
                command.Content.EndsWith("Unlock.png", StringComparison.Ordinal));
            var point = Center(unlocked.Rect);
            RenderDock(dock, new Event(EventType.MouseDown) { mousePosition = point, button = 0 });
            RenderDock(dock, new Event(EventType.MouseUp) { mousePosition = point, button = 0 });
            Require(window.isLocked && window.LockChanges == 1,
                "The dock title lock button did not change the EditorWindow lock state.");

            commands.Clear();
            RenderDock(dock, new Event(EventType.Repaint), commands);
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                            command.Content.EndsWith("Lock.png", StringComparison.Ordinal)),
                "The dock title did not render the locked state icon.");

            var nested = typeof(GpuEditorApplication).GetNestedTypes(BindingFlags.NonPublic)
                .Where(type => type.Name is "ImGuiHierarchyWindow" or "ImGuiProjectWindow" or
                    "ImGuiInspectorWindow").ToArray();
            Require(nested.Length == 3 && nested.All(type =>
            {
                var instance = (EditorWindow)Activator.CreateInstance(type, nonPublic: true)!;
                var property = typeof(EditorWindow).GetProperty("supportsLocking",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                return (bool)property.GetValue(instance)!;
            }), "Hierarchy, Project, and Inspector do not all expose title lock controls.");
        }
        finally { window.CloseInternal(); }
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
        List<GpuCanvasCommand>? commands = null, Rect? bounds = null,
        bool hostIsInteractive = true)
    {
        var area = bounds ?? new Rect(0, 0, 1000, 700);
        GUI.BeginFrame(evt, (int)area.width, (int)area.height, commands ?? []);
        dock.HostIsInteractive = hostIsInteractive;
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

    private sealed class LockProbeWindow : EditorWindow
    {
        internal int LockChanges { get; private set; }
        internal override bool supportsLocking => true;

        internal LockProbeWindow() => titleContent = new GUIContent("Lock Probe");

        protected override void OnLockStateChanged() => LockChanges++;
    }
}
