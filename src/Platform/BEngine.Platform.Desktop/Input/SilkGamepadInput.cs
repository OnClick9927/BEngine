using Silk.NET.Input;

namespace BEngine.Platform;

/// <summary>Shared desktop gamepad collection for Player and editor native windows.</summary>
public sealed class SilkGamepadInput : IDisposable
{
    private readonly IInputContext _input;
    private readonly Func<bool> _enabled;
    private readonly HashSet<IGamepad> _registeredGamepads = [];
    private static readonly Type? ConnectedWrapper = typeof(IGamepad).Assembly.GetType("Silk.NET.Input.Internals.IsConnectedWrapper`1");
    private IReadOnlyList<IGamepad>? _gamepadCollection;
    private bool _indexConnectedSlots;
    private bool _disposed;
    public SilkGamepadInput(IInputContext input, Func<bool>? enabled = null)
    {
        _input = input; _enabled = enabled ?? (() => true);
        foreach (var gamepad in input.Gamepads) RegisterGamepad(gamepad);
        input.ConnectionChanged += OnInputConnectionChanged;
    }
    public void Poll() { if (!_disposed && _enabled()) PollGamepadInput(); }
    public void ReleaseInput()
    {
        foreach (var gamepad in _registeredGamepads)
        {
            foreach (var button in Enum.GetValues<GamepadButton>()) Input.SetGamepadButtonState(gamepad.Index, button, false);
            foreach (var axis in Enum.GetValues<GamepadAxis>()) Input.SetGamepadAxis(gamepad.Index, axis, 0);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_enabled()) ReleaseInput();
        _disposed = true; _input.ConnectionChanged -= OnInputConnectionChanged;
        foreach (var gamepad in _registeredGamepads)
        { gamepad.ButtonDown -= OnGamepadButtonDown; gamepad.ButtonUp -= OnGamepadButtonUp; }
        _registeredGamepads.Clear();
        _gamepadCollection = null;
    }

    private void RegisterGamepad(IGamepad gamepad)
    {
        if (!_registeredGamepads.Add(gamepad)) return;
        Input.SetGamepadConnected(gamepad.Index, gamepad.IsConnected, gamepad.Name);
        gamepad.ButtonDown += OnGamepadButtonDown;
        gamepad.ButtonUp += OnGamepadButtonUp;
    }

    private void OnInputConnectionChanged(IInputDevice device, bool connected)
    {
        if (device is not IGamepad gamepad) return;
        if (connected) RegisterGamepad(gamepad);
        Input.SetGamepadConnected(gamepad.Index, connected, gamepad.Name);
    }

    private void OnGamepadButtonDown(IGamepad gamepad, Button button)
    {
        if (_enabled() && gamepad.IsConnected && TryMap(button.Name, out var mapped))
            Input.SetGamepadButtonState(gamepad.Index, mapped, true);
    }

    private void OnGamepadButtonUp(IGamepad gamepad, Button button)
    {
        if (_enabled() && gamepad.IsConnected && TryMap(button.Name, out var mapped))
            Input.SetGamepadButtonState(gamepad.Index, mapped, false);
    }

    private void PollGamepadInput()
    {
        var gamepads = _input.Gamepads;
        if (!ReferenceEquals(_gamepadCollection, gamepads))
        {
            _gamepadCollection = gamepads;
            var type = gamepads.GetType();
            // Silk 2.x exposes all fixed native slots through Count/Item,
            // but its enumerator allocates and filters null/disconnected slots.
            // Only substitute this exact wrapper over an array. Custom lists,
            // including wrappers over mutable lists, keep their own enumerator.
            _indexConnectedSlots = type.IsGenericType && type.GetGenericTypeDefinition() == ConnectedWrapper &&
                type.GetField("_list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(gamepads) is Array;
        }
        if (_indexConnectedSlots)
        {
            for (var index = 0; index < gamepads.Count; index++)
            {
                var gamepad = gamepads[index];
                // Match IsConnectedEnumerator's live filter and second Current
                // read: a device getter may itself replace its native slot.
                if (gamepad is not null && gamepad.IsConnected) PollGamepad(gamepads[index]);
            }
            return;
        }
        foreach (var gamepad in gamepads) PollGamepad(gamepad);
    }

    private void PollGamepad(IGamepad gamepad)
    {
        RegisterGamepad(gamepad);
        Input.SetGamepadConnected(gamepad.Index, gamepad.IsConnected, gamepad.Name);
        if (!gamepad.IsConnected) return;
        foreach (var button in gamepad.Buttons)
            if (_enabled() && gamepad.IsConnected && TryMap(button.Name, out var mapped))
                Input.SetGamepadButtonState(gamepad.Index, mapped, button.Pressed);
        foreach (var stick in gamepad.Thumbsticks)
        {
            var x = stick.Index == 0 ? GamepadAxis.LeftStickX : GamepadAxis.RightStickX;
            var y = stick.Index == 0 ? GamepadAxis.LeftStickY : GamepadAxis.RightStickY;
            Input.SetGamepadAxis(gamepad.Index, x, (Fix64)stick.X);
            Input.SetGamepadAxis(gamepad.Index, y, (Fix64)stick.Y);
        }
        foreach (var trigger in gamepad.Triggers)
            Input.SetGamepadAxis(gamepad.Index,
                trigger.Index == 0 ? GamepadAxis.LeftTrigger : GamepadAxis.RightTrigger,
                (Fix64)trigger.Position);
    }

    private static bool TryMap(ButtonName button, out GamepadButton mapped)
    {
        mapped = button switch
        {
            ButtonName.A => GamepadButton.South,
            ButtonName.B => GamepadButton.East,
            ButtonName.X => GamepadButton.West,
            ButtonName.Y => GamepadButton.North,
            ButtonName.LeftBumper => GamepadButton.LeftShoulder,
            ButtonName.RightBumper => GamepadButton.RightShoulder,
            ButtonName.Back => GamepadButton.Back,
            ButtonName.Start => GamepadButton.Start,
            ButtonName.Home => GamepadButton.Home,
            ButtonName.LeftStick => GamepadButton.LeftStick,
            ButtonName.RightStick => GamepadButton.RightStick,
            ButtonName.DPadUp => GamepadButton.DPadUp,
            ButtonName.DPadRight => GamepadButton.DPadRight,
            ButtonName.DPadDown => GamepadButton.DPadDown,
            ButtonName.DPadLeft => GamepadButton.DPadLeft,
            _ => default
        };
        return button != ButtonName.Unknown;
    }
}
