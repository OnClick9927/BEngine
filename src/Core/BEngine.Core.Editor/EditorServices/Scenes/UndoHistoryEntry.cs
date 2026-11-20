namespace BEngine.Editor;

internal readonly record struct UndoHistoryEntry(
    string Name,
    int Group,
    bool IsRedo,
    int OperationCount = 1,
    int TargetCount = 0,
    bool AffectsScene = false,
    string[]? TargetNames = null,
    string Details = "");
