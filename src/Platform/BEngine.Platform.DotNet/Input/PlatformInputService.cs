namespace BEngine.Platform;

public sealed class PlatformInputService : IInputService
{
    static PlatformInputService()
    {
        // These axis bodies may otherwise first compile when a scene starts
        // accepting movement. Prepare metadata at service creation, without
        // sampling input or changing keyboard/gamepad state.
        if (!OperatingSystem.IsWindows() || !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ||
            !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled ||
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith("Mono", StringComparison.OrdinalIgnoreCase)) return;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        foreach (var name in new[] { nameof(DigitalAxis), nameof(GetAxisRawCore), nameof(GetAxisRaw), "get_" + nameof(gamepadCount) })
        {
            var method = typeof(PlatformInputService).GetMethod(name, flags);
            if (method is not null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle);
        }
    }

    private readonly HashSet<KeyCode> Held = [];
    private readonly HashSet<KeyCode> Down = [];
    private readonly HashSet<KeyCode> Up = [];
    private readonly Dictionary<string, Fix64> Axes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, GamepadState> Gamepads = [];
    private readonly List<Touch> CurrentTouches = [];
    private string _inputString = string.Empty;
    private Fix64 _mouseX;
    private Fix64 _mouseY;
    private Vector2 _mousePosition;
    private Vector2 _mouseScrollDelta;

    public Fix64 mouseX
    {
        get { return _mouseX; }
        set => _mouseX = value;
    }
    public Fix64 mouseY
    {
        get { return _mouseY; }
        set => _mouseY = value;
    }
    public Vector2 mousePosition
    {
        get { return _mousePosition; }
        set => _mousePosition = value;
    }
    public Vector2 mouseScrollDelta
    {
        get { return _mouseScrollDelta; }
        set => _mouseScrollDelta = value;
    }
    public bool anyKey
    {
        get { return Held.Count > 0; }
    }
    public bool anyKeyDown
    {
        get { return Down.Count > 0; }
    }
    public string inputString => _inputString;
    public string compositionString { get; private set; } = string.Empty;
    public void SetComposition(string text) => compositionString = text ?? string.Empty;
    public TextInputRequest? textInputRequest { get; set; }
    private readonly object _textEditGate = new();
    private TextInputEdit? _pendingTextEdit;
    public void SubmitTextEdit(TextInputEdit edit) { lock (_textEditGate) _pendingTextEdit = edit; }
    public bool TryTakeTextEdit(out TextInputEdit? edit)
    { lock (_textEditGate) { edit = _pendingTextEdit; _pendingTextEdit = null; return edit is not null; } }
    public Func<string?>? ReadClipboard { get; set; }
    public Func<string, bool>? WriteClipboard { get; set; }
    public bool TryGetClipboard(out string text)
    {
        text = string.Empty;
        try { if (ReadClipboard?.Invoke() is not { } value) return false; text = value; return true; }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { return false; }
    }
    public bool TrySetClipboard(string text)
    {
        try { return WriteClipboard?.Invoke(text) == true; }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { return false; }
    }
    public Rect? textInputRect { get; set; }
    public int touchCount => CurrentTouches.Count;
    public Touch[] touches => [.. CurrentTouches];
    public int gamepadCount
    {
        get
        {
            var count = 0;
            foreach (var state in Gamepads.Values) if (state.Connected) count++;
            return count;
        }
    }
    public bool IsGamepadConnected(int gamepad) => Gamepads.TryGetValue(gamepad, out var state) && state.Connected;
    public GamepadInfo[] GetGamepads() => Gamepads.Count == 0 ? Array.Empty<GamepadInfo>() : Gamepads.OrderBy(pair => pair.Key)
        .Select(pair => new GamepadInfo(pair.Key, pair.Value.Name, pair.Value.Connected)).ToArray();

    public bool GetKey(KeyCode key)
    {
        return Held.Contains(key);
    }

    public bool GetKeyDown(KeyCode key)
    {
        return Down.Contains(key);
    }

