namespace UnityEditor.IMGUI.Controls;

/// <summary>A node in the data model rendered by a TreeView.</summary>
public class TreeViewItem<TIdentifier>
    where TIdentifier : unmanaged, IEquatable<TIdentifier>
{
    public TIdentifier id { get; set; }
    public int depth { get; set; }
    public string displayName { get; set; }
    public string icon { get; set; } = string.Empty;
    public TreeViewItem<TIdentifier>? parent { get; set; }
    public IList<TreeViewItem<TIdentifier>>? children { get; set; }
    public bool hasChildren => children is { Count: > 0 };

    public TreeViewItem() : this(default, -1, string.Empty) { }

    public TreeViewItem(TIdentifier id, int depth, string displayName)
    {
        this.id = id;
        this.depth = depth;
        this.displayName = displayName ?? string.Empty;
    }

    public void AddChild(TreeViewItem<TIdentifier> child)
    {
        ArgumentNullException.ThrowIfNull(child);
        children ??= [];
        if (child.parent is { } previous && !ReferenceEquals(previous, this))
            previous.children?.Remove(child);
        child.parent = this;
        child.depth = depth + 1;
        children.Add(child);
    }
}
