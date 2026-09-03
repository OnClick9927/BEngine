namespace BEngine.UIElements;

public readonly record struct TreeViewDragAndDropEvent(
    TreeViewItem Item,
    TreeViewItem Target,
    TreeViewDropPosition Position);
