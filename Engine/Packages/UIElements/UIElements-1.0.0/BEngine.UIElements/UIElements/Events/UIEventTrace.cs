namespace BEngine.UIElements;

public sealed record UIEventTrace(
    long DispatchId,
    Fix64 Timestamp,
    UIEventTraceStage Stage,
    UIEventBase Event,
    VisualElement? Target,
    VisualElement? CurrentTarget,
    UIEventPropagationPhase PropagationPhase,
    string Handler,
    IReadOnlyList<VisualElement> PropagationPath);
