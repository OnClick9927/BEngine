using BEngine.Editor.Diagnostics;

namespace BEngine.Editor;

public sealed class FrameDebuggerWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public long ObservedVersion { get; internal set; } = -1;
    public int SelectedEventIndex { get; internal set; } = -1;
    public bool CountedAsOpen { get; internal set; }
    public bool Enabled { get; internal set; }
    public string TargetName { get; internal set; } = string.Empty;
    public int StepLimit { get; internal set; } = -1;
    public FrameDebugCaptureSnapshot? Snapshot { get; internal set; }
    public FrameDebugPreviewSnapshot? Preview { get; internal set; }
}
