namespace BEngine;

public static class Input
{
    private static readonly HashSet<KeyCode> Held = [];
    private static readonly HashSet<KeyCode> Down = [];
    private static readonly HashSet<KeyCode> Up = [];
    private static readonly Dictionary<string, Fix64> Axes = new(StringComparer.OrdinalIgnoreCase);
    private static Fix64 _mouseX;
    private static Fix64 _mouseY;
    private static Vector2 _mousePosition;
    private static Vector2 _mouseScrollDelta;

    public static Fix64 mouseX
    {
        get { return _mouseX; }
        internal set => _mouseX = value;
    }
    public static Fix64 mouseY
    {
        get { return _mouseY; }
        internal set => _mouseY = value;
    }
    public static Vector2 mousePosition
    {
        get { return _mousePosition; }
        internal set => _mousePosition = value;
    }
    public static Vector2 mouseScrollDelta
    {
        get { return _mouseScrollDelta; }
        internal set => _mouseScrollDelta = value;
    }
    public static bool anyKey
    {
        get { return Held.Count > 0; }
    }
    public static bool anyKeyDown
    {
        get { return Down.Count > 0; }
    }

    public static bool GetKey(KeyCode key)
    {
        return Held.Contains(key);
    }

    public static bool GetKeyDown(KeyCode key)
    {
        return Down.Contains(key);
    }

    public static bool GetKeyUp(KeyCode key)
    {
        return Up.Contains(key);
    }

    public static bool GetMouseButton(int button)
    {
        return Held.Contains(ToMouseKey(button));
    }

    public static bool GetMouseButtonDown(int button)
    {
        return Down.Contains(ToMouseKey(button));
    }

    public static bool GetMouseButtonUp(int button)
    {
        return Up.Contains(ToMouseKey(button));
    }

    public static Fix64 GetAxis(string axisName)
    {
        return GetAxisRawCore(axisName);
    }

    public static Fix64 GetAxisRaw(string axisName)
    {
        return GetAxisRawCore(axisName);
    }

    internal static void BeginFrame()
    {
        Down.Clear();
        Up.Clear();
        _mouseX = Fix64.Zero;
        _mouseY = Fix64.Zero;
        _mouseScrollDelta = Vector2.zero;
    }

    public static void SetKeyState(KeyCode key, bool isDown)
    {
        if (isDown && Held.Add(key))
        {
            Down.Add(key);
        }
        else if (!isDown && Held.Remove(key))
        {
            Up.Add(key);
        }
    }

    public static void SetMouseDelta(Fix64 x, Fix64 y)
    {
        _mouseX = x;
        _mouseY = y;
    }

    public static void SetAxis(string axisName, Fix64 value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisName);
        Axes[axisName] = Fix64.Clamp(value, -Fix64.One, Fix64.One);
    }

    public static void SetMousePosition(Fix64 x, Fix64 y)
    {
        _mousePosition = new Vector2(x, y);
    }

    public static void SetMouseScroll(Fix64 x, Fix64 y)
    {
        _mouseScrollDelta = new Vector2(x, y);
    }

    private static Fix64 GetAxisRawCore(string axisName) =>
        Axes.TryGetValue(axisName, out var value) ? value : Fix64.Zero;

    private static KeyCode ToMouseKey(int button) => button is >= 0 and <= 6
        ? (KeyCode)((int)KeyCode.Mouse0 + button)
        : throw new ArgumentOutOfRangeException(nameof(button));
}
