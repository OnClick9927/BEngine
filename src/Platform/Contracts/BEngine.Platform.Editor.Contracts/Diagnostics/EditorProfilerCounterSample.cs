namespace BEngine.Editor;

/// <summary>One custom module counter value captured during an editor frame.</summary>
public readonly record struct EditorProfilerCounterSample(
    string ModuleId,
    string CounterName,
    double Value);
