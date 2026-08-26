using BEngine.Editor.Rendering;

namespace BEngine.Editor;

public static class GUI
{
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
    public static string tooltip => _context?.Tooltip ?? string.Empty;
    internal static bool isEditingTextField => _activeTextControl != 0 &&
        GUIUtility.keyboardControl == _activeTextControl;

    internal static void BeginFrame(Event inputEvent, int width, int height, List<GpuCanvasCommand> commands)
    {
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
        Event.ClearCurrent();
    }

    public static void Label(Rect position, string text) => Label(position, new GUIContent(text), skin.label);
    public static void Label(Rect position, GUIContent content, GUIStyle? style = null) =>
        DrawContent(position, content, style ?? skin.label, false, false);
    public static void Box(Rect position, string text = "") => Box(position, new GUIContent(text), skin.box);
    public static void Box(Rect position, GUIContent content, GUIStyle? style = null) =>
        DrawContent(position, content, style ?? skin.box, true, false);

    public static bool Button(Rect position, string text) => Button(position, new GUIContent(text), skin.button);
    public static bool Button(Rect position, GUIContent content, GUIStyle? style = null)
    {
        var id = GUIUtility.GetControlID(content.text.GetHashCode(StringComparison.Ordinal), FocusType.Keyboard, position);
        var pressed = DoButton(id, position);
        DrawContent(position, content, style ?? skin.button, true, GUIUtility.hotControl == id,
            GUIUtility.keyboardControl == id);
        return pressed;
    }

