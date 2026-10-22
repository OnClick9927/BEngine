namespace BEngine.Editor;

[EditorTool("profiler")]
public sealed class ProfilerTool : IEditorTool
{
    public void Open(EditorWindow? target) => EditorWindow.GetWindow<ProfilerWindow>("Profiler");
}

[EditorTool("frame-debugger")]
public sealed class FrameDebuggerTool : IEditorTool
{
    public void Open(EditorWindow? target)
    {
        if (target is null) EditorWindow.GetWindow<FrameDebuggerWindow>("Frame Debugger");
        else FrameDebuggerWindow.OpenForTarget(target, target.titleContent.text);
    }
}

public sealed class MemoryProfilerTool : IEditorTool
{
    public void Open(EditorWindow? target) => EditorWindow.GetWindow<MemoryProfilerWindow>("Memory Profiler");
}

public sealed class RemotePlayerProfilerTool : IEditorTool
{
    public void Open(EditorWindow? target) => EditorWindow.GetWindow<RemotePlayerProfilerWindow>("Remote Player");
}
