using BEngine.Editor;

namespace BEngine.UIElements.Editor;

public sealed class UIElementsEventDebuggerWindowModel(string windowId) : EditorWindowModel(windowId)
{
    private readonly List<UIEventDebugRecord> _events = [];
    private readonly HashSet<string> _disabledEventTypes = new(StringComparer.Ordinal);

    public IReadOnlyList<UIEventDebugRecord> Events => _events;
    public IReadOnlyCollection<string> DisabledEventTypes => _disabledEventTypes;
    public long? SelectedDispatchId { get; internal set; }
    public VisualElement? SelectedPanelRoot { get; internal set; }
    public bool Suspended { get; internal set; }
    public int MaxLogEntries { get; internal set; } = 1000;
    public string Search { get; set; } = string.Empty;
    public Vector2 EventScroll { get; set; }
    public Vector2 CallbackScroll { get; set; }
    public Vector2 PathScroll { get; set; }
    public Vector2 InformationScroll { get; set; }

    public UIEventDebugRecord? SelectedRecord => SelectedDispatchId is { } id
        ? _events.FirstOrDefault(item => item.DispatchId == id)
        : null;

    internal void Add(UIEventDebugRecord record)
    {
        _events.Add(record);
        while (_events.Count > MaxLogEntries) _events.RemoveAt(0);
        if (SelectedDispatchId is { } selected && _events.All(item => item.DispatchId != selected))
            SelectedDispatchId = null;
    }

    internal void Clear()
    {
        _events.Clear();
        SelectedDispatchId = null;
    }

    internal void Trim()
    {
        while (_events.Count > MaxLogEntries) _events.RemoveAt(0);
    }

    internal bool IsEventTypeEnabled(string eventType) => !_disabledEventTypes.Contains(eventType);

    internal void ToggleEventType(string eventType)
    {
        if (!_disabledEventTypes.Add(eventType)) _disabledEventTypes.Remove(eventType);
    }
}
