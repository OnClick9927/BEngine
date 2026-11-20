using BEngine.Editor;

namespace BEngine.UIElements.Editor;

public sealed class UIElementsEventDebuggerWindowModel(string windowId) : EditorWindowModel(windowId)
{
    private readonly List<UIEventDebugRecord> _events = [];
    private readonly HashSet<string> _disabledEventTypes = new(StringComparer.Ordinal);
    private long _selectedRecordRevision = -1;
    private long? _selectedRecordId;
    private UIEventDebugRecord? _selectedRecord;

    internal long Revision { get; private set; }

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

    public UIEventDebugRecord? SelectedRecord
    {
        get
        {
            if (_selectedRecordRevision == Revision && _selectedRecordId == SelectedDispatchId)
                return _selectedRecord;
            _selectedRecordRevision = Revision;
            _selectedRecordId = SelectedDispatchId;
            return _selectedRecord = SelectedDispatchId is { } id ? Matches1516() : null;
            [global::BEngine.SkipLoopGuard]
            global::BEngine.UIElements.Editor.UIEventDebugRecord? Matches1516()
            {
                var matchSource1516 = _events;
                ArgumentNullException.ThrowIfNull(matchSource1516, "source");
                if (matchSource1516.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.UIElements.Editor.UIEventDebugRecord>))
                {
                    foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.UIElements.Editor.UIEventDebugRecord>)matchSource1516)
                    {
                        if (item.DispatchId == id)
                            return item;
                    }
            
                    return default!;
                }
            
                global::System.ReadOnlySpan<global::BEngine.UIElements.Editor.UIEventDebugRecord> matchValues1516 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource1516);
                foreach (var item in matchValues1516)
                {
                    if (item.DispatchId == id)
                        return item;
                }
            
                return default!;
            }
        }
    }

    internal void Add(UIEventDebugRecord record)
    {
        _events.Add(record);
        if (_events.Count > MaxLogEntries) _events.RemoveRange(0, _events.Count - MaxLogEntries);
        Revision++;
        if (SelectedDispatchId is { } selected && Query1847())
            SelectedDispatchId = null;
        bool Query1847()
        {
            var querySource1847 = _events;
            ArgumentNullException.ThrowIfNull(querySource1847, "source");
            if (querySource1847.GetType() == typeof(global::BEngine.UIElements.Editor.UIEventDebugRecord[]) || querySource1847.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.UIElements.Editor.UIEventDebugRecord>))
            {
                global::System.ReadOnlySpan<global::BEngine.UIElements.Editor.UIEventDebugRecord> queryValues1847 = querySource1847.GetType() == typeof(global::BEngine.UIElements.Editor.UIEventDebugRecord[]) ? (global::BEngine.UIElements.Editor.UIEventDebugRecord[])(object)querySource1847 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.UIElements.Editor.UIEventDebugRecord>)(object)querySource1847);
                foreach (var item in queryValues1847)
                {
                    if (!(item.DispatchId != selected))
                        return false;
                }
            }
            else
            {
                foreach (var item in querySource1847)
                {
                    if (!(item.DispatchId != selected))
                        return false;
                }
            }
        
            return true;
        }
    }

    internal void Clear()
    {
        _events.Clear();
        SelectedDispatchId = null;
        Revision++;
        _selectedRecord = null;
    }

    internal void Trim()
    {
        if (_events.Count <= MaxLogEntries) return;
        _events.RemoveRange(0, _events.Count - MaxLogEntries);
        Revision++;
    }

    internal bool IsEventTypeEnabled(string eventType) => !_disabledEventTypes.Contains(eventType);

    internal void ToggleEventType(string eventType)
    {
        if (!_disabledEventTypes.Add(eventType)) _disabledEventTypes.Remove(eventType);
        Revision++;
    }
}
