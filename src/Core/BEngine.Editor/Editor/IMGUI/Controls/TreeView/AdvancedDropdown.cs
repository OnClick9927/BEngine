using BEngine;
using BEngine.Editor;

namespace UnityEditor.IMGUI.Controls;

/// <summary>
/// Extensible hierarchical dropdown. The built tree is presented through the editor's searchable menu host.
/// </summary>
public abstract class AdvancedDropdown
{
    protected AdvancedDropdownState state { get; }
    public Vector2 minimumSize { get; set; } = new(240, 280);

    protected AdvancedDropdown(AdvancedDropdownState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    protected abstract AdvancedDropdownItem BuildRoot();
    protected virtual void ItemSelected(AdvancedDropdownItem item) { }

    public void Show(Rect rect)
    {
        var root = BuildRoot() ?? throw new InvalidOperationException("BuildRoot returned null.");
        var menu = new GenericMenu();
        AddItems(menu, root, string.Empty);
        menu.ShowAsAdvancedDropdown(rect);
    }

    private void AddItems(GenericMenu menu, AdvancedDropdownItem parent, string parentPath)
    {
        var children = parent.children.ToArray();
        foreach (var child in children)
        {
            if (child.isSeparator)
            {
                menu.AddSeparator(parentPath);
                continue;
            }

            var path = string.IsNullOrEmpty(parentPath) ? child.name : $"{parentPath}/{child.name}";
            if (child.hasChildren)
            {
                AddItems(menu, child, path);
                continue;
            }

            var content = new GUIContent(path, child.icon, string.Empty);
            if (!child.enabled) menu.AddDisabledItem(content, state.selectedId == child.id);
            else menu.AddItem(content, state.selectedId == child.id, () => Select(child));
        }
    }

    private void Select(AdvancedDropdownItem item)
    {
        state.selectedId = item.id;
        ItemSelected(item);
    }
}
