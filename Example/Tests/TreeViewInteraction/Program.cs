using System.Reflection;
using BEngine.UIElements;
using UiTreeView = BEngine.UIElements.TreeView;

namespace BEngine.ExampleTests.TreeViewInteraction;

internal static class Program
{
    private const int ViewportWidth = 320;
    private const int ViewportHeight = 240;

    private static int Main()
    {
        var childA = new TreeViewItem(2, "Child A");
        var childB = new TreeViewItem(3, "Child B");
        var rootItem = new TreeViewItem(1, "Root", Children: [childA, childB]);
        var tree = new UiTreeView { items = [rootItem] };
        Require(tree.IsExpanded(1), "TreeView did not expand the root on its first data binding.");
        tree.CollapseAll();
        tree.items = [rootItem];
        Require(!tree.IsExpanded(1), "Refreshing items incorrectly reopened an explicitly collapsed tree.");

        tree.style.flexGrow = 1;
        var root = new VisualElement();
        root.style.flexGrow = 1;
        root.Add(tree);
        tree.SetExpanded(1, true);

        var initial = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight);
        Require(initial.TryGetRect(tree, out var treeRect) && treeRect.Width > 0 && treeRect.Height > 0,
            "TreeView was not laid out.");
        Require(ReferenceEquals(initial.Pick(new Vector2(80, 33)), tree),
            "TreeView render-list hit testing did not pick the collection.");
        Require(initial.Commands.Count(command => command.Type == UIRenderCommandType.Text &&
                                                  ReferenceEquals(command.Element, tree)) >= 4,
            "Expanded TreeView did not render the disclosure and all three row labels.");

        var selected = new List<int>();
        tree.selectionChanged += item => selected.Add(item?.Id ?? -1);
        tree.SetSelection([2]);
        Require(tree.selectedId == 2 && tree.selectedIds.SequenceEqual([2]) && selected.SequenceEqual([2]),
            "Selecting the second row did not update state and notify listeners.");
        var selectedCommands = UIRenderListBuilder.Build(root, ViewportWidth, ViewportHeight).Commands;
        Require(selectedCommands.Any(command => command.Type == UIRenderCommandType.SolidRect &&
                                                ReferenceEquals(command.Element, tree) &&
                                                (double)command.Rect.Y == tree.fixedItemHeight),
            "Selected child row did not produce a highlight render command.");

        InvokeInternal(tree, "SetHoveredFromView", childA);
        InvokeInternal(tree, "SetPressedFromView", childA);
        Require(ReadInternal<int?>(tree, "hoveredId") == 2,
            "Applying the current view hover state did not target the second row.");
        Require(ReadInternal<int?>(tree, "pressedId") == 2,
            "Applying the current view pressed state did not target the second row.");

        var visibleBeforeRepeatedSelection = tree.GetVisibleRows().Count;
        tree.SetSelection([2]);
        Require(tree.GetVisibleRows().Count == visibleBeforeRepeatedSelection,
            "Repeated row selection progressively removed visible TreeView content.");

        tree.ToggleExpanded(1);
        Require(!tree.IsExpanded(1) && tree.GetVisibleRows().Count == 1,
            "Collapsing the disclosure did not hide child rows.");
        tree.SetExpanded(1, true);
        Require(tree.GetVisibleRows().Select(row => row.Item.Id).SequenceEqual([1, 2, 3]),
            "Expanding the disclosure did not restore child rows in hierarchy order.");

        tree.scrollOffset = 999;
        InvokeInternal(tree, "ScrollBy", 0f, (float)ViewportHeight);
        Require(tree.scrollOffset == 0,
            "TreeView scrolling was not clamped to its content bounds.");

        InvokeInternal(tree, "SetHoveredFromView", (object?)null);
        InvokeInternal(tree, "SetPressedFromView", (object?)null);
        Require(ReadInternal<int?>(tree, "hoveredId") is null &&
                ReadInternal<int?>(tree, "pressedId") is null,
            "Clearing view interaction state did not remove hover and pressed rows.");

        var list = new BEngine.UIElements.ListView { itemsSource = new[] { "One", "Two", "Three" } };
        object? selectedListItem = null;
        list.selectionChanged += item => selectedListItem = item;
        list.SetSelection([1]);
        Require(list.selectedIndex == 1 && list.selectedIndices.SequenceEqual([1]) &&
                Equals(selectedListItem, "Two"),
            "ListView public selection API did not select and notify the second item.");
        var listRoot = new VisualElement();
        list.style.flexGrow = 1;
        listRoot.Add(list);
        var listCommands = UIRenderListBuilder.Build(listRoot, ViewportWidth, ViewportHeight).Commands;
        Require(listCommands.Any(command => command.Type == UIRenderCommandType.SolidRect &&
                                            ReferenceEquals(command.Element, list) &&
                                            (double)command.Rect.Y == list.fixedItemHeight),
            "Selected ListView row did not produce a highlight render command.");

        Console.WriteLine(
            "TREEVIEW_INTERACTION_OK|layout,pick,select,hover,pressed,stable-repeat,disclosure,scroll,clear,list-select");
        return 0;
    }

    private static void InvokeInternal(object target, string method, params object?[] arguments)
    {
        var member = target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                     throw new MissingMethodException(target.GetType().FullName, method);
        member.Invoke(target, arguments);
    }

    private static T ReadInternal<T>(object target, string property)
    {
        var member = target.GetType().GetProperty(property,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                     throw new MissingMemberException(target.GetType().FullName, property);
        return (T)member.GetValue(target)!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
