namespace BEngine;

public enum KeyCode
{
    None,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
    Space, Return, Escape, Tab, Backspace, Delete,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
    UpArrow, DownArrow, LeftArrow, RightArrow,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Mouse0, Mouse1, Mouse2, Mouse3, Mouse4, Mouse5, Mouse6
}

public static class Input
{
    private static readonly HashSet<KeyCode> Held = [];
    private static readonly HashSet<KeyCode> Down = [];
    private static readonly HashSet<KeyCode> Up = [];
    private static readonly Dictionary<string, Fix64> Axes = new(StringComparer.OrdinalIgnoreCase);

    public static Fix64 mouseX { get; internal set; }
    public static Fix64 mouseY { get; internal set; }
    public static Vector3 mousePosition { get; internal set; }
    public static Vector2 mouseScrollDelta { get; internal set; }
    public static bool anyKey => Held.Count > 0;
    public static bool anyKeyDown => Down.Count > 0;
    public static bool GetKey(KeyCode key) => Held.Contains(key);
    public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
    public static bool GetKeyUp(KeyCode key) => Up.Contains(key);
    public static bool GetMouseButton(int button) => GetKey(ToMouseKey(button));
    public static bool GetMouseButtonDown(int button) => GetKeyDown(ToMouseKey(button));
    public static bool GetMouseButtonUp(int button) => GetKeyUp(ToMouseKey(button));
    public static Fix64 GetAxis(string axisName) => GetAxisRaw(axisName);
    public static Fix64 GetAxisRaw(string axisName) => Axes.TryGetValue(axisName, out var value) ? value : Fix64.Zero;

    internal static void BeginFrame()
    {
        Down.Clear();
        Up.Clear();
        mouseX = Fix64.Zero;
        mouseY = Fix64.Zero;
        mouseScrollDelta = Vector2.zero;
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
        mouseX = x;
        mouseY = y;
    }

    public static void SetAxis(string axisName, Fix64 value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisName);
        Axes[axisName] = Fix64.Clamp(value, -Fix64.One, Fix64.One);
    }

    public static void SetMousePosition(Fix64 x, Fix64 y) => mousePosition = new Vector3(x, y, 0);
    public static void SetMouseScroll(Fix64 x, Fix64 y) => mouseScrollDelta = new Vector2(x, y);

    private static KeyCode ToMouseKey(int button) => button is >= 0 and <= 6
        ? (KeyCode)((int)KeyCode.Mouse0 + button)
        : throw new ArgumentOutOfRangeException(nameof(button));
}
