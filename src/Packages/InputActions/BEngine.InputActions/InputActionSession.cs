namespace BEngine.InputActions;

/// <summary>Independent action state and overrides for one player; never changes the authored asset.</summary>
public sealed partial class InputActionSession : IDisposable
{
    private readonly Dictionary<string, InputAction> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InputBinding> _overrides = new(StringComparer.Ordinal);
    private readonly HashSet<string> _enabledMaps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownMaps = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _updating;
    private string _scheme = string.Empty;
    private InputRebindingOperation? _rebind;
    private int _gamepadIndex;
    private bool _useKeyboard = true, _useTouch = true;
    public int gamepadIndex { get => _gamepadIndex; set => PairGamepad(value, exclusive: false); }
    public bool useKeyboard
    {
        get => _useKeyboard;
        set { EnsureAlive(); if (_useKeyboard == value) return; _useKeyboard = value; _rebind?.Cancel(); CancelActions(); }
    }
    public bool useTouch
    {
        get => _useTouch;
        set { EnsureAlive(); if (_useTouch == value) return; _useTouch = value; touchInput.Reset(); CancelActions(); }
    }
    public TouchGestureTracker touchInput { get; }
    public string scheme
    {
        get => _scheme;
        set { EnsureAlive(); if (_scheme == value) return; _scheme = value ?? string.Empty; CancelActions(); }
    }
    public IEnumerable<InputAction> actions => _actions.Values;

