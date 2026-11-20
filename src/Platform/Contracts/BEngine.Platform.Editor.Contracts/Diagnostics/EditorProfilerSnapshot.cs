namespace BEngine.Editor;

/// <summary>An immutable, oldest-to-newest view of the profiler history.</summary>
public readonly record struct EditorProfilerSnapshot(
    long Version,
    bool Recording,
    int Capacity,
    EditorProfilerFrame[] Frames)
{
    public EditorProfilerFrame? Latest => Frames is { Length: > 0 } ? Frames[^1] : null;
}
