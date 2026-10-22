namespace BEngine.InputActions;

public sealed partial class InputActionSession
{
    private static readonly Dictionary<int, WeakReference<InputActionSession>> ExclusiveGamepads = [];
    private bool _exclusivePairing, _padWasConnected;
    private readonly HashSet<InputAction> _deviceCanceled = [];
    public bool hasExclusiveGamepad => _exclusivePairing;
    public bool isGamepadAvailable => CanReadGamepad;
    internal bool CanReadGamepad => _gamepadIndex >= 0 && Input.IsGamepadConnected(_gamepadIndex) &&
        (OwnerOf(_gamepadIndex) is not { } owner || ReferenceEquals(owner, this));
    public event Action<InputActionSession>? deviceLost;
    public event Action<InputActionSession>? deviceRegained;
    public event Action<InputActionSession>? pairingChanged;

    /// <summary>Pairs a host slot. Exclusive pairing reserves it across disconnect/reconnect until released.</summary>
    public void PairGamepad(int index, bool exclusive = true)
    {
        EnsureAlive();
        if (index < -1) throw new ArgumentOutOfRangeException(nameof(index));
        exclusive &= index >= 0;
        if (index >= 0 && OwnerOf(index) is { } owner && !ReferenceEquals(owner, this))
            throw new InvalidOperationException($"Gamepad slot {index} is exclusively paired to another input session.");
        if (_gamepadIndex == index && _exclusivePairing == exclusive) return;
        // Do not release the old reservation until the replacement has been checked.
        ReleasePairing();
        _gamepadIndex = index; _exclusivePairing = exclusive;
        if (exclusive) ExclusiveGamepads[index] = new(this);
        _padWasConnected = CanReadGamepad;
        _rebind?.Cancel(); CancelActions(); InvokeDeviceEvent(pairingChanged);
    }

    public bool TryPairNextAvailableGamepad(bool exclusive = true)
    {
        EnsureAlive();
        foreach (var gamepad in Input.GetGamepads().Where(gamepad => gamepad.connected).OrderBy(gamepad => gamepad.index))
            if (OwnerOf(gamepad.index) is not { } owner || ReferenceEquals(owner, this))
            { PairGamepad(gamepad.index, exclusive); return true; }
        return false;
    }

    public void UnpairGamepad() => PairGamepad(-1, false);

    private void PollPairedDevice()
    {
        var available = CanReadGamepad;
        if (_padWasConnected == available) return;
        _padWasConnected = available;
        _rebind?.Cancel();
        if (!available)
            foreach (var action in _actions.Values)
                if (action.bindings.Length > 0 && action.bindings.All(original =>
                    _overrides.GetValueOrDefault(original.id, original).control is InputControlKind.GamepadButton or InputControlKind.GamepadAxis or InputControlKind.GamepadStick))
                { action.Cancel(); _deviceCanceled.Add(action); }
        InvokeDeviceEvent(available ? deviceRegained : deviceLost);
    }
    private void ReleasePairing()
    {
        if (OwnerOf(_gamepadIndex) is { } owner && ReferenceEquals(owner, this)) ExclusiveGamepads.Remove(_gamepadIndex);
        _exclusivePairing = false;
    }
    private static InputActionSession? OwnerOf(int index)
    {
        if (ExclusiveGamepads.TryGetValue(index, out var weak))
        {
            if (weak.TryGetTarget(out var owner) && !owner._disposed) return owner;
            ExclusiveGamepads.Remove(index);
        }
        return null;
    }
    private void InvokeDeviceEvent(Action<InputActionSession>? callbacks)
    {
        foreach (var callback in Delegate.EnumerateInvocationList(callbacks))
            try { callback(this); } catch (Exception error) { Debug.LogException(error); }
    }
}