    public InputActionSession(InputActionAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset); asset.Validate();
        touchInput = new(asset.touchGestures);
        _padWasConnected = CanReadGamepad;
        foreach (var map in asset.maps)
        {
            _knownMaps.Add(map.name);
            foreach (var action in map.actions)
                _actions.Add(map.name + "/" + action.name, new InputAction(map.name, action));
        }
    }

    public InputAction FindAction(string path) => _actions.TryGetValue(path, out var action)
        ? action : throw new KeyNotFoundException($"Unknown input action '{path}'.");
    public void EnableMap(string name)
    {
        EnsureAlive();
        if (!_knownMaps.Contains(name)) throw new KeyNotFoundException($"Unknown input map '{name}'.");
        _enabledMaps.Add(name);
    }
    public void DisableMap(string name)
    {
        EnsureAlive(); _enabledMaps.Remove(name);
        foreach (var action in _actions.Values) if (action.map == name) action.Cancel();
    }
    public void SwitchMap(string name)
    {
        EnsureAlive();
        if (!_knownMaps.Contains(name)) throw new KeyNotFoundException($"Unknown input map '{name}'.");
        _enabledMaps.Clear(); CancelActions(); _enabledMaps.Add(name);
    }

    public void Update() => Update(Time.unscaledDeltaTime);

    /// <summary>Samples input using elapsed unscaled seconds; useful for deterministic replay and tests.</summary>
    public void Update(Fix64 deltaTime)
    {
        EnsureAlive();
        if (deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (_updating) throw new InvalidOperationException("Input action updates cannot be reentrant.");
        _updating = true;
        using var budget = LoopExecutionGuard.AllowIterations(_actions.Count + 1);
        try
        {
            foreach (var action in _actions.Values) action.BeginFrame();
            _deviceCanceled.Clear();
            PollPairedDevice();
            if (_disposed) return;
            touchInput.Update(useTouch ? Input.touches : [], deltaTime);
            var rebinding = _rebind;
            rebinding?.Poll();
            foreach (var action in _actions.Values)
            {
                if (_disposed) break;
                if (_deviceCanceled.Contains(action)) continue;
                if (!_enabledMaps.Contains(action.map) || ReferenceEquals(rebinding?.action, action)) { action.Cancel(); continue; }
                var value = Vector2.zero;
                foreach (var original in action.bindings)
                {
                    var binding = _overrides.GetValueOrDefault(original.id, original);
                    if (binding.scheme.Length > 0 && _scheme.Length > 0 && binding.scheme != _scheme) continue;
                    var candidate = Read(binding);
                    if (candidate.sqrMagnitude > value.sqrMagnitude) value = candidate;
                }
                if (action.type != InputActionType.Vector2) value = new Vector2(value.x, Fix64.Zero);
                if (action.type == InputActionType.Button) value = new Vector2(Fix64.Abs(value.x) >= Fix64.Half ? Fix64.One : Fix64.Zero, 0);
                action.Sample(value, deltaTime);
            }
        }
        finally { _updating = false; }
    }

    private Vector2 Read(InputBinding binding)
    {
        Fix64 Key(KeyCode key) => useKeyboard && key != KeyCode.None && Input.GetKey(key) ? Fix64.One : Fix64.Zero;
        var pad = CanReadGamepad;
        var touch = touchInput.GetTouch(binding.fingerId);
        Vector2 Gesture(TouchGestureKind kind) => touchInput.gestures
            .Where(gesture => gesture.kind == kind && (binding.fingerId < 0 || gesture.fingerId == binding.fingerId))
            .Select(gesture => kind == TouchGestureKind.Swipe ? gesture.displacement : Vector2.right).FirstOrDefault();
        if (useTouch && binding.control is >= InputControlKind.TouchPosition and <= InputControlKind.TouchRotate)
        {
            var raw = binding.control switch
            {
                InputControlKind.TouchPosition => touch is { phase: not (TouchPhase.Ended or TouchPhase.Canceled) } ? touch.Value.position : Vector2.zero,
                InputControlKind.TouchDelta => touch?.deltaPosition ?? Vector2.zero,
                InputControlKind.TouchTap => Gesture(TouchGestureKind.Tap),
                InputControlKind.TouchLongPress => Gesture(TouchGestureKind.LongPress),
                InputControlKind.TouchSwipe => Gesture(TouchGestureKind.Swipe),
                InputControlKind.TouchPan => touchInput.panDelta,
                InputControlKind.TouchPinch => new(touchInput.pinchDelta, 0),
                InputControlKind.TouchRotate => new(touchInput.rotationDelta, 0),
                _ => Vector2.zero
            };
            return binding.control != InputControlKind.TouchPosition && raw.magnitude <= binding.deadZone ? Vector2.zero : raw * binding.scale;
        }
        var value = binding.control switch
        {
            InputControlKind.Key => new Vector2(Key(binding.key), 0),
            InputControlKind.KeyAxis => new Vector2(Key(binding.key) - Key(binding.negative), 0),
            InputControlKind.KeyVector2 => new Vector2(Key(binding.key) - Key(binding.negative), Key(binding.up) - Key(binding.down)),
            InputControlKind.GamepadButton => new Vector2(pad && Input.GetGamepadButton(gamepadIndex, binding.button) ? Fix64.One : Fix64.Zero, 0),
            InputControlKind.GamepadAxis => new Vector2(pad ? Input.GetGamepadAxis(gamepadIndex, binding.axis) : Fix64.Zero, 0),
            InputControlKind.GamepadStick => pad ? new Vector2(Input.GetGamepadAxis(gamepadIndex, binding.axis), Input.GetGamepadAxis(gamepadIndex, binding.axisY)) : Vector2.zero,
            InputControlKind.TouchPress => new Vector2(useTouch && touchInput.activeTouchCount >= binding.requiredTouches &&
                (binding.fingerId < 0 || touch is { phase: not (TouchPhase.Ended or TouchPhase.Canceled) }) ? Fix64.One : Fix64.Zero, 0),
            _ => Vector2.zero
        };
        var magnitude = value.magnitude;
        if (magnitude <= binding.deadZone) return Vector2.zero;
        var strength = Mathf.Clamp01((magnitude - binding.deadZone) / (Fix64.One - binding.deadZone));
        return value.normalized * strength * binding.scale;
    }

    public InputBinding GetBinding(string actionPath, int index)
    {
        var original = FindAction(actionPath).bindings[index];
        return _overrides.GetValueOrDefault(original.id, original).Copy();
    }
    public void ApplyBindingOverride(string actionPath, int index, InputBinding value)
    {
        EnsureAlive(); ArgumentNullException.ThrowIfNull(value);
        var action = FindAction(actionPath); var original = action.bindings[index];
        var copy = value.Copy(); copy.id = original.id; copy.Validate();
        _overrides[original.id] = copy; action.Cancel();
    }
    public string SaveBindingOverrides() => YamlUtility.Serialize(new BindingOverrideDocument { bindings = _overrides.Values.Select(InputBindingOverride.FromBinding).ToList() });
    public void LoadBindingOverrides(string yaml)
    {
        EnsureAlive();
        var document = YamlUtility.Deserialize<BindingOverrideDocument>(yaml, strict: true);
        if (document.format != "BEngine.InputBindings" || document.version is not (1 or 2)) throw new InvalidDataException("Unsupported binding override document.");
        var known = _actions.Values.SelectMany(a => a.bindings).Select(b => b.id).ToHashSet(StringComparer.Ordinal);
        var pending = new Dictionary<string, InputBinding>(StringComparer.Ordinal);
        if (document.bindings is null || document.bindings.Count > known.Count || document.bindings.Any(binding => binding is null))
            throw new InvalidDataException("Invalid binding override entries.");
        using var budget = LoopExecutionGuard.AllowIterations(document.bindings.Count + 1);
        foreach (var saved in document.bindings)
        {
            var binding = saved.ToBinding();
            binding.Validate();
            if (!known.Contains(binding.id) || !pending.TryAdd(binding.id, binding.Copy())) throw new InvalidDataException("Unknown or duplicate binding ID: " + binding.id);
        }
        _overrides.Clear(); foreach (var pair in pending) _overrides.Add(pair.Key, pair.Value);
        CancelActions();
    }
    public void ResetBindingOverrides() { EnsureAlive(); _overrides.Clear(); CancelActions(); }
    public InputRebindingOperation BeginRebind(string actionPath, int bindingIndex)
    {
        EnsureAlive();
        var action = FindAction(actionPath); _ = action.bindings[bindingIndex];
        _rebind?.Cancel(); action.Cancel();
        return _rebind = new InputRebindingOperation(this, action, bindingIndex);
    }
    internal void EndRebind(InputRebindingOperation operation) { if (ReferenceEquals(_rebind, operation)) _rebind = null; }
    private void CancelActions() { foreach (var action in _actions.Values) action.Cancel(); }
    private void EnsureAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _rebind?.Cancel(); CancelActions();
        ReleasePairing(); touchInput.Reset();
        deviceLost = null; deviceRegained = null; pairingChanged = null;
        foreach (var action in _actions.Values) action.ClearCallbacks();
        _enabledMaps.Clear(); _overrides.Clear();
    }
}

