namespace UnityEditor.IMGUI.Controls;

[Flags]
public enum TreeViewSelectionOptions
{
    None = 0,
    FireSelectionChanged = 1 << 0,
    RevealAndFrame = 1 << 1
}
