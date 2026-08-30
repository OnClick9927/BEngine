using System.Collections;
using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.DockPreview;

internal static class Program
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic;

    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            VerifyGpuDockPreview();
            Console.WriteLine(
                "DOCK_PREVIEW_OK|gpu-imgui,four-areas,five-drop-targets,accent-overlay,preview-change-signal,split-region,empty-collapse");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DOCK_PREVIEW_FAILED|{Unwrap(exception)}");
            return 1;
        }
    }

    private static void VerifyGpuDockPreview()
    {
        var editorAssembly = typeof(EditorWindow).Assembly;
        var workspaceType = editorAssembly.GetType("BEngine.Editor.ImGuiDockWorkspace", true)!;
        var areaType = editorAssembly.GetType("BEngine.Editor.DockArea", true)!;
        var workspace = Activator.CreateInstance(workspaceType, nonPublic: true)!;
        var add = workspaceType.GetMethod("Add", InstanceMembers)!;
        var remove = workspaceType.GetMethod("Remove", InstanceMembers)!;
        var dockExternal = workspaceType.GetMethod("DockExternal", InstanceMembers)!;
        var captureLayout = workspaceType.GetMethod("CaptureLayout", InstanceMembers)!;
        var setExternalDragPoint = workspaceType.GetMethod("SetExternalDragPoint", InstanceMembers)!;
        var tryGetDrop = workspaceType.GetMethod("TryGetDrop", InstanceMembers)!;

        foreach (var areaName in new[] { "Left", "Center", "Right", "Bottom" })
            add.Invoke(workspace,
                [areaName, new ProbeWindow(areaName), Enum.Parse(areaType, areaName), true]);

        Render(workspaceType, workspace, new Event(EventType.Layout));
        var panels = ((IEnumerable)workspaceType.GetProperty("Panels", InstanceMembers)!
            .GetValue(workspace)!).Cast<object>().ToArray();
        Require(panels.Length == 4, "GPU dock workspace did not retain its four default areas.");

        var centerPanel = panels.Single(panel =>
            (string)panel.GetType().GetProperty("Id", InstanceMembers)!.GetValue(panel)! == "Center");
        var centerGroup = centerPanel.GetType().GetProperty("Group", InstanceMembers)!.GetValue(centerPanel)!;
        var bounds = (Rect)centerGroup.GetType().GetProperty("Bounds", InstanceMembers)!.GetValue(centerGroup)!;
        Require(bounds.width > 100 && bounds.height > 100,
            "The center GPU dock group did not receive render bounds.");

        var cases = new[]
        {
            ("Left", new Vector2(bounds.x + 2, bounds.center.y),
                new Rect(bounds.x, bounds.y, bounds.width / 2, bounds.height)),
            ("Right", new Vector2(bounds.xMax - 2, bounds.center.y),
                new Rect(bounds.x + bounds.width / 2, bounds.y, bounds.width / 2, bounds.height)),
            ("Top", new Vector2(bounds.center.x, bounds.y + 2),
                new Rect(bounds.x, bounds.y, bounds.width, bounds.height / 2)),
            ("Bottom", new Vector2(bounds.center.x, bounds.yMax - 2),
                new Rect(bounds.x, bounds.y + bounds.height / 2, bounds.width, bounds.height / 2)),
            ("Center", bounds.center, bounds)
        };

        foreach (var (name, point, expectedPreview) in cases)
        {
            var arguments = new object?[] { point, null, null, null };
            Require((bool)tryGetDrop.Invoke(workspace, arguments)!,
                $"The {name} point did not resolve a GPU dock target.");
            Require(arguments[2]?.ToString() == name,
                $"The {name} point resolved the '{arguments[2]}' drop position.");
            Require(((Rect)arguments[3]!).Equals(expectedPreview),
                $"The {name} point produced the wrong preview rectangle.");
        }

        Require((bool)setExternalDragPoint.Invoke(workspace, [cases[0].Item2])!,
            "Setting a new external dock point did not signal that the host needs repainting.");
        Require(!(bool)setExternalDragPoint.Invoke(workspace, [cases[0].Item2])!,
            "Setting an unchanged external dock point requested a redundant host repaint.");
        var commands = new List<GpuCanvasCommand>();
        Render(workspaceType, workspace, new Event(EventType.Repaint), commands);
        Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                        SameRect(command.Rect, cases[0].Item3) &&
                                        command.Color.A is > 0 and < 255),
            "External docking did not emit its translucent GPU accent preview.");
        Require((bool)setExternalDragPoint.Invoke(workspace, [null])!,
            "Clearing the external dock point did not signal that the preview needs repainting.");

        var beforeSplit = (EditorDockNodeDocument)captureLayout.Invoke(workspace, null)!;
        var splitPanel = dockExternal.Invoke(workspace,
            ["Split", new ProbeWindow("Split"), cases[1].Item2])!;
        var splitGroup = splitPanel.GetType().GetProperty("Group", InstanceMembers)!.GetValue(splitPanel);
        Require(splitGroup is not null && !ReferenceEquals(splitGroup, centerGroup),
            "Right-side docking joined the center tab group instead of creating a split region.");
        var afterSplit = (EditorDockNodeDocument)captureLayout.Invoke(workspace, null)!;
        Require(ContainsPanel(afterSplit, "Split") && CountSplits(afterSplit) == CountSplits(beforeSplit) + 1,
            "Right-side docking did not persist a new 50/50 split node.");

        remove.Invoke(workspace, ["Split"]);
        var collapsed = (EditorDockNodeDocument)captureLayout.Invoke(workspace, null)!;
        Require(!ContainsPanel(collapsed, "Split") && CountSplits(collapsed) == CountSplits(beforeSplit),
            "Removing the last panel did not collapse its empty GPU dock split.");
    }

    private static void Render(Type workspaceType, object workspace, Event evt,
        List<GpuCanvasCommand>? commands = null)
    {
        BeginFrame.Invoke(null, [evt, 1000, 700, commands ?? []]);
        try
        {
            workspaceType.GetMethod("OnGUI", InstanceMembers)!
                .Invoke(workspace, [new Rect(0, 0, 1000, 700)]);
        }
        finally
        {
            EndFrame.Invoke(null, null);
        }
    }

    private static bool SameRect(GpuCanvasRect actual, Rect expected) =>
        MathF.Abs(actual.X - (float)expected.x) < 0.01f &&
        MathF.Abs(actual.Y - (float)expected.y) < 0.01f &&
        MathF.Abs(actual.Width - (float)expected.width) < 0.01f &&
        MathF.Abs(actual.Height - (float)expected.height) < 0.01f;

    private static int CountSplits(EditorDockNodeDocument? node) => node is null ? 0 :
        node.Type.Equals("Split", StringComparison.OrdinalIgnoreCase)
            ? 1 + CountSplits(node.First) + CountSplits(node.Second)
            : 0;

    private static bool ContainsPanel(EditorDockNodeDocument? node, string id) => node is not null &&
        (node.Panels.Contains(id, StringComparer.Ordinal) || ContainsPanel(node.First, id) ||
         ContainsPanel(node.Second, id));

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: { } inner }) exception = inner;
        return exception;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ProbeWindow(string title) : EditorWindow
    {
        protected override void OnGUI() => GUI.Label(new Rect(4, 4, 100, 20), title);
    }
}
