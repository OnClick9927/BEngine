using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Android.Util;
using Android.Views;

namespace BEngine.Platform.AndroidNative;

/// <summary>Converts Android events to engine input while preserving touch edges across render frames.</summary>
public sealed class AndroidInputAdapter : IDisposable
{
    private const string LogTag = "BEngine";
    private readonly View _surfaceView;
    private readonly Dictionary<int, BEngine.Vector2> _touchPositions = [];
    private readonly ConcurrentQueue<TouchInputState> _touchInput = new();
    private readonly HashSet<int> _connectedGamepads = [];
    private int _surfaceWidth, _surfaceHeight;
    private bool _inputLogged;

    public AndroidInputAdapter(View surface)
    {
        _surfaceView = surface;
        surface.Touch += OnTouch;
        surface.GenericMotion += OnGenericMotion;
    }

    public void SetSurfaceSize(int width, int height)
    {
        _surfaceWidth = Math.Max(1, width);
        _surfaceHeight = Math.Max(1, height);
    }

    public void ApplyFrameInput()
    {
        ApplyTouchInput();
        PollGamepads();
    }

    public void Dispose()
    {
        _surfaceView.Touch -= OnTouch;
        _surfaceView.GenericMotion -= OnGenericMotion;
    }
    public bool TryHandleKeyEvent(KeyEvent? e, bool textEditing)
    {
        if (e is not null && (e.Source & (InputSourceType.Gamepad | InputSourceType.Joystick)) != 0 &&
            TryMapGamepadButton(e.KeyCode, out var button))
        {
            var index = Math.Max(0, e.DeviceId);
            _connectedGamepads.Add(index);
            Input.SetGamepadConnected(index, true, InputDevice.GetDevice(e.DeviceId)?.Name ?? "Android gamepad");
            Input.SetGamepadButtonState(index, button, e.Action != KeyEventActions.Up);
            return true;
        }
        if (textEditing) return false;
        if (e is not null && TryMapKey(e.KeyCode, out var key))
        {
            Input.SetKeyState(key, e.Action != KeyEventActions.Up);
            if (e.Action == KeyEventActions.Down && e.UnicodeChar is > 0 and <= char.MaxValue)
                Input.AppendTextInput((char)e.UnicodeChar);
            return true;
        }
        return false;
    }

    private void OnTouch(object? sender, View.TouchEventArgs args)
    {
        var e = args.Event;
        if (e is null) return;
        var changedIndex = e.ActionIndex;
        var action = e.ActionMasked;
        var touches = new List<BEngine.Touch>(e.PointerCount);
        for (var index = 0; index < e.PointerCount; index++)
        {
            var id = e.GetPointerId(index);
            // Input uses a bottom-left origin on every platform; Android events
            // use a top-left origin. UI converts back to its own top-left space.
            var height = _surfaceView?.Height ?? _surfaceHeight;
            var position = new BEngine.Vector2((Fix64)(e.GetX(index) * _surfaceWidth / Math.Max(1f, _surfaceView?.Width ?? _surfaceWidth)),
                (Fix64)((height - e.GetY(index)) * _surfaceHeight / Math.Max(1f, height)));
            var previous = _touchPositions.GetValueOrDefault(id, position);
            var phase = action switch
            {
                MotionEventActions.Down or MotionEventActions.PointerDown when index == changedIndex => TouchPhase.Began,
                MotionEventActions.Up or MotionEventActions.PointerUp when index == changedIndex => TouchPhase.Ended,
                MotionEventActions.Cancel => TouchPhase.Canceled,
                _ when position == previous => TouchPhase.Stationary,
                _ => TouchPhase.Moved
            };
            touches.Add(new BEngine.Touch(id, position, position - previous,
                Time.unscaledDeltaTime, 1, phase, (Fix64)e.GetPressure(index), Fix64.One,
                (Fix64)Math.Max(e.GetTouchMajor(index) * .5f, 0), Fix64.Zero));
            if (phase is TouchPhase.Ended or TouchPhase.Canceled) _touchPositions.Remove(id);
            else _touchPositions[id] = position;
        }
        var mousePosition = e.PointerCount > 0 ? touches[0].position : BEngine.Vector2.zero;
        _touchInput.Enqueue(new TouchInputState(touches.ToArray(), mousePosition,
            action is not MotionEventActions.Up and not MotionEventActions.Cancel));
        if (!_inputLogged)
        {
            _inputLogged = true;
            Log.Info(LogTag, $"BENGINE_ANDROID_INPUT_OK|kind=touch|count={touches.Count}");
        }
        args.Handled = true;
    }

    private void ApplyTouchInput()
    {
        if (!_touchInput.TryDequeue(out var state)) return;
        // UI-thread callbacks must not mutate Input while the GL thread ticks.
        // Keep down/up in separate frames, including a tap shorter than one frame,
        // but collapse intermediate motion samples to prevent a dragging backlog.
        if (!state.HasEdge)
            while (_touchInput.TryPeek(out var next) && !next.HasEdge && _touchInput.TryDequeue(out var newer))
                state = newer;
        Input.SetTouches(state.Touches);
        Input.SetMousePosition(state.MousePosition.x, state.MousePosition.y);
        Input.SetKeyState(KeyCode.Mouse0, state.MouseHeld);
    }

