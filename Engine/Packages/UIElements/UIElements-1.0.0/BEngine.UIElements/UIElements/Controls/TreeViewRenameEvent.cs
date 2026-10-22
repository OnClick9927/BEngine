namespace BEngine.UIElements;

public readonly record struct TreeViewRenameEvent(TreeViewItem Item, string PreviousName, string NewName);
