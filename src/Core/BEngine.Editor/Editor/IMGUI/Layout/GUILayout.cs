namespace BEngine.Editor;

public static class GUILayout
{
    [ThreadStatic] private static LayoutContext? _context;
    [ThreadStatic] private static Rect _lastRect;
    internal static void BeginFrame(Rect area) => _context = new LayoutContext(area);
    internal static void EndFrame() => _context = null;
    public static void BeginHorizontal(params GUILayoutOption[] options) =>
        _lastRect = Context.BeginGroup(true, options);
    public static void EndHorizontal() => Context.EndGroup();
    public static void BeginVertical(params GUILayoutOption[] options) =>
        _lastRect = Context.BeginGroup(false, options);
    public static void EndVertical() => Context.EndGroup();
    public static void Space(Fix64 pixels) => Context.Space(pixels);
    public static void FlexibleSpace() => Context.Space(8);
    public static void Label(string text, params GUILayoutOption[] options) =>
        GUI.Label(Next(DefaultControlHeight, options), text);
    public static void Label(GUIContent content, params GUILayoutOption[] options) =>
        GUI.Label(Next(DefaultControlHeight, options), content);
    public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) =>
        GUI.Label(Next(style.fixedHeight > 0 ? style.fixedHeight : DefaultControlHeight, options),
            new GUIContent(text), style);
    public static void Label(GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
        GUI.Label(Next(style.fixedHeight > 0 ? style.fixedHeight : DefaultControlHeight, options), content, style);
    public static void Box(string text, params GUILayoutOption[] options) =>
        GUI.Box(Next(DefaultControlHeight + 2, options), text);
    public static bool Button(string text, params GUILayoutOption[] options) =>
        GUI.Button(Next(DefaultControlHeight, options), text);
    public static bool Button(GUIContent content, params GUILayoutOption[] options) =>
        GUI.Button(Next(DefaultControlHeight, options), content);
    public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) =>
        GUI.Button(Next(style.fixedHeight > 0 ? style.fixedHeight : DefaultControlHeight, options),
            new GUIContent(text), style);
    public static bool Button(GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
        GUI.Button(Next(style.fixedHeight > 0 ? style.fixedHeight : DefaultControlHeight, options), content, style);
    public static bool Toggle(bool value, string text, params GUILayoutOption[] options) =>
        GUI.Toggle(Next(DefaultControlHeight, options), value, text);
    public static bool Toggle(bool value, GUIContent content, params GUILayoutOption[] options) =>
        GUI.Toggle(Next(DefaultControlHeight, options), value, content);
    public static string TextField(string text, params GUILayoutOption[] options) =>
        GUI.TextField(Next(DefaultControlHeight, options), text);
    public static string TextField(string text, GUIStyle style, params GUILayoutOption[] options) =>
        GUI.TextField(Next(style.fixedHeight > 0 ? style.fixedHeight : DefaultControlHeight, options), text,
            style: style);
    public static string TextArea(string text, params GUILayoutOption[] options) =>
        GUI.TextArea(Next(100, options), text);
    public static Fix64 HorizontalSlider(Fix64 value, Fix64 leftValue, Fix64 rightValue,
        params GUILayoutOption[] options) => GUI.HorizontalSlider(Next(18, options), value, leftValue, rightValue);
    public static RectUtilityScope Area(Rect screenRect) => new(screenRect);
    public static void BeginArea(Rect screenRect) { GUI.BeginGroup(screenRect); Context.BeginArea(screenRect); }
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
    private static Fix64 DefaultControlHeight => Fix64.Max(22,
        GUITextMetrics.MeasureLineHeight(GUI.skin.label.fontSize, GUIUtility.fontFamily));
    private static LayoutContext Context => _context ?? throw new InvalidOperationException("GUILayout is only valid during OnGUI.");

    public readonly struct RectUtilityScope : IDisposable
    {
        internal RectUtilityScope(Rect rect) => BeginArea(rect);
        public void Dispose() => EndArea();
    }

    private sealed class LayoutContext(Rect root)
    {
        private readonly Stack<Group> _groups = new();
        public LayoutContext() : this(default) { }
        public Rect BeginGroup(bool horizontal, GUILayoutOption[] options)
        {
            var parent = Current;
            var rect = parent.Next(DefaultControlHeight, options);
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
