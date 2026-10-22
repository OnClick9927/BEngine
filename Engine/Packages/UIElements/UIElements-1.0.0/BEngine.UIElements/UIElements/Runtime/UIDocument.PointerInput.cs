using System.Buffers;

namespace BEngine.UIElements;

public sealed partial class UIDocument
{
    private readonly Dictionary<int, PointerInteraction> _pointers = [];
    private Vector2? _lastMousePosition;
    private long _pointerVersion;
    private long _validatedPointerVersion = -1, _validatedLayoutVersion = -1;
    private VisualElement? _validatedPointerRoot;
    private bool _validatedInteractable, _validatedVisible;
    public bool DispatchPointerDown(Vector2 position, int viewportWidth, int viewportHeight) =>
        DispatchPointerDown(position, viewportWidth, viewportHeight, -1, UIPointerType.Mouse);
    public bool DispatchPointerDown(Vector2 position, int viewportWidth, int viewportHeight,
        int pointerId, UIPointerType pointerType, Fix64 pressure = default)
    {
        ValidatePointer(pointerId, pointerType);
        if (!interactable || !isVisible) return false;
        CancelPointer(pointerId);
        using var list = BuildInputList(viewportWidth, viewportHeight); var picked = list.Pick(position);
        bool observesHover = pointerType == UIPointerType.Mouse && hoveredElementChanged is not null;
        if (pointerType == UIPointerType.Mouse) SetHovered(picked);
        // A hover observer can cancel input, replace the root, or start its own
        // pointer interaction. Do not focus/press a stale target afterwards.
        if (observesHover)
        {
            if (!interactable || !isVisible || picked is not null && !IsAvailable(picked)) return false;
            if (_pointers.ContainsKey(pointerId)) return true;
        }
        var control = FindControl(picked);
        FocusFromPointer(control, position, list, viewportWidth, viewportHeight);
        if (picked is null) return false;
        var state = new PointerInteraction { Id = pointerId, Kind = pointerType, Target = picked, Position = position, Start = position, Pressure = pressure };
        _pointers.Add(pointerId, state);
        _pointerVersion++;
        var evt = new PointerDownEvent { position = position, pointerId = pointerId, pointerType = pointerType, pressure = pressure, button = 0, clickCount = 1 };
        picked.SendEvent(evt);
        if (!IsCurrent(state)) return true;
        if (!IsAvailable(picked) || !interactable || !isVisible) { CancelPointer(pointerId); return true; }
        if (evt.propagationStopped) return true;
        if (control is not null && IsAvailable(control))
        {
            state.Pressed = control; state.PressInside = true; RefreshPressed(control);
            if (control is Slider { isReadOnly: false } slider && !_pointers.Values.Any(other => !ReferenceEquals(other, state) && ReferenceEquals(other.Slider, slider)))
            { state.Slider = slider; UpdateSlider(slider, position, list); }
        }
        if (pointerType == UIPointerType.Touch && state.Slider is null)
            for (var ancestor = picked; ancestor is not null; ancestor = ancestor.parent)
                if (ancestor is ScrollView scroll)
                {
                    if (!_pointers.Values.Any(other => ReferenceEquals(other.Scroll, scroll))) state.Scroll = scroll;
                    break;
                }
        // Controls capture their drag; custom controls may select another capture target in the callback.
        if (state.Captured is null && control is not null)
        { state.Captured = control; _pointerVersion++; }
        return true;
    }

