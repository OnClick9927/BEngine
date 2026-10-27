using Silk.NET.Input;
using BEngine.Platform;

namespace BEngine.Platform;

public sealed class SilkInputSession : IDisposable
{
    private readonly Silk.NET.Windowing.IWindow _window;
    private readonly IInputContext _input;
    private readonly SilkGamepadInput _gamepads;
    private IMouse? _primaryMouse;
    private System.Numerics.Vector2? _lastMousePosition;
    private bool? _appliedCursorVisible;
    private CursorLockMode? _appliedCursorLockState;
    private readonly IntPtr _nativeHandle;
    private readonly TextInputUtility? _textInput;
    private readonly SdlTextInputSession? _sdlTextInput;
    private readonly ITouchInputSource? _touchInput;
    private long _lastTouchPoll = System.Diagnostics.Stopwatch.GetTimestamp();
    private bool _disposed;

    public SilkInputSession(Silk.NET.Windowing.IWindow window)
    {
        _window = window;
        if (Silk.NET.Windowing.Sdl.SdlWindowing.IsViewSdl(window))
        {
            _nativeHandle = window.Handle;
            _sdlTextInput = new SdlTextInputSession(window.Handle);
            _textInput = new TextInputUtility(_sdlTextInput);
        }
        else
        {
            _nativeHandle = window.Native?.Win32?.Hwnd ?? IntPtr.Zero;
            _textInput = RuntimeServiceAccess.GetService<TextInputUtility>();
        }
        _input = window.CreateInput();
        if (RuntimeServiceAccess.GetService<IInputService>() is PlatformInputService state && _input.Keyboards.FirstOrDefault() is { } clipboardKeyboard)
        {
            state.ReadClipboard = () => clipboardKeyboard.ClipboardText;
            state.WriteClipboard = text => { clipboardKeyboard.ClipboardText = text; return true; };
        }
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }
        foreach (var mouse in _input.Mice)
        {
            _primaryMouse ??= mouse;
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnMouseScroll;
        }
        _gamepads = new SilkGamepadInput(_input);
        try { _touchInput = RuntimeServiceAccess.GetService<TouchInputUtility>()?.Attach(_nativeHandle); }
        catch { _gamepads.Dispose(); _input.Dispose(); throw; }
        ApplyCursorState();
    }

    public event Action? CancelPressed;

    public void Poll()
    {
        PollMouseInput();
        _gamepads.Poll();
        if (_touchInput is not null)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var elapsed = Fix64.FromTimeSpan(System.Diagnostics.Stopwatch.GetElapsedTime(_lastTouchPoll, now));
            _lastTouchPoll = now;
            Input.SetTouches(_touchInput.Poll(Math.Max(1, _window.FramebufferSize.X), Math.Max(1, _window.FramebufferSize.Y), elapsed));
        }
    }

    public void ApplyCursorState()
    {
        var caret = Input.textInputRect;
        if (caret is { } rect)
        {
            var sx = (Fix64)_window.Size.X / Math.Max(1, _window.FramebufferSize.X);
            var sy = (Fix64)_window.Size.Y / Math.Max(1, _window.FramebufferSize.Y);
            caret = new Rect(rect.x * sx, rect.y * sy, rect.width * sx, rect.height * sy);
        }
        _textInput?.SetCaret(_nativeHandle, caret);
        Input.SetComposition(caret is null ? string.Empty : _textInput?.GetComposition(_nativeHandle) ?? string.Empty);
        if (_primaryMouse is not { } mouse ||
            _appliedCursorVisible == Cursor.visible && _appliedCursorLockState == Cursor.lockState) return;
        _appliedCursorVisible = Cursor.visible;
        _appliedCursorLockState = Cursor.lockState;
        var mode = Cursor.lockState == CursorLockMode.Locked
            ? Silk.NET.Input.CursorMode.Disabled
            : Cursor.visible ? Silk.NET.Input.CursorMode.Normal : Silk.NET.Input.CursorMode.Hidden;
        try
        {
            mouse.Cursor.IsConfined = Cursor.lockState == CursorLockMode.Confined;
            if (mouse.Cursor.IsSupported(mode)) mouse.Cursor.CursorMode = mode;
        }
        catch (NotSupportedException)
        {
            mouse.Cursor.IsConfined = false;
            if (mouse.Cursor.IsSupported(Silk.NET.Input.CursorMode.Normal))
                mouse.Cursor.CursorMode = Silk.NET.Input.CursorMode.Normal;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _touchInput?.Dispose();
        _disposed = true;
        if (_touchInput is not null) Input.SetTouches([]);
        if (RuntimeServiceAccess.GetService<IInputService>() is PlatformInputService state)
        { state.ReadClipboard = null; state.WriteClipboard = null; }
        _textInput?.ReleaseWindow(_nativeHandle); _sdlTextInput?.Dispose(); _gamepads.Dispose(); _input.Dispose();
    }

    private void OnKeyDown(IKeyboard _, Key key, int __)
    {
        if (Input.textInputRect is not null && _textInput?.IsComposing(_nativeHandle) == true) return;
        if (key == Key.Escape && Input.textInputRect is null) CancelPressed?.Invoke();
        if (TryMap(key, out var code)) Input.SetKeyState(code, true);
    }

    private static void OnKeyUp(IKeyboard _, Key key, int __)
    {
        if (TryMap(key, out var code)) Input.SetKeyState(code, false);
    }

    private static void OnKeyChar(IKeyboard _, char character) => Input.AppendTextInput(character);

    private void OnMouseMove(IMouse _, System.Numerics.Vector2 position)
    {
        if (_lastMousePosition is { } previous)
        {
            var delta = position - previous;
            Input.SetMouseDelta((Fix64)delta.X, (Fix64)delta.Y);
        }
        _lastMousePosition = position;
        SetMousePosition(position);
    }

    private static void OnMouseDown(IMouse _, MouseButton button) =>
        Input.SetKeyState(ToMouseKey(button), true);

    private static void OnMouseUp(IMouse _, MouseButton button) =>
        Input.SetKeyState(ToMouseKey(button), false);

    private static void OnMouseScroll(IMouse _, ScrollWheel scroll) =>
        Input.SetMouseScroll((Fix64)scroll.X, (Fix64)scroll.Y);

    private void PollMouseInput()
    {
        if (_primaryMouse is not { } mouse) return;
        SetMousePosition(mouse.Position);
        Input.SetKeyState(KeyCode.Mouse0, mouse.IsButtonPressed(MouseButton.Left));
        Input.SetKeyState(KeyCode.Mouse1, mouse.IsButtonPressed(MouseButton.Right));
        Input.SetKeyState(KeyCode.Mouse2, mouse.IsButtonPressed(MouseButton.Middle));
    }

    private void SetMousePosition(System.Numerics.Vector2 position)
    {
        var framebuffer = _window.FramebufferSize;
        var window = _window.Size;
        var scaleX = framebuffer.X / (float)Math.Max(1, window.X);
        var scaleY = framebuffer.Y / (float)Math.Max(1, window.Y);
        Input.SetMousePosition(
            (Fix64)(position.X * scaleX),
            (Fix64)((window.Y - position.Y) * scaleY));
    }

    private static KeyCode ToMouseKey(MouseButton button) => button switch
    {
        MouseButton.Left => KeyCode.Mouse0,
        MouseButton.Right => KeyCode.Mouse1,
        MouseButton.Middle => KeyCode.Mouse2,
        MouseButton.Button4 => KeyCode.Mouse3,
        MouseButton.Button5 => KeyCode.Mouse4,
        _ => KeyCode.Mouse0
    };

    private static bool TryMap(Key key, out KeyCode code)
    {
        code = key switch
        {
            >= Key.A and <= Key.Z => (KeyCode)((int)KeyCode.A + key - Key.A),
            >= Key.Number0 and <= Key.Number9 => (KeyCode)((int)KeyCode.Alpha0 + key - Key.Number0),
            >= Key.F1 and <= Key.F15 => (KeyCode)((int)KeyCode.F1 + key - Key.F1),
            >= Key.Keypad0 and <= Key.Keypad9 => (KeyCode)((int)KeyCode.Keypad0 + key - Key.Keypad0),
            Key.Enter => KeyCode.Return,
            Key.Right => KeyCode.RightArrow,
            Key.Left => KeyCode.LeftArrow,
            Key.Down => KeyCode.DownArrow,
            Key.Up => KeyCode.UpArrow,
            Key.ShiftLeft => KeyCode.LeftShift,
            Key.ShiftRight => KeyCode.RightShift,
            Key.ControlLeft => KeyCode.LeftControl,
            Key.ControlRight => KeyCode.RightControl,
            Key.AltLeft => KeyCode.LeftAlt,
            Key.AltRight => KeyCode.RightAlt,
            Key.SuperLeft => KeyCode.LeftCommand,
            Key.SuperRight => KeyCode.RightCommand,
            Key.Apostrophe => KeyCode.Quote,
            Key.Equal => KeyCode.Equals,
            Key.BackSlash => KeyCode.Backslash,
            Key.GraveAccent => KeyCode.BackQuote,
            Key.PrintScreen => KeyCode.Print,
            Key.NumLock => KeyCode.Numlock,
            Key.KeypadDecimal => KeyCode.KeypadPeriod,
            Key.KeypadSubtract => KeyCode.KeypadMinus,
            Key.KeypadAdd => KeyCode.KeypadPlus,
            Key.KeypadEqual => KeyCode.KeypadEquals,
            Key.Unknown or >= Key.F16 => KeyCode.None,
            _ when Enum.TryParse<KeyCode>(key.ToString(), ignoreCase: false, out var direct) => direct,
            _ => default
        };
        return code != KeyCode.None;
    }


}
