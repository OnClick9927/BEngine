namespace BEngine;

public static class Input
{
    private static readonly HashSet<KeyCode> Held = [];
    private static readonly HashSet<KeyCode> Down = [];
    private static readonly HashSet<KeyCode> Up = [];
    private static readonly Dictionary<string, Fix64> Axes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, GamepadState> Gamepads = [];
    private static readonly List<Touch> CurrentTouches = [];
    private static string _inputString = string.Empty;
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
    public static string inputString => _inputString;
    public static int touchCount => CurrentTouches.Count;
    public static Touch[] touches => [.. CurrentTouches];
    public static int gamepadCount => Gamepads.Values.Count(static state => state.Connected);

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

    public static bool GetKey(string name) => GetKey(ParseKey(name));
    public static bool GetKeyDown(string name) => GetKeyDown(ParseKey(name));
    public static bool GetKeyUp(string name) => GetKeyUp(ParseKey(name));

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

    public static bool GetButton(string buttonName) => GetButtonCore(buttonName, GetKey);
    public static bool GetButtonDown(string buttonName) => GetButtonCore(buttonName, GetKeyDown);
    public static bool GetButtonUp(string buttonName) => GetButtonCore(buttonName, GetKeyUp);

    public static Touch GetTouch(int index) => CurrentTouches[index];

    public static bool GetGamepadButton(int gamepad, GamepadButton button) =>
        GetGamepad(gamepad).Held.Contains(button);

    public static bool GetGamepadButtonDown(int gamepad, GamepadButton button) =>
        GetGamepad(gamepad).Down.Contains(button);

    public static bool GetGamepadButtonUp(int gamepad, GamepadButton button) =>
        GetGamepad(gamepad).Up.Contains(button);

    public static Fix64 GetGamepadAxis(int gamepad, GamepadAxis axis) =>
        GetGamepad(gamepad).Axes.GetValueOrDefault(axis);

    public static string[] GetJoystickNames() => Gamepads.OrderBy(static pair => pair.Key)
        .Where(static pair => pair.Value.Connected)
        .Select(static pair => pair.Value.Name).ToArray();

    public static void ResetInputAxes() => Axes.Clear();

    internal static void EndFrame()
    {
        Down.Clear();
        Up.Clear();
        _mouseX = Fix64.Zero;
        _mouseY = Fix64.Zero;
        _mouseScrollDelta = Vector2.zero;
        _inputString = string.Empty;
        foreach (var state in Gamepads.Values)
        {
            state.Down.Clear();
            state.Up.Clear();
        }
        for (var index = CurrentTouches.Count - 1; index >= 0; index--)
        {
            var touch = CurrentTouches[index];
            if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
            {
                CurrentTouches.RemoveAt(index);
                continue;
            }
            CurrentTouches[index] = touch with
            {
                deltaPosition = Vector2.zero,
                deltaTime = Fix64.Zero,
                phase = TouchPhase.Stationary
            };
        }
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
        _mouseX += x;
        _mouseY += y;
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
        _mouseScrollDelta += new Vector2(x, y);
    }

    public static void AppendTextInput(char character)
    {
        if (!char.IsControl(character)) _inputString += character;
    }

    public static void SetTouches(IEnumerable<Touch> touches)
    {
        ArgumentNullException.ThrowIfNull(touches);
        CurrentTouches.Clear();
        CurrentTouches.AddRange(touches.OrderBy(static touch => touch.fingerId));
    }

    public static void SetGamepadConnected(int gamepad, bool connected, string? name = null)
    {
        if (gamepad < 0) throw new ArgumentOutOfRangeException(nameof(gamepad));
        var state = GetOrCreateGamepad(gamepad);
        state.Connected = connected;
        if (!string.IsNullOrWhiteSpace(name)) state.Name = name.Trim();
        if (!connected)
        {
            state.Held.Clear();
            state.Down.Clear();
            state.Up.Clear();
            state.Axes.Clear();
        }
    }

    public static void SetGamepadButtonState(int gamepad, GamepadButton button, bool isDown)
    {
        var state = GetOrCreateGamepad(gamepad);
        state.Connected = true;
        if (isDown && state.Held.Add(button)) state.Down.Add(button);
        else if (!isDown && state.Held.Remove(button)) state.Up.Add(button);
    }

