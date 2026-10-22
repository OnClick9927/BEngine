namespace UnityEditor.IMGUI.Controls;

/// <summary>Unity-compatible integer identifier TreeView.</summary>
public abstract class TreeView : TreeView<int>
{
    protected TreeView(TreeViewState state) : base(state) { }
    protected TreeView(TreeViewState state, MultiColumnHeader multiColumnHeader)
        : base(state, multiColumnHeader) { }
}
