namespace BEngine.Editor;

public static class EditorToolbar
{
    public static bool Button(Rect position, GUIContent content) => Button(position, content, null);

    public static bool Button(Rect position, GUIContent content, GUIStyle? style) =>
        GUI.Button(position, content, style ?? EditorStyles.toolbarIconButton);

    public static bool Toggle(Rect position, bool value, GUIContent content) =>
        Toggle(position, value, content, null, null);

    public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle? style) =>
        Toggle(position, value, content, style, style);

    public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle? style,
        GUIStyle? selectedStyle)
    {
        var resolved = value
            ? selectedStyle ?? EditorStyles.toolbarIconButtonSelected
            : style ?? EditorStyles.toolbarIconButton;
        if (GUI.Button(position, content, resolved)) value = !value;
        return value;
    }

    public static bool Button(GUIContent content, params GUILayoutOption[] options) =>
        Button(content, null, options);

    public static bool Button(GUIContent content, GUIStyle? style, params GUILayoutOption[] options) =>
        GUILayout.Button(content, style ?? EditorStyles.toolbarIconButton, options);

    public static bool Toggle(bool value, GUIContent content, params GUILayoutOption[] options) =>
        Toggle(value, content, null, null, options);

    public static bool Toggle(bool value, GUIContent content, GUIStyle? style,
        params GUILayoutOption[] options) => Toggle(value, content, style, style, options);

    public static bool Toggle(bool value, GUIContent content, GUIStyle? style,
        GUIStyle? selectedStyle, params GUILayoutOption[] options)
    {
        var resolved = value
            ? selectedStyle ?? EditorStyles.toolbarIconButtonSelected
            : style ?? EditorStyles.toolbarIconButton;
        if (GUILayout.Button(content, resolved, options)) value = !value;
        return value;
    }

    public static bool IconButton(string icon, string tooltip, params GUILayoutOption[] options) =>
        IconButton(icon, tooltip, null, options);

    public static bool IconButton(string icon, string tooltip, GUIStyle? style,
        params GUILayoutOption[] options) => Button(new GUIContent(string.Empty, icon, tooltip), style, options);

    public static bool IconToggle(bool value, string icon, string tooltip,
        params GUILayoutOption[] options) => IconToggle(value, icon, tooltip, null, null, options);

    public static bool IconToggle(bool value, string icon, string tooltip, GUIStyle? style,
        params GUILayoutOption[] options) => IconToggle(value, icon, tooltip, style, style, options);

    public static bool IconToggle(bool value, string icon, string tooltip, GUIStyle? style,
        GUIStyle? selectedStyle, params GUILayoutOption[] options) =>
        Toggle(value, new GUIContent(string.Empty, icon, tooltip), style, selectedStyle, options);

    public static string SearchField(string value, params GUILayoutOption[] options) =>
        DrawSearchField(value, string.Empty, GUI.skin.toolbarSearchField, options);

    public static string SearchField(string value, GUIStyle? style, params GUILayoutOption[] options) =>
        DrawSearchField(value, string.Empty, style ?? GUI.skin.toolbarSearchField, options);

    public static string SearchField(string value, string placeholder,
        params GUILayoutOption[] options) =>
        DrawSearchField(value, placeholder, GUI.skin.toolbarSearchField, options);

    public static string SearchField(string value, string placeholder, GUIStyle? style,
        params GUILayoutOption[] options) =>
        DrawSearchField(value, placeholder, style ?? GUI.skin.toolbarSearchField, options);

    private static string DrawSearchField(string value, string placeholder, GUIStyle style,
        GUILayoutOption[] options)
    {
        GUILayout.BeginHorizontal(options);
        GUILayout.Label(new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Search, "Search"),
            GUILayout.Width(22));
        var clearWidth = string.IsNullOrEmpty(value) ? Fix64.Zero : (Fix64)26;
        value = GUILayout.TextField(value, style, GUILayout.Width(Fix64.Max(30,
            GUILayout.CurrentGroupWidth - 34 - clearWidth)));
        var fieldRect = GUILayoutUtility.GetLastRect();
        if (string.IsNullOrEmpty(value) && !string.IsNullOrWhiteSpace(placeholder))
            GUI.Label(new Rect(fieldRect.x + 5, fieldRect.y,
                    Fix64.Max(0, fieldRect.width - 10), fieldRect.height),
                new GUIContent(placeholder), EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(value) && IconButton(EditorBuiltinIcons.Toolbar.Clear, "Clear search",
                GUILayout.Width(22))) value = string.Empty;
        GUILayout.EndHorizontal();
        return value;
    }
}
