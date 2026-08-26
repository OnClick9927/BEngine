namespace BEngine.Editor;

internal sealed class EditorWindowLayer
{
    private const int ResizeLeft = 1;
    private const int ResizeRight = 2;
    private const int ResizeTop = 4;
    private const int ResizeBottom = 8;
    private static readonly Fix64 BorderWidth = 1;
    private static readonly Fix64 ResizeBorder = 5;
    private readonly List<FloatingEditorWindow> _presentations = [];

    public event Action<EditorWindow>? WindowClosed;
    public event Action<EditorWindow, Vector2>? DockRequested;

    public IReadOnlyList<FloatingEditorWindow> Presentations => _presentations;
    public IReadOnlyList<FloatingEditorWindow> Windows => _presentations;
    public int Count => _presentations.Count;
    public bool HasModal => TopModalWindow is not null;
    public EditorWindow? TopModalWindow => FindTopModal()?.Window;

    public FloatingEditorWindow Show(EditorWindow window, EditorWindowState state)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (TryGet(window, out var existing))
        {
            if (state == EditorWindowState.Modal)
                foreach (var current in _presentations) CancelInteraction(current);
            existing.State = state;
            window.windowState = state;
            window.docked = false;
            SetBounds(existing, ConstrainSize(window, window.position));
            BringToFront(window);
            return existing;
        }

