namespace BEngine.InputActions;

public sealed class InputRebindingOperation : IDisposable
{
    private static readonly KeyCode[] Keys = Enum.GetValues<KeyCode>();
    private static readonly GamepadButton[] Buttons = Enum.GetValues<GamepadButton>();
    private readonly InputActionSession _session;
    private readonly int _bindingIndex;
    private readonly HashSet<KeyCode> _heldAtStart;
    private readonly HashSet<GamepadButton> _buttonsAtStart;
    internal InputAction action { get; }
    public bool isCompleted { get; private set; }
    public bool isCanceled { get; private set; }
    public KeyCode cancelKey { get; set; } = KeyCode.Escape;
    public event Action<InputRebindingOperation>? completed;
    internal InputRebindingOperation(InputActionSession session, InputAction action, int bindingIndex)
    {
        _session = session; this.action = action; _bindingIndex = bindingIndex;
        _heldAtStart = Keys.Where(Input.GetKey).ToHashSet();
        _buttonsAtStart = session.CanReadGamepad
            ? Buttons.Where(button => Input.GetGamepadButton(session.gamepadIndex, button)).ToHashSet() : [];
    }
    internal void Poll()
    {
        if (isCompleted) return;
        _heldAtStart.RemoveWhere(key => !Input.GetKey(key));
        if (_session.CanReadGamepad)
            _buttonsAtStart.RemoveWhere(button => !Input.GetGamepadButton(_session.gamepadIndex, button));
        if (cancelKey != KeyCode.None && Input.GetKeyDown(cancelKey)) { Cancel(); return; }
        if (_session.useKeyboard)
            foreach (var key in Keys)
                if (key != KeyCode.None && key != cancelKey && !_heldAtStart.Contains(key) && Input.GetKeyDown(key) && Input.GetKey(key))
                { var binding = _session.GetBinding(action.path, _bindingIndex); binding.control = InputControlKind.Key; binding.key = key; Complete(binding); return; }
        if (_session.CanReadGamepad)
            foreach (var button in Buttons)
                if (!_buttonsAtStart.Contains(button) && Input.GetGamepadButtonDown(_session.gamepadIndex, button) && Input.GetGamepadButton(_session.gamepadIndex, button))
                { var binding = _session.GetBinding(action.path, _bindingIndex); binding.control = InputControlKind.GamepadButton; binding.button = button; Complete(binding); return; }
    }
    /// <summary>Also accepts composite/stick bindings supplied by a custom rebind UI.</summary>
    public void Complete(InputBinding binding)
    {
        if (isCompleted) throw new InvalidOperationException("Rebinding already completed.");
        _session.ApplyBindingOverride(action.path, _bindingIndex, binding);
        Finish(false);
    }
    public void Cancel() { if (!isCompleted) Finish(true); }
    private void Finish(bool canceled)
    {
        isCanceled = canceled; isCompleted = true; _session.EndRebind(this);
        foreach (var callback in Delegate.EnumerateInvocationList(completed)) try { callback(this); } catch (Exception error) { Debug.LogException(error); }
        completed = null;
    }
    public void Dispose() => Cancel();
}
