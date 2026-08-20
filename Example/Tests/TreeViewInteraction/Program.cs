using System.Reflection;
using BEngine.UIElements;
using UiTreeView = BEngine.UIElements.TreeView;

namespace BEngine.ExampleTests.TreeViewInteraction;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        var editorAssembly = Assembly.Load("BEngine.Editor");
        var hostType = editorAssembly.GetType("BEngine.UIElements.Editor.GpuVisualElementHost", true)!;
        using var host = (Control)Activator.CreateInstance(hostType, nonPublic: true)!;
        host.Size = new Size(320, 240);

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
        hostType.GetProperty("Root")!.SetValue(host, root);

        var selected = new List<int>();
        tree.selectionChanged += item => selected.Add(item?.Id ?? -1);
        tree.SetExpanded(1, true);

        InvokeMouse(hostType, host, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 80, 33, 0));
        Require((int?)GetInternal(tree, "hoveredId") == 2, "Moving over the second row did not set row hover.");

        InvokeMouse(hostType, host, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 80, 33, 0));
        Require(tree.selectedId == 2 && selected.SequenceEqual([2]), "Clicking the second row did not select it.");
        Require((int?)GetInternal(tree, "pressedId") == 2, "Mouse down did not set row pressed state.");
        InvokeMouse(hostType, host, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 80, 33, 0));
        Require(GetInternal(tree, "pressedId") is null, "Mouse up did not clear row pressed state.");

        var visibleBeforeRepeatedClick = tree.GetVisibleRows().Count;
        InvokeMouse(hostType, host, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 2, 80, 11, 0));
        Require(tree.IsExpanded(1), "Double-clicking row content unexpectedly collapsed the parent.");
        Require(tree.GetVisibleRows().Count == visibleBeforeRepeatedClick,
            "Repeated row clicks progressively removed visible TreeView content.");
        InvokeMouse(hostType, host, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 80, 11, 0));

        InvokeMouse(hostType, host, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 8, 11, 0));
        Require(!tree.IsExpanded(1), "Clicking the disclosure arrow did not collapse the parent.");
        Require(tree.GetVisibleRows().Count == 1, "Collapsed parent still exposed child rows.");
        InvokeMouse(hostType, host, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 8, 11, 0));

        tree.SetExpanded(1, true);
        tree.scrollOffset = 999;
        InvokeMouse(hostType, host, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 80, 80, -120));
        Require(tree.scrollOffset == 0, "Tree scrolling was not clamped to its content bounds.");

        InvokeMethod(hostType, host, "OnMouseLeave", EventArgs.Empty);
        Require(GetInternal(tree, "hoveredId") is null, "Mouse leave did not clear row hover state.");

        var list = new BEngine.UIElements.ListView { itemsSource = new[] { "One", "Two", "Three" } };
        var listRoot = new VisualElement();
        list.style.flexGrow = 1;
        listRoot.Add(list);
        hostType.GetProperty("Root")!.SetValue(host, listRoot);
        object? selectedListItem = null;
        list.selectionChanged += item => selectedListItem = item;
        InvokeMouse(hostType, host, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 80, 33, 0));
        Require(list.selectedIndex == 1 && Equals(selectedListItem, "Two"),
            "ListView coordinate-based selection regressed with the same render-list invalidation path.");

        Console.WriteLine("TREEVIEW_INTERACTION_OK|hover,select,pressed,stable-repeat-click,disclosure,scroll,leave,list-select");
        return 0;
    }

    private static void InvokeMouse(Type type, Control host, string method, MouseEventArgs args) =>
        InvokeMethod(type, host, method, args);

    private static void InvokeMethod(Type type, object target, string method, object argument) =>
        type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [argument]);

    private static object? GetInternal(object target, string property) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
