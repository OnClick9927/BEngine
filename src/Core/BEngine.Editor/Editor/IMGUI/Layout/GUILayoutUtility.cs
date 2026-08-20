namespace BEngine.Editor;

public static class GUILayoutUtility
{
    public static Rect GetControlRect(Fix64 height, params GUILayoutOption[] options) =>
        GUILayout.Next(height, options);
    public static Rect GetRect(Fix64 width, Fix64 height, params GUILayoutOption[] options) =>
        GUILayout.Next(height, [GUILayout.Width(width), .. options]);
    public static Rect GetRect(Fix64 minWidth, Fix64 maxWidth, Fix64 minHeight, Fix64 maxHeight,
        params GUILayoutOption[] options) => GUILayout.Next(minHeight,
        [GUILayout.MinWidth(minWidth), GUILayout.MaxWidth(maxWidth), GUILayout.MinHeight(minHeight),
            GUILayout.MaxHeight(maxHeight), .. options]);
    public static Rect GetRect(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
    {
        var size = style.CalcSize(content);
        return GUILayout.Next(size.y, [GUILayout.MinWidth(size.x), .. options]);
    }
    public static Rect GetLastRect() => GUILayout.LastRect;
}
