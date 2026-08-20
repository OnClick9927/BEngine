using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed class ContextMenuBuilder
{
    private readonly List<ContextMenuItem> _items = [];
    public IReadOnlyList<ContextMenuItem> Items => _items;
    public void AddAction(string name, Action action, bool enabled = true, bool isChecked = false) =>
        _items.Add(new ContextMenuItem(name, action, enabled, isChecked, false));
    public void AddSeparator() => _items.Add(new ContextMenuItem(string.Empty, null, false, false, true));
}
