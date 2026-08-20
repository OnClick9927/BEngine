using BEngine.UIElements;

namespace BEngine.ExampleTests.ProjectFolderTree;

internal static class Program
{
    private static int Main()
    {
        var file = new TreeViewItem(3, "Player.cs", IconPath: "script.png");
        var folder = new TreeViewItem(2, "Scripts", Children: [file],
            IconPath: "folder-closed.png", ExpandedIconPath: "folder-open.png");
        var empty = new TreeViewItem(4, "Empty", IconPath: "folder-empty.png");
        var root = new TreeViewItem(1, "Assets", Children: [folder, empty]);
        var tree = new TreeView { items = [root] };
        tree.CollapseAll();
        Require(tree.GetVisibleRows().Count == 1, "Collapsed root still shows descendants.");
        tree.ExpandItem(1);
        Require(tree.GetVisibleRows().Select(row => row.Item.Id).SequenceEqual([1, 2, 4]),
            "Expanding a folder did not expose its direct children.");
        tree.ExpandItem(2);
        Require(tree.GetVisibleRows().Select(row => row.Item.Id).SequenceEqual([1, 2, 3, 4]),
            "Expanding a non-empty child folder did not expose its files.");
        tree.ToggleExpanded(2);
        Require(!tree.IsExpanded(2) && tree.GetVisibleRows().All(row => row.Item.Id != 3),
            "Folder fold state did not persist after toggling.");
        Require(empty.Children is null or { Count: 0 } && empty.ExpandedIconPath.Length == 0,
            "Empty folder incorrectly exposes a disclosure state.");

        Console.WriteLine("PROJECT_FOLDER_TREE_OK|children-only-fold,expand,collapse,persistent-state,folder-icons");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
