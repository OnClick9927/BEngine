namespace BEngine.Platform;

/// <summary>Platform input state SPI. Consumers use Input or InputUtility.</summary>
public interface IInputService
{
    Fix64 mouseX { get; set; }
    Fix64 mouseY { get; set; }
    Vector2 mousePosition { get; set; }
    Vector2 mouseScrollDelta { get; set; }
    bool anyKey { get; }
    bool anyKeyDown { get; }
    string inputString { get; }
    string compositionString => string.Empty;
    void SetComposition(string text) { }
    TextInputRequest? textInputRequest { get => null; set { } }
    void SubmitTextEdit(TextInputEdit edit) { }
    bool TryTakeTextEdit(out TextInputEdit? edit) { edit = null; return false; }
    bool TryGetClipboard(out string text) { text = string.Empty; return false; }
    bool TrySetClipboard(string text) => false;
    Rect? textInputRect { get; set; }
    int touchCount { get; }
    Touch[] touches { get; }
    int gamepadCount { get; }
    bool IsGamepadConnected(int gamepad);
    /// <summary>Known host slots, including disconnected slots. Older providers may return an empty inventory.</summary>
    GamepadInfo[] GetGamepads() => [];
    bool GetKey(KeyCode key);
    bool GetKeyDown(KeyCode key);
    bool GetKeyUp(KeyCode key);
    bool GetKey(string name);
    bool GetKeyDown(string name);
    bool GetKeyUp(string name);
    bool GetMouseButton(int button);
    bool GetMouseButtonDown(int button);
    bool GetMouseButtonUp(int button);
    Fix64 GetAxis(string axisName);
    Fix64 GetAxisRaw(string axisName);
    bool GetButton(string buttonName);
    bool GetButtonDown(string buttonName);
    bool GetButtonUp(string buttonName);
    Touch GetTouch(int index);
    bool GetGamepadButton(int gamepad, GamepadButton button);
    bool GetGamepadButtonDown(int gamepad, GamepadButton button);
    bool GetGamepadButtonUp(int gamepad, GamepadButton button);
    Fix64 GetGamepadAxis(int gamepad, GamepadAxis axis);
    string[] GetJoystickNames();
    void ResetInputAxes();
    void EndFrame();
    void SetKeyState(KeyCode key, bool isDown);
    void SetMouseDelta(Fix64 x, Fix64 y);
    void SetAxis(string axisName, Fix64 value);
    void SetMousePosition(Fix64 x, Fix64 y);
    void SetMouseScroll(Fix64 x, Fix64 y);
    void AppendTextInput(char character);
    void SetTouches(IEnumerable<Touch> touches);
    void SetGamepadConnected(int gamepad, bool connected, string? name = null);
    void SetGamepadButtonState(int gamepad, GamepadButton button, bool isDown);
    void SetGamepadAxis(int gamepad, GamepadAxis axis, Fix64 value);
}
