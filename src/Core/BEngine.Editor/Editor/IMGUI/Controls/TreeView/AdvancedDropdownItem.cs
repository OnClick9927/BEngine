namespace UnityEditor.IMGUI.Controls;

/// <summary>A hierarchical entry displayed by an AdvancedDropdown.</summary>
public class AdvancedDropdownItem : IComparable<AdvancedDropdownItem>
{
    private readonly List<AdvancedDropdownItem> _children = [];

    public string name { get; set; }
    public int id { get; set; }
    public string icon { get; set; } = string.Empty;
    public bool enabled { get; set; } = true;
    public IEnumerable<AdvancedDropdownItem> children => _children;
    public bool hasChildren => _children.Count > 0;
    internal bool isSeparator { get; private set; }

    public AdvancedDropdownItem(string name)
    {
        this.name = name ?? string.Empty;
        id = this.name.GetHashCode(StringComparison.Ordinal);
    }

    public void AddChild(AdvancedDropdownItem child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    public void AddSeparator() => _children.Add(new AdvancedDropdownItem(string.Empty)
    {
        isSeparator = true,
        enabled = false
    });

    public int CompareTo(AdvancedDropdownItem? other) => other is null
        ? 1
        : StringComparer.OrdinalIgnoreCase.Compare(name, other.name);
}
