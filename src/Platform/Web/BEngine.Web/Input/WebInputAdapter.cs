using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

namespace BEngine.Platform;

/// <summary>Converts browser input, IME and gamepad reports into the engine's platform input contract.</summary>
public sealed partial class WebInputAdapter
{
    private readonly Dictionary<int, Touch> Touches = [];
    private readonly HashSet<int> ConnectedGamepads = [];
    private long _keyEvents, _pointerEvents, _touchEvents, _wheelEvents, _gamepadReports;

    public long KeyEvents => _keyEvents;
    public long PointerEvents => _pointerEvents;
    public long TouchEvents => _touchEvents;
    public long WheelEvents => _wheelEvents;
    public long GamepadReports => _gamepadReports;

    [SupportedOSPlatform("browser")]
    public static void ConfigureClipboard()
    {
        if (PlatformRuntime.GetService<IInputService>() is PlatformInputService input)
        { input.ReadClipboard = ReadClipboard; input.WriteClipboard = WriteClipboard; }
    }

    public void Reset()
    {
        Touches.Clear();
        ConnectedGamepads.Clear();
        _keyEvents = _pointerEvents = _touchEvents = _wheelEvents = _gamepadReports = 0;
    }

    public void SetComposition(string text) => Input.SetComposition(text);
    public string GetTextInputRequest() => Input.textInputRequest is { } request
        ? JsonSerializer.Serialize(new WebTextInputRequest(request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Text, request.Anchor, request.Caret, request.Password, request.Multiline), WebPlatformJsonContext.Default.WebTextInputRequest)
        : string.Empty;
    public void SubmitTextEdit(string sessionId, string text, int anchor, int caret)
    {
        if (long.TryParse(sessionId, System.Globalization.CultureInfo.InvariantCulture, out var id))
            Input.SubmitTextEdit(new(id, text, anchor, caret));
    }
    public void ReportSafeArea(double left, double bottom, double right, double top) =>
        DisplayUtility.Current.ReportSafeArea(new((Fix64)left, (Fix64)bottom, (Fix64)right, (Fix64)top));
    [JSImport("globalThis.BEngineText.readClipboard")]
    private static partial string ReadClipboard();
    [JSImport("globalThis.BEngineText.writeClipboard")]
    private static partial bool WriteClipboard(string text);

    public void KeyEvent(string code, bool pressed, string text)
    {
        _keyEvents++;
        if (TryMapKey(code, out var key)) Input.SetKeyState(key, pressed);
        if (pressed && !string.IsNullOrEmpty(text))
            foreach (var character in text) Input.AppendTextInput(character);
    }

    public void PointerEvent(int pointerId, string pointerType, int button, bool pressed,
        double x, double y, double deltaX, double deltaY, double pressure, double radius)
    {
        _pointerEvents++;
        if (string.Equals(pointerType, "touch", StringComparison.OrdinalIgnoreCase))
        {
            _touchEvents++;
            var phase = Touches.ContainsKey(pointerId) ? TouchPhase.Moved : TouchPhase.Began;
            Touches[pointerId] = new Touch(pointerId,
                new Vector2((Fix64)x, (Fix64)y),
                new Vector2((Fix64)deltaX, (Fix64)deltaY),
                (Fix64)Math.Max(0, (double)Time.unscaledDeltaTime),
                1, phase, (Fix64)Math.Max(0, pressure), Fix64.One,
                (Fix64)Math.Max(0, radius), Fix64.Zero);
            Input.SetTouches(Touches.Values);
            return;
        }

        Input.SetMousePosition((Fix64)x, (Fix64)y);
        Input.SetMouseDelta((Fix64)deltaX, (Fix64)deltaY);
        if (button is >= 0 and <= 6) Input.SetKeyState((KeyCode)((int)KeyCode.Mouse0 + button), pressed);
    }

    public void PointerEnded(int pointerId, string pointerType, int button,
        double x, double y, bool canceled)
    {
        _pointerEvents++;
        if (string.Equals(pointerType, "touch", StringComparison.OrdinalIgnoreCase))
        {
            _touchEvents++;
            Touches[pointerId] = new Touch(pointerId,
                new Vector2((Fix64)x, (Fix64)y), Vector2.zero, Fix64.Zero, 1,
                canceled ? TouchPhase.Canceled : TouchPhase.Ended);
            Input.SetTouches(Touches.Values);
            Touches.Remove(pointerId);
            return;
        }
        Input.SetMousePosition((Fix64)x, (Fix64)y);
        if (button is >= 0 and <= 6) Input.SetKeyState((KeyCode)((int)KeyCode.Mouse0 + button), false);
    }

    public void WheelEvent(double x, double y)
    {
        _wheelEvents++;
        Input.SetMouseScroll((Fix64)x, (Fix64)(-y));
    }

