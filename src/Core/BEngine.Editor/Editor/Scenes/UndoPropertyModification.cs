namespace BEngine.Editor;

public readonly record struct UndoPropertyModification(
    PropertyModification previousValue,
    PropertyModification currentValue,
    bool keepPrefabOverride = false);
