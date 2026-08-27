namespace BEngine.Editor;

public static class GUILayout
{
    [ThreadStatic] private static LayoutContext? _context;
    [ThreadStatic] private static Rect _lastRect;
    internal static void BeginFrame(Rect area) => _context = new LayoutContext(area);
    internal static void EndFrame() => _context = null;
    public static void BeginHorizontal(params GUILayoutOption[] options) =>
        _lastRect = Context.BeginGroup(true, DefaultControlHeight, options);
    public static void BeginHorizontal(GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.box;
        _lastRect = Context.BeginGroup(true, StyleHeight(style, DefaultControlHeight), options);
        GUI.Box(new Rect(0, 0, _lastRect.width, _lastRect.height), GUIContent.none, style);
    }
    public static void EndHorizontal() => Context.EndGroup();
    public static void BeginVertical(params GUILayoutOption[] options) =>
        _lastRect = Context.BeginGroup(false, DefaultControlHeight, options);
    public static void BeginVertical(GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.box;
        _lastRect = Context.BeginGroup(false, StyleHeight(style, DefaultControlHeight), options);
        GUI.Box(new Rect(0, 0, _lastRect.width, _lastRect.height), GUIContent.none, style);
    }
    public static void EndVertical() => Context.EndGroup();
    public static void Space(Fix64 pixels) => Context.Space(pixels);
    public static void FlexibleSpace() => Context.Space(8);
    public static void Label(string text, params GUILayoutOption[] options) =>
        Label(text, (GUIStyle?)null, options);
    public static void Label(GUIContent content, params GUILayoutOption[] options) =>
        Label(content, (GUIStyle?)null, options);
    public static void Label(string text, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.label;
        GUI.Label(Next(StyleHeight(style, DefaultControlHeight), options), new GUIContent(text), style);
    }
    public static void Label(GUIContent content, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.label;
        GUI.Label(Next(StyleHeight(style, DefaultControlHeight), options), content, style);
    }
    public static void Box(string text, params GUILayoutOption[] options) =>
        Box(text, (GUIStyle?)null, options);
    public static void Box(GUIContent content, params GUILayoutOption[] options) =>
        Box(content, (GUIStyle?)null, options);
    public static void Box(string text, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.box;
        GUI.Box(Next(StyleHeight(style, DefaultControlHeight + 2), options), new GUIContent(text), style);
    }
    public static void Box(GUIContent content, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.box;
        GUI.Box(Next(StyleHeight(style, DefaultControlHeight + 2), options), content, style);
    }
    public static bool Button(string text, params GUILayoutOption[] options) =>
        Button(text, (GUIStyle?)null, options);
    public static bool Button(GUIContent content, params GUILayoutOption[] options) =>
        Button(content, (GUIStyle?)null, options);
    public static bool Button(string text, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.button;
        return GUI.Button(Next(StyleHeight(style, DefaultControlHeight), options), new GUIContent(text), style);
    }
    public static bool Button(GUIContent content, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.button;
        return GUI.Button(Next(StyleHeight(style, DefaultControlHeight), options), content, style);
    }
    public static bool Toggle(bool value, string text, params GUILayoutOption[] options) =>
        Toggle(value, text, (GUIStyle?)null, options);
    public static bool Toggle(bool value, GUIContent content, params GUILayoutOption[] options) =>
        Toggle(value, content, (GUIStyle?)null, options);
    public static bool Toggle(bool value, string text, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.toggle;
        return GUI.Toggle(Next(StyleHeight(style, DefaultControlHeight), options), value, text, style);
    }
    public static bool Toggle(bool value, GUIContent content, GUIStyle? style,
        params GUILayoutOption[] options)
    {
        style ??= GUI.skin.toggle;
        return GUI.Toggle(Next(StyleHeight(style, DefaultControlHeight), options), value, content, style);
    }
    public static string TextField(string text, params GUILayoutOption[] options) =>
        TextField(text, (GUIStyle?)null, options);
    public static string TextField(string text, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.textField;
        return GUI.TextField(Next(StyleHeight(style, DefaultControlHeight), options), text, style: style);
    }
    public static string TextArea(string text, params GUILayoutOption[] options) =>
        TextArea(text, (GUIStyle?)null, options);
    public static string TextArea(string text, GUIStyle? style, params GUILayoutOption[] options)
    {
        style ??= GUI.skin.textArea;
        return GUI.TextArea(Next(StyleHeight(style, 100), options), text, style: style);
    }
    public static string PasswordField(string password, char maskChar = '*', int maxLength = -1,
        params GUILayoutOption[] options) => PasswordField(
        password, maskChar, maxLength, null, options);
    public static string PasswordField(string password, GUIStyle? style,
        params GUILayoutOption[] options) => PasswordField(password, '*', -1, style, options);
    public static string PasswordField(string password, char maskChar, int maxLength, GUIStyle? style,
        params GUILayoutOption[] options)
    {
        style ??= GUI.skin.textField;
        return GUI.PasswordField(Next(StyleHeight(style, DefaultControlHeight), options), password,
            maskChar, maxLength, style);
    }
    public static Fix64 HorizontalSlider(Fix64 value, Fix64 leftValue, Fix64 rightValue,
        params GUILayoutOption[] options) =>
        HorizontalSlider(value, leftValue, rightValue, null, null, options);
    public static Fix64 HorizontalSlider(Fix64 value, Fix64 leftValue, Fix64 rightValue,
        GUIStyle? slider, params GUILayoutOption[] options) =>
        HorizontalSlider(value, leftValue, rightValue, slider, null, options);
    public static Fix64 HorizontalSlider(Fix64 value, Fix64 leftValue, Fix64 rightValue,
        GUIStyle? slider, GUIStyle? thumb, params GUILayoutOption[] options)
    {
        slider ??= GUI.skin.horizontalSlider;
        thumb ??= GUI.skin.horizontalSliderThumb;
        return GUI.HorizontalSlider(Next(StyleHeight(slider, 18), options), value, leftValue, rightValue,
            slider, thumb);
    }
    public static Fix64 VerticalSlider(Fix64 value, Fix64 topValue, Fix64 bottomValue,
        params GUILayoutOption[] options) =>
        VerticalSlider(value, topValue, bottomValue, null, null, options);
    public static Fix64 VerticalSlider(Fix64 value, Fix64 topValue, Fix64 bottomValue,
        GUIStyle? slider, params GUILayoutOption[] options) =>
        VerticalSlider(value, topValue, bottomValue, slider, null, options);
    public static Fix64 VerticalSlider(Fix64 value, Fix64 topValue, Fix64 bottomValue,
        GUIStyle? slider, GUIStyle? thumb, params GUILayoutOption[] options)
    {
        slider ??= GUI.skin.verticalSlider;
        thumb ??= GUI.skin.verticalSliderThumb;
        return GUI.VerticalSlider(Next(StyleHeight(slider, 100), options), value, topValue, bottomValue,
            slider, thumb);
    }
    public static RectUtilityScope Area(Rect screenRect) => new(screenRect);
    public static RectUtilityScope Area(Rect screenRect, GUIStyle? style) => new(screenRect, style);
    public static void BeginArea(Rect screenRect) { GUI.BeginGroup(screenRect); Context.BeginArea(screenRect); }
    public static void BeginArea(Rect screenRect, GUIStyle? style)
    {
        style ??= GUI.skin.box;
        BeginArea(screenRect);
        GUI.Box(new Rect(0, 0, screenRect.width, screenRect.height), GUIContent.none, style);
    }
    public static void EndArea() { Context.EndArea(); GUI.EndGroup(); }
    public static GUILayoutOption Width(Fix64 width) => new(GUILayoutOptionType.Width, width);
    public static GUILayoutOption Height(Fix64 height) => new(GUILayoutOptionType.Height, height);
    public static GUILayoutOption MinWidth(Fix64 width) => new(GUILayoutOptionType.MinWidth, width);
    public static GUILayoutOption MaxWidth(Fix64 width) => new(GUILayoutOptionType.MaxWidth, width);
    public static GUILayoutOption MinHeight(Fix64 height) => new(GUILayoutOptionType.MinHeight, height);
    public static GUILayoutOption MaxHeight(Fix64 height) => new(GUILayoutOptionType.MaxHeight, height);
    public static GUILayoutOption ExpandWidth(bool expand) => new(GUILayoutOptionType.ExpandWidth, expand ? 1 : 0);
    public static GUILayoutOption ExpandHeight(bool expand) => new(GUILayoutOptionType.ExpandHeight, expand ? 1 : 0);
    internal static Rect Next(Fix64 defaultHeight, GUILayoutOption[] options)
    {
        _lastRect = Context.Next(defaultHeight, options);
        return _lastRect;
    }
    internal static Rect LastRect => _lastRect;
    internal static void BeginContainer(Rect rect) => Context.BeginArea(rect);
    internal static void EndContainer() => Context.EndArea();
    internal static object? CaptureState() => _context?.CaptureState();
    internal static void RestoreState(object? state)
    {
        if (_context is not null && state is not null) _context.RestoreState(state);
    }
    internal static Fix64 CurrentContentHeight => Context.ContentHeight;
    internal static Fix64 CurrentGroupWidth => Context.Width;
    private static Fix64 StyleHeight(GUIStyle style, Fix64 fallback) =>
        style.fixedHeight > 0 ? style.fixedHeight : fallback;
    internal static Fix64 DefaultControlHeight => Fix64.Max(22,
        GUITextMetrics.MeasureLineHeight(GUI.skin.label.fontSize, GUIUtility.fontFamily));
    private static LayoutContext Context => _context ?? throw new InvalidOperationException("GUILayout is only valid during OnGUI.");

