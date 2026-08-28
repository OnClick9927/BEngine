using BEngine.Editor.Rendering;

namespace BEngine.Editor;

public static class GUI
{
    internal const int ContentImageTextOffset = 22;

    [ThreadStatic] private static ImGuiContext? _context;
    [ThreadStatic] private static Dictionary<int, TextState>? _textStates;
    [ThreadStatic] private static string? _focusedControlName;
    [ThreadStatic] private static string? _pendingFocusControlName;
    [ThreadStatic] private static int _activeTextControl;
    [ThreadStatic] private static TextState? _activeTextState;
    [ThreadStatic] private static long _caretBlinkStart;
    [ThreadStatic] private static Stack<CoordinateScopeState>? _coordinateScopes;
    [ThreadStatic] private static MouseCursor _requestedMouseCursor;
    [ThreadStatic] private static string? _tooltipCandidate;
    [ThreadStatic] private static long _tooltipHoverStarted;
    [ThreadStatic] private static Vector2 _tooltipPointer;
    [ThreadStatic] private static bool _endUndoGroupAfterPointerEvent;
    [ThreadStatic] private static bool _endUndoGroupAfterKeyboardEvent;
    public static Color color { get; set; } = Color.white;
    public static Color backgroundColor { get; set; } = Color.white;
    public static Color contentColor { get; set; } = Color.white;
    public static bool enabled { get; set; } = true;
    public static bool changed { get; set; }
    public static int depth { get; set; }
    public static GUISkin skin
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    internal static GUISkin initialSkin { get; private set; } = null!;

    static GUI()
    {
        skin.ApplyDefaultTheme();
        initialSkin = skin;
    }
    public static string tooltip => _context?.Tooltip ?? string.Empty;
    internal static bool isEditingTextField => _activeTextControl != 0 &&
        GUIUtility.keyboardControl == _activeTextControl;

    internal static void BeginFrame(Event inputEvent, int width, int height, List<GpuCanvasCommand> commands)
    {
        var rawType = inputEvent.rawType;
        if (rawType == EventType.MouseDown ||
            rawType == EventType.KeyDown && !isEditingTextField)
            Undo.EndCurrentEventGroup();
        var editorScale = Fix64.Clamp(GUIUtility.pixelsPerPoint, Fix64.FromDecimal(0.5m), (Fix64)4);
        var deviceScale = GUIUtility.devicePixelsPerPoint > 0
            ? Fix64.Clamp(GUIUtility.devicePixelsPerPoint, Fix64.FromDecimal(0.5m), (Fix64)4)
            : Fix64.One;
        var renderScale = editorScale * deviceScale;
        inputEvent.mousePosition /= editorScale;
        inputEvent.delta /= editorScale;
        var logicalWidth = Fix64.Max(1, (Fix64)width / renderScale);
        var logicalHeight = Fix64.Max(1, (Fix64)height / renderScale);
        GUIUtility.currentViewWidth = logicalWidth;
        GUIUtility.currentViewHeight = logicalHeight;
        _requestedMouseCursor = MouseCursor.Arrow;
        DragAndDrop.BeginEvent(inputEvent);
        _endUndoGroupAfterPointerEvent = rawType is EventType.MouseUp or EventType.DragPerform ||
                                         inputEvent.type == EventType.DragPerform;
        _endUndoGroupAfterKeyboardEvent = rawType is EventType.KeyUp or EventType.ExecuteCommand;
        Event.current = inputEvent;
        GUIUtility.BeginEvent();
        _textStates ??= [];
        _coordinateScopes ??= [];
        _coordinateScopes.Clear();
        _context = new ImGuiContext(logicalWidth, logicalHeight, renderScale, commands, _textStates)
        {
            FocusedName = _focusedControlName ?? string.Empty,
            FocusRequest = _pendingFocusControlName ?? string.Empty
        };
        GUILayout.BeginFrame(new Rect(0, 0, logicalWidth, logicalHeight));
        changed = false;
    }

    internal static void EndFrame()
    {
        UpdateAndDrawTooltip();
        if (_context is not null) _focusedControlName = _context.FocusedName;
        GUILayout.EndFrame();
        _context = null;
        DragAndDrop.EndEvent(Event.current);
        if (_endUndoGroupAfterPointerEvent ||
            _endUndoGroupAfterKeyboardEvent && !isEditingTextField)
            Undo.EndCurrentEventGroup();
        _endUndoGroupAfterPointerEvent = false;
        _endUndoGroupAfterKeyboardEvent = false;
        Event.ClearCurrent();
    }

    public static void Label(Rect position, string text) => Label(position, new GUIContent(text), null);
    public static void Label(Rect position, string text, GUIStyle? style) =>
        Label(position, new GUIContent(text), style);
    public static void Label(Rect position, GUIContent content, GUIStyle? style = null) =>
        DrawContent(position, content, style ?? skin.label, false, false);
    internal static void Label(Rect position, GUIContent content, GUIStyle style, Fix64 leadingTextOffset) =>
        DrawContent(position, content, style, false, false, leadingTextOffset: leadingTextOffset);
    public static void Box(Rect position, string text = "") => Box(position, new GUIContent(text), null);
    public static void Box(Rect position, string text, GUIStyle? style) =>
        Box(position, new GUIContent(text), style);
    public static void Box(Rect position, GUIContent content, GUIStyle? style = null) =>
        DrawContent(position, content, style ?? skin.box, true, false);

    public static bool Button(Rect position, string text) => Button(position, new GUIContent(text), null);
    public static bool Button(Rect position, string text, GUIStyle? style) =>
        Button(position, new GUIContent(text), style);
    public static bool Button(Rect position, GUIContent content, GUIStyle? style = null) =>
        Button(position, content, style, FocusType.Keyboard);

    internal static bool Button(Rect position, GUIContent content, GUIStyle? style, FocusType focusType)
    {
        var id = GUIUtility.GetControlID(content.text.GetHashCode(StringComparison.Ordinal), focusType, position);
        var pressed = DoButton(id, position, focusType == FocusType.Keyboard);
        DrawContent(position, content, style ?? skin.button, true, GUIUtility.hotControl == id,
            GUIUtility.keyboardControl == id);
        return pressed;
    }

