namespace UnityEditor.IMGUI.Controls;

/// <summary>Persistent navigation state for an AdvancedDropdown.</summary>
public class AdvancedDropdownState
{
    public int selectedId { get; set; } = -1;
    public List<int> expandedIds { get; set; } = [];
    public string searchString { get; set; } = string.Empty;
}
