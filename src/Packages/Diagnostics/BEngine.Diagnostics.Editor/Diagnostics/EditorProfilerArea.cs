namespace BEngine.Editor;

/// <summary>Top-level editor frame phases recorded by <see cref="EditorProfiler"/>.</summary>
public enum EditorProfilerArea
{
    Update,
    Runtime,
    Render,
    IMGUI,
    Present
}
