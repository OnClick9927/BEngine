namespace BEngine.UIElements.Editor;

public sealed record UIEventDebugCallbackRecord(
    Fix64 Timestamp,
    string Handler,
    string CurrentTarget,
    UIEventPropagationPhase PropagationPhase);
