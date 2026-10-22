namespace BEngine.InputActions;

[CreateAssetMenu(fileName = "Input Actions", menuName = "Input/Actions", order = 420)]
public sealed class InputActionAsset : ScriptableObject
{
    public List<InputActionMapDefinition> maps { get; set; } = [];
    public TouchGestureSettings touchGestures { get; set; } = new();
    public void Validate()
    {
        if (touchGestures is null) throw new InvalidDataException("Touch gesture settings are required.");
        touchGestures.Validate();
        if (maps is null || maps.Count > 256) throw new InvalidDataException("Invalid input maps.");
        var mapsSeen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in maps)
        {
            if (map is null || string.IsNullOrWhiteSpace(map.name) || map.name.Contains('/') || !mapsSeen.Add(map.name)) throw new InvalidDataException("Input map names must be non-empty, unique and contain no '/'.");
            if (map.actions is null || map.actions.Count > 1024) throw new InvalidDataException("Invalid input actions.");
            using var budget = LoopExecutionGuard.AllowIterations(map.actions.Count + 1);
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in map.actions)
            {
                if (action is null || string.IsNullOrWhiteSpace(action.name) || action.name.Contains('/') || !actions.Add(action.name)) throw new InvalidDataException("Input action names must be non-empty, unique within a map and contain no '/'.");
                if (!Enum.IsDefined(action.type)) throw new InvalidDataException("Invalid input action type.");
                if (!Enum.IsDefined(action.interaction) || (action.type != InputActionType.Button && action.interaction != InputInteraction.Press))
                    throw new InvalidDataException("Timed interactions require a Button action.");
                if (action.holdDuration <= 0 || action.tapDuration <= 0 || action.tapInterval <= 0 || action.tapCount is < 2 or > 16)
                    throw new InvalidDataException("Interaction durations must be positive; tap count must be between 2 and 16.");
                if (action.bindings is null || action.bindings.Count > 256 || action.bindings.Any(binding => binding is null)) throw new InvalidDataException("Invalid input bindings.");
                foreach (var binding in action.bindings)
                {
                    binding.Validate();
                    if (!ids.Add(binding.id)) throw new InvalidDataException($"Duplicate binding ID '{binding.id}'.");
                }
            }
        }
    }
}

public enum InputActionType { Button, Axis, Vector2 }
public enum InputInteraction { Press, Hold, Tap, MultiTap }
public enum InputActionPhase { Waiting, Started, Performed, Canceled }
public enum InputControlKind
{
    Key, KeyAxis, KeyVector2, GamepadButton, GamepadAxis, GamepadStick, TouchPress,
    TouchPosition, TouchDelta, TouchTap, TouchLongPress, TouchSwipe, TouchPan, TouchPinch, TouchRotate
}
public sealed class InputActionMapDefinition
{
    public string name { get; set; } = "Gameplay";
    public List<InputActionDefinition> actions { get; set; } = [];
}
public sealed class InputActionDefinition
{
    public string name { get; set; } = "Action";
    public InputActionType type { get; set; }
    public InputInteraction interaction { get; set; }
    public Fix64 holdDuration { get; set; } = Fix64.Half;
    public Fix64 tapDuration { get; set; } = Fix64.FromDecimal(0.25m);
    public Fix64 tapInterval { get; set; } = Fix64.FromDecimal(0.3m);
    public int tapCount { get; set; } = 2;
    public List<InputBinding> bindings { get; set; } = [];
}
public sealed class InputBinding
{
    public string id { get; set; } = Guid.NewGuid().ToString("N");
    public string scheme { get; set; } = string.Empty;
    public InputControlKind control { get; set; }
    public KeyCode key { get; set; } = KeyCode.Space;
    public KeyCode negative { get; set; } = KeyCode.A;
    public KeyCode up { get; set; } = KeyCode.W;
    public KeyCode down { get; set; } = KeyCode.S;
    public GamepadButton button { get; set; }
    public GamepadAxis axis { get; set; }
    public GamepadAxis axisY { get; set; } = GamepadAxis.LeftStickY;
    public Fix64 deadZone { get; set; } = Fix64.FromDecimal(0.15m);
    public Fix64 scale { get; set; } = Fix64.One;
    /// <summary>-1 uses the primary contact, or any completed gesture; otherwise an explicit host finger ID.</summary>
    public int fingerId { get; set; } = -1;
    public int requiredTouches { get; set; } = 1;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Input binding ID is empty.");
        if (scheme is null) throw new InvalidDataException("Input scheme must not be null.");
        if (!Enum.IsDefined(control) || !Enum.IsDefined(key) || !Enum.IsDefined(negative) || !Enum.IsDefined(up) ||
            !Enum.IsDefined(down) || !Enum.IsDefined(button) || !Enum.IsDefined(axis) || !Enum.IsDefined(axisY))
            throw new InvalidDataException("Invalid input control.");
        if (deadZone < Fix64.Zero || deadZone >= Fix64.One) throw new InvalidDataException("Input dead zone must be in [0,1).");
        if (fingerId < -1 || requiredTouches is < 1 or > 128) throw new InvalidDataException("Invalid touch finger ID or required contact count.");
    }
    internal InputBinding Copy() => (InputBinding)MemberwiseClone();
}
