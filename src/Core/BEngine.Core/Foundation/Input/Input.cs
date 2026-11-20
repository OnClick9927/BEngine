using BEngine.Platform;

namespace BEngine;

/// <summary>Unified API backed by the active platform service provider.</summary>
public static class Input
{
    public static Fix64 mouseX { get => InputUtility.Current.mouseX; internal set => InputUtility.Current.mouseX = value; }

    public static Fix64 mouseY { get => InputUtility.Current.mouseY; internal set => InputUtility.Current.mouseY = value; }

    public static Vector2 mousePosition { get => InputUtility.Current.mousePosition; internal set => InputUtility.Current.mousePosition = value; }

    public static Vector2 mouseScrollDelta { get => InputUtility.Current.mouseScrollDelta; internal set => InputUtility.Current.mouseScrollDelta = value; }

    public static bool anyKey => InputUtility.Current.anyKey;

    public static bool anyKeyDown => InputUtility.Current.anyKeyDown;

    public static string inputString => InputUtility.Current.inputString;
    public static string compositionString => InputUtility.Current.compositionString;
    public static void SetComposition(string text) => InputUtility.Current.SetComposition(text);
    public static TextInputRequest? textInputRequest { get => InputUtility.Current.textInputRequest; internal set => InputUtility.Current.textInputRequest = value; }
    public static void SubmitTextEdit(TextInputEdit edit) => InputUtility.Current.SubmitTextEdit(edit);
    public static bool TryTakeTextEdit(out TextInputEdit? edit) => InputUtility.Current.TryTakeTextEdit(out edit);
    public static bool TryGetClipboard(out string text) => InputUtility.Current.TryGetClipboard(out text);
    public static bool TrySetClipboard(string text) => InputUtility.Current.TrySetClipboard(text);

    public static Rect? textInputRect { get => InputUtility.Current.textInputRect; internal set => InputUtility.Current.textInputRect = value; }

    public static int touchCount => InputUtility.Current.touchCount;

    public static Touch[] touches => InputUtility.Current.touches;

    public static int gamepadCount => InputUtility.Current.gamepadCount;
    public static bool IsGamepadConnected(int gamepad) => InputUtility.Current.IsGamepadConnected(gamepad);
    public static GamepadInfo[] GetGamepads() => InputUtility.Current.GetGamepads();

    public static bool GetKey(KeyCode key) =>
        InputUtility.Current.GetKey(key);

    public static bool GetKeyDown(KeyCode key) =>
        InputUtility.Current.GetKeyDown(key);

    public static bool GetKeyUp(KeyCode key) =>
        InputUtility.Current.GetKeyUp(key);

    public static bool GetKey(string name) =>
        InputUtility.Current.GetKey(name);

    public static bool GetKeyDown(string name) =>
        InputUtility.Current.GetKeyDown(name);

    public static bool GetKeyUp(string name) =>
        InputUtility.Current.GetKeyUp(name);

    public static bool GetMouseButton(int button) =>
        InputUtility.Current.GetMouseButton(button);

    public static bool GetMouseButtonDown(int button) =>
        InputUtility.Current.GetMouseButtonDown(button);

    public static bool GetMouseButtonUp(int button) =>
        InputUtility.Current.GetMouseButtonUp(button);

    public static Fix64 GetAxis(string axisName) =>
        InputUtility.Current.GetAxis(axisName);

    public static Fix64 GetAxisRaw(string axisName) =>
        InputUtility.Current.GetAxisRaw(axisName);

    public static bool GetButton(string buttonName) =>
        InputUtility.Current.GetButton(buttonName);

    public static bool GetButtonDown(string buttonName) =>
        InputUtility.Current.GetButtonDown(buttonName);

    public static bool GetButtonUp(string buttonName) =>
        InputUtility.Current.GetButtonUp(buttonName);

    public static Touch GetTouch(int index) =>
        InputUtility.Current.GetTouch(index);

    public static bool GetGamepadButton(int gamepad, GamepadButton button) =>
        InputUtility.Current.GetGamepadButton(gamepad, button);

    public static bool GetGamepadButtonDown(int gamepad, GamepadButton button) =>
        InputUtility.Current.GetGamepadButtonDown(gamepad, button);

    public static bool GetGamepadButtonUp(int gamepad, GamepadButton button) =>
        InputUtility.Current.GetGamepadButtonUp(gamepad, button);

    public static Fix64 GetGamepadAxis(int gamepad, GamepadAxis axis) =>
        InputUtility.Current.GetGamepadAxis(gamepad, axis);

    public static string[] GetJoystickNames() =>
        InputUtility.Current.GetJoystickNames();

    public static void ResetInputAxes() =>
        InputUtility.Current.ResetInputAxes();

    internal static void EndFrame() =>
        InputUtility.Current.EndFrame();

    public static void SetKeyState(KeyCode key, bool isDown) =>
        InputUtility.Current.SetKeyState(key, isDown);

    public static void SetMouseDelta(Fix64 x, Fix64 y) =>
        InputUtility.Current.SetMouseDelta(x, y);

    public static void SetAxis(string axisName, Fix64 value) =>
        InputUtility.Current.SetAxis(axisName, value);

    public static void SetMousePosition(Fix64 x, Fix64 y) =>
        InputUtility.Current.SetMousePosition(x, y);

    public static void SetMouseScroll(Fix64 x, Fix64 y) =>
        InputUtility.Current.SetMouseScroll(x, y);

    public static void AppendTextInput(char character) =>
        InputUtility.Current.AppendTextInput(character);

    public static void SetTouches(IEnumerable<Touch> touches) =>
        InputUtility.Current.SetTouches(touches);

    public static void SetGamepadConnected(int gamepad, bool connected, string? name = null) =>
        InputUtility.Current.SetGamepadConnected(gamepad, connected, name);

    public static void SetGamepadButtonState(int gamepad, GamepadButton button, bool isDown) =>
        InputUtility.Current.SetGamepadButtonState(gamepad, button, isDown);

    public static void SetGamepadAxis(int gamepad, GamepadAxis axis, Fix64 value) =>
        InputUtility.Current.SetGamepadAxis(gamepad, axis, value);
}