    public static bool Toggle(Rect position, bool value, string text) =>
        Toggle(position, value, new GUIContent(text), skin.toggle);
    public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle? style = null)
    {
        var id = GUIUtility.GetControlID(content.text.GetHashCode(StringComparison.Ordinal), FocusType.Keyboard, position);
        if (DoButton(id, position)) { value = !value; changed = true; }
        var boxSize = Fix64.Clamp(position.height - 6, 13, 16);
        var box = new Rect(position.x, position.y + (position.height - boxSize) / 2, boxSize, boxSize);
        DrawRect(box, value ? EditorAppearance.palette.Accent : EditorAppearance.palette.Field);
        DrawBorder(box, EditorAppearance.palette.Border, 1);
        if (value)
            AddCommand(GpuCanvasCommandType.Image,
                new Rect(box.x + 1, box.y + 1, Fix64.Max(0, box.width - 2), Fix64.Max(0, box.height - 2)),
                enabled ? Color.white : EditorAppearance.palette.DisabledText,
                EditorBuiltinIcons.Toolbar.Check);
        Label(new Rect(position.x + boxSize + 6, position.y,
                Fix64.Max(0, position.width - boxSize - 6), position.height), content,
            style ?? skin.toggle);
        return value;
    }

    public static string TextField(Rect position, string text, int maxLength = -1, GUIStyle? style = null) =>
        DoTextField(position, text, maxLength, false, style ?? skin.textField);
    public static string TextArea(Rect position, string text, int maxLength = -1, GUIStyle? style = null) =>
        DoTextField(position, text, maxLength, true, style ?? skin.textArea);
    public static string PasswordField(Rect position, string password, char maskChar = '*', int maxLength = -1,
        GUIStyle? style = null)
    {
        var value = DoTextField(position, password, maxLength, false, style ?? skin.textField, maskChar);
        return value;
    }

    public static Fix64 HorizontalSlider(Rect position, Fix64 value, Fix64 leftValue, Fix64 rightValue) =>
        Slider(position, value, leftValue, rightValue, false);
    public static Fix64 VerticalSlider(Rect position, Fix64 value, Fix64 topValue, Fix64 bottomValue) =>
        Slider(position, value, topValue, bottomValue, true);
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
    {
        if (_context is null) return scrollPosition;
        var viewport = _context.Translate(position);
        var pointer = PointerPosition;
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
        scrollPosition = HandleAndDrawScrollbars(position, scrollPosition, viewRect);

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

    private static bool DoButton(int id, Rect rect)
    {
        if (!enabled) return false;
        var evt = Event.current;
        var absolute = _context?.Translate(rect) ?? rect;
        var contains = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when contains && evt.button == 0:
                GUIUtility.hotControl = id; GUIUtility.keyboardControl = id; evt.Use(); return false;
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
        if (enabled && evt.type == EventType.MouseDown && absolute.Contains(PointerPosition) &&
            PointerInsideClip(PointerPosition) && evt.button == 0)
        {
            GUIUtility.keyboardControl = id;
            var visibleValue = mask is null ? value : new string(mask.Value, value.Length);
            var pointer = PointerPosition;
            var localX = Fix64.Max(0, pointer.x - absolute.x - 5);
            var characterWidth = Fix64.Max(1, style.fontSize * Fix64.FromDecimal(0.58m));
            var clickedIndex = Math.Clamp((int)Math.Round((double)(localX / characterWidth)), 0,
                visibleValue.Length);
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
        if (focused && Event.current.type == EventType.Repaint && state.Caret != state.Anchor)
        {
            var start = Math.Min(state.Caret, state.Anchor);
            var length = Math.Abs(state.Caret - state.Anchor);
            var characterWidth = style.fontSize * Fix64.FromDecimal(0.58m);
            DrawRect(new Rect(rect.x + 5 + characterWidth * start, rect.y + 3,
                characterWidth * length, Fix64.Max(0, rect.height - 6)),
                EditorAppearance.palette.Selection);
        }
        if (focused && state.Caret != state.Anchor) DrawContent(rect, displayed, style, false, false, true);
        if (focused && Event.current.type == EventType.Repaint &&
            (Environment.TickCount64 - _caretBlinkStart) / 500 % 2 == 0)
        {
            var caretX = (double)rect.x + 5 + Math.Min((double)(rect.width - 8),
                state.Caret * (double)style.fontSize * 0.58);
            DrawRect(new Rect((Fix64)caretX, rect.y + 3, 1, Fix64.Max(0, rect.height - 6)),
                EditorAppearance.palette.Text);
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

    private static Fix64 Slider(Rect rect, Fix64 value, Fix64 first, Fix64 second, bool vertical)
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
        var track = vertical
            ? new Rect(rect.x + (rect.width - 4) / 2, rect.y, 4, rect.height)
            : new Rect(rect.x, rect.y + (rect.height - 4) / 2, rect.width, 4);
        DrawRect(track, EditorAppearance.palette.ScrollTrack);
        var thumb = vertical
            ? new Rect(rect.x, rect.y + normalized * Fix64.Max(0, rect.height - 10), rect.width, 10)
            : new Rect(rect.x + normalized * Fix64.Max(0, rect.width - 10), rect.y, 10, rect.height);
        var hovered = contains || GUIUtility.hotControl == id;
        DrawRect(thumb, hovered ? EditorAppearance.palette.ScrollThumbHover : EditorAppearance.palette.ScrollThumb);
        DrawBorder(thumb, EditorAppearance.palette.Border, 1);
        return value;
    }

    private static void DrawContent(Rect rect, GUIContent content, GUIStyle style, bool background, bool active,
        bool focused = false)
    {
        if (_context is null || Event.current.type != EventType.Repaint) return;
        var absolute = _context?.Translate(rect) ?? rect;
        var hovered = absolute.Contains(PointerPosition) && PointerInsideClip(PointerPosition);
        var state = !enabled ? style.disabled : active ? style.active : focused ? style.focused :
            hovered ? style.hover : style.normal;
        if (background && state.backgroundColor.a > 0)
        {
            DrawRect(rect, state.backgroundColor * backgroundColor);
            DrawBorder(rect, state.borderColor, style.borderWidth);
        }
        var hasImage = !string.IsNullOrWhiteSpace(content.image);
        if (hasImage)
        {
            var iconSize = Fix64.Min(16, Fix64.Max(0, rect.height - 4));
            AddCommand(GpuCanvasCommandType.Image,
                new Rect(rect.x + 3, rect.y + (rect.height - iconSize) / 2, iconSize, iconSize),
                enabled ? Color.white : EditorAppearance.palette.DisabledText, content.image);
        }
        var inset = background ? (Fix64)4 : Fix64.Zero;
        var textRect = hasImage
            ? new Rect(rect.x + 22, rect.y, Fix64.Max(0, rect.width - 24), rect.height)
            : new Rect(rect.x + inset, rect.y, Fix64.Max(0, rect.width - inset * 2), rect.height);
        textRect = AlignTextRect(textRect, content.text, style);
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
        DrawRect(panel, EditorAppearance.palette.PanelRaised);
        DrawBorder(panel, EditorAppearance.palette.Border, 1);
        AddCommand(GpuCanvasCommandType.Text,
            new Rect(panel.x + 6, panel.y + 3, Fix64.Max(1, panel.width - 12), panel.height - 6),
            EditorAppearance.palette.Text, value, (float)fontSize);
    }

    private static bool PointerInsideClip(Vector2 pointer)
    {
        if (_context is null) return true;
        var clip = _context.Clip;
        return pointer.x >= (Fix64)clip.X && pointer.y >= (Fix64)clip.Y &&
               pointer.x <= (Fix64)(clip.X + clip.Width) && pointer.y <= (Fix64)(clip.Y + clip.Height);
    }

    private static void AddCommand(GpuCanvasCommandType type, Rect rect, Color commandColor,
        string content = "", float fontSize = 13)
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

    private static Vector2 HandleAndDrawScrollbars(Rect viewport, Vector2 scroll, Rect content)
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
            var track = new Rect(viewport.xMax - 9, viewport.y, 9, viewport.height);
            var thumbHeight = Fix64.Min(viewport.height,
                Fix64.Max(Fix64.Min(24, viewport.height), viewport.height * viewport.height / content.height));
            var thumbY = viewport.y + scroll.y / verticalRange * Fix64.Max(0, viewport.height - thumbHeight);
            var thumb = new Rect(track.x + 2, thumbY, 5, thumbHeight);
            scroll = new Vector2(scroll.x,
                HandleScrollbar(verticalId, track, thumb, scroll.y, verticalRange, true));
            DrawRect(track, EditorAppearance.palette.ScrollTrack);
            DrawRect(thumb, IsScrollbarHovered(verticalId, thumb)
                ? EditorAppearance.palette.ScrollThumbHover : EditorAppearance.palette.ScrollThumb);
        }
        var horizontalRange = Fix64.Max(0, content.width - viewport.width);
        if (horizontalRange > 0)
        {
            var track = new Rect(viewport.x, viewport.yMax - 9, viewport.width, 9);
            var thumbWidth = Fix64.Min(viewport.width,
                Fix64.Max(Fix64.Min(24, viewport.width), viewport.width * viewport.width / content.width));
            var thumbX = viewport.x + scroll.x / horizontalRange * Fix64.Max(0, viewport.width - thumbWidth);
            var thumb = new Rect(thumbX, track.y + 2, thumbWidth, 5);
            scroll = new Vector2(
                HandleScrollbar(horizontalId, track, thumb, scroll.x, horizontalRange, false), scroll.y);
            DrawRect(track, EditorAppearance.palette.ScrollTrack);
            DrawRect(thumb, IsScrollbarHovered(horizontalId, thumb)
                ? EditorAppearance.palette.ScrollThumbHover : EditorAppearance.palette.ScrollThumb);
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

    private static Rect AlignTextRect(Rect rect, string text, GUIStyle style)
    {
        if (string.IsNullOrEmpty(text) || style.alignment is TextAnchor.UpperLeft or
            TextAnchor.MiddleLeft or TextAnchor.LowerLeft) return rect;
        var textWidth = Fix64.Min(rect.width,
            GUITextMetrics.MeasureWidth(text, style.fontSize, GUIUtility.fontFamily));
        var x = style.alignment is TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter
            ? rect.x + (rect.width - textWidth) / 2
            : rect.xMax - textWidth;
        return new Rect(x, rect.y, textWidth, rect.height);
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