    public static void SetGamepadAxis(int gamepad, GamepadAxis axis, Fix64 value)
    {
        var state = GetOrCreateGamepad(gamepad);
        state.Connected = true;
        state.Axes[axis] = axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger
            ? Fix64.Clamp(value, Fix64.Zero, Fix64.One)
            : Fix64.Clamp(value, -Fix64.One, Fix64.One);
    }

    private static Fix64 GetAxisRawCore(string axisName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisName);
        if (Axes.TryGetValue(axisName, out var value)) return value;
        return axisName.Trim() switch
        {
            var name when name.Equals("Horizontal", StringComparison.OrdinalIgnoreCase) =>
                DigitalAxis(KeyCode.A, KeyCode.LeftArrow, KeyCode.D, KeyCode.RightArrow,
                    GamepadAxis.LeftStickX),
            var name when name.Equals("Vertical", StringComparison.OrdinalIgnoreCase) =>
                DigitalAxis(KeyCode.S, KeyCode.DownArrow, KeyCode.W, KeyCode.UpArrow,
                    GamepadAxis.LeftStickY),
            var name when name.Equals("Mouse X", StringComparison.OrdinalIgnoreCase) => _mouseX,
            var name when name.Equals("Mouse Y", StringComparison.OrdinalIgnoreCase) => _mouseY,
            var name when name.Equals("Mouse ScrollWheel", StringComparison.OrdinalIgnoreCase) =>
                _mouseScrollDelta.y,
            _ => Fix64.Zero
        };
    }

    private static Fix64 DigitalAxis(KeyCode negative, KeyCode alternateNegative,
        KeyCode positive, KeyCode alternatePositive, GamepadAxis gamepadAxis)
    {
        var keyboard = (GetKey(positive) || GetKey(alternatePositive) ? Fix64.One : Fix64.Zero) -
                       (GetKey(negative) || GetKey(alternateNegative) ? Fix64.One : Fix64.Zero);
        var gamepad = Gamepads.Values.Where(static state => state.Connected)
            .Select(state => state.Axes.GetValueOrDefault(gamepadAxis))
            .OrderByDescending(static value => Fix64.Abs(value)).FirstOrDefault();
        return Fix64.Abs(keyboard) >= Fix64.Abs(gamepad) ? keyboard : gamepad;
    }

    private static bool GetButtonCore(string name, Func<KeyCode, bool> query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim() switch
        {
            var value when value.Equals("Jump", StringComparison.OrdinalIgnoreCase) => query(KeyCode.Space),
            var value when value.Equals("Submit", StringComparison.OrdinalIgnoreCase) =>
                query(KeyCode.Return) || query(KeyCode.KeypadEnter),
            var value when value.Equals("Cancel", StringComparison.OrdinalIgnoreCase) => query(KeyCode.Escape),
            _ => query(ParseKey(name))
        };
    }

    private static KeyCode ParseKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Enum.TryParse<KeyCode>(name.Trim(), ignoreCase: true, out var key)) return key;
        throw new ArgumentException($"Unknown key '{name}'.", nameof(name));
    }

    private static GamepadState GetGamepad(int gamepad)
    {
        if (!Gamepads.TryGetValue(gamepad, out var state) || !state.Connected)
            throw new ArgumentOutOfRangeException(nameof(gamepad), "The requested gamepad is not connected.");
        return state;
    }

    private static GamepadState GetOrCreateGamepad(int gamepad)
    {
        if (gamepad < 0) throw new ArgumentOutOfRangeException(nameof(gamepad));
        if (!Gamepads.TryGetValue(gamepad, out var state))
            Gamepads[gamepad] = state = new GamepadState { Name = $"Gamepad {gamepad + 1}" };
        return state;
    }

    private static KeyCode ToMouseKey(int button) => button is >= 0 and <= 6
        ? (KeyCode)((int)KeyCode.Mouse0 + button)
        : throw new ArgumentOutOfRangeException(nameof(button));

    private sealed class GamepadState
    {
        public bool Connected;
        public string Name = string.Empty;
        public HashSet<GamepadButton> Held { get; } = [];
        public HashSet<GamepadButton> Down { get; } = [];
        public HashSet<GamepadButton> Up { get; } = [];
        public Dictionary<GamepadAxis, Fix64> Axes { get; } = [];
    }
}