    public bool GetKeyUp(KeyCode key)
    {
        return Up.Contains(key);
    }

    public bool GetKey(string name) => GetKey(ParseKey(name));
    public bool GetKeyDown(string name) => GetKeyDown(ParseKey(name));
    public bool GetKeyUp(string name) => GetKeyUp(ParseKey(name));

    public bool GetMouseButton(int button)
    {
        return Held.Contains(ToMouseKey(button));
    }

    public bool GetMouseButtonDown(int button)
    {
        return Down.Contains(ToMouseKey(button));
    }

    public bool GetMouseButtonUp(int button)
    {
        return Up.Contains(ToMouseKey(button));
    }

    public Fix64 GetAxis(string axisName)
    {
        return GetAxisRawCore(axisName);
    }

    public Fix64 GetAxisRaw(string axisName)
    {
        return GetAxisRawCore(axisName);
    }

    public bool GetButton(string buttonName) => GetButtonCore(buttonName, GetKey);
    public bool GetButtonDown(string buttonName) => GetButtonCore(buttonName, GetKeyDown);
    public bool GetButtonUp(string buttonName) => GetButtonCore(buttonName, GetKeyUp);

    public Touch GetTouch(int index) => CurrentTouches[index];

    public bool GetGamepadButton(int gamepad, GamepadButton button) =>
        GetGamepad(gamepad).Held.Contains(button);

    public bool GetGamepadButtonDown(int gamepad, GamepadButton button) =>
        GetGamepad(gamepad).Down.Contains(button);

    public bool GetGamepadButtonUp(int gamepad, GamepadButton button) =>
        GetGamepad(gamepad).Up.Contains(button);

    public Fix64 GetGamepadAxis(int gamepad, GamepadAxis axis) =>
        GetGamepad(gamepad).Axes.GetValueOrDefault(axis);

    public string[] GetJoystickNames() => Gamepads.Count == 0 ? Array.Empty<string>() : Gamepads.OrderBy(static pair => pair.Key)
        .Where(static pair => pair.Value.Connected)
        .Select(static pair => pair.Value.Name).ToArray();

    public void ResetInputAxes()
    {
        Axes.Clear(); Held.Clear(); Down.Clear(); Up.Clear();
        _inputString = ""; compositionString = ""; _mouseX = _mouseY = 0; _mouseScrollDelta = Vector2.zero;
        foreach (var state in Gamepads.Values) { state.Held.Clear(); state.Down.Clear(); state.Up.Clear(); state.Axes.Clear(); }
    }

    public void EndFrame()
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

    public void SetKeyState(KeyCode key, bool isDown)
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

    public void SetMouseDelta(Fix64 x, Fix64 y)
    {
        _mouseX += x;
        _mouseY += y;
    }

