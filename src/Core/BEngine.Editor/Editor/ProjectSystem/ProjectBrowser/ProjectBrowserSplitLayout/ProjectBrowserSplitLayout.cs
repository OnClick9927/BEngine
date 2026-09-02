namespace BEngine.Editor;

internal readonly record struct ProjectBrowserSplitLayout(
    Rect Assets,
    Rect Separator,
    Rect SeparatorHitArea,
    Rect Packages);
