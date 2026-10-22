namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IInputService through dependency injection.</summary>
public sealed class InputUtility
{
    private readonly IInputService _implementation;

    public InputUtility(IInputService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static InputUtility Current => RuntimeServiceAccess.GetRequiredService<InputUtility>();

    public Fix64 mouseX { get => _implementation.mouseX; set => _implementation.mouseX = value; }

    public Fix64 mouseY { get => _implementation.mouseY; set => _implementation.mouseY = value; }

    public Vector2 mousePosition { get => _implementation.mousePosition; set => _implementation.mousePosition = value; }

    public Vector2 mouseScrollDelta { get => _implementation.mouseScrollDelta; set => _implementation.mouseScrollDelta = value; }

    public bool anyKey => _implementation.anyKey;

    public bool anyKeyDown => _implementation.anyKeyDown;

    public string inputString => _implementation.inputString;
    public string compositionString => _implementation.compositionString;
    public void SetComposition(string text) => _implementation.SetComposition(text);
    public TextInputRequest? textInputRequest { get => _implementation.textInputRequest; set => _implementation.textInputRequest = value; }
    public void SubmitTextEdit(TextInputEdit edit) => _implementation.SubmitTextEdit(edit);
    public bool TryTakeTextEdit(out TextInputEdit? edit) => _implementation.TryTakeTextEdit(out edit);
    public bool TryGetClipboard(out string text) => _implementation.TryGetClipboard(out text);
    public bool TrySetClipboard(string text) => _implementation.TrySetClipboard(text);

    public Rect? textInputRect { get => _implementation.textInputRect; set => _implementation.textInputRect = value; }

    public int touchCount => _implementation.touchCount;

    public Touch[] touches => _implementation.touches;

    public int gamepadCount => _implementation.gamepadCount;
    public bool IsGamepadConnected(int gamepad) => _implementation.IsGamepadConnected(gamepad);
    public GamepadInfo[] GetGamepads() => _implementation.GetGamepads();

    public bool GetKey(KeyCode key) =>
        _implementation.GetKey(key);

    public bool GetKeyDown(KeyCode key) =>
        _implementation.GetKeyDown(key);

    public bool GetKeyUp(KeyCode key) =>
        _implementation.GetKeyUp(key);

    public bool GetKey(string name) =>
        _implementation.GetKey(name);

    public bool GetKeyDown(string name) =>
        _implementation.GetKeyDown(name);

    public bool GetKeyUp(string name) =>
        _implementation.GetKeyUp(name);

    public bool GetMouseButton(int button) =>
        _implementation.GetMouseButton(button);

    public bool GetMouseButtonDown(int button) =>
        _implementation.GetMouseButtonDown(button);

    public bool GetMouseButtonUp(int button) =>
        _implementation.GetMouseButtonUp(button);

    public Fix64 GetAxis(string axisName) =>
        _implementation.GetAxis(axisName);

    public Fix64 GetAxisRaw(string axisName) =>
        _implementation.GetAxisRaw(axisName);

    public bool GetButton(string buttonName) =>
        _implementation.GetButton(buttonName);

    public bool GetButtonDown(string buttonName) =>
        _implementation.GetButtonDown(buttonName);

    public bool GetButtonUp(string buttonName) =>
        _implementation.GetButtonUp(buttonName);

    public Touch GetTouch(int index) =>
        _implementation.GetTouch(index);

    public bool GetGamepadButton(int gamepad, GamepadButton button) =>
        _implementation.GetGamepadButton(gamepad, button);

    public bool GetGamepadButtonDown(int gamepad, GamepadButton button) =>
        _implementation.GetGamepadButtonDown(gamepad, button);

    public bool GetGamepadButtonUp(int gamepad, GamepadButton button) =>
        _implementation.GetGamepadButtonUp(gamepad, button);

    public Fix64 GetGamepadAxis(int gamepad, GamepadAxis axis) =>
        _implementation.GetGamepadAxis(gamepad, axis);

    public string[] GetJoystickNames() =>
        _implementation.GetJoystickNames();

    public void ResetInputAxes() =>
        _implementation.ResetInputAxes();

    public void EndFrame() =>
        _implementation.EndFrame();

    public void SetKeyState(KeyCode key, bool isDown) =>
        _implementation.SetKeyState(key, isDown);

    public void SetMouseDelta(Fix64 x, Fix64 y) =>
        _implementation.SetMouseDelta(x, y);

    public void SetAxis(string axisName, Fix64 value) =>
        _implementation.SetAxis(axisName, value);

    public void SetMousePosition(Fix64 x, Fix64 y) =>
        _implementation.SetMousePosition(x, y);

    public void SetMouseScroll(Fix64 x, Fix64 y) =>
        _implementation.SetMouseScroll(x, y);

    public void AppendTextInput(char character) =>
        _implementation.AppendTextInput(character);

    public void SetTouches(IEnumerable<Touch> touches) =>
        _implementation.SetTouches(touches);

    public void SetGamepadConnected(int gamepad, bool connected, string? name = null) =>
        _implementation.SetGamepadConnected(gamepad, connected, name);

    public void SetGamepadButtonState(int gamepad, GamepadButton button, bool isDown) =>
        _implementation.SetGamepadButtonState(gamepad, button, isDown);

    public void SetGamepadAxis(int gamepad, GamepadAxis axis, Fix64 value) =>
        _implementation.SetGamepadAxis(gamepad, axis, value);
}
