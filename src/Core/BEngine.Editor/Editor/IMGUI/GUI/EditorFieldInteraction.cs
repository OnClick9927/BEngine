using System.Globalization;

namespace BEngine.Editor;

/// <summary>Shared value clipboard and label scrubbing for immediate-mode fields.</summary>
internal static class EditorFieldInteraction
{
    private static readonly Dictionary<int, object?> Pending = [];
    private static object? _copied;
    private static Type? _copiedType;
    private static string _copiedText = string.Empty;
    private static double _dragValue;
    private static double _dragStep;

    internal static T Context<T>(Rect rect, T value, Func<T, bool>? validate = null,
        bool readOnly = false)
    {
        var id = GUIUtility.GetControlID(typeof(T).GetHashCode(), FocusType.Passive);
        if (Pending.Remove(id, out var pending) && GUI.enabled && !readOnly &&
            (pending is T || pending is null && default(T) is null))
        {
            var next = (T)pending!;
            if (validate?.Invoke(next) != false) { value = next; GUI.changed = true; }
        }
        var evt = Event.current;
        if (evt.type != EventType.ContextClick || !GUI.IsRectHovered(rect)) return value;
        var menu = new GenericMenu();
        var captured = value;
        menu.AddItem(new GUIContent("Copy"), false, () =>
        {
            _copied = captured;
            _copiedType = typeof(T);
            _copiedText = Format(captured);
            GUIUtility.systemCopyBuffer = _copiedText;
        });
        if (GUI.enabled && !readOnly && TryRead<T>(out var paste) && validate?.Invoke(paste) != false)
            menu.AddItem(new GUIContent("Paste"), false, () =>
            {
                Pending[id] = paste;
                EditorApplication.QueuePlayerLoopUpdate();
            });
        else menu.AddDisabledItem(new GUIContent("Paste"));
        menu.ShowAsContext();
        evt.Use();
        return value;
    }

    internal static double Drag(Rect label, double value, bool integer = false)
    {
        var id = GUIUtility.GetControlID("NumericLabel".GetHashCode(StringComparison.Ordinal), FocusType.Passive);
        var evt = Event.current;
        if (GUI.enabled) EditorGUIUtility.AddCursorRect(label, MouseCursor.SlideArrow);
        if (GUI.enabled && evt.type == EventType.MouseDown && evt.button == 0 && GUI.IsRectHovered(label))
        {
            GUIUtility.hotControl = id;
            GUIUtility.keyboardControl = 0;
            _dragValue = value;
            _dragStep = integer ? 1 : Math.Max(0.01, Math.Sqrt(Math.Abs(value)) * 0.03);
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            if (GUI.enabled)
            {
                _dragValue += (double)evt.delta.x * _dragStep * (evt.shift ? 10 : evt.alt ? 0.1 : 1);
                var next = integer ? Math.Round(_dragValue, MidpointRounding.AwayFromZero) : _dragValue;
                if (double.IsFinite(next) && next != value) { value = next; GUI.changed = true; }
            }
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == id)
        {
            GUIUtility.hotControl = 0;
            evt.Use();
        }
        return value;
    }

    private static string Format<T>(T value) => value switch
    {
        Vector2 v => FormattableString.Invariant($"{v.x},{v.y}"),
        Vector4 v => FormattableString.Invariant($"{v.x},{v.y},{v.z},{v.w}"),
        Color c => FormattableString.Invariant($"{c.r},{c.g},{c.b},{c.a}"),
        BObject obj => obj.name,
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value?.ToString() ?? "null"
    };

    private static bool TryRead<T>(out T value)
    {
        var text = GUIUtility.systemCopyBuffer;
        if (_copiedType is not null && text == _copiedText &&
            (_copied is T || _copied is null && default(T) is null && typeof(T).IsAssignableFrom(_copiedType)))
        { value = (T)_copied!; return true; }
        try
        {
            object? parsed;
            if (typeof(T) == typeof(Vector2) || typeof(T) == typeof(Vector4) || typeof(T) == typeof(Color))
            {
                var parts = text.Trim('(', ')', ' ').Split(',');
                var count = typeof(T) == typeof(Vector2) ? 2 : 4;
                if (parts.Length != count) { value = default!; return false; }
                var n = parts.Select(part => Fix64.Parse(part.Trim())).ToArray();
                parsed = typeof(T) == typeof(Vector2) ? (object)new Vector2(n[0], n[1]) :
                    typeof(T) == typeof(Vector4) ? new Vector4(n[0], n[1], n[2], n[3]) :
                    new Color(n[0], n[1], n[2], n[3]);
            }
            else parsed = Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
            if (parsed is float f && !float.IsFinite(f) || parsed is double d && !double.IsFinite(d))
            { value = default!; return false; }
            value = (T)parsed!;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        { value = default!; return false; }
    }
}