    public void DispatchPointerMove(Vector2 position, int viewportWidth, int viewportHeight) =>
        DispatchPointerMove(position, viewportWidth, viewportHeight, -1, UIPointerType.Mouse);
    public void DispatchPointerMove(Vector2 position, int viewportWidth, int viewportHeight,
        int pointerId, UIPointerType pointerType, Fix64 pressure = default)
    {
        ValidatePointer(pointerId, pointerType);
        if (!interactable || !isVisible) { CancelInteraction(); return; }
        ValidatePointerState();
        _pointers.TryGetValue(pointerId, out var state);
        if (state is null && pointerType != UIPointerType.Mouse) return;
        using var list = BuildInputList(viewportWidth, viewportHeight); var picked = list.Pick(position);
        bool observesHover = pointerType == UIPointerType.Mouse && hoveredElementChanged is not null;
        if (pointerType == UIPointerType.Mouse && !observesHover) SetHovered(picked);
        var previous = state?.Position ?? _lastMousePosition ?? position;
        if (pointerType == UIPointerType.Mouse) _lastMousePosition = position;
        if (state is not null) { state.Position = position; state.Pressure = pressure; }
        // Commit the position before notifying hover observers. A nested move
        // must remain the latest position rather than being overwritten here.
        if (observesHover)
        {
            SetHovered(picked);
            if (!interactable || !isVisible || picked is not null && !IsAvailable(picked) || state is not null && !IsCurrent(state)) return;
        }
        var target = state?.Captured ?? picked;
        PointerMoveEvent? evt = null;
        if (target is not null)
        {
            // Moves have no default event action. Keep creating distinct events
            // for every callback/diagnostic observer; an unobserved route only
            // needs the document's pointer/hover/drag state below.
            if (UIEventDiagnostics.hasObservers || target.HasCallbacksOnPath<PointerMoveEvent>())
            {
                evt = new PointerMoveEvent { position = position, delta = position - previous, pointerId = pointerId, pointerType = pointerType, pressure = pressure };
                target.SendEvent(evt);
            }
            else UIEventDiagnostics.SkipUnobservedDispatch();
        }
        if (state is null || !IsCurrent(state) || evt?.propagationStopped == true) return;
        if (!interactable || !isVisible || !IsAvailable(state.Captured ?? state.Target)) { CancelPointer(pointerId); return; }
        if (state.Slider is { isReadOnly: false } slider && IsAvailable(slider)) UpdateSlider(slider, position, list);
        if (state.Pressed is TextField field && pointerType == UIPointerType.Mouse)
            FocusFromPointer(field, position, list, viewportWidth, viewportHeight, extendSelection: true);
        if (state.Scroll is { } scroll && IsAvailable(scroll))
        {
            var scale = ResolveScale(viewportWidth, viewportHeight);
            state.Scrolling |= Fix64.Abs(position.y - state.Start.y) > 8 * scale;
            if (state.Scrolling)
            {
                if (state.Pressed is { } pressed) { state.Pressed = null; RefreshPressed(pressed); }
                CapturePointer(pointerId, scroll);
                if (!IsCurrent(state)) return;
                ScrollBy(scroll, -(float)((position.y - previous.y) / Fix64.Max(scale, Fix64.FromDecimal(.01m))), list, scale);
            }
        }
        if (state.Pressed is { } element)
        { state.PressInside = IsAvailable(element) && ReferenceEquals(FindControl(picked), element); RefreshPressed(element); }
    }

    public bool DispatchPointerUp(Vector2 position, int viewportWidth, int viewportHeight) =>
        DispatchPointerUp(position, viewportWidth, viewportHeight, -1, UIPointerType.Mouse);
    public bool DispatchPointerUp(Vector2 position, int viewportWidth, int viewportHeight,
        int pointerId, UIPointerType pointerType, Fix64 pressure = default)
    {
        ValidatePointer(pointerId, pointerType); ValidatePointerState();
        if (!_pointers.TryGetValue(pointerId, out var state)) return false;
        if (!interactable || !isVisible) { CancelPointer(pointerId); return false; }
        using var list = BuildInputList(viewportWidth, viewportHeight);
        var picked = list.Pick(position);
        state.Position = position;
        var evt = new PointerUpEvent { position = position, pointerId = pointerId, pointerType = pointerType, pressure = pressure, button = 0 };
        (state.Captured ?? state.Target).SendEvent(evt);
        if (!IsCurrent(state)) return true;
        var pressed = state.Pressed;
        FinishPointer(state);
        if (evt.propagationStopped || !interactable || !isVisible || state.Scrolling || pressed is null || !IsAvailable(pressed) || !ReferenceEquals(FindControl(picked), pressed)) return false;
        switch (pressed)
        {
            case Button button: button.Click(); return true;
            case Toggle { isReadOnly: false } toggle: toggle.value = toggle is RadioButton || !toggle.value; return true;
            default: return false;
        }
    }

