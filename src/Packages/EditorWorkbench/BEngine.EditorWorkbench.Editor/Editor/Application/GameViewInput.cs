namespace BEngine.Editor;

internal sealed class GameViewInput
{
    private readonly HashSet<KeyCode> _held = [];
    private readonly HashSet<int> _touches = [];
    private bool _hasPointer;

    public bool Forward(Event current, Rect viewport, int width, int height, bool focused)
    {
        if (viewport.width <= 0 || viewport.height <= 0) return false;
        if (focused && current.type == EventType.Repaint && Input.textInputRect is { } caret)
            GUI.SetTextInputRect(new Rect(viewport.x + caret.x * viewport.width / Math.Max(1, width),
                viewport.y + caret.y * viewport.height / Math.Max(1, height),
                caret.width * viewport.width / Math.Max(1, width), caret.height * viewport.height / Math.Max(1, height)));
        var inside = viewport.Contains(current.mousePosition);
        if (current.type is EventType.TouchDown or EventType.TouchMove or EventType.TouchUp)
        {
            var id = current.pointerId;
            if (id < 0 || current.type == EventType.TouchDown && !inside || current.type != EventType.TouchDown && !_touches.Contains(id)) return false;
            var position = new Vector2((current.mousePosition.x - viewport.x) * width / viewport.width,
                height - (current.mousePosition.y - viewport.y) * height / viewport.height);
            var delta = new Vector2(current.delta.x * width / viewport.width, -current.delta.y * height / viewport.height);
            var phase = current.pointerCanceled ? TouchPhase.Canceled : current.type switch
            { EventType.TouchDown => TouchPhase.Began, EventType.TouchUp => TouchPhase.Ended, _ => delta == Vector2.zero ? TouchPhase.Stationary : TouchPhase.Moved };
            if (phase is TouchPhase.Ended or TouchPhase.Canceled) _touches.Remove(id); else _touches.Add(id);
            var snapshot = Input.touches.ToDictionary(touch => touch.fingerId);
            snapshot[id] = new(id, position, delta, Time.unscaledDeltaTime, Math.Max(1, current.clickCount), phase,
                Fix64.Max(0, current.pressure), 1);
            Input.SetTouches(snapshot.Values);
            return true;
        }
        // Native touch reports are forwarded above; Windows also promotes the primary contact to mouse.
        if (current.pointerType == PointerType.Touch && current.isMouse) return false;
        if (current.isMouse || current.isScrollWheel)
        {
            if (!inside && _held.Count == 0) return false;
            _hasPointer = true;
            Input.SetMousePosition((current.mousePosition.x - viewport.x) * width / viewport.width,
                height - (current.mousePosition.y - viewport.y) * height / viewport.height);
            if (current.type == EventType.MouseDown && inside)
                SetKey((KeyCode)((int)KeyCode.Mouse0 + current.button), true);
            else if (current.type == EventType.MouseUp)
                SetKey((KeyCode)((int)KeyCode.Mouse0 + current.button), false);
            else if (current.type == EventType.ScrollWheel && inside)
                Input.SetMouseScroll(-current.delta.x, -current.delta.y);
            else if (current.type is EventType.MouseMove or EventType.MouseDrag)
                Input.SetMouseDelta(current.delta.x * width / viewport.width,
                    -current.delta.y * height / viewport.height);
            else return false;
            return true;
        }
        if (!focused || !current.isKey) return false;
        // Leave lifecycle shortcuts for the host's existing global handler after the Game view.
        // Key releases still reach the game so a previously held key cannot remain stuck.
        if (current.type == EventType.KeyDown && !EditorGUIUtility.editingTextField &&
            EditorShortcutMap.Resolve(current.keyCode, current.modifiers) is
                EditorShortcut.Play or EditorShortcut.Pause or EditorShortcut.Step) return false;
        SetKey(current.keyCode, current.type == EventType.KeyDown);
        if (current.type == EventType.KeyDown) Input.AppendTextInput(current.character);
        return true;
    }

    public void Release()
    {
        if (_touches.Count > 0)
            Input.SetTouches(Input.touches.Select(touch => _touches.Contains(touch.fingerId)
                ? touch with { phase = TouchPhase.Canceled, deltaPosition = Vector2.zero, pressure = 0 } : touch));
        _touches.Clear();
        foreach (var key in _held) Input.SetKeyState(key, false);
        _held.Clear();
        if (_hasPointer) Input.SetMousePosition(-1, -1);
        _hasPointer = false;
    }

    private void SetKey(KeyCode key, bool down)
    {
        if (key == KeyCode.None) return;
        if (down) _held.Add(key);
        else _held.Remove(key);
        Input.SetKeyState(key, down);
    }
}