        var bounds = ConstrainSize(window, window.position);
        var presentation = new FloatingEditorWindow(window, state, bounds);
        if (state == EditorWindowState.Modal)
            foreach (var existingPresentation in _presentations) CancelInteraction(existingPresentation);
        var modalIndex = FirstModalIndex();
        if (state != EditorWindowState.Modal && modalIndex >= 0)
            _presentations.Insert(modalIndex, presentation);
        else
            _presentations.Add(presentation);
        window.windowState = state;
        window.docked = false;
        window.position = bounds;
        return presentation;
    }

    public bool Remove(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var index = IndexOf(window);
        if (index < 0) return false;
        var removed = _presentations[index];
        CancelInteraction(removed);
        _presentations.RemoveAt(index);
        if (ReferenceEquals(EditorWindow.mouseOverWindow, window))
            EditorWindow.SetMouseOverWindow(null);
        FocusTopAfterRemoval(window);
        return true;
    }

    public bool Contains(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return IndexOf(window) >= 0;
    }

    public bool TryGet(EditorWindow window, out FloatingEditorWindow presentation)
    {
        ArgumentNullException.ThrowIfNull(window);
        var index = IndexOf(window);
        if (index >= 0)
        {
            presentation = _presentations[index];
            return true;
        }
        presentation = null!;
        return false;
    }

    public bool TryGetContentRect(EditorWindow window, out Rect content)
    {
        if (TryGet(window, out var presentation))
        {
            content = ContentRect(presentation);
            return true;
        }
        content = default;
        return false;
    }

    public bool BringToFront(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var index = IndexOf(window);
        if (index < 0) return false;
        var presentation = _presentations[index];
        _presentations.RemoveAt(index);
        var modalIndex = FirstModalIndex();
        if (presentation.State != EditorWindowState.Modal && modalIndex >= 0)
            _presentations.Insert(modalIndex, presentation);
        else
            _presentations.Add(presentation);
        return true;
    }

    public bool Focus(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!TryGet(window, out var presentation)) return false;
        var modal = FindTopModal();
        if (modal is not null && !ReferenceEquals(modal, presentation)) return false;
        BringToFront(window);
        window.FocusInternal();
        return true;
    }

    public void Draw(Rect canvas, bool inputPass)
    {
        if (_presentations.Count == 0) return;
        canvas = NormalizeCanvas(canvas);
        ConstrainPresentations(canvas);
        if (inputPass)
        {
            HandleInput(canvas);
            return;
        }
        DrawPresentations(canvas);
    }

    private void HandleInput(Rect canvas)
    {
        var evt = Event.current;
        if (evt.type == EventType.Used) return;
        var rawType = evt.rawType;
        var pointer = evt.mousePosition;
        var modal = FindTopModal();
        var popup = modal is null ? FindTop(EditorWindowState.Pop) : null;
        if (popup is not null && rawType == EventType.MouseDown && !popup.Bounds.Contains(pointer))
        {
            RequestClose(popup);
            ConsumeEvent();
            return;
        }
        if (popup is not null && rawType == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
        {
            RequestClose(popup);
            ConsumeEvent();
            return;
        }

        var active = modal is null ? FindActiveInteraction() : null;
        var target = modal ?? active ?? ResolveInputTarget(rawType, pointer, null);
        if (IsPointerInput(rawType))
            EditorWindow.SetMouseOverWindow(target is not null && target.Bounds.Contains(pointer)
                ? target.Window
                : null);

        if (target is null)
        {
            if (modal is not null) ConsumeEvent();
            return;
        }

        if (HandleChromeInput(target, canvas, rawType, pointer)) return;

        var content = ContentRect(target);
        var dispatchContent = IsKeyboardInput(rawType) || GUIUtility.hotControl != 0 || content.Contains(pointer);
        if (dispatchContent)
        {
            using (GUI.BeginWindow(content)) target.Window.OnGUIInternal();
        }

        if (modal is not null || IsPointerInput(rawType) && target.Bounds.Contains(pointer) ||
            IsKeyboardInput(rawType))
            ConsumeEvent();
    }

    private bool HandleChromeInput(FloatingEditorWindow presentation, Rect canvas, EventType type,
        Vector2 pointer)
    {
        var window = presentation.Window;
        if (presentation.MenuPressed)
        {
            if (type is not EventType.MouseUp) return ConsumeHandledInput();
            presentation.MenuPressed = false;
            var menu = MenuRect(presentation);
            ConsumeEvent();
            if (menu.Contains(pointer)) ShowWindowMenu(presentation, pointer);
            return true;
        }
        if (presentation.DockPressed)
        {
            if (type is not EventType.MouseUp) return ConsumeHandledInput();
            presentation.DockPressed = false;
            var dock = DockRect(presentation);
            ConsumeEvent();
            if (dock.Contains(pointer)) DockRequested?.Invoke(window, pointer);
            return true;
        }
        if (presentation.ClosePressed)
        {
            if (type is not EventType.MouseUp) return ConsumeHandledInput();
            presentation.ClosePressed = false;
            var close = CloseRect(presentation);
            ConsumeEvent();
            if (close.Contains(pointer)) RequestClose(presentation);
            return true;
        }
        if (presentation.IsDragging || presentation.IsResizing)
        {
            if (type is EventType.MouseDrag or EventType.MouseMove)
            {
                ApplyInteraction(presentation, canvas, pointer);
                ConsumeEvent();
                return true;
            }
            if (type == EventType.MouseUp)
            {
                ApplyInteraction(presentation, canvas, pointer);
                CancelInteraction(presentation);
                ConsumeEvent();
                return true;
            }
            return ConsumeHandledInput();
        }
        if (type == EventType.ContextClick && TitleRect(presentation).Contains(pointer))
        {
            Focus(window);
            ShowWindowMenu(presentation, pointer);
            ConsumeEvent();
            return true;
        }
        if (type != EventType.MouseDown || Event.current.button != 0 ||
            !presentation.Bounds.Contains(pointer)) return false;

        Focus(window);
        if (MenuRect(presentation).Contains(pointer))
        {
            presentation.MenuPressed = true;
            ConsumeEvent();
            return true;
        }
        if (DockRect(presentation).Contains(pointer))
        {
            presentation.DockPressed = true;
            ConsumeEvent();
            return true;
        }
        if (CloseRect(presentation).Contains(pointer))
        {
            presentation.ClosePressed = true;
            ConsumeEvent();
            return true;
        }
        if (presentation.State != EditorWindowState.Pop)
        {
            var resizeEdges = HitResizeEdges(presentation.Bounds, pointer);
            if (resizeEdges != 0)
            {
                BeginInteraction(presentation, pointer);
                presentation.ResizeEdges = resizeEdges;
                presentation.IsResizing = true;
                ConsumeEvent();
                return true;
            }
            if (TitleRect(presentation).Contains(pointer))
            {
                if (presentation.State == EditorWindowState.Normal && Event.current.clickCount >= 2)
                {
                    ConsumeEvent();
                    DockRequested?.Invoke(window, pointer);
                    return true;
                }
                BeginInteraction(presentation, pointer);
                presentation.IsDragging = true;
                ConsumeEvent();
                return true;
            }
        }
        return false;
    }

    private void DrawPresentations(Rect canvas)
    {
        GUI.BeginClip(canvas);
        try
        {
            var modal = FindTopModal();
            foreach (var presentation in _presentations)
            {
                if (ReferenceEquals(presentation, modal)) break;
                DrawPresentation(presentation);
            }
            if (modal is not null)
            {
                GUI.DrawRect(canvas, new Color(0, 0, 0, Fix64.FromDecimal(0.55m)));
                DrawPresentation(modal);
            }
        }
        finally
        {
            GUI.EndClip();
        }
    }

    private static void DrawPresentation(FloatingEditorWindow presentation)
    {
        var bounds = presentation.Bounds;
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawRect(new Rect(bounds.x + 5, bounds.y + 5, bounds.width, bounds.height),
                EditorAppearance.palette.Shadow);
            var sceneSurface = presentation.Window.titleContent.text is "Scene" or "Game";
            if (sceneSurface) DrawSurfaceBorder(bounds);
            else GUI.Box(bounds, GUIContent.none, GUI.skin.window);
            if (presentation.State != EditorWindowState.Pop)
            {
                var title = TitleRect(presentation);
                GUI.Box(title, GUIContent.none, EditorStyles.windowTitle);
                var dock = DockRect(presentation);
                var menu = MenuRect(presentation);
                var close = CloseRect(presentation);
                var titleRight = menu.x - 3;
                GUI.Label(new Rect(title.x + 5, title.y, Fix64.Max(0, titleRight - title.x - 5),
                    title.height), presentation.Window.titleContent, EditorStyles.windowTitle);
                GUI.Box(menu, new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.More, string.Empty),
                    EditorStyles.toolbarIconButton);
                if (dock.width > 0)
                    GUI.Box(dock, new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Container, string.Empty),
                        EditorStyles.toolbarIconButton);
                GUI.Box(close, new GUIContent("x"), EditorStyles.toolbarIconButton);
                DrawResizeCursors(bounds);
            }
        }
        using (GUI.BeginWindow(ContentRect(presentation))) presentation.Window.OnGUIInternal();
    }

    private static void DrawSurfaceBorder(Rect bounds)
    {
        GUI.DrawRect(new Rect(bounds.x, bounds.y, bounds.width, BorderWidth),
            EditorAppearance.palette.Border);
        GUI.DrawRect(new Rect(bounds.x, bounds.yMax - BorderWidth, bounds.width, BorderWidth),
            EditorAppearance.palette.Border);
        GUI.DrawRect(new Rect(bounds.x, bounds.y, BorderWidth, bounds.height),
            EditorAppearance.palette.Border);
        GUI.DrawRect(new Rect(bounds.xMax - BorderWidth, bounds.y, BorderWidth, bounds.height),
            EditorAppearance.palette.Border);
    }

    private static void DrawResizeCursors(Rect bounds)
    {
        GUI.AddCursorRect(new Rect(bounds.x, bounds.y + ResizeBorder, ResizeBorder,
            Fix64.Max(0, bounds.height - ResizeBorder * 2)), MouseCursor.ResizeHorizontal);
        GUI.AddCursorRect(new Rect(bounds.xMax - ResizeBorder, bounds.y + ResizeBorder, ResizeBorder,
            Fix64.Max(0, bounds.height - ResizeBorder * 2)), MouseCursor.ResizeHorizontal);
        GUI.AddCursorRect(new Rect(bounds.x + ResizeBorder, bounds.y, Fix64.Max(0,
            bounds.width - ResizeBorder * 2), ResizeBorder), MouseCursor.ResizeVertical);
        GUI.AddCursorRect(new Rect(bounds.x + ResizeBorder, bounds.yMax - ResizeBorder, Fix64.Max(0,
            bounds.width - ResizeBorder * 2), ResizeBorder), MouseCursor.ResizeVertical);
        GUI.AddCursorRect(new Rect(bounds.x, bounds.y, ResizeBorder * 2, ResizeBorder * 2),
            MouseCursor.ResizeUpLeft);
        GUI.AddCursorRect(new Rect(bounds.xMax - ResizeBorder * 2, bounds.y, ResizeBorder * 2,
            ResizeBorder * 2), MouseCursor.ResizeUpRight);
        GUI.AddCursorRect(new Rect(bounds.x, bounds.yMax - ResizeBorder * 2, ResizeBorder * 2,
            ResizeBorder * 2), MouseCursor.ResizeUpRight);
        GUI.AddCursorRect(new Rect(bounds.xMax - ResizeBorder * 2, bounds.yMax - ResizeBorder * 2,
            ResizeBorder * 2, ResizeBorder * 2), MouseCursor.ResizeUpLeft);
    }

    private static Rect TitleRect(FloatingEditorWindow presentation)
    {
        if (presentation.State == EditorWindowState.Pop) return default;
        var bounds = presentation.Bounds;
        return new Rect(bounds.x + BorderWidth, bounds.y + BorderWidth,
            Fix64.Max(0, bounds.width - BorderWidth * 2), TitleHeight());
    }

    private static Rect ContentRect(FloatingEditorWindow presentation)
    {
        var bounds = presentation.Bounds;
        var titleHeight = presentation.State == EditorWindowState.Pop ? Fix64.Zero : TitleHeight();
        return new Rect(bounds.x + BorderWidth, bounds.y + BorderWidth + titleHeight,
            Fix64.Max(1, bounds.width - BorderWidth * 2),
            Fix64.Max(1, bounds.height - BorderWidth * 2 - titleHeight));
    }

    private static Rect CloseRect(FloatingEditorWindow presentation)
    {
        if (presentation.State == EditorWindowState.Pop) return default;
        var title = TitleRect(presentation);
        var size = Fix64.Min(title.height, Fix64.Max(18, EditorStyles.toolbarIconButton.fixedHeight));
        return new Rect(title.xMax - size, title.y, size, title.height);
    }

    private static Rect DockRect(FloatingEditorWindow presentation)
    {
        if (presentation.State != EditorWindowState.Normal) return default;
        var close = CloseRect(presentation);
        return new Rect(close.x - close.width, close.y, close.width, close.height);
    }

    private static Rect MenuRect(FloatingEditorWindow presentation)
    {
        if (presentation.State == EditorWindowState.Pop) return default;
        var action = DockRect(presentation);
        if (action.width <= 0) action = CloseRect(presentation);
        return new Rect(action.x - action.width, action.y, action.width, action.height);
    }

    private void ShowWindowMenu(FloatingEditorWindow presentation, Vector2 pointer)
    {
        var menu = new GenericMenu();
        presentation.Window.PopulateContextMenu(menu);
        if (menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
        if (presentation.State == EditorWindowState.Normal)
            menu.AddItem(new GUIContent("Dock"), false,
                () => DockRequested?.Invoke(presentation.Window, pointer));
        menu.AddItem(new GUIContent("Close"), false, () => RequestClose(presentation));
        menu.ShowAsContext();
    }

    private static Fix64 TitleHeight() => Fix64.Max(24, EditorStyles.windowTitle.fixedHeight);

    private static void BeginInteraction(FloatingEditorWindow presentation, Vector2 pointer)
    {
        presentation.InteractionStartPointer = pointer;
        presentation.InteractionStartBounds = presentation.Bounds;
    }

    private static void ApplyInteraction(FloatingEditorWindow presentation, Rect canvas, Vector2 pointer)
    {
        var delta = pointer - presentation.InteractionStartPointer;
        var initial = presentation.InteractionStartBounds;
        if (presentation.IsDragging)
        {
            SetBounds(presentation, ClampToCanvas(new Rect(initial.x + delta.x, initial.y + delta.y,
                initial.width, initial.height), canvas));
            return;
        }

        var left = initial.x;
        var top = initial.y;
        var right = initial.xMax;
        var bottom = initial.yMax;
        if ((presentation.ResizeEdges & ResizeLeft) != 0) left += delta.x;
        if ((presentation.ResizeEdges & ResizeRight) != 0) right += delta.x;
        if ((presentation.ResizeEdges & ResizeTop) != 0) top += delta.y;
        if ((presentation.ResizeEdges & ResizeBottom) != 0) bottom += delta.y;

        var minimum = presentation.Window.minSize;
        var maximum = presentation.Window.maxSize;
        var minWidth = Fix64.Max(80, minimum.x);
        var minHeight = Fix64.Max(48, minimum.y);
        var maxWidth = Fix64.Max(minWidth, maximum.x);
        var maxHeight = Fix64.Max(minHeight, maximum.y);
        if (right - left < minWidth)
        {
            if ((presentation.ResizeEdges & ResizeLeft) != 0) left = right - minWidth;
            else right = left + minWidth;
        }
        if (bottom - top < minHeight)
        {
            if ((presentation.ResizeEdges & ResizeTop) != 0) top = bottom - minHeight;
            else bottom = top + minHeight;
        }
        if (right - left > maxWidth)
        {
            if ((presentation.ResizeEdges & ResizeLeft) != 0) left = right - maxWidth;
            else right = left + maxWidth;
        }
        if (bottom - top > maxHeight)
        {
            if ((presentation.ResizeEdges & ResizeTop) != 0) top = bottom - maxHeight;
            else bottom = top + maxHeight;
        }
        SetBounds(presentation, ClampToCanvas(new Rect(left, top,
            Fix64.Max(1, right - left), Fix64.Max(1, bottom - top)), canvas));
    }

    private static int HitResizeEdges(Rect bounds, Vector2 pointer)
    {
        var result = 0;
        if (pointer.x < bounds.x + ResizeBorder) result |= ResizeLeft;
        else if (pointer.x >= bounds.xMax - ResizeBorder) result |= ResizeRight;
        if (pointer.y < bounds.y + ResizeBorder) result |= ResizeTop;
        else if (pointer.y >= bounds.yMax - ResizeBorder) result |= ResizeBottom;
        return result;
    }

    private static void CancelInteraction(FloatingEditorWindow presentation)
    {
        presentation.IsDragging = false;
        presentation.IsResizing = false;
        presentation.DockPressed = false;
        presentation.MenuPressed = false;
        presentation.ClosePressed = false;
        presentation.ResizeEdges = 0;
    }

    private void ConstrainPresentations(Rect canvas)
    {
        foreach (var presentation in _presentations)
        {
            var sized = ConstrainSize(presentation.Window, presentation.Bounds);
            SetBounds(presentation, ClampToCanvas(sized, canvas));
        }
    }

    private static Rect ConstrainSize(EditorWindow window, Rect bounds)
    {
        var minimum = window.minSize;
        var maximum = window.maxSize;
        var minWidth = Fix64.Max(80, minimum.x);
        var minHeight = Fix64.Max(48, minimum.y);
        var maxWidth = Fix64.Max(minWidth, maximum.x);
        var maxHeight = Fix64.Max(minHeight, maximum.y);
        var width = Fix64.Clamp(bounds.width > 0 ? bounds.width : 480, minWidth, maxWidth);
        var height = Fix64.Clamp(bounds.height > 0 ? bounds.height : 320, minHeight, maxHeight);
        return new Rect(bounds.x, bounds.y, width, height);
    }

    private static Rect ClampToCanvas(Rect bounds, Rect canvas)
    {
        var width = Fix64.Min(bounds.width, canvas.width);
        var height = Fix64.Min(bounds.height, canvas.height);
        var x = Fix64.Clamp(bounds.x, canvas.x, Fix64.Max(canvas.x, canvas.xMax - width));
        var y = Fix64.Clamp(bounds.y, canvas.y, Fix64.Max(canvas.y, canvas.yMax - height));
        return new Rect(x, y, Fix64.Max(1, width), Fix64.Max(1, height));
    }

    private static Rect NormalizeCanvas(Rect canvas) => new(canvas.x, canvas.y,
        Fix64.Max(1, canvas.width), Fix64.Max(1, canvas.height));

    private static void SetBounds(FloatingEditorWindow presentation, Rect bounds)
    {
        presentation.Bounds = bounds;
        presentation.Window.position = bounds;
    }

    private FloatingEditorWindow? ResolveInputTarget(EventType type, Vector2 pointer,
        FloatingEditorWindow? modal)
    {
        if (modal is not null) return modal;
        if (IsKeyboardInput(type))
        {
            var focused = EditorWindow.focusedWindow;
            if (focused is not null && TryGet(focused, out var presentation)) return presentation;
            return _presentations.Count == 0 ? null : _presentations[^1];
        }
        for (var index = _presentations.Count - 1; index >= 0; index--)
            if (_presentations[index].Bounds.Contains(pointer)) return _presentations[index];
        return null;
    }

    private FloatingEditorWindow? FindActiveInteraction()
    {
        for (var index = _presentations.Count - 1; index >= 0; index--)
        {
            var presentation = _presentations[index];
            if (presentation.IsDragging || presentation.IsResizing || presentation.ClosePressed)
                return presentation;
        }
        return null;
    }

    private FloatingEditorWindow? FindTopModal() => FindTop(EditorWindowState.Modal);

    private FloatingEditorWindow? FindTop(EditorWindowState state)
    {
        for (var index = _presentations.Count - 1; index >= 0; index--)
            if (_presentations[index].State == state) return _presentations[index];
        return null;
    }

    private int FirstModalIndex()
    {
        for (var index = 0; index < _presentations.Count; index++)
            if (_presentations[index].State == EditorWindowState.Modal) return index;
        return -1;
    }

    private int IndexOf(EditorWindow window)
    {
        for (var index = 0; index < _presentations.Count; index++)
            if (ReferenceEquals(_presentations[index].Window, window)) return index;
        return -1;
    }

    private void RequestClose(FloatingEditorWindow presentation)
    {
        var window = presentation.Window;
        if (!Remove(window)) return;
        WindowClosed?.Invoke(window);
    }

    private void FocusTopAfterRemoval(EditorWindow removed)
    {
        if (!ReferenceEquals(EditorWindow.focusedWindow, removed)) return;
        if (_presentations.Count == 0)
        {
            removed.LoseFocusInternal();
            return;
        }
        _presentations[^1].Window.FocusInternal();
    }

    private static bool IsPointerInput(EventType type) => type is EventType.MouseDown or EventType.MouseUp or
        EventType.MouseMove or EventType.MouseDrag or EventType.ContextClick or EventType.ScrollWheel or
        EventType.TouchDown or EventType.TouchUp or EventType.TouchMove or EventType.DragUpdated or
        EventType.DragPerform or EventType.DragExited;

    private static bool IsKeyboardInput(EventType type) => type is EventType.KeyDown or EventType.KeyUp or
        EventType.ValidateCommand or EventType.ExecuteCommand;

    private static bool ConsumeHandledInput()
    {
        ConsumeEvent();
        return true;
    }

    private static void ConsumeEvent()
    {
        if (Event.current.type != EventType.Used) Event.current.Use();
    }
}
