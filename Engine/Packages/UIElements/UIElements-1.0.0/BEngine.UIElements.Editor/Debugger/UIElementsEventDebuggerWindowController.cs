using System.Reflection;
using BEngine.Editor;

namespace BEngine.UIElements.Editor;

public sealed class UIElementsEventDebuggerWindowController :
    EditorWindowController<UIElementsEventDebuggerWindow, UIElementsEventDebuggerWindowModel>
{
    private readonly Dictionary<long, PendingEvent> _pending = [];
    private bool _recording;

    public UIElementsEventDebuggerWindowController(
        EditorWindowMvcContext context,
        UIElementsEventDebuggerWindowModel model) : base(context, model) { }

    public void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        UIEventDiagnostics.traceEmitted += OnTrace;
    }

    public void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        UIEventDiagnostics.traceEmitted -= OnTrace;
        _pending.Clear();
    }

    public void ToggleSuspended()
    {
        Model.Suspended = !Model.Suspended;
        if (Model.Suspended) _pending.Clear();
        Repaint();
    }

    public void Clear()
    {
        _pending.Clear();
        Model.Clear();
        Repaint();
    }

    public void Select(long dispatchId)
    {
        Model.SelectedDispatchId = dispatchId;
        Repaint();
    }

    public void SelectPanel(VisualElement? root)
    {
        Model.SelectedPanelRoot = root;
        Model.SelectedDispatchId = null;
        Repaint();
    }

    public void ToggleEventType(string eventType)
    {
        Model.ToggleEventType(eventType);
        Model.SelectedDispatchId = null;
        Repaint();
    }

    public void SetMaxLogEntries(int value)
    {
        Model.MaxLogEntries = Math.Clamp(value, 10, 10000);
        Model.Trim();
        Repaint();
    }

    private void OnTrace(UIEventTrace trace)
    {
        if (Model.Suspended)
        {
            if (trace.Stage == UIEventTraceStage.DispatchCompleted) _pending.Remove(trace.DispatchId);
            return;
        }
        switch (trace.Stage)
        {
            case UIEventTraceStage.DispatchStarted:
                _pending[trace.DispatchId] = new PendingEvent(trace);
                break;
            case UIEventTraceStage.CallbackInvoked:
                if (_pending.TryGetValue(trace.DispatchId, out var pending))
                    pending.Callbacks.Add(new UIEventDebugCallbackRecord(
                        trace.Timestamp,
                        trace.Handler,
                        ElementPath(trace.CurrentTarget),
                        trace.PropagationPhase));
                break;
            case UIEventTraceStage.DispatchCompleted:
                Complete(trace);
                break;
        }
    }

    private void Complete(UIEventTrace trace)
    {
        if (!_pending.Remove(trace.DispatchId, out var pending)) return;
        var evt = trace.Event;
        var record = new UIEventDebugRecord(
            trace.DispatchId,
            pending.Timestamp,
            evt.GetType().Name,
            ElementName(evt.target),
            ElementPath(evt.target),
            pending.PanelRoot,
            pending.PropagationPath.Select(ElementPath).Reverse().ToArray(),
            pending.Callbacks.ToArray(),
            EventInformation(evt),
            evt.propagationStopped,
            evt.immediatePropagationStopped);
        Model.Add(record);
        Repaint();
    }

    private static IReadOnlyList<string> EventInformation(UIEventBase evt)
    {
        var values = new List<string>
        {
            $"Event: {evt.GetType().FullName}",
            $"Target: {ElementPath(evt.target)}",
            $"Propagation stopped: {evt.propagationStopped}",
            $"Immediate propagation stopped: {evt.immediatePropagationStopped}"
        };
        foreach (var property in evt.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.GetIndexParameters().Length == 0 &&
                                        property.Name is not nameof(UIEventBase.target) and
                                        not nameof(UIEventBase.currentTarget) and
                                        not nameof(UIEventBase.propagationStopped) and
                                        not nameof(UIEventBase.immediatePropagationStopped)))
        {
            object? value;
            try { value = property.GetValue(evt); }
            catch { value = "<unavailable>"; }
            values.Add($"{property.Name}: {DisplayValue(value)}");
        }
        return values;
    }

    private static string DisplayValue(object? value) => value switch
    {
        null => "null",
        VisualElement element => ElementPath(element),
        _ => value.ToString() ?? string.Empty
    };

    internal static string ElementName(VisualElement? element) => element is null
        ? "<none>"
        : string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name;

    internal static string ElementPath(VisualElement? element)
    {
        if (element is null) return "<none>";
        var parts = new Stack<string>();
        for (var current = element; current is not null; current = current.parent)
            parts.Push(ElementName(current));
        return string.Join('/', parts);
    }

    private sealed class PendingEvent
    {
        public PendingEvent(UIEventTrace trace)
        {
            Timestamp = trace.Timestamp;
            PropagationPath = trace.PropagationPath;
            PanelRoot = trace.PropagationPath.Count > 0 ? trace.PropagationPath[^1] : trace.Target;
        }

        public Fix64 Timestamp { get; }
        public IReadOnlyList<VisualElement> PropagationPath { get; }
        public VisualElement? PanelRoot { get; }
        public List<UIEventDebugCallbackRecord> Callbacks { get; } = [];
    }
}
