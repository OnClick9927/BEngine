using BEngine.Editor;
using ImGuiTreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using ImGuiTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using ImGuiTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;

namespace BEngine.UIElements.Editor;

internal sealed class UIBuilderLibraryTreeView : ImGuiTreeView
{
    private readonly UIBuilderWindow _window;
    private readonly Dictionary<int, UIBuilderLibraryItem> _items = [];

    internal UIBuilderLibraryTreeView(UIBuilderWindow window, ImGuiTreeViewState state) : base(state)
    {
        _window = window;
        showBorder = false;
        rowHeight = 22;
    }

    protected override ImGuiTreeViewItem BuildRoot()
    {
        _items.Clear();
        var root = new ImGuiTreeViewItem(0, -1, "Library");
        var itemId = 1;
        var categoryId = -1;
        var library = _window.model.LibraryProjectControls ? UIBuilderLibrary.Project : UIBuilderLibrary.Standard;
        foreach (var group in library.GroupBy(item => item.Category, StringComparer.Ordinal))
        {
            var category = new ImGuiTreeViewItem(categoryId--, 0, group.Key)
            {
                icon = EditorBuiltinIcons.Assets.FolderClosed
            };
            foreach (var libraryItem in group)
            {
                _items[itemId] = libraryItem;
                category.AddChild(new ImGuiTreeViewItem(itemId++, 1, libraryItem.DisplayName)
                {
                    icon = libraryItem.IconPath
                });
            }
            root.AddChild(category);
        }
        return root;
    }

    protected override bool CanMultiSelect(ImGuiTreeViewItem item) => false;
    protected override void DoubleClickedItem(int id)
    {
        if (_items.TryGetValue(id, out var item)) _window.controller.AddElement(item.Create());
    }
}
