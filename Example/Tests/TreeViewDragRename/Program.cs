using System.Reflection;
using BEngine.Editor;
using BEngine.ProjectSystem.Editor;
using BEngine.UIElements;

namespace BEngine.ExampleTests.TreeViewDragRename;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var child = new TreeViewItem(2, "Child");
            var first = new TreeViewItem(1, "First", Children: [child]);
            var second = new TreeViewItem(3, "Second");
            var tree = new TreeView { items = [first, second] };
            tree.SetSelection([2]);

            TreeViewRenameEvent? renamed = null;
            tree.itemRenamed += value => renamed = value;
            Require(tree.BeginRename(2), "A normal tree item could not enter rename mode.");
            tree.SetRenameValue("Renamed Child");
            var renameCommands = UIRenderListBuilder.Build(tree, 320, 160).Commands;
            Require(renameCommands.Any(command => command.Type == UIRenderCommandType.Text &&
                                                  command.Content == "Renamed Child"),
                "GPU TreeView did not render its live rename value.");
            Require(tree.CommitRename(), "A valid rename could not be committed.");
            Require(tree.GetItemForId(2)?.Text == "Renamed Child" && tree.selectedId == 2,
                "Rename did not update the item while preserving selection.");
            Require(renamed is { PreviousName: "Child", NewName: "Renamed Child" },
                "Rename event did not contain the previous and new names.");

            TreeViewDragAndDropEvent? dropped = null;
            tree.itemDropped += value => dropped = value;
            Require(tree.BeginDrag(3), "A reorderable item could not start dragging.");
            Require(tree.UpdateDragTarget(1, TreeViewDropPosition.Inside),
                "A valid child drop target was rejected.");
            var dragCommands = UIRenderListBuilder.Build(tree, 320, 160).Commands;
            Require(dragCommands.Any(command => command.Type == UIRenderCommandType.SolidRect &&
                                                command.Color == new UIColor(38, 94, 132, 170)),
                "GPU TreeView did not render its drop target marker.");
            Require(tree.PerformDrop(), "A valid tree drop could not be committed.");
            Require(tree.GetItemForId(1)?.Children?.Select(item => item.Id).SequenceEqual([2, 3]) == true,
                "Dropped item was not inserted into the target hierarchy.");
            Require(tree.IsExpanded(1) && dropped is { Item.Id: 3, Target.Id: 1,
                        Position: TreeViewDropPosition.Inside },
                "Inside drop did not expand its target or report the correct event.");

            Require(tree.BeginDrag(1), "Root drag could not start.");
            Require(!tree.UpdateDragTarget(2, TreeViewDropPosition.Inside),
                "Tree accepted a cyclic drop into a descendant.");
            tree.CancelDrag();
            tree.canRenameItem = item => item.Id != 1;
            Require(!tree.BeginRename(1), "Rename predicate did not protect a read-only node.");

            var editor = typeof(EditorWindow).Assembly.GetType(
                "BEngine.Editor.GpuEditorApplication", throwOnError: true)!;
            var hierarchy = editor.GetNestedType("ImGuiHierarchyWindow", BindingFlags.NonPublic)!;
            var project = editor.GetNestedType("ImGuiProjectWindow", BindingFlags.NonPublic)!;
            Require(hierarchy.GetMethod("HandleDrag", BindingFlags.Instance | BindingFlags.NonPublic) is not null &&
                    hierarchy.GetMethod("BeginRename", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                "Hierarchy window is not connected to drag and rename interaction.");
            Require(project.GetMethod("HandleDrag", BindingFlags.Instance | BindingFlags.NonPublic) is not null &&
                    project.GetMethod("BeginRename", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                "Project window is not connected to drag and rename interaction.");
            VerifyAssetMoveWithMetadata();

            Console.WriteLine(
                "TREEVIEW_DRAG_RENAME_OK|rename,gpu-state,event,selection,inside-drop,cycle-guard,readonly,editor-wiring,meta-move");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TREEVIEW_DRAG_RENAME_FAILED|{exception}");
            return 1;
        }
    }

    private static void VerifyAssetMoveWithMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineTreeMove_{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "Assets", "Source.cs");
            var target = Path.Combine(root, "Assets", "Scripts", "Renamed.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "source");
            File.WriteAllText(source + ".meta", "guid: test");
            Require(AssetFileOperations.Move(source, target).Length == 0,
                "Project asset move returned an error.");
            Require(File.Exists(target) && File.Exists(target + ".meta") &&
                    !File.Exists(source) && !File.Exists(source + ".meta"),
                "Project asset drag did not move its metadata sidecar together with the asset.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