    private sealed record TouchInputState(BEngine.Touch[] Touches, BEngine.Vector2 MousePosition, bool MouseHeld)
    {
        internal bool HasEdge => Touches.Any(touch => touch.phase is TouchPhase.Began or TouchPhase.Ended or TouchPhase.Canceled);
    }

    private void OnGenericMotion(object? sender, View.GenericMotionEventArgs args)
    {
        var e = args.Event;
        if (e is null || (e.Source & InputSourceType.Joystick) == 0) return;
        var index = Math.Max(0, e.DeviceId);
        _connectedGamepads.Add(index);
        Input.SetGamepadConnected(index, true, InputDevice.GetDevice(e.DeviceId)?.Name ?? "Android gamepad");
        Input.SetGamepadAxis(index, GamepadAxis.LeftStickX, (Fix64)e.GetAxisValue(Axis.X));
        Input.SetGamepadAxis(index, GamepadAxis.LeftStickY, (Fix64)(-e.GetAxisValue(Axis.Y)));
        Input.SetGamepadAxis(index, GamepadAxis.RightStickX, (Fix64)e.GetAxisValue(Axis.Z));
        Input.SetGamepadAxis(index, GamepadAxis.RightStickY, (Fix64)(-e.GetAxisValue(Axis.Rz)));
        Input.SetGamepadAxis(index, GamepadAxis.LeftTrigger, (Fix64)e.GetAxisValue(Axis.Ltrigger));
        Input.SetGamepadAxis(index, GamepadAxis.RightTrigger, (Fix64)e.GetAxisValue(Axis.Rtrigger));
        args.Handled = true;
    }

    private void PollGamepads()
    {
        var current = new HashSet<int>();
        foreach (var deviceId in InputDevice.GetDeviceIds() ?? [])
        {
            var device = InputDevice.GetDevice(deviceId);
            if (device is null || (device.Sources & (InputSourceType.Gamepad | InputSourceType.Joystick)) == 0) continue;
            var index = Math.Max(0, deviceId);
            current.Add(index);
            _connectedGamepads.Add(index);
            Input.SetGamepadConnected(index, true, device.Name ?? "Android gamepad");
        }
        foreach (var disconnected in _connectedGamepads.Except(current).ToArray())
        {
            Input.SetGamepadConnected(disconnected, false);
            _connectedGamepads.Remove(disconnected);
        }
    }

    private static bool TryMapKey(Keycode code, out KeyCode key)
    {
        if (code is >= Keycode.A and <= Keycode.Z)
        {
            key = (KeyCode)((int)KeyCode.A + (int)(code - Keycode.A));
            return true;
        }
        if (code is >= Keycode.Num0 and <= Keycode.Num9)
        {
            key = (KeyCode)((int)KeyCode.Alpha0 + (int)(code - Keycode.Num0));
            return true;
        }
        key = code switch
        {
            Keycode.Space => KeyCode.Space,
            Keycode.Enter => KeyCode.Return,
            Keycode.Escape or Keycode.Back => KeyCode.Escape,
            Keycode.Tab => KeyCode.Tab,
            Keycode.Del => KeyCode.Backspace,
            Keycode.ForwardDel => KeyCode.Delete,
            Keycode.DpadUp => KeyCode.UpArrow,
            Keycode.DpadDown => KeyCode.DownArrow,
            Keycode.DpadLeft => KeyCode.LeftArrow,
            Keycode.DpadRight => KeyCode.RightArrow,
            _ => KeyCode.None
        };
        return key != KeyCode.None;
    }

    private static bool TryMapGamepadButton(Keycode code, out GamepadButton button)
    {
        button = code switch
        {
            Keycode.ButtonA => GamepadButton.South,
            Keycode.ButtonB => GamepadButton.East,
            Keycode.ButtonX => GamepadButton.West,
            Keycode.ButtonY => GamepadButton.North,
            Keycode.ButtonL1 => GamepadButton.LeftShoulder,
            Keycode.ButtonR1 => GamepadButton.RightShoulder,
            Keycode.ButtonSelect => GamepadButton.Back,
            Keycode.ButtonStart => GamepadButton.Start,
            Keycode.ButtonMode => GamepadButton.Home,
            Keycode.ButtonThumbl => GamepadButton.LeftStick,
            Keycode.ButtonThumbr => GamepadButton.RightStick,
            Keycode.DpadUp => GamepadButton.DPadUp,
            Keycode.DpadRight => GamepadButton.DPadRight,
            Keycode.DpadDown => GamepadButton.DPadDown,
            Keycode.DpadLeft => GamepadButton.DPadLeft,
            _ => (GamepadButton)(-1)
        };
        return (int)button >= 0;
    }

}
