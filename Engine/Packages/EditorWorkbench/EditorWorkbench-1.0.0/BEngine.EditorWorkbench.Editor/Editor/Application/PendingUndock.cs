namespace BEngine.Editor;

internal readonly record struct PendingUndock(ImGuiDockPanel Panel, Vector2 CanvasPosition);