public sealed class BindingOverrideDocument
{
    public string format { get; set; } = "BEngine.InputBindings";
    public int version { get; set; } = 2;
    public List<InputBindingOverride> bindings { get; set; } = [];
}

public sealed class InputBindingOverride
{
    public string id { get; set; } = string.Empty;
    public string scheme { get; set; } = string.Empty;
    public InputControlKind control { get; set; }
    public KeyCode key { get; set; }
    public KeyCode negative { get; set; }
    public KeyCode up { get; set; }
    public KeyCode down { get; set; }
    public GamepadButton button { get; set; }
    public GamepadAxis axis { get; set; }
    public GamepadAxis axisY { get; set; }
    public long deadZoneRaw { get; set; }
    public long scaleRaw { get; set; }
    public int fingerId { get; set; } = -1;
    public int requiredTouches { get; set; } = 1;
    internal static InputBindingOverride FromBinding(InputBinding binding) => new()
    {
        id = binding.id, scheme = binding.scheme, control = binding.control, key = binding.key,
        negative = binding.negative, up = binding.up, down = binding.down, button = binding.button,
        axis = binding.axis, axisY = binding.axisY, deadZoneRaw = binding.deadZone.RawValue, scaleRaw = binding.scale.RawValue,
        fingerId = binding.fingerId, requiredTouches = binding.requiredTouches
    };
    internal InputBinding ToBinding() => new()
    {
        id = id, scheme = scheme, control = control, key = key, negative = negative, up = up,
        down = down, button = button, axis = axis, axisY = axisY,
        deadZone = Fix64.FromRaw(deadZoneRaw), scale = Fix64.FromRaw(scaleRaw), fingerId = fingerId, requiredTouches = requiredTouches
    };
}