    public readonly struct RectUtilityScope : IDisposable
    {
        internal RectUtilityScope(Rect rect) => BeginArea(rect);
        internal RectUtilityScope(Rect rect, GUIStyle? style) => BeginArea(rect, style);
        public void Dispose() => EndArea();
    }

    private sealed class LayoutContext(Rect root)
    {
        private readonly Stack<Group> _groups = new();
        public LayoutContext() : this(default) { }
        public Rect BeginGroup(bool horizontal, Fix64 defaultHeight, GUILayoutOption[] options)
        {
            var parent = Current;
            var rect = parent.Next(defaultHeight, options);
            _groups.Push(new Group(rect, horizontal));
            GUI.BeginGroup(rect);
            return rect;
        }
        public void EndGroup() { if (_groups.Count > 1) { _groups.Pop(); GUI.EndGroup(); } }
        public void BeginArea(Rect rect) => _groups.Push(new Group(new Rect(0, 0, rect.width, rect.height), false));
        public void EndArea() { if (_groups.Count > 1) _groups.Pop(); }
        public Rect Next(Fix64 defaultHeight, GUILayoutOption[] options) => Current.Next(defaultHeight, options);
        public void Space(Fix64 pixels) => Current.Space(pixels);
        public Fix64 ContentHeight => Current.ContentHeight;
        public Fix64 Width => Current.Width;
        public object CaptureState() => _groups.Reverse().ToArray();
        public void RestoreState(object state)
        {
            if (state is not Group[] groups) return;
            _groups.Clear();
            foreach (var group in groups) _groups.Push(group);
        }
        private Group Current
        {
            get
            {
                if (_groups.Count == 0) _groups.Push(new Group(root, false));
                return _groups.Peek();
            }
        }
    }