    public void CancelPointer(int pointerId)
    {
        if (!_pointers.TryGetValue(pointerId, out var state)) return;
        var target = state.Captured ?? state.Target;
        FinishPointer(state);
        target.SendEvent(new PointerCancelEvent { pointerId = state.Id, pointerType = state.Kind, position = state.Position });
    }
    public void CapturePointer(int pointerId, VisualElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_pointers.TryGetValue(pointerId, out var state) || !IsAvailable(element)) throw new InvalidOperationException("Capture requires an active pointer and an available element in this document.");
        if (ReferenceEquals(state.Captured, element)) return;
        var previous = state.Captured; state.Captured = element;
        _pointerVersion++;
        previous?.SendEvent(new PointerCaptureLostEvent { pointerId = state.Id, pointerType = state.Kind, position = state.Position });
    }
    public void ReleasePointer(int pointerId, VisualElement element)
    {
        if (!_pointers.TryGetValue(pointerId, out var state) || !ReferenceEquals(state.Captured, element)) return;
        state.Captured = null;
        _pointerVersion++;
        element.SendEvent(new PointerCaptureLostEvent { pointerId = state.Id, pointerType = state.Kind, position = state.Position });
    }
    public bool HasPointerCapture(int pointerId, VisualElement element) =>
        _pointers.TryGetValue(pointerId, out var state) && ReferenceEquals(state.Captured, element);
    internal void BlurKeyboardFocus() { SetFocused(null); FocusText(null); }
    internal void ValidatePointerState()
    {
        var root = _root;
        var layoutVersion = root?.layoutVersion ?? -1;
        var pointerVersion = _pointerVersion;
        var visible = isVisible;
        var acceptsInput = interactable;
        if (ReferenceEquals(_validatedPointerRoot, root) && _validatedLayoutVersion == layoutVersion &&
            _validatedPointerVersion == pointerVersion && _validatedInteractable == acceptsInput &&
            _validatedVisible == visible) return;
        var count = _pointers.Count;
        if (count > 0)
        {
            var states = ArrayPool<PointerInteraction>.Shared.Rent(count);
            _pointers.Values.CopyTo(states, 0);
            try
            {
                for (var index = 0; index < count; index++)
                {
                    var state = states[index];
                    if (IsCurrent(state) && (!interactable || !isVisible || !IsAvailable(state.Captured ?? state.Target)))
                        CancelPointer(state.Id);
                }
            }
            finally { ArrayPool<PointerInteraction>.Shared.Return(states, clearArray: true); }
        }
        if (_focused is not null && !IsAvailable(_focused)) BlurKeyboardFocus();
        // Save the versions observed before callbacks. Changes made while
        // canceling a pointer must be checked on the next dispatch as well.
        _validatedPointerRoot = root; _validatedLayoutVersion = layoutVersion;
        _validatedPointerVersion = pointerVersion; _validatedInteractable = acceptsInput;
        _validatedVisible = visible;
    }
    private void CancelPointers()
    {
        var count = _pointers.Count;
        if (count == 0) return;
        var states = ArrayPool<PointerInteraction>.Shared.Rent(count);
        _pointers.Values.CopyTo(states, 0);
        try
        {
            for (var index = 0; index < count; index++)
                if (IsCurrent(states[index])) CancelPointer(states[index].Id);
        }
        finally { ArrayPool<PointerInteraction>.Shared.Return(states, clearArray: true); }
    }
    private bool IsCurrent(PointerInteraction state) => _pointers.TryGetValue(state.Id, out var current) && ReferenceEquals(state, current);
    private void FinishPointer(PointerInteraction state)
    {
        if (_pointers.Remove(state.Id)) _pointerVersion++;
        if (state.Pressed is { } pressed) RefreshPressed(pressed);
        state.Captured?.SendEvent(new PointerCaptureLostEvent { pointerId = state.Id, pointerType = state.Kind, position = state.Position });
    }
    private void RefreshPressed(VisualElement element)
    {
        foreach (var state in _pointers.Values)
            if (ReferenceEquals(state.Pressed, element) && state.PressInside)
            { element.SetInteractionState(isPressed: true); return; }
        element.SetInteractionState(isPressed: false);
    }
    private static void ValidatePointer(int id, UIPointerType type)
    { if (id < -1 || !Enum.IsDefined(type) || type == UIPointerType.Mouse && id != -1 || type != UIPointerType.Mouse && id < 0) throw new ArgumentException("Mouse pointer ID is -1; contact IDs are non-negative."); }
    private static bool ScrollBy(ScrollView scroll, float delta, UIRenderListBuilder.RendererCommandLease list, Fix64 scale)
    {
        var maximum = list.GetMaximumScrollOffset(scroll, scale);
        var next = Math.Clamp(scroll.scrollOffset + delta, 0, maximum);
        if (next == scroll.scrollOffset) return false;
        scroll.scrollOffset = next; return true;
    }
    private sealed class PointerInteraction
    {
        internal int Id;
        internal UIPointerType Kind;
        internal VisualElement Target = null!;
        internal VisualElement? Captured, Pressed;
        internal Slider? Slider;
        internal ScrollView? Scroll;
        internal Vector2 Start, Position;
        internal Fix64 Pressure;
        internal bool PressInside, Scrolling;
    }
}
