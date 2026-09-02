namespace UnityEditor.IMGUI.Controls;

/// <summary>Unity-compatible integer TreeView item.</summary>
public class TreeViewItem : TreeViewItem<int>
{
    public TreeViewItem() { }
    public TreeViewItem(int id, int depth, string displayName) : base(id, depth, displayName) { }
}