    private sealed class Group(Rect rect, bool horizontal)
    {
        private Fix64 _cursorX = 4;
        private Fix64 _cursorY = horizontal ? Fix64.Zero : (Fix64)4;
        private Fix64 _contentHeight = 4;
        public Rect Next(Fix64 defaultHeight, IEnumerable<GUILayoutOption> options)
        {
            var width = horizontal ? (Fix64)100 : Fix64.Max(0, rect.width - 8);
            var height = defaultHeight;
            foreach (var option in options)
            {
                if (option.Type == GUILayoutOptionType.Width) width = option.Value;
                if (option.Type == GUILayoutOptionType.Height) height = option.Value;
                if (option.Type == GUILayoutOptionType.MinWidth) width = Fix64.Max(width, option.Value);
                if (option.Type == GUILayoutOptionType.MaxWidth) width = Fix64.Min(width, option.Value);
                if (option.Type == GUILayoutOptionType.MinHeight) height = Fix64.Max(height, option.Value);
                if (option.Type == GUILayoutOptionType.MaxHeight) height = Fix64.Min(height, option.Value);
                if (option.Type == GUILayoutOptionType.ExpandWidth && option.Value > 0)
                    width = horizontal ? Fix64.Max(0, rect.width - _cursorX - 4) :
                        Fix64.Max(0, rect.width - 8);
                if (option.Type == GUILayoutOptionType.ExpandHeight && option.Value > 0)
                    height = Fix64.Max(height, rect.height - _cursorY - 4);
            }
            var result = new Rect(_cursorX, _cursorY, width, height);
            _contentHeight = Fix64.Max(_contentHeight, result.yMax + 3);
            if (horizontal) _cursorX += width + 4; else _cursorY += height + 3;
            return result;
        }
        public void Space(Fix64 pixels)
        {
            if (horizontal) _cursorX += pixels;
            else
            {
                _cursorY += pixels;
                _contentHeight = Fix64.Max(_contentHeight, _cursorY);
            }
        }
        public Fix64 ContentHeight => _contentHeight;
        public Fix64 Width => rect.width;
    }
}