public sealed class InputAction
{
    private readonly Fix64 _holdDuration, _tapDuration, _tapInterval;
    private readonly int _tapCount;
    private Fix64 _elapsed;
    private int _taps, _cancellationVersion;
    private bool _timedOut;
    internal InputBinding[] bindings { get; }
    public string map { get; }
    public string name { get; }
    public string path => map + "/" + name;
    public InputActionType type { get; }
    public InputInteraction interaction { get; }
    public InputActionPhase phase { get; private set; }
    public Vector2 value { get; private set; }
    public bool isPressed => value.sqrMagnitude > Fix64.Zero;
    public bool wasPressedThisFrame { get; private set; }
    public bool wasReleasedThisFrame { get; private set; }
    public bool wasPerformedThisFrame { get; private set; }
    public event Action<InputAction>? started;
    public event Action<InputAction>? performed;
    public event Action<InputAction>? canceled;
    internal InputAction(string map, InputActionDefinition definition)
    {
        this.map = map; name = definition.name; type = definition.type; interaction = definition.interaction;
        bindings = definition.bindings.Select(binding => binding.Copy()).ToArray();
        _holdDuration = definition.holdDuration; _tapDuration = definition.tapDuration;
        _tapInterval = definition.tapInterval; _tapCount = definition.tapCount;
    }
    internal void BeginFrame()
    {
        wasPressedThisFrame = wasReleasedThisFrame = wasPerformedThisFrame = false;
        if (phase == InputActionPhase.Canceled || (!isPressed && phase == InputActionPhase.Performed)) phase = InputActionPhase.Waiting;
    }
    internal void Sample(Vector2 next, Fix64 deltaTime)
    {
        var previous = value; var wasPressed = isPressed; value = next;
        var pressed = isPressed;
        wasPressedThisFrame = !wasPressed && pressed;
        wasReleasedThisFrame = wasPressed && !pressed;
        if (interaction == InputInteraction.Press)
        {
            if (previous == next) return;
            var version = _cancellationVersion;
            if (wasPressedThisFrame) Start();
            if (version != _cancellationVersion) return;
            if (wasReleasedThisFrame) Cancel();
            else if (pressed) Perform();
            return;
        }

        // A failed tap must be released before a new interaction can start.
        if (_timedOut) { if (!pressed) _timedOut = false; return; }
        if (phase == InputActionPhase.Started || phase == InputActionPhase.Performed) _elapsed += deltaTime;
        if (interaction == InputInteraction.MultiTap && !wasPressed && _taps > 0 && _elapsed > _tapInterval)
        {
            CancelInteraction();
            if (!pressed) return;
        }
        if (wasPressedThisFrame)
        {
            _elapsed = 0;
            var version = _cancellationVersion;
            if (_taps == 0) Start();
            if (version != _cancellationVersion) return;
        }
        if (interaction == InputInteraction.Hold)
        {
            if (pressed && phase == InputActionPhase.Started && _elapsed >= _holdDuration) Perform();
            if (wasReleasedThisFrame) Cancel();
            return;
        }
        if ((pressed || wasReleasedThisFrame) && _elapsed > _tapDuration)
        {
            CancelInteraction(); _timedOut = pressed; return;
        }
        if (wasReleasedThisFrame && phase == InputActionPhase.Started)
        {
            _elapsed = 0; _taps++;
            if (interaction == InputInteraction.Tap || _taps >= _tapCount)
            { _taps = 0; Perform(); }
        }
    }
    private void Start() { phase = InputActionPhase.Started; Invoke(started); }
    private void Perform() { phase = InputActionPhase.Performed; wasPerformedThisFrame = true; Invoke(performed); }
    private void CancelInteraction()
    {
        var active = phase is InputActionPhase.Started or InputActionPhase.Performed;
        _elapsed = 0; _taps = 0;
        phase = active ? InputActionPhase.Canceled : InputActionPhase.Waiting;
        if (active) Invoke(canceled);
    }
    internal void Cancel()
    {
        _cancellationVersion++;
        if (isPressed) wasReleasedThisFrame = true;
        value = Vector2.zero; _timedOut = false;
        CancelInteraction();
    }
    private void Invoke(Action<InputAction>? callbacks)
    { foreach (var callback in Delegate.EnumerateInvocationList(callbacks)) try { callback(this); } catch (Exception error) { Debug.LogException(error); } }
    internal void ClearCallbacks() { started = null; performed = null; canceled = null; }
}
