namespace BEngine.Editor;

public static class EditorGUIUtility
{
    public static Fix64 singleLineHeight => Fix64.Max(18,
        GUITextMetrics.MeasureLineHeight(GUI.skin.label.fontSize, GUIUtility.fontFamily));
    public static Fix64 standardVerticalSpacing => (Fix64)2;
    public static bool isProSkin => EditorAppearance.isDarkTheme;
    public static Fix64 pixelsPerPoint => GUIUtility.pixelsPerPoint;
    public static bool wideMode { get; set; } = true;
    public static bool editingTextField => GUI.isEditingTextField;
    public static Fix64 currentViewWidth => GUIUtility.currentViewWidth;
    public static string systemCopyBuffer
    {
        get => GUIUtility.systemCopyBuffer;
        set => GUIUtility.systemCopyBuffer = value ?? string.Empty;
    }

    public static GUIContent IconContent(string name, string? text = null)
    {
        name ??= string.Empty;
        var resolved = EditorBuiltinIcons.Resolve(name);
        var resourcePath = resolved.Contains('/') || resolved.Contains('\\') || Path.HasExtension(resolved)
            ? resolved
            : $"Icons/Windows/{resolved}.png";
        return new GUIContent(text ?? string.Empty, resourcePath, name);
    }
    public static void PingObject(BObject target) => EditorUtility.PingObject(target);
    public static void PingObject(int instanceId) => EditorUtility.PingObject(instanceId);
    public static void AddCursorRect(Rect position, MouseCursor mouse) => GUI.AddCursorRect(position, mouse);
}