    public static bool Toggle(Rect position, bool value, string text) =>
        Toggle(position, value, new GUIContent(text), null);
    public static bool Toggle(Rect position, bool value, string text, GUIStyle? style) =>
        Toggle(position, value, new GUIContent(text), style);
    public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle? style = null)
    {
        style ??= skin.toggle;
        var id = GUIUtility.GetControlID(content.text.GetHashCode(StringComparison.Ordinal), FocusType.Keyboard, position);
        if (DoButton(id, position)) { value = !value; changed = true; }
        var absolute = _context?.Translate(position) ?? position;
        var hovered = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        var active = GUIUtility.hotControl == id;
        var focused = GUIUtility.keyboardControl == id;
        var state = ResolveStyleState(style, value, active, focused, hovered);
        var boxSize = Fix64.Clamp(position.height - 6, 13, 16);
        var box = new Rect(position.x, position.y + (position.height - boxSize) / 2, boxSize, boxSize);
        DrawStyleBackground(box, state, style.borderWidth);
        if (value)
            AddCommand(GpuCanvasCommandType.Image,
                new Rect(box.x + 1, box.y + 1, Fix64.Max(0, box.width - 2), Fix64.Max(0, box.height - 2)),
                state.textColor * contentColor * color,
                EditorBuiltinIcons.Toolbar.Check);
        DrawContent(new Rect(position.x + boxSize + 6, position.y,
                Fix64.Max(0, position.width - boxSize - 6), position.height), content,
            style, false, active, focused, value);
        return value;
    }

    public static string TextField(Rect position, string text, int maxLength = -1, GUIStyle? style = null) =>
        DoTextField(position, text, maxLength, false, style ?? skin.textField);
    public static string TextField(Rect position, string text, GUIStyle? style) =>
        DoTextField(position, text, -1, false, style ?? skin.textField);
    public static string TextArea(Rect position, string text, int maxLength = -1, GUIStyle? style = null) =>
        DoTextField(position, text, maxLength, true, style ?? skin.textArea);
    public static string TextArea(Rect position, string text, GUIStyle? style) =>
        DoTextField(position, text, -1, true, style ?? skin.textArea);
    public static string PasswordField(Rect position, string password, char maskChar = '*', int maxLength = -1,
        GUIStyle? style = null)
    {
        var value = DoTextField(position, password, maxLength, false, style ?? skin.textField, maskChar);
        return value;
    }
    public static string PasswordField(Rect position, string password, GUIStyle? style) =>
        PasswordField(position, password, '*', -1, style);
    public static string PasswordField(Rect position, string password, char maskChar, GUIStyle? style) =>
        PasswordField(position, password, maskChar, -1, style);

    public static Fix64 HorizontalSlider(Rect position, Fix64 value, Fix64 leftValue, Fix64 rightValue) =>
        HorizontalSlider(position, value, leftValue, rightValue, null, null);
    public static Fix64 HorizontalSlider(Rect position, Fix64 value, Fix64 leftValue, Fix64 rightValue,
        GUIStyle? slider) => HorizontalSlider(position, value, leftValue, rightValue, slider, null);
    public static Fix64 HorizontalSlider(Rect position, Fix64 value, Fix64 leftValue, Fix64 rightValue,
        GUIStyle? slider, GUIStyle? thumb) =>
        Slider(position, value, leftValue, rightValue, false,
            slider ?? skin.horizontalSlider, thumb ?? skin.horizontalSliderThumb);
    public static Fix64 VerticalSlider(Rect position, Fix64 value, Fix64 topValue, Fix64 bottomValue) =>
        VerticalSlider(position, value, topValue, bottomValue, null, null);
    public static Fix64 VerticalSlider(Rect position, Fix64 value, Fix64 topValue, Fix64 bottomValue,
        GUIStyle? slider) => VerticalSlider(position, value, topValue, bottomValue, slider, null);
    public static Fix64 VerticalSlider(Rect position, Fix64 value, Fix64 topValue, Fix64 bottomValue,
        GUIStyle? slider, GUIStyle? thumb) =>
        Slider(position, value, topValue, bottomValue, true,
            slider ?? skin.verticalSlider, thumb ?? skin.verticalSliderThumb);
    public static void DrawTexture(Rect position, string imagePath) => AddCommand(GpuCanvasCommandType.Image,
        position, new Color(1, 1, 1, 1), imagePath);
    public static void DrawRect(Rect position, Color colorValue) =>
        AddCommand(GpuCanvasCommandType.SolidRect, position, colorValue);
    public static void DrawGradientRect(Rect position, Color topLeft, Color topRight,
        Color bottomRight, Color bottomLeft)
    {
        if (_context is null || Event.current.type != EventType.Repaint) return;
        var translated = _context.Translate(position);
        _context.Commands.Add(new GpuCanvasCommand(GpuCanvasCommandType.GradientRect,
            _context.Scale(translated), _context.Scale(_context.Clip),
            GpuCanvasColor.FromColor(topLeft), Color2: GpuCanvasColor.FromColor(topRight),
            Color3: GpuCanvasColor.FromColor(bottomRight),
            Color4: GpuCanvasColor.FromColor(bottomLeft)));
    }
    public static void BeginGroup(Rect position) => _context?.PushGroup(position);
    public static void EndGroup() => _context?.PopGroup();
    public static void BeginClip(Rect position) => _context?.PushClip(position);
    public static void EndClip() => _context?.PopClip();
    public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect)
        => BeginScrollView(position, scrollPosition, viewRect, null, null, null, null, null);

    public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect,
        GUIStyle? background) => BeginScrollView(position, scrollPosition, viewRect,
        null, null, null, null, background);

    public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect,
        GUIStyle? horizontalScrollbar, GUIStyle? verticalScrollbar, GUIStyle? background) =>
        BeginScrollView(position, scrollPosition, viewRect, horizontalScrollbar, null,
            verticalScrollbar, null, background);

    public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect,
        GUIStyle? horizontalScrollbar, GUIStyle? horizontalScrollbarThumb,
        GUIStyle? verticalScrollbar, GUIStyle? verticalScrollbarThumb, GUIStyle? background)
    {
        if (_context is null) return scrollPosition;
        horizontalScrollbar ??= skin.horizontalScrollbar;
        horizontalScrollbarThumb ??= skin.horizontalScrollbarThumb;
        verticalScrollbar ??= skin.verticalScrollbar;
        verticalScrollbarThumb ??= skin.verticalScrollbarThumb;
        background ??= skin.scrollView;
        var viewport = _context.Translate(position);
        var pointer = PointerPosition;
        var hovered = viewport.Contains(pointer) && PointerInsideClip(pointer);
        DrawStyleBackground(position, ResolveStyleState(background, false, false, false, hovered),
            background.borderWidth);
        var maxX = Fix64.Max(0, viewRect.width - position.width);
        var maxY = Fix64.Max(0, viewRect.height - position.height);
        if (Event.current.type == EventType.ScrollWheel && viewport.Contains(pointer))
        {
            scrollPosition = new Vector2(scrollPosition.x + Event.current.delta.x * 24,
                scrollPosition.y + Event.current.delta.y * 24);
            Event.current.Use();
        }
        scrollPosition = new Vector2(Fix64.Clamp(scrollPosition.x, 0, maxX),
            Fix64.Clamp(scrollPosition.y, 0, maxY));
        scrollPosition = HandleAndDrawScrollbars(position, scrollPosition, viewRect,
            horizontalScrollbar, horizontalScrollbarThumb, verticalScrollbar, verticalScrollbarThumb);

        var contentOrigin = new Vector2(viewport.x - scrollPosition.x, viewport.y - scrollPosition.y);
        _context.PushScrollView(position, scrollPosition);
        PushCoordinateScope(contentOrigin, pointer - contentOrigin, viewRect.width, viewRect.height);
        return scrollPosition;
    }

    public static void EndScrollView()
    {
        PopCoordinateScope();
        _context?.PopGroup();
    }

    internal static WindowScope BeginWindow(Rect position)
    {
        if (_context is null) return default;
        var absolute = _context.Translate(position);
        var pointer = PointerPosition;
        _context.PushGroup(position);
        PushCoordinateScope(absolute.position, pointer - absolute.position, position.width, position.height);
        GUILayout.BeginContainer(new Rect(0, 0, position.width, position.height));
        return new WindowScope(true);
    }

    internal static FeatureIsolationScope BeginFeatureIsolation()
    {
        if (_context is null) return default;
        return new FeatureIsolationScope(
            _context,
            _context.CaptureStructuralState(),
            GUILayout.CaptureState(),
            EditorGUI.CaptureFeatureState(),
            _context.Commands.Count,
            _coordinateScopes?.Reverse().ToArray() ?? [],
            GUIUtility.CaptureContainerScopes(),
            Event.current.mousePosition,
            Event.current.type,
            GUIUtility.currentViewWidth,
            GUIUtility.currentViewHeight,
            GUIUtility.hotControl,
            GUIUtility.keyboardControl,
            _activeTextControl,
            _activeTextState,
            _requestedMouseCursor,
            enabled,
            changed,
            depth,
            color,
            backgroundColor,
            contentColor,
            EditorGUIUtility.wideMode,
            _context.Tooltip,
            _context.NextControlName,
            _context.FocusRequest,
            _pendingFocusControlName,
            _context.FocusedName);
    }

    internal static Vector2 GUIToRootPoint(Vector2 point) =>
        point + (_context?.InputOrigin ?? Vector2.zero);
    internal static Vector2 RootToGUIPoint(Vector2 point) =>
        point - (_context?.InputOrigin ?? Vector2.zero);
    public static void FocusControl(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            if (_context is not null)
            {
                _context.FocusRequest = string.Empty;
                _context.FocusedName = string.Empty;
            }
            _focusedControlName = null;
            _pendingFocusControlName = null;
            GUIUtility.keyboardControl = 0;
            _activeTextControl = 0;
            _activeTextState = null;
            return;
        }
        if (_context is null)
        {
            if (!string.Equals(_focusedControlName, name, StringComparison.Ordinal))
            {
                GUIUtility.keyboardControl = 0;
                _activeTextControl = 0;
                _activeTextState = null;
            }
            _pendingFocusControlName = name;
            return;
        }
        if (!_context.FocusedName.Equals(name, StringComparison.Ordinal))
        {
            GUIUtility.keyboardControl = 0;
            _activeTextControl = 0;
            _activeTextState = null;
        }
        _context.FocusRequest = name;
        _pendingFocusControlName = name;
    }
    public static void SetNextControlName(string name) { if (_context is not null) _context.NextControlName = name ?? ""; }
    public static string GetNameOfFocusedControl() => _context?.FocusedName ?? string.Empty;
    internal static void ClearTextState(int controlId)
    {
        if (controlId == 0) return;
        _textStates?.Remove(controlId);
        if (_activeTextControl != controlId) return;
        _activeTextControl = 0;
        _activeTextState = null;
    }
    internal static MouseCursor requestedMouseCursor => _requestedMouseCursor;
    internal static Fix64 visibleViewWidth => _context is null
        ? GUIUtility.currentViewWidth
        : Fix64.Min(GUIUtility.currentViewWidth, Fix64.Max(1, (Fix64)_context.Clip.Width));
    internal static Fix64 GetVisibleWidth(Rect position)
    {
        if (_context is null) return position.width;
        var absolute = _context.Translate(position);
        var clip = _context.Clip;
        var left = Fix64.Max(absolute.x, (Fix64)clip.X);
        var right = Fix64.Min(absolute.xMax, (Fix64)(clip.X + clip.Width));
        return Fix64.Max(0, right - left);
    }
    internal static void AddCursorRect(Rect position, MouseCursor mouse)
    {
        if (_context is not null && _context.Translate(position).Contains(PointerPosition) &&
            PointerInsideClip(PointerPosition))
            _requestedMouseCursor = mouse;
    }

    private static bool DoButton(int id, Rect rect, bool takesKeyboardFocus = true)
    {
        if (!enabled) return false;
        var evt = Event.current;
        var absolute = _context?.Translate(rect) ?? rect;
        var contains = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when contains && evt.button == 0:
                GUIUtility.hotControl = id;
                if (takesKeyboardFocus) GUIUtility.keyboardControl = id;
                evt.Use();
                return false;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0; evt.Use(); return contains;
            case EventType.KeyDown when GUIUtility.keyboardControl == id && evt.keyCode is KeyCode.Return or KeyCode.Space:
                evt.Use(); return true;
            default: return false;
        }
    }

    private static string DoTextField(Rect rect, string value, int maxLength, bool multiline,
        GUIStyle style, char? mask = null)
    {
        value ??= string.Empty;
        var id = GUIUtility.GetControlID("TextField".GetHashCode(StringComparison.Ordinal), FocusType.Keyboard, rect);
        var controlName = _context?.AssignNextControlName(id) ?? string.Empty;
        var evt = Event.current;
        var absolute = _context?.Translate(rect) ?? rect;
        var hovered = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        if (enabled && evt.type == EventType.MouseDown && hovered && evt.button == 0)
        {
            GUIUtility.keyboardControl = id;
            var visibleValue = mask is null ? value : new string(mask.Value, value.Length);
            var pointer = PointerPosition;
            var interactionTextRect = GetContentTextRect(rect, visibleValue, style, true, false);
            var absoluteTextRect = _context?.Translate(interactionTextRect) ?? interactionTextRect;
            var localX = Fix64.Max(0, pointer.x - absoluteTextRect.x);
            var clickedIndex = ClosestTextBoundary(visibleValue, localX, style);
            _context?.SetText(id, new TextState(value,
                evt.clickCount >= 2 ? value.Length : clickedIndex,
                evt.clickCount >= 2 ? 0 : clickedIndex,
                value));
            _activeTextControl = id;
            _activeTextState = new TextState(value, evt.clickCount >= 2 ? value.Length : clickedIndex,
                evt.clickCount >= 2 ? 0 : clickedIndex, value);
            _caretBlinkStart = Environment.TickCount64;
            if (controlName.Length > 0 && _context is not null) _context.FocusedName = controlName;
            evt.Use();
        }
        var focused = GUIUtility.keyboardControl == id;
        var state = focused
            ? _activeTextControl == id && _activeTextState is { } active
                ? active
                : _context?.GetText(id, value) ?? new TextState(value, value.Length, value.Length, value)
            : new TextState(value, value.Length, value.Length, value);
        if (focused && !state.ObservedValue.Equals(value, StringComparison.Ordinal))
            state = state.Text.Equals(value, StringComparison.Ordinal)
                ? state with { ObservedValue = value }
                : new TextState(value, value.Length, value.Length, value);
        if (enabled && focused && evt.type == EventType.KeyDown)
        {
            var before = state.Text;
            state = EditText(state, evt, multiline, maxLength);
            if (state.Text != before) changed = true;
            _caretBlinkStart = Environment.TickCount64;
            if (evt.type != EventType.Used) evt.Use();
        }
        _context?.SetText(id, state);
        if (focused) { _activeTextControl = id; _activeTextState = state; }
        GUIUtility.textFieldInput = focused;
        var displayed = new GUIContent(mask is null ? state.Text : new string(mask.Value, state.Text.Length));
        DrawContent(rect, displayed, style, true, false, focused);
        var textRect = GetContentTextRect(rect, displayed.text, style, true, false);
        var visualState = ResolveStyleState(style, false, false, focused, hovered);
        if (focused && Event.current.type == EventType.Repaint && state.Caret != state.Anchor)
        {
            var start = Math.Min(state.Caret, state.Anchor);
            var length = Math.Abs(state.Caret - state.Anchor);
            var visibleText = displayed.text;
            var selectionX = TextBoundaryOffset(visibleText, start, style);
            var selectionWidth = TextBoundaryOffset(visibleText, start + length, style) - selectionX;
            DrawRect(new Rect(textRect.x + selectionX, rect.y + 3,
                selectionWidth, Fix64.Max(0, rect.height - 6)),
                EditorStyles.selectionRect.normal.backgroundColor);
        }
        if (focused && state.Caret != state.Anchor) DrawContent(rect, displayed, style, false, false, true);
        if (focused && Event.current.type == EventType.Repaint &&
            (Environment.TickCount64 - _caretBlinkStart) / 500 % 2 == 0)
        {
            var visibleText = displayed.text;
            var caretOffset = Fix64.Min(Fix64.Max(0, textRect.width),
                TextBoundaryOffset(visibleText, state.Caret, style));
            DrawRect(new Rect(textRect.x + caretOffset, rect.y + 3,
                1, Fix64.Max(0, rect.height - 6)),
                visualState.textColor * contentColor * color);
        }
        return state.Text;
    }

    private static TextState EditText(TextState state, Event evt, bool multiline, int maxLength)
    {
        var text = state.Text; var start = Math.Min(state.Caret, state.Anchor); var end = Math.Max(state.Caret, state.Anchor);
        void DeleteSelection() { if (end <= start) return; text = text.Remove(start, end - start); state = state with { Caret = start, Anchor = start }; }
        if (evt.control && evt.keyCode == KeyCode.A) return state with { Caret = text.Length, Anchor = 0 };
        if (evt.control && evt.keyCode == KeyCode.C) { GUIUtility.systemCopyBuffer = text[start..end]; return state; }
        if (evt.control && evt.keyCode == KeyCode.X) { GUIUtility.systemCopyBuffer = text[start..end]; DeleteSelection(); return state with { Text = text }; }
        if (evt.control && evt.keyCode == KeyCode.V)
        {
            DeleteSelection(); var insert = GUIUtility.systemCopyBuffer;
            if (maxLength >= 0) insert = insert[..Math.Min(insert.Length, Math.Max(0, maxLength - text.Length))];
            text = text.Insert(state.Caret, insert);
            return state with
            {
                Text = text,
                Caret = state.Caret + insert.Length,
                Anchor = state.Caret + insert.Length
            };
        }
        switch (evt.keyCode)
        {
            case KeyCode.LeftArrow: return state with { Caret = Math.Max(0, state.Caret - 1), Anchor = evt.shift ? state.Anchor : Math.Max(0, state.Caret - 1) };
            case KeyCode.RightArrow: return state with { Caret = Math.Min(text.Length, state.Caret + 1), Anchor = evt.shift ? state.Anchor : Math.Min(text.Length, state.Caret + 1) };
            case KeyCode.Home: return state with { Caret = 0, Anchor = evt.shift ? state.Anchor : 0 };
            case KeyCode.End: return state with { Caret = text.Length, Anchor = evt.shift ? state.Anchor : text.Length };
            case KeyCode.Backspace:
                if (end > start) DeleteSelection(); else if (state.Caret > 0) { text = text.Remove(state.Caret - 1, 1); state = state with { Caret = state.Caret - 1, Anchor = state.Caret - 1 }; }
                return state with { Text = text };
            case KeyCode.Delete:
                if (end > start) DeleteSelection(); else if (state.Caret < text.Length) text = text.Remove(state.Caret, 1);
                return state with { Text = text };
            case KeyCode.Return when multiline: evt.character = '\n'; break;
            case KeyCode.Return: GUIUtility.keyboardControl = 0; return state;
        }
        if (!char.IsControl(evt.character))
        {
            DeleteSelection();
            if (maxLength < 0 || text.Length < maxLength)
            {
                text = text.Insert(state.Caret, evt.character.ToString());
                return state with { Text = text, Caret = state.Caret + 1, Anchor = state.Caret + 1 };
            }
        }
        return state with { Text = text };
    }

    private static Fix64 Slider(Rect rect, Fix64 value, Fix64 first, Fix64 second, bool vertical,
        GUIStyle sliderStyle, GUIStyle thumbStyle)
    {
        var id = GUIUtility.GetControlID("Slider".GetHashCode(StringComparison.Ordinal), FocusType.Passive, rect);
        var evt = Event.current;
        var absolute = _context?.Translate(rect) ?? rect;
        var contains = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        if (enabled && evt.type is EventType.MouseDown or EventType.MouseDrag &&
            (contains || GUIUtility.hotControl == id))
        {
            GUIUtility.hotControl = id;
            var pointer = PointerPosition;
            var t = vertical ? (pointer.y - absolute.y) / Fix64.Max(1, absolute.height) :
                (pointer.x - absolute.x) / Fix64.Max(1, absolute.width);
            value = first + Fix64.Clamp(t, 0, 1) * (second - first); changed = true; evt.Use();
        }
        if (evt.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; evt.Use(); }
        var range = second - first; var normalized = range == 0 ? Fix64.Zero : Fix64.Clamp((value - first) / range, 0, 1);
        var trackThickness = vertical
            ? sliderStyle.fixedWidth > 0 ? sliderStyle.fixedWidth : (Fix64)4
            : sliderStyle.fixedHeight > 0 ? sliderStyle.fixedHeight : (Fix64)4;
        trackThickness = Fix64.Clamp(trackThickness, 1, vertical ? rect.width : rect.height);
        var track = vertical
            ? new Rect(rect.x + (rect.width - trackThickness) / 2, rect.y, trackThickness, rect.height)
            : new Rect(rect.x, rect.y + (rect.height - trackThickness) / 2, rect.width, trackThickness);
        var active = GUIUtility.hotControl == id;
        DrawStyleBackground(track, ResolveStyleState(sliderStyle, false, active, false, contains),
            sliderStyle.borderWidth);
        var thumbLength = vertical
            ? thumbStyle.fixedHeight > 0 ? thumbStyle.fixedHeight : (Fix64)10
            : thumbStyle.fixedWidth > 0 ? thumbStyle.fixedWidth : (Fix64)10;
        thumbLength = Fix64.Clamp(thumbLength, 1, vertical ? rect.height : rect.width);
        var thumbCross = vertical
            ? thumbStyle.fixedWidth > 0 ? Fix64.Min(rect.width, thumbStyle.fixedWidth) : rect.width
            : thumbStyle.fixedHeight > 0 ? Fix64.Min(rect.height, thumbStyle.fixedHeight) : rect.height;
        var thumb = vertical
            ? new Rect(rect.x + (rect.width - thumbCross) / 2,
                rect.y + normalized * Fix64.Max(0, rect.height - thumbLength), thumbCross, thumbLength)
            : new Rect(rect.x + normalized * Fix64.Max(0, rect.width - thumbLength),
                rect.y + (rect.height - thumbCross) / 2, thumbLength, thumbCross);
        var absoluteThumb = _context?.Translate(thumb) ?? thumb;
        var thumbHovered = absoluteThumb.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        DrawStyleBackground(thumb, ResolveStyleState(thumbStyle, false, active, false, thumbHovered),
            thumbStyle.borderWidth);
        return value;
    }

    private static void DrawContent(Rect rect, GUIContent content, GUIStyle style, bool background, bool active,
        bool focused = false, bool on = false, Fix64 leadingTextOffset = default)
    {
        if (_context is null || Event.current.type != EventType.Repaint) return;
        var absolute = _context?.Translate(rect) ?? rect;
        var hovered = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        var state = ResolveStyleState(style, on, active, focused, hovered);
        if (background && (state.backgroundColor.a > 0 || state.backgroundImage is not null))
            DrawStyleBackground(rect, state, style.borderWidth);
        var hasImage = !string.IsNullOrWhiteSpace(content.image);
        if (hasImage)
        {
            var iconSize = Fix64.Min(16, Fix64.Max(0, rect.height - 4));
            AddCommand(GpuCanvasCommandType.Image,
                new Rect(rect.x + 3, rect.y + (rect.height - iconSize) / 2, iconSize, iconSize),
                state.textColor * contentColor * color, content.image);
        }
        var textRect = GetContentTextRect(rect, content.text, style, background, hasImage,
            leadingTextOffset);
        if (!string.IsNullOrEmpty(content.text)) AddCommand(GpuCanvasCommandType.Text, textRect,
            state.textColor * contentColor * color, content.text, (float)style.fontSize);
        if (hovered && _context is { } context)
            context.Tooltip = string.IsNullOrWhiteSpace(content.tooltip) ? string.Empty : content.tooltip;
    }

    private static void UpdateAndDrawTooltip()
    {
        if (_context is null || Event.current.type != EventType.Repaint) return;
        var value = _context.Tooltip.Trim();
        if (value.Length == 0)
        {
            _tooltipCandidate = null;
            _tooltipHoverStarted = 0;
            return;
        }

        var pointer = PointerPosition;
        var moved = (pointer - _tooltipPointer).sqrMagnitude > 16;
        if (!string.Equals(value, _tooltipCandidate, StringComparison.Ordinal) || moved)
        {
            _tooltipCandidate = value;
            _tooltipHoverStarted = Environment.TickCount64;
            _tooltipPointer = pointer;
            return;
        }
        if (Environment.TickCount64 - _tooltipHoverStarted < 500) return;

        var fontSize = Fix64.Max(10, EditorStyles.miniLabel.fontSize);
        var maximumWidth = Fix64.Max(40, GUIUtility.currentViewWidth - 8);
        var width = Fix64.Min(maximumWidth, GUITextMetrics.MeasureWidth(value, fontSize,
            GUIUtility.fontFamily) + 12);
        var height = Fix64.Max(20, GUITextMetrics.MeasureLineHeight(fontSize, GUIUtility.fontFamily) + 8);
        var x = pointer.x + 14;
        if (x + width > GUIUtility.currentViewWidth - 4) x = GUIUtility.currentViewWidth - width - 4;
        var y = pointer.y + 18;
        if (y + height > GUIUtility.currentViewHeight - 4) y = pointer.y - height - 8;
        x = Fix64.Clamp(x, 4, Fix64.Max(4, GUIUtility.currentViewWidth - width - 4));
        y = Fix64.Clamp(y, 4, Fix64.Max(4, GUIUtility.currentViewHeight - height - 4));
        var panel = new Rect(x, y, width, height);
        var style = EditorStyles.tooltip;
        DrawStyleBackground(panel, style.normal, style.borderWidth);
        AddCommand(GpuCanvasCommandType.Text,
            new Rect(panel.x + 6, panel.y + 3, Fix64.Max(1, panel.width - 12), panel.height - 6),
            style.normal.textColor, value, (float)fontSize);
    }

    private static bool PointerInsideClip(Vector2 pointer)
    {
        if (_context is null) return true;
        var clip = _context.Clip;
        return pointer.x >= (Fix64)clip.X && pointer.y >= (Fix64)clip.Y &&
               pointer.x <= (Fix64)(clip.X + clip.Width) && pointer.y <= (Fix64)(clip.Y + clip.Height);
    }

    private static void AddCommand(GpuCanvasCommandType type, Rect rect, Color commandColor,
        string content = "", float fontSize = 14)
    {
        if (_context is null || Event.current.type != EventType.Repaint) return;
        var translated = _context.Translate(rect);
        _context.Commands.Add(new GpuCanvasCommand(type, _context.Scale(translated),
            _context.Scale(_context.Clip), GpuCanvasColor.FromColor(commandColor), content,
            fontSize * (float)_context.ScaleFactor, GUIUtility.fontFamily));
    }

    private static Vector2 PointerPosition => Event.current.mousePosition +
        (_context?.InputOrigin ?? Vector2.zero);

    private static void PushCoordinateScope(Vector2 inputOrigin, Vector2 mousePosition,
        Fix64 viewWidth, Fix64 viewHeight)
    {
        if (_context is null) return;
        _coordinateScopes ??= [];
        _coordinateScopes.Push(new CoordinateScopeState(Event.current.mousePosition, _context.InputOrigin,
            GUIUtility.currentViewWidth, GUIUtility.currentViewHeight));
        Event.current.mousePosition = mousePosition;
        _context.InputOrigin = inputOrigin;
        GUIUtility.currentViewWidth = Fix64.Max(1, viewWidth);
        GUIUtility.currentViewHeight = Fix64.Max(1, viewHeight);
    }

    private static void PopCoordinateScope()
    {
        if (_context is null || _coordinateScopes is null || _coordinateScopes.Count == 0) return;
        var state = _coordinateScopes.Pop();
        Event.current.mousePosition = state.MousePosition;
        _context.InputOrigin = state.InputOrigin;
        GUIUtility.currentViewWidth = state.ViewWidth;
        GUIUtility.currentViewHeight = state.ViewHeight;
    }

    private static Vector2 HandleAndDrawScrollbars(Rect viewport, Vector2 scroll, Rect content,
        GUIStyle horizontalStyle, GUIStyle horizontalThumbStyle,
        GUIStyle verticalStyle, GUIStyle verticalThumbStyle)
    {
        var verticalId = GUIUtility.GetControlID(
            "VerticalScrollbar".GetHashCode(StringComparison.Ordinal), FocusType.Passive, viewport);
        var horizontalId = GUIUtility.GetControlID(
            "HorizontalScrollbar".GetHashCode(StringComparison.Ordinal), FocusType.Passive, viewport);
        ReleaseScrollbarOnMouseUp(verticalId);
        ReleaseScrollbarOnMouseUp(horizontalId);
        var verticalRange = Fix64.Max(0, content.height - viewport.height);
        if (verticalRange > 0)
        {
            var trackWidth = verticalStyle.fixedWidth > 0 ? verticalStyle.fixedWidth : (Fix64)9;
            trackWidth = Fix64.Clamp(trackWidth, 1, viewport.width);
            var track = new Rect(viewport.xMax - trackWidth, viewport.y, trackWidth, viewport.height);
            var minimumThumbHeight = verticalThumbStyle.fixedHeight > 0
                ? verticalThumbStyle.fixedHeight : Fix64.Min(24, viewport.height);
            var thumbHeight = Fix64.Min(viewport.height,
                Fix64.Max(Fix64.Min(minimumThumbHeight, viewport.height),
                    viewport.height * viewport.height / content.height));
            var thumbY = viewport.y + scroll.y / verticalRange * Fix64.Max(0, viewport.height - thumbHeight);
            var thumbWidth = verticalThumbStyle.fixedWidth > 0
                ? Fix64.Min(track.width, verticalThumbStyle.fixedWidth)
                : Fix64.Max(1, track.width - 4);
            var thumb = new Rect(track.x + (track.width - thumbWidth) / 2, thumbY, thumbWidth, thumbHeight);
            scroll = new Vector2(scroll.x,
                HandleScrollbar(verticalId, track, thumb, scroll.y, verticalRange, true));
            var trackHovered = IsRectHovered(track);
            DrawStyleBackground(track,
                ResolveStyleState(verticalStyle, false, GUIUtility.hotControl == verticalId, false, trackHovered),
                verticalStyle.borderWidth);
            DrawStyleBackground(thumb, ResolveStyleState(verticalThumbStyle, false,
                    GUIUtility.hotControl == verticalId, false, IsScrollbarHovered(verticalId, thumb)),
                verticalThumbStyle.borderWidth);
        }
        var horizontalRange = Fix64.Max(0, content.width - viewport.width);
        if (horizontalRange > 0)
        {
            var trackHeight = horizontalStyle.fixedHeight > 0 ? horizontalStyle.fixedHeight : (Fix64)9;
            trackHeight = Fix64.Clamp(trackHeight, 1, viewport.height);
            var track = new Rect(viewport.x, viewport.yMax - trackHeight, viewport.width, trackHeight);
            var minimumThumbWidth = horizontalThumbStyle.fixedWidth > 0
                ? horizontalThumbStyle.fixedWidth : Fix64.Min(24, viewport.width);
            var thumbWidth = Fix64.Min(viewport.width,
                Fix64.Max(Fix64.Min(minimumThumbWidth, viewport.width),
                    viewport.width * viewport.width / content.width));
            var thumbX = viewport.x + scroll.x / horizontalRange * Fix64.Max(0, viewport.width - thumbWidth);
            var thumbHeight = horizontalThumbStyle.fixedHeight > 0
                ? Fix64.Min(track.height, horizontalThumbStyle.fixedHeight)
                : Fix64.Max(1, track.height - 4);
            var thumb = new Rect(thumbX, track.y + (track.height - thumbHeight) / 2, thumbWidth, thumbHeight);
            scroll = new Vector2(
                HandleScrollbar(horizontalId, track, thumb, scroll.x, horizontalRange, false), scroll.y);
            var trackHovered = IsRectHovered(track);
            DrawStyleBackground(track, ResolveStyleState(horizontalStyle, false,
                    GUIUtility.hotControl == horizontalId, false, trackHovered), horizontalStyle.borderWidth);
            DrawStyleBackground(thumb, ResolveStyleState(horizontalThumbStyle, false,
                    GUIUtility.hotControl == horizontalId, false, IsScrollbarHovered(horizontalId, thumb)),
                horizontalThumbStyle.borderWidth);
        }
        return scroll;
    }

    private static Fix64 HandleScrollbar(int id, Rect track, Rect thumb, Fix64 value,
        Fix64 range, bool vertical)
    {
        if (!enabled || _context is null || range <= 0) return value;
        var evt = Event.current;
        var absoluteTrack = _context.Translate(track);
        var absoluteThumb = _context.Translate(thumb);
        var pointer = PointerPosition;
        var trackContains = absoluteTrack.Contains(pointer) && PointerInsideClip(pointer);
        var thumbContains = absoluteThumb.Contains(pointer) && PointerInsideClip(pointer);
        var trackLength = vertical ? track.height : track.width;
        var thumbLength = vertical ? thumb.height : thumb.width;
        var travel = Fix64.Max(0, trackLength - thumbLength);
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when evt.button == 0 && trackContains:
                GUIUtility.hotControl = id;
                if (!thumbContains && travel > 0)
                {
                    var pointerAxis = vertical ? pointer.y : pointer.x;
                    var trackAxis = vertical ? absoluteTrack.y : absoluteTrack.x;
                    value = Fix64.Clamp((pointerAxis - trackAxis - thumbLength / 2) / travel * range,
                        0, range);
                    changed = true;
                }
                evt.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                if (travel > 0)
                {
                    var delta = vertical ? evt.delta.y : evt.delta.x;
                    var next = Fix64.Clamp(value + delta / travel * range, 0, range);
                    if (next != value) changed = true;
                    value = next;
                }
                evt.Use();
                break;
        }
        return value;
    }

    private static void ReleaseScrollbarOnMouseUp(int id)
    {
        if (GUIUtility.hotControl != id) return;
        var evt = Event.current;
        if (evt.GetTypeForControl(id) != EventType.MouseUp) return;
        GUIUtility.hotControl = 0;
        evt.Use();
    }

    private static bool IsScrollbarHovered(int id, Rect thumb)
    {
        if (GUIUtility.hotControl == id) return true;
        if (_context is null) return false;
        var pointer = PointerPosition;
        return _context.Translate(thumb).Contains(pointer) && PointerInsideClip(pointer);
    }

    private static bool IsRectHovered(Rect rect)
    {
        if (_context is null) return false;
        var pointer = PointerPosition;
        return _context.Translate(rect).Contains(pointer) && PointerInsideClip(pointer);
    }

    private static GUIStyleState ResolveStyleState(GUIStyle style, bool on, bool active,
        bool focused, bool hovered)
    {
        if (!enabled) return style.disabled;
        if (on)
            return active ? style.onActive : focused ? style.onFocused : hovered ? style.onHover : style.onNormal;
        return active ? style.active : focused ? style.focused : hovered ? style.hover : style.normal;
    }

    private static Rect AlignTextRect(Rect rect, string text, GUIStyle style)
    {
        if (style.alignment is TextAnchor.UpperLeft or TextAnchor.MiddleLeft or TextAnchor.LowerLeft)
            return rect;
        var textWidth = string.IsNullOrEmpty(text) ? Fix64.Zero : Fix64.Min(rect.width,
            GUITextMetrics.MeasureRenderedAdvance(text, style.fontSize, GUIUtility.fontFamily,
                _context?.ScaleFactor ?? Fix64.One));
        var x = style.alignment is TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter
            ? rect.x + (rect.width - textWidth) / 2
            : rect.xMax - textWidth;
        return new Rect(x, rect.y, textWidth, rect.height);
    }

    private static Rect GetContentTextRect(Rect rect, string text, GUIStyle style,
        bool background, bool hasImage, Fix64 leadingTextOffset = default)
    {
        var inset = background ? TextHorizontalInset : Fix64.Zero;
        var desiredLeading = hasImage
            ? (Fix64)ContentImageTextOffset
            : Fix64.Max(inset, Fix64.Max(0, leadingTextOffset));
        var leading = Fix64.Min(Fix64.Max(0, rect.width), desiredLeading);
        var trailing = Fix64.Min(Fix64.Max(0, rect.width - leading),
            hasImage ? (Fix64)2 : inset);
        var contentRect = new Rect(rect.x + leading, rect.y,
            Fix64.Max(0, rect.width - leading - trailing), rect.height);
        return AlignTextRect(contentRect, text, style);
    }

    private static int ClosestTextBoundary(string text, Fix64 localX, GUIStyle style)
    {
        if (text.Length == 0 || localX <= 0) return 0;
        var previous = Fix64.Zero;
        for (var index = 1; index <= text.Length; index++)
        {
            var next = TextBoundaryOffset(text, index, style);
            if (localX < (previous + next) / 2) return index - 1;
            previous = next;
        }
        return text.Length;
    }

    private static Fix64 TextBoundaryOffset(string text, int index, GUIStyle style)
    {
        index = Math.Clamp(index, 0, text.Length);
        return index == 0 ? Fix64.Zero :
            GUITextMetrics.MeasureRenderedAdvance(text[..index], style.fontSize,
                GUIUtility.fontFamily, _context?.ScaleFactor ?? Fix64.One);
    }

    private static void DrawStyleBackground(Rect rect, GUIStyleState state, Fix64 borderWidth)
    {
        var tint = state.backgroundColor * backgroundColor;
        if (GUIStyleBackground.IsSegmentedButton(state.backgroundImage))
        {
            if (tint.a > 0) DrawRect(rect, tint);
            var highlight = state.textColor;
            DrawRect(new Rect(rect.x, rect.y, rect.width, Fix64.One),
                new Color(highlight.r, highlight.g, highlight.b, Fix64.FromDecimal(.04m)));
            var separator = state.borderColor.a > 0
                ? state.borderColor
                : EditorStyles.separator.normal.backgroundColor;
            DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), separator);
        }
        else if (state.backgroundImage is { } image)
            AddCommand(GpuCanvasCommandType.Image, rect, tint.a > 0 ? tint : Color.white,
                GUIStyleBackground.ToRenderSource(image));
        else if (tint.a > 0) DrawRect(rect, tint);
        DrawBorder(rect, state.borderColor, borderWidth);
    }

    private static void DrawBorder(Rect rect, Color borderColor, Fix64 width)
    {
        if (width <= 0 || borderColor.a <= 0 || rect.width <= 0 || rect.height <= 0) return;
        width = Fix64.Min(width, Fix64.Min(rect.width / 2, rect.height / 2));
        DrawRect(new Rect(rect.x, rect.y, rect.width, width), borderColor);
        DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), borderColor);
        DrawRect(new Rect(rect.x, rect.y + width, width, Fix64.Max(0, rect.height - width * 2)), borderColor);
        DrawRect(new Rect(rect.xMax - width, rect.y + width, width,
            Fix64.Max(0, rect.height - width * 2)), borderColor);
    }

    private static readonly Fix64 TextHorizontalInset = 4;

    internal sealed class ImGuiContext(
        Fix64 width,
        Fix64 height,
        Fix64 scaleFactor,
        List<GpuCanvasCommand> commands,
        Dictionary<int, TextState> texts)
    {
        private readonly Stack<Rect> _groups = new();
        private readonly Stack<GpuCanvasRect> _clips = new();
        private readonly Dictionary<int, TextState> _texts = texts;
        public List<GpuCanvasCommand> Commands { get; } = commands;
        public string Tooltip { get; set; } = string.Empty;
        public string NextControlName { get; set; } = string.Empty;
        public string FocusRequest { get; set; } = string.Empty;
        public string FocusedName { get; set; } = string.Empty;
        public Fix64 ScaleFactor { get; } = scaleFactor;
        public Vector2 InputOrigin { get; set; }
        public GpuCanvasRect Clip => _clips.Count > 0 ? _clips.Peek() : new(0, 0, (float)width, (float)height);
        public GpuCanvasRect Scale(Rect rect) => new((float)(rect.x * ScaleFactor),
            (float)(rect.y * ScaleFactor), (float)(rect.width * ScaleFactor),
            (float)(rect.height * ScaleFactor));
        public GpuCanvasRect Scale(GpuCanvasRect rect) => new(rect.X * (float)ScaleFactor,
            rect.Y * (float)ScaleFactor, rect.Width * (float)ScaleFactor, rect.Height * (float)ScaleFactor);
        public Rect Translate(Rect rect)
        {
            var offset = _groups.Count > 0 ? _groups.Peek().position : Vector2.zero;
            return new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
        }
        public void PushGroup(Rect rect)
        {
            var absolute = Translate(rect); _groups.Push(absolute);
            _clips.Push(GpuCanvasRect.Intersect(Clip, new((float)absolute.x, (float)absolute.y,
                (float)absolute.width, (float)absolute.height)));
            GUIUtility.BeginContainer(HashCode.Combine(absolute.x, absolute.y, absolute.width, absolute.height));
        }
        public void PushScrollView(Rect viewport, Vector2 scroll)
        {
            var absoluteViewport = Translate(viewport);
            var content = new Rect(absoluteViewport.x - scroll.x, absoluteViewport.y - scroll.y,
                viewport.width, viewport.height);
            _groups.Push(content);
            _clips.Push(GpuCanvasRect.Intersect(Clip, new((float)absoluteViewport.x,
                (float)absoluteViewport.y, (float)absoluteViewport.width, (float)absoluteViewport.height)));
            GUIUtility.BeginContainer(HashCode.Combine(absoluteViewport.x, absoluteViewport.y,
                absoluteViewport.width, absoluteViewport.height));
        }
        public void PopGroup() { if (_groups.Count > 0) _groups.Pop(); if (_clips.Count > 0) _clips.Pop(); GUIUtility.EndContainer(); }
        public void PushClip(Rect rect)
        {
            var absolute = Translate(rect); _clips.Push(GpuCanvasRect.Intersect(Clip,
                new((float)absolute.x, (float)absolute.y, (float)absolute.width, (float)absolute.height)));
        }
        public void PopClip() { if (_clips.Count > 0) _clips.Pop(); }
        public object CaptureStructuralState() => new ImGuiStructuralState(
            _groups.Reverse().ToArray(), _clips.Reverse().ToArray(), InputOrigin);
        public void RestoreStructuralState(object state)
        {
            if (state is not ImGuiStructuralState structural) return;
            _groups.Clear();
            foreach (var group in structural.Groups) _groups.Push(group);
            _clips.Clear();
            foreach (var clip in structural.Clips) _clips.Push(clip);
            InputOrigin = structural.InputOrigin;
        }
        public TextState GetText(int id, string fallback) => _texts.GetValueOrDefault(id,
            new(fallback, fallback.Length, fallback.Length, fallback));
        public void SetText(int id, TextState state) => _texts[id] = state;
        public string AssignNextControlName(int id)
        {
            if (string.IsNullOrEmpty(NextControlName)) return string.Empty;
            var name = NextControlName;
            NextControlName = string.Empty;
            if (FocusRequest.Equals(name, StringComparison.Ordinal))
            {
                FocusRequest = string.Empty;
                _pendingFocusControlName = null;
                if (GUIUtility.keyboardControl != id)
                {
                    _texts.Remove(id);
                    if (_activeTextControl == id)
                    {
                        _activeTextControl = 0;
                        _activeTextState = null;
                    }
                }
                GUIUtility.keyboardControl = id;
            }
            if (GUIUtility.keyboardControl == id) FocusedName = name;
            return name;
        }
    }

    internal readonly struct WindowScope(bool active) : IDisposable
    {
        public void Dispose()
        {
            if (!active) return;
            GUILayout.EndContainer();
            PopCoordinateScope();
            _context?.PopGroup();
        }
    }

    internal readonly struct FeatureIsolationScope(
        ImGuiContext? context,
        object? contextState,
        object? layoutState,
        object? editorGuiState,
        int commandCount,
        CoordinateScopeState[] coordinateStates,
        int[] containerScopes,
        Vector2 mousePosition,
        EventType eventType,
        Fix64 viewWidth,
        Fix64 viewHeight,
        int hotControl,
        int keyboardControl,
        int activeTextControl,
        TextState? activeTextState,
        MouseCursor requestedMouseCursor,
        bool wasEnabled,
        bool wasChanged,
        int guiDepth,
        Color guiColor,
        Color guiBackgroundColor,
        Color guiContentColor,
        bool editorWideMode,
        string tooltip,
        string nextControlName,
        string focusRequest,
        string? pendingFocusControlName,
        string focusedName)
    {
        internal void Restore(bool succeeded)
        {
            if (context is null || !ReferenceEquals(_context, context)) return;
            GUILayout.RestoreState(layoutState);
            context.RestoreStructuralState(contextState!);
            if (editorGuiState is not null)
                EditorGUI.RestoreFeatureState(editorGuiState, restorePopupSelections: !succeeded);
            EditorGUIUtility.wideMode = editorWideMode;
            _coordinateScopes ??= [];
            _coordinateScopes.Clear();
            foreach (var state in coordinateStates) _coordinateScopes.Push(state);
            Event.current.mousePosition = mousePosition;
            GUIUtility.currentViewWidth = viewWidth;
            GUIUtility.currentViewHeight = viewHeight;
            GUIUtility.RestoreContainerScopes(containerScopes);
            enabled = wasEnabled;
            depth = guiDepth;
            color = guiColor;
            backgroundColor = guiBackgroundColor;
            contentColor = guiContentColor;
            if (succeeded) return;
            if (context.Commands.Count > commandCount)
                context.Commands.RemoveRange(commandCount, context.Commands.Count - commandCount);
            changed = wasChanged;
            Event.current.type = eventType;
            GUIUtility.hotControl = hotControl;
            GUIUtility.keyboardControl = keyboardControl;
            _activeTextControl = activeTextControl;
            _activeTextState = activeTextState;
            _requestedMouseCursor = requestedMouseCursor;
            context.Tooltip = tooltip;
            context.NextControlName = nextControlName;
            context.FocusRequest = focusRequest;
            _pendingFocusControlName = pendingFocusControlName;
            context.FocusedName = focusedName;
        }
    }

    private readonly record struct ImGuiStructuralState(
        Rect[] Groups,
        GpuCanvasRect[] Clips,
        Vector2 InputOrigin);

    internal readonly record struct CoordinateScopeState(
        Vector2 MousePosition,
        Vector2 InputOrigin,
        Fix64 ViewWidth,
        Fix64 ViewHeight);

    internal readonly record struct TextState(string Text, int Caret, int Anchor, string ObservedValue);
}