    public void ReportGamepads(string json)
    {
        _gamepadReports++;
        var snapshots = JsonSerializer.Deserialize(
            json, WebPlatformJsonContext.Default.GamepadStateSnapshotArray) ?? [];
        var current = snapshots.Select(item => item.Index).ToHashSet();
        foreach (var disconnected in ConnectedGamepads.Except(current).ToArray())
        {
            Input.SetGamepadConnected(disconnected, false);
            ConnectedGamepads.Remove(disconnected);
        }
        foreach (var gamepad in snapshots)
        {
            ConnectedGamepads.Add(gamepad.Index);
            Input.SetGamepadConnected(gamepad.Index, true, gamepad.Id);
            SetGamepadAxes(gamepad);
            SetGamepadButtons(gamepad);
        }
    }

    private static void SetGamepadAxes(GamepadStateSnapshot gamepad)
    {
        var axes = gamepad.Axes ?? [];
        if (axes.Length > 0) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.LeftStickX, (Fix64)axes[0]);
        if (axes.Length > 1) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.LeftStickY, (Fix64)(-axes[1]));
        if (axes.Length > 2) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.RightStickX, (Fix64)axes[2]);
        if (axes.Length > 3) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.RightStickY, (Fix64)(-axes[3]));
        var buttons = gamepad.Buttons ?? [];
        if (buttons.Length > 6) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.LeftTrigger, (Fix64)buttons[6]);
        if (buttons.Length > 7) Input.SetGamepadAxis(gamepad.Index, GamepadAxis.RightTrigger, (Fix64)buttons[7]);
    }

    private static void SetGamepadButtons(GamepadStateSnapshot gamepad)
    {
        var buttons = gamepad.Buttons ?? [];
        (int Index, GamepadButton Button)[] mappings =
        [
            (0, GamepadButton.South), (1, GamepadButton.East), (2, GamepadButton.West),
            (3, GamepadButton.North), (4, GamepadButton.LeftShoulder),
            (5, GamepadButton.RightShoulder), (8, GamepadButton.Back),
            (9, GamepadButton.Start), (10, GamepadButton.LeftStick),
            (11, GamepadButton.RightStick), (12, GamepadButton.DPadUp),
            (13, GamepadButton.DPadDown), (14, GamepadButton.DPadLeft),
            (15, GamepadButton.DPadRight), (16, GamepadButton.Home)
        ];
        foreach (var mapping in mappings)
            Input.SetGamepadButtonState(gamepad.Index, mapping.Button,
                mapping.Index < buttons.Length && buttons[mapping.Index] >= 0.5);
    }

    private static bool TryMapKey(string code, out KeyCode key)
    {
        key = code switch
        {
            "Space" => KeyCode.Space, "Enter" => KeyCode.Return, "Escape" => KeyCode.Escape,
            "Tab" => KeyCode.Tab, "Backspace" => KeyCode.Backspace, "Delete" => KeyCode.Delete,
            "Home" => KeyCode.Home, "End" => KeyCode.End, "PageUp" => KeyCode.PageUp,
            "PageDown" => KeyCode.PageDown, "Insert" => KeyCode.Insert,
            "CapsLock" => KeyCode.CapsLock, "NumLock" => KeyCode.Numlock,
            "PrintScreen" => KeyCode.Print, "ScrollLock" => KeyCode.ScrollLock,
            "Pause" => KeyCode.Pause, "Backquote" => KeyCode.BackQuote, "Minus" => KeyCode.Minus,
            "Equal" => KeyCode.Equals, "BracketLeft" => KeyCode.LeftBracket,
            "BracketRight" => KeyCode.RightBracket, "Backslash" => KeyCode.Backslash,
            "Semicolon" => KeyCode.Semicolon, "Quote" => KeyCode.Quote, "Comma" => KeyCode.Comma,
            "Period" => KeyCode.Period, "Slash" => KeyCode.Slash,
            "ShiftLeft" => KeyCode.LeftShift, "ShiftRight" => KeyCode.RightShift,
            "ControlLeft" => KeyCode.LeftControl, "ControlRight" => KeyCode.RightControl,
            "AltLeft" => KeyCode.LeftAlt, "AltRight" => KeyCode.RightAlt,
            "MetaLeft" => KeyCode.LeftCommand, "MetaRight" => KeyCode.RightCommand,
            "ArrowUp" => KeyCode.UpArrow, "ArrowDown" => KeyCode.DownArrow,
            "ArrowLeft" => KeyCode.LeftArrow, "ArrowRight" => KeyCode.RightArrow,
            _ => KeyCode.None
        };
        if (key != KeyCode.None) return true;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) &&
            code[3] is >= 'A' and <= 'Z')
        {
            key = (KeyCode)((int)KeyCode.A + code[3] - 'A');
            return true;
        }
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) &&
            code[5] is >= '0' and <= '9')
        {
            key = (KeyCode)((int)KeyCode.Alpha0 + code[5] - '0');
            return true;
        }
        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out var function) &&
            function is >= 1 and <= 15)
        {
            key = (KeyCode)((int)KeyCode.F1 + function - 1);
            return true;
        }
        return false;
    }
}

internal sealed class GamepadStateSnapshot
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public double[]? Axes { get; set; }
    public double[]? Buttons { get; set; }
}

internal sealed record WebTextInputRequest(string Id, string Text, int Anchor, int Caret, bool Password, bool Multiline);