    public void SetAxis(string axisName, Fix64 value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisName);
        Axes[axisName] = Fix64.Clamp(value, -Fix64.One, Fix64.One);
    }

    public void SetMousePosition(Fix64 x, Fix64 y)
    {
        _mousePosition = new Vector2(x, y);
    }

    public void SetMouseScroll(Fix64 x, Fix64 y)
    {
        _mouseScrollDelta += new Vector2(x, y);
    }

    public void AppendTextInput(char character)
    {
        if (!char.IsControl(character)) _inputString += character;
    }

    public void SetTouches(IEnumerable<Touch> touches)
    {
        ArgumentNullException.ThrowIfNull(touches);
        if (touches is Touch[] { Length: 0 })
        {
            CurrentTouches.Clear();
            return;
        }
        var snapshot = touches.Take(129).ToArray();
        if (snapshot.Length > 128 || snapshot.Any(touch => touch.fingerId < 0 || !Enum.IsDefined(touch.phase) ||
            touch.deltaTime < 0 || touch.tapCount < 0 || touch.pressure < 0 || touch.maximumPossiblePressure < 0 ||
            touch.radius < 0 || touch.radiusVariance < 0) || snapshot.Select(touch => touch.fingerId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Touch snapshots require at most 128 unique non-negative finger IDs and valid phases/measurements.", nameof(touches));
        Array.Sort(snapshot, (a, b) => a.fingerId.CompareTo(b.fingerId));
        CurrentTouches.Clear();
        CurrentTouches.AddRange(snapshot);
    }

    public void SetGamepadConnected(int gamepad, bool connected, string? name = null)
    {
        if (gamepad < 0) throw new ArgumentOutOfRangeException(nameof(gamepad));
        var state = GetOrCreateGamepad(gamepad);
        state.HasConnectionReport = true;
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

    public void SetGamepadButtonState(int gamepad, GamepadButton button, bool isDown)
    {
        var state = GetOrCreateGamepad(gamepad);
        if (state.HasConnectionReport && !state.Connected) return;
        state.Connected = true;
        if (isDown && state.Held.Add(button)) state.Down.Add(button);
        else if (!isDown && state.Held.Remove(button)) state.Up.Add(button);
    }

    public void SetGamepadAxis(int gamepad, GamepadAxis axis, Fix64 value)
    {
        var state = GetOrCreateGamepad(gamepad);
        if (state.HasConnectionReport && !state.Connected) return;
        state.Connected = true;
        state.Axes[axis] = axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger
            ? Fix64.Clamp(value, Fix64.Zero, Fix64.One)
            : Fix64.Clamp(value, -Fix64.One, Fix64.One);
    }

    private Fix64 GetAxisRawCore(string axisName)
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

    private Fix64 DigitalAxis(KeyCode negative, KeyCode alternateNegative,
        KeyCode positive, KeyCode alternatePositive, GamepadAxis gamepadAxis)
    {
        var keyboard = (GetKey(positive) || GetKey(alternatePositive) ? Fix64.One : Fix64.Zero) -
                       (GetKey(negative) || GetKey(alternateNegative) ? Fix64.One : Fix64.Zero);
        var gamepad = Fix64.Zero;
        var magnitude = Fix64.Zero;
        foreach (var state in Gamepads.Values)
        {
            if (!state.Connected) continue;
            var value = state.Axes.GetValueOrDefault(gamepadAxis);
            var absolute = Fix64.Abs(value);
            // OrderBy was stable: equal magnitudes keep the first connected slot's value.
            if (absolute <= magnitude) continue;
            gamepad = value;
            magnitude = absolute;
        }
        return Fix64.Abs(keyboard) >= Fix64.Abs(gamepad) ? keyboard : gamepad;
    }

    private bool GetButtonCore(string name, Func<KeyCode, bool> query)
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

    private KeyCode ParseKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Enum.TryParse<KeyCode>(name.Trim(), ignoreCase: true, out var key)) return key;
        throw new ArgumentException($"Unknown key '{name}'.", nameof(name));
    }

    private GamepadState GetGamepad(int gamepad)
    {
        if (!Gamepads.TryGetValue(gamepad, out var state) || !state.Connected)
            throw new ArgumentOutOfRangeException(nameof(gamepad), "The requested gamepad is not connected.");
        return state;
    }

    private GamepadState GetOrCreateGamepad(int gamepad)
    {
        if (gamepad < 0) throw new ArgumentOutOfRangeException(nameof(gamepad));
        if (!Gamepads.TryGetValue(gamepad, out var state))
            Gamepads[gamepad] = state = new GamepadState { Name = $"Gamepad {gamepad + 1}" };
        return state;
    }

    private KeyCode ToMouseKey(int button) => button is >= 0 and <= 6
        ? (KeyCode)((int)KeyCode.Mouse0 + button)
        : throw new ArgumentOutOfRangeException(nameof(button));

    private sealed class GamepadState
    {
        public bool HasConnectionReport;
        public bool Connected;
        public string Name = string.Empty;
        public HashSet<GamepadButton> Held { get; } = [];
        public HashSet<GamepadButton> Down { get; } = [];
        public HashSet<GamepadButton> Up { get; } = [];
        public Dictionary<GamepadAxis, Fix64> Axes { get; } = [];
    }
}
