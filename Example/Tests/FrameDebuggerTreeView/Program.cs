using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Diagnostics;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;
using UnityEditor.IMGUI.Controls;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.ExampleTests.FrameDebuggerTreeView;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            VerifyWindowUsesTreeView();
            var source = new GameObject("Debug Camera");
            var selectedSnapshotIndex = -1;
            var snapshot = CreateSnapshot(source.GetInstanceID());
            var tree = CreateTree(index => selectedSnapshotIndex = index);
            Invoke(tree, "SetSnapshot", snapshot, 0);

            VerifyHierarchy(tree);
            VerifySearch(tree);
            VerifySelectionAndKeyboard(tree, () => selectedSnapshotIndex,
                value => selectedSnapshotIndex = value);
            VerifyObjectLocation(tree, source);
            VerifyRenderedCounts(tree);
            VerifyAfterStepPresentation(tree);

            Console.WriteLine(
                "FRAME_DEBUGGER_TREEVIEW_OK|imgui-tree,hierarchy,event-counts,search,selection," +
                "keyboard-navigation,ctrl-ping,double-click-select,after-step-disabled");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FRAME_DEBUGGER_TREEVIEW_FAILED|{Unwrap(exception)}");
            return 1;
        }
        finally
        {
            FrameDebuggerService.Shared.Disable();
            Selection.activeObject = null;
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
    }

    private static void VerifyWindowUsesTreeView()
    {
        var editorAssembly = typeof(EditorWindow).Assembly;
        var windowType = editorAssembly.GetType("BEngine.Editor.FrameDebuggerWindow", true)!;
        var treeType = editorAssembly.GetType("BEngine.Editor.FrameDebuggerEventTreeView", true)!;
        var window = Activator.CreateInstance(windowType, nonPublic: true) ??
                     throw new InvalidOperationException("Could not create Frame Debugger window.");
        try
        {
            Invoke(window, "OnEnable");
            var tree = windowType.GetField("_eventTree", HiddenInstance)!.GetValue(window);
            Require(tree is not null && tree.GetType() == treeType,
                "FrameDebuggerWindow did not use FrameDebuggerEventTreeView for its event browser.");
        }
        finally { Invoke(window, "OnDisable"); }
    }

    private static TreeView<int> CreateTree(Action<int> selected)
    {
        var type = typeof(EditorWindow).Assembly.GetType(
            "BEngine.Editor.FrameDebuggerEventTreeView", throwOnError: true)!;
        Require(typeof(TreeView<int>).IsAssignableFrom(type),
            "Frame Debugger event browser does not directly use IMGUI.Controls.TreeView<int>.");
        return (TreeView<int>)(Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [new TreeViewState<int>(), selected], culture: null) ??
            throw new InvalidOperationException("Could not construct Frame Debugger TreeView."));
    }

    private static void VerifyHierarchy(TreeView<int> tree)
    {
        var rows = tree.GetRows().ToArray();
        Require(rows.Length == 8, $"Expected 8 expanded rows, found {rows.Length}.");
        Require(rows[0].displayName == "Camera: Main" && rows[0].depth == 0,
            "Camera marker was not represented as the root event group.");
        Require(rows[1].displayName == "Opaque Sprites" && rows[1].depth == 1,
            "Batch marker was not nested below its camera group.");
        Require(rows[2].depth == 2 && rows[3].depth == 2,
            "Render events were not nested below their batch.");
        Require(rows[4].displayName == "Transparent Sprites" && rows[4].depth == 1 &&
                rows[5].depth == 2,
            "A consecutive batch was not retained as a separate ordered branch.");
        Require(rows[6].displayName == "Camera: UI" && rows[6].depth == 0,
            "A subsequent camera marker did not retain render order.");
        Require(rows[7].depth == 1,
            "An unbatched render event was not nested directly below its marker group.");
    }

    private static void VerifySearch(TreeView<int> tree)
    {
        tree.searchString = "Player";
        var rows = tree.GetRows().ToArray();
        Require(rows.Length == 3 && rows[0].displayName == "Camera: Main" &&
                rows[1].displayName == "Opaque Sprites" && rows[2].depth == 2,
            "TreeView search did not keep the complete parent chain for a matching event.");
        tree.searchString = "Camera: Main";
        Require(tree.GetRows().Count == 6,
            "Matching a marker group did not expose its complete event subtree.");
        tree.searchString = string.Empty;
    }

    private static void VerifySelectionAndKeyboard(TreeView<int> tree, Func<int> selected,
        Action<int> reset)
    {
        var eventRows = tree.GetRows().Where(item => item.depth == 2).ToArray();
        tree.SetSelection([eventRows[1].id], TreeViewSelectionOptions.FireSelectionChanged);
        Require(selected() == 1, "Selecting an event row did not select its capture event.");
        tree.SetSelection([eventRows[0].id], TreeViewSelectionOptions.FireSelectionChanged);
        Require(selected() == 0, "Selecting an event row did not select its capture event.");
        reset(-1);
        tree.SetFocus();
        Dispatch(new Event(EventType.KeyDown) { keyCode = KeyCode.DownArrow },
            () => tree.OnGUI(new Rect(0, 0, 500, 280)));
        Require(selected() == 1 && tree.GetSelection().Single() == eventRows[1].id,
            "Down Arrow did not move Frame Debugger selection to the next render event.");
        Dispatch(new Event(EventType.KeyDown) { keyCode = KeyCode.UpArrow },
            () => tree.OnGUI(new Rect(0, 0, 500, 280)));
        Require(selected() == 0 && tree.GetSelection().Single() == eventRows[0].id,
            "Up Arrow did not restore the preceding render event selection.");
    }

    private static void VerifyObjectLocation(TreeView<int> tree, GameObject source)
    {
        var eventId = tree.GetRows().First(item => item.depth == 2).id;
        var type = tree.GetType();
        var single = type.GetMethod("SingleClickedItem", HiddenInstance)!;
        Dispatch(new Event(EventType.MouseUp) { modifiers = EventModifiers.Control },
            () => single.Invoke(tree, [eventId]));
        var pingType = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.EditorObjectPing", true)!;
        var currentTarget = pingType.GetProperty("currentTarget", BindingFlags.Static |
            BindingFlags.NonPublic)!.GetValue(null);
        Require(ReferenceEquals(currentTarget, source) && Selection.activeObject is null,
            "Ctrl-click did not Ping only the marker's source BObject.");

        var doubleClick = type.GetMethod("DoubleClickedItem", HiddenInstance)!;
        doubleClick.Invoke(tree, [eventId]);
        Require(ReferenceEquals(Selection.activeObject, source),
            "Double-click did not select the marker's source BObject.");
    }

    private static void VerifyRenderedCounts(TreeView<int> tree)
    {
        var commands = Render(() => tree.OnGUI(new Rect(0, 0, 500, 280)));
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(texts.Contains("3") && texts.Contains("2") && texts.Contains("1"),
            "Group and batch rows did not render their event counts.");
    }

    private static void VerifyAfterStepPresentation(TreeView<int> tree)
    {
        var service = FrameDebuggerService.Shared;
        service.Disable();
        service.Enable(new object(), "Hidden Game");
        service.SetStepLimit(1);
        var commands = Render(() => tree.OnGUI(new Rect(0, 0, 500, 280)));
        var texts = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(texts.Any(text => text.Contains("Draw Environment", StringComparison.Ordinal)) &&
                texts.All(text => !text.Contains("After Step", StringComparison.Ordinal)),
            "Events after the selected step changed their names instead of using disabled styling: " +
            string.Join(" | ", texts));
        service.Disable();
    }

    private static FrameDebugCaptureSnapshot CreateSnapshot(int sourceInstanceId)
    {
        var state = new FrameDebugRenderState(new GraphicsRect(0, 0, 1280, 720), null,
            GraphicsDepthState.Disabled, GraphicsBlendMode.AlphaBlend,
            GraphicsRasterizerState.CullBackFaces, "Sprite/Default", "Game Color", []);
        FrameDebugEvent Create(int index, string group, string batch, string name,
            string sourceName) => new(index, FrameDebugEventKind.Draw, name, state, "Quad",
            GraphicsPrimitiveTopology.TriangleList, 0, 6, 2, 0, default, NVector4.Zero,
            true, string.Empty, new FrameDebugMarker(group, batch, null, "Sprite/Default",
                "Gameplay Atlas", sourceName, sourceInstanceId));
        FrameDebugEvent[] events =
        [
            Create(0, "Camera: Main", "Opaque Sprites", "Draw Player", "Player"),
            Create(1, "Camera: Main", "Opaque Sprites", "Draw Environment", "Environment"),
            Create(2, "Camera: Main", "Transparent Sprites", "Draw Effects", "Effects"),
            Create(3, "Camera: UI", string.Empty, "Clear UI", "Debug Camera")
        ];
        return new FrameDebugCaptureSnapshot(42, "Game", GraphicsBackend.Vulkan,
            DateTimeOffset.UtcNow, events);
    }

    private static void Invoke(object target, string name, params object[] arguments) =>
        (target.GetType().GetMethod(name, HiddenInstance) ??
         throw new MissingMethodException(target.GetType().FullName, name)).Invoke(target, arguments);

    private static IReadOnlyList<GpuCanvasCommand> Render(Action draw)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [new Event(EventType.Repaint), 500, 280, commands]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static void Dispatch(Event evt, Action draw)
    {
        BeginFrame.Invoke(null, [evt, 500, 280, new List<GpuCanvasCommand>()]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : exception;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
