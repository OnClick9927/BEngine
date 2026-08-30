namespace BEngine.Editor;

internal sealed record ClosedEditorWindowPlacement(
    EditorWindow Window,
    EditorWindowState State,
    Rect Bounds,
    bool Docked,
    DockArea PreferredArea,
    string? PreviousPanelId,
    string? NextPanelId,
    int PanelIndex,
    Vector2 DockPoint,
    bool WasMaximized);
