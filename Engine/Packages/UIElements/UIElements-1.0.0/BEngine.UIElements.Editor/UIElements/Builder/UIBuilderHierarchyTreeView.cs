using BEngine.Editor;
using ImGuiTreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using ImGuiTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using ImGuiTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;

namespace BEngine.UIElements.Editor;

internal sealed class UIBuilderHierarchyTreeView : ImGuiTreeView
{
    private readonly UIBuilderWindow _window;

    internal UIBuilderHierarchyTreeView(UIBuilderWindow window, ImGuiTreeViewState state) : base(state)
    {
        _window = window;
        showBorder = false;
        rowHeight = 22;
    }

    protected override ImGuiTreeViewItem BuildRoot()
    {
        var root = new ImGuiTreeViewItem(0, -1, "Root");
        root.AddChild(CreateItem(_window.model.DocumentRoot));
        return root;
    }

    private ImGuiTreeViewItem CreateItem(VisualElement element)
    {
        var name = string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name;
        var item = new ImGuiTreeViewItem(_window.model.GetElementId(element), 0, name)
        {
            icon = IconFor(element)
        };
        foreach (var child in element.Children) item.AddChild(CreateItem(child));
        return item;
    }

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        if (selectedIds.Count > 0 && _window.model.FindElement(selectedIds[^1]) is { } element)
            _window.controller.Select(element);
    }

    protected override bool CanMultiSelect(ImGuiTreeViewItem item) => false;

    internal void RenameSelected()
    {
        var item = FindItem(_window.model.GetElementId(_window.model.Selected), rootItem);
        if (item is not null) BeginRename(item);
    }
    protected override bool CanRename(ImGuiTreeViewItem item) => item.id != 0;

    protected override void RenameEnded(RenameEndedArgs args)
    {
        if (!args.acceptedRename || _window.model.FindElement(args.itemID) is not { } element) return;
        _window.controller.Change("Rename element", () => element.name = args.newName);
    }

    private static string IconFor(VisualElement element) => element switch
    {
        Button or ToolbarButton => EditorBuiltinIcons.Toolbar.Button,
        Image => EditorBuiltinIcons.Assets.Image,
        TextElement => EditorBuiltinIcons.Toolbar.Label,
        TextField or IntegerField or FloatField or ColorField or DropdownField or Slider or Toggle =>
            EditorBuiltinIcons.Toolbar.Field,
        _ => EditorBuiltinIcons.Toolbar.Container
    };
}
