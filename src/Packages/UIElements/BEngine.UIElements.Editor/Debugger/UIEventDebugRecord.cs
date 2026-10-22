namespace BEngine.UIElements.Editor;

public sealed record UIEventDebugRecord(
    long DispatchId,
    Fix64 Timestamp,
    string EventType,
    string Target,
    string TargetPath,
    VisualElement? PanelRoot,
    IReadOnlyList<string> PropagationPath,
    IReadOnlyList<UIEventDebugCallbackRecord> Callbacks,
    IReadOnlyList<string> Information,
    bool PropagationStopped,
    bool ImmediatePropagationStopped);
