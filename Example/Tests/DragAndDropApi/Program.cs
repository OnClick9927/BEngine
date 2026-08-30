using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.DragAndDropApi;

internal static class Program
{
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        try
        {
            VerifyDataAndAcceptanceLifetime();
            VerifyDelayedDragAndCursor();
            VerifyDropHandlerChains();
            Console.WriteLine("DRAG_AND_DROP_API_OK|data-copy,generic-data,active-control,delayed-drag," +
                              "cursor,accept-frame-lifetime,project,scene,inspector,hierarchy,handler-chain");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DRAG_AND_DROP_API_FAILED|{exception}");
            return 1;
        }
        finally
        {
            Invoke("ClearDropHandlers");
            DragAndDrop.PrepareStartDrag();
        }
    }

    private static void VerifyDataAndAcceptanceLifetime()
    {
        var dragged = new GameObject("Dragged");
        BObject[] references = [dragged];
        string[] paths = ["Assets/Dragged.asset"];
        DragAndDrop.objectReferences = references;
        DragAndDrop.paths = paths;
        references[0] = new GameObject("Replacement");
        paths[0] = "Changed";
        Require(ReferenceEquals(DragAndDrop.objectReferences.Single(), dragged) &&
                DragAndDrop.paths.SequenceEqual(["Assets/Dragged.asset"]),
            "Drag payload setters retained caller-owned arrays.");
        var returned = DragAndDrop.objectReferences;
        returned[0] = new GameObject("Returned replacement");
        Require(ReferenceEquals(DragAndDrop.objectReferences.Single(), dragged),
            "Drag payload getters exposed mutable internal arrays.");

        DragAndDrop.SetGenericData("probe", null);
        Require(DragAndDrop.GetGenericData("probe") is null && (bool)Invoke("HasGenericDragData")!,
            "Generic drag data did not preserve a registered null value.");

        DragAndDrop.StartDrag("  Dragged  ");
        DragAndDrop.activeControlID = 73;
        Dispatch(new Event(EventType.MouseUp) { mousePosition = new Vector2(4, 4) }, () =>
        {
            Require(DragAndDrop.objectReferences.Length == 1,
                "Payload disappeared before the DragPerform callback.");
            DragAndDrop.AcceptDrag();
            Require(DragAndDrop.objectReferences.Length == 1,
                "AcceptDrag cleared the payload before the frame ended.");
        });
        Require(DragAndDrop.objectReferences.Length == 0 && DragAndDrop.activeControlID == 0,
            "Accepted drag data was not cleared at the end of DragPerform.");
    }

    private static void VerifyDelayedDragAndCursor()
    {
        var dragged = new GameObject("Delayed Drag");
        var rect = new Rect(0, 0, 100, 30);
        const int controlId = 991;
        var started = false;
        Dispatch(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(5, 5), button = 0, clickCount = 1
        }, () => started = (bool)Invoke("HandleDelayedDrag", rect, controlId, dragged)!);
        Require(started && DragAndDrop.objectReferences.Length == 0,
            "Delayed drag did not capture MouseDown without starting immediately.");

        Dispatch(new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(8, 5), button = 0
        }, () => started = (bool)Invoke("HandleDelayedDrag", rect, controlId, dragged)!);
        Require(!started && DragAndDrop.objectReferences.Length == 0,
            "Delayed drag started below the movement threshold.");

        Dispatch(new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(14, 5), button = 0
        }, () =>
        {
            started = (bool)Invoke("HandleDelayedDrag", rect, controlId, dragged)!;
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
        });
        Require(started && ReferenceEquals(DragAndDrop.objectReferences.Single(), dragged) &&
                DragAndDrop.activeControlID == controlId,
            "Delayed drag did not start with its BObject payload and active control ID.");
        var cursor = typeof(GUI).GetProperty("requestedMouseCursor",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Require(Equals(cursor, MouseCursor.Link),
            "A link drag did not request the link mouse cursor.");
        Invoke("Cancel");
    }

    private static void VerifyDropHandlerChains()
    {
        Invoke("ClearDropHandlers");
        var calls = new List<string>();
        DragAndDrop.ProjectBrowserDropHandler projectFallback = (_, path, perform) =>
        {
            calls.Add($"fallback:{path}:{perform}");
            return DragAndDropVisualMode.Copy;
        };
        DragAndDrop.ProjectBrowserDropHandler projectNewest = (_, _, _) =>
        {
            calls.Add("newest");
            return DragAndDropVisualMode.None;
        };
        DragAndDrop.AddDropHandler(projectFallback);
        DragAndDrop.AddDropHandler(projectNewest);
        Require(DragAndDrop.HasHandler(1, projectNewest), "Registered Project handler was not discoverable.");
        var projectMode = (DragAndDropVisualMode)Invoke("DropOnProjectBrowserWindow",
            1, "Assets/Target", true)!;
        Require(projectMode == DragAndDropVisualMode.Copy && calls.SequenceEqual([
                    "newest", "fallback:Assets/Target:True"]),
            "Project handlers did not run newest-first until one handled the drop.");
        DragAndDrop.RemoveDropHandler(projectNewest);

        var sceneCalled = false;
        DragAndDrop.SceneDropHandler scene = (target, world, viewport, parent, perform) =>
        {
            sceneCalled = target is null && world == new Vector2(2, 3) && viewport == new Vector2(4, 5) &&
                          parent is null && !perform;
            return DragAndDropVisualMode.Link;
        };
        DragAndDrop.AddDropHandler(scene);
        Require((DragAndDropVisualMode)Invoke("DropOnSceneWindow", null,
                    new Vector2(2, 3), new Vector2(4, 5), null, false)! == DragAndDropVisualMode.Link && sceneCalled,
            "Scene handler did not receive the 2D drop context.");

        var target = new GameObject("Inspector target");
        DragAndDrop.InspectorDropHandler inspector = (targets, perform) =>
            targets.Length == 1 && ReferenceEquals(targets[0], target) && perform
                ? DragAndDropVisualMode.Generic
                : DragAndDropVisualMode.Rejected;
        DragAndDrop.AddDropHandler(inspector);
        Require((DragAndDropVisualMode)Invoke("DropOnInspectorWindow", new BObject[] { target }, true)! ==
                DragAndDropVisualMode.Generic, "Inspector handler did not receive its targets.");

        DragAndDrop.HierarchyDropHandler hierarchy = (id, flags, parent, perform) =>
            id == target.GetInstanceID() && flags == HierarchyDropFlags.DropUpon && parent is null && perform
                ? DragAndDropVisualMode.Move
                : DragAndDropVisualMode.Rejected;
        DragAndDrop.AddDropHandler(hierarchy);
        Require((DragAndDropVisualMode)Invoke("DropOnHierarchyWindow", target.GetInstanceID(),
                    HierarchyDropFlags.DropUpon, null, true)! == DragAndDropVisualMode.Move,
            "Hierarchy handler did not receive its target and drop flags.");

        var duplicateRejected = false;
        try { DragAndDrop.AddDropHandler(hierarchy); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        Require(duplicateRejected, "A duplicate handler registration was accepted.");
    }

    private static void Dispatch(Event input, Action draw)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [input, 320, 100, commands]);
        try { draw(); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static object? Invoke(string name, params object?[] arguments) =>
        typeof(DragAndDrop).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
