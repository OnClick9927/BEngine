using BEngine;

namespace UnityEditor.IMGUI.Controls;

/// <summary>Serializable interaction state owned by a TreeView.</summary>
public class TreeViewState<TIdentifier>
    where TIdentifier : unmanaged, IEquatable<TIdentifier>
{
    public Vector2 scrollPos { get; set; }
    public List<TIdentifier> selectedIDs { get; set; } = [];
    public TIdentifier lastClickedID { get; set; }
    public List<TIdentifier> expandedIDs { get; set; } = [];
    public string searchString { get; set; } = string.Empty;

    internal bool hasLastClickedID { get; set; }
}

/// <summary>Unity-compatible integer TreeView state.</summary>
public class TreeViewState : TreeViewState<int>;
