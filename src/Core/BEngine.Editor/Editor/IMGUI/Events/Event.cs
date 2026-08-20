namespace BEngine.Editor;

/// <summary>Unity-compatible immediate GUI input event. One current event is active during each OnGUI pass.</summary>
public sealed class Event
{
    [ThreadStatic] private static Event? _current;
    [ThreadStatic] private static Func<Event?>? _popEvent;
    [ThreadStatic] private static Func<int>? _eventCount;
    public static Event current
    {
        get => _current ?? throw new InvalidOperationException("Event.current is only available during OnGUI.");
        internal set => _current = value;
    }

    public EventType type { get; set; }
    public EventType rawType { get; set; }
    public Vector2 mousePosition { get; set; }
    public Vector2 delta { get; set; }
    public int button { get; set; }
    public EventModifiers modifiers { get; set; }
    public Fix64 pressure { get; set; }
    public PointerType pointerType { get; set; }
    public int clickCount { get; set; }
    public char character { get; set; }
    public KeyCode keyCode { get; set; }
    public string commandName { get; set; } = string.Empty;
    public int displayIndex { get; set; }
    public bool shift => (modifiers & EventModifiers.Shift) != 0;
    public bool control => (modifiers & EventModifiers.Control) != 0;
    public bool alt => (modifiers & EventModifiers.Alt) != 0;
    public bool command => (modifiers & EventModifiers.Command) != 0;
    public bool capsLock => (modifiers & EventModifiers.CapsLock) != 0;
    public bool numeric => (modifiers & EventModifiers.Numeric) != 0;
    public bool functionKey => (modifiers & EventModifiers.FunctionKey) != 0;
    public bool isKey => type is EventType.KeyDown or EventType.KeyUp;
    public bool isMouse => type is EventType.MouseDown or EventType.MouseUp or EventType.MouseMove or
        EventType.MouseDrag or EventType.ContextClick or EventType.MouseEnterWindow or EventType.MouseLeaveWindow;
    public bool isScrollWheel => type == EventType.ScrollWheel;
    public bool isDirectManipulationDevice => pointerType is PointerType.Touch or PointerType.Pen;

    public Event() : this(EventType.Layout) { }
    public Event(int displayIndex) : this(EventType.Layout) => this.displayIndex = displayIndex;
    public Event(EventType type) { this.type = type; rawType = type; }
    public Event(Event other)
    {
        ArgumentNullException.ThrowIfNull(other);
        type = other.type; rawType = other.rawType; mousePosition = other.mousePosition; delta = other.delta;
        button = other.button; modifiers = other.modifiers; pressure = other.pressure; clickCount = other.clickCount;
        character = other.character; keyCode = other.keyCode; commandName = other.commandName;
        displayIndex = other.displayIndex; pointerType = other.pointerType;
    }

    public void Use()
    {
        if (type is EventType.Repaint or EventType.Layout)
        {
            Debug.LogWarning($"Event.Use() cannot consume an event of type {type}.");
            return;
        }
        type = EventType.Used;
    }

    public static Event PopEvent() => _popEvent?.Invoke() ?? new Event(EventType.Ignore);
    public static bool PopEvent(Event outEvent)
    {
        ArgumentNullException.ThrowIfNull(outEvent);
        var next = _popEvent?.Invoke();
        if (next is null) return false;
        outEvent.CopyFrom(next);
        return true;
    }
    public static int GetEventCount() => _eventCount?.Invoke() ?? 0;
    internal static void BindQueue(Func<Event?> popEvent, Func<int> eventCount)
    {
        _popEvent = popEvent;
        _eventCount = eventCount;
    }
    internal static void ClearCurrent()
    {
        _current = null;
        _popEvent = null;
        _eventCount = null;
    }

    public EventType GetTypeForControl(int controlId)
    {
        if ((type is EventType.MouseDrag or EventType.MouseUp) && GUIUtility.hotControl != 0 &&
            GUIUtility.hotControl != controlId) return EventType.Ignore;
        if (isKey && GUIUtility.keyboardControl != 0 && GUIUtility.keyboardControl != controlId)
            return EventType.Ignore;
        return type;
    }

    public static Event KeyboardEvent(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var result = new Event(EventType.KeyDown);
        var token = key.Trim();
        while (token.Length > 0)
        {
            if (token[0] == '#') result.modifiers |= EventModifiers.Shift;
            else if (token[0] == '^') result.modifiers |= EventModifiers.Control;
            else if (token[0] == '&') result.modifiers |= EventModifiers.Alt;
            else if (token[0] == '%') result.modifiers |= EventModifiers.Command;
            else break;
            token = token[1..];
        }
        var aliases = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = KeyCode.LeftArrow, ["right"] = KeyCode.RightArrow,
            ["up"] = KeyCode.UpArrow, ["down"] = KeyCode.DownArrow,
            ["enter"] = KeyCode.Return, ["return"] = KeyCode.Return,
            ["esc"] = KeyCode.Escape, ["escape"] = KeyCode.Escape,
            ["del"] = KeyCode.Delete, ["delete"] = KeyCode.Delete,
            ["backspace"] = KeyCode.Backspace, ["home"] = KeyCode.Home, ["end"] = KeyCode.End,
            ["page up"] = KeyCode.PageUp, ["page down"] = KeyCode.PageDown
        };
        if (aliases.TryGetValue(token.Trim('[', ']'), out var code)) result.keyCode = code;
        else if (Enum.TryParse<KeyCode>(token, true, out code)) result.keyCode = code;
        else if (token.Length == 1)
        {
            result.character = token[0];
            Enum.TryParse(token.ToUpperInvariant(), out code);
            result.keyCode = code;
        }
        return result;
    }

    public override string ToString() => isMouse
        ? $"{type} button={button} position={mousePosition} delta={delta} clicks={clickCount}"
        : isKey ? $"{type} key={keyCode} character='{character}' modifiers={modifiers}"
        : $"{type} command='{commandName}' modifiers={modifiers}";

    public override bool Equals(object? obj) => obj is Event other && type == other.type &&
        rawType == other.rawType && mousePosition == other.mousePosition && delta == other.delta &&
        button == other.button && modifiers == other.modifiers && pressure == other.pressure &&
        clickCount == other.clickCount && character == other.character && keyCode == other.keyCode &&
        commandName == other.commandName && displayIndex == other.displayIndex &&
        pointerType == other.pointerType;

    public override int GetHashCode() => HashCode.Combine(type, rawType, mousePosition, delta, button,
        modifiers, clickCount, keyCode);

    private void CopyFrom(Event other)
    {
        type = other.type; rawType = other.rawType; mousePosition = other.mousePosition; delta = other.delta;
        button = other.button; modifiers = other.modifiers; pressure = other.pressure; clickCount = other.clickCount;
        character = other.character; keyCode = other.keyCode; commandName = other.commandName;
        displayIndex = other.displayIndex; pointerType = other.pointerType;
    }
}
