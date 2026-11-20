using BEngine.Editor.Diagnostics;

namespace BEngine.Editor;

public sealed class FrameDebuggerWindowController :
    EditorWindowController<FrameDebuggerWindow, FrameDebuggerWindowModel>
{
    private static int _openWindowCount;
    private readonly FrameDebuggerService _service;

    public FrameDebuggerWindowController(
        EditorWindowMvcContext context,
        FrameDebuggerWindowModel model,
        FrameDebuggerService service) : base(context, model) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public void Activate()
    {
        if (!Model.CountedAsOpen)
        {
            Model.CountedAsOpen = true;
            Interlocked.Increment(ref _openWindowCount);
        }
        Synchronize();
    }

    public void Deactivate()
    {
        if (!Model.CountedAsOpen) return;
        Model.CountedAsOpen = false;
        if (Interlocked.Decrement(ref _openWindowCount) > 0) return;
        Interlocked.Exchange(ref _openWindowCount, 0);
        _service.Disable();
        Synchronize();
    }

    public void Enable(object? target = null, string targetName = "Game")
    {
        if (target is null) _service.Enable();
        else _service.Enable(target, string.IsNullOrWhiteSpace(targetName) ? "Game" : targetName);
        _service.RequestCapture();
        Synchronize();
        Repaint();
    }

    public void ToggleEnabled()
    {
        if (_service.Enabled) _service.Disable();
        else Enable();
        Synchronize();
        Repaint();
    }

    public void RequestCapture()
    {
        _service.RequestCapture();
        Synchronize();
        Repaint();
    }

    public void Clear()
    {
        _service.Clear();
        Synchronize();
        ClampSelection();
        Repaint();
    }

    public void Refresh()
    {
        _ = _service.Preview;
        if (Model.ObservedVersion == _service.Version) return;
        Synchronize();
        ClampSelection();
        Repaint();
    }

    public void SelectEventIndex(int snapshotIndex)
    {
        var snapshot = _service.Snapshot;
        if (snapshot is null || (uint)snapshotIndex >= (uint)snapshot.Events.Count) return;
        Model.SelectedEventIndex = snapshotIndex;
        _service.SetStepLimit(snapshot.Events[snapshotIndex].Index + 1);
        Synchronize();
        View.ResetDetailsScroll();
        Repaint();
    }

    public void ClampSelection()
    {
        var snapshot = Model.Snapshot;
        if (snapshot is null || snapshot.Events.Count == 0)
        {
            Model.SelectedEventIndex = -1;
            return;
        }

        Model.SelectedEventIndex = Model.StepLimit < 0
            ? snapshot.Events.Count - 1
            : Math.Clamp(Math.Max(0, Model.StepLimit - 1), 0, snapshot.Events.Count - 1);
    }

    private void Synchronize()
    {
        Model.ObservedVersion = _service.Version;
        Model.Enabled = _service.Enabled;
        Model.TargetName = _service.TargetName;
        Model.StepLimit = _service.StepLimit;
        Model.Snapshot = _service.Snapshot;
        Model.Preview = _service.Preview;
    }
}
