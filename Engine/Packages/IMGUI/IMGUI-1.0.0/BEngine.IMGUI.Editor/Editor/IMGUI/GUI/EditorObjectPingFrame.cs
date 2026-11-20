namespace BEngine.Editor;

internal readonly record struct EditorObjectPingFrame(
    bool IsActive,
    Fix64 Progress,
    Fix64 Expansion,
    Fix64 Alpha);
