using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed record TreeViewItem(
    int Id,
    string Text,
    object? Data = null,
    IReadOnlyList<TreeViewItem>? Children = null,
    string IconPath = "",
    string ExpandedIconPath = "");
