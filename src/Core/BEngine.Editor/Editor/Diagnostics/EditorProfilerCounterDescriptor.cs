namespace BEngine.Editor;

public sealed record EditorProfilerCounterDescriptor(
    string Name,
    string DisplayName,
    EditorProfilerCounterUnit Unit,
    Color Color);
