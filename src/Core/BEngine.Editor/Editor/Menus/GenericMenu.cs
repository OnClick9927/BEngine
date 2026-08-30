namespace BEngine.Editor;

/// <summary>
/// Editor menu model. Context and dropdown presentations are projected onto the host operating
/// system's native menu API; explicit advanced dropdowns retain the searchable editor UI.
/// </summary>
public sealed class GenericMenu
{
    public delegate void MenuFunction();
    public delegate void MenuFunction2(object? userData);

    private readonly List<GenericMenuItem> _items = [];

    public int GetItemCount() => _items.Count(item => !item.Separator);

    public void AddItem(GUIContent content, bool on, MenuFunction function)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(function);
        _items.Add(new GenericMenuItem(content.text, on, true, false, function.Invoke));
    }

    public void AddItem(GUIContent content, bool on, MenuFunction2 function, object? userData)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(function);
        _items.Add(new GenericMenuItem(content.text, on, true, false, () => function(userData)));
    }

    public void AddDisabledItem(GUIContent content, bool on = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        _items.Add(new GenericMenuItem(content.text, on, false, false, null));
    }

    public void AddSeparator(string path) =>
        _items.Add(new GenericMenuItem(path ?? string.Empty, false, false, true, null));

    internal IReadOnlyList<GenericMenuItem> Items => _items;

    public void ShowAsContext() => GenericMenuDispatcher.Show(_items);

    public void DropDown(Rect position) => GenericMenuDispatcher.Show(_items,
        GenericMenuPresentation.DropDown(position));

    public void ShowAsAdvancedDropdown() => GenericMenuDispatcher.Show(_items,
        GenericMenuPresentation.AdvancedDropDown());

    public void ShowAsAdvancedDropdown(Rect position) => GenericMenuDispatcher.Show(_items,
        GenericMenuPresentation.AdvancedDropDown(position));
}
