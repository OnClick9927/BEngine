using System.Globalization;

namespace BEngine.Editor;

public static class EditorToolbar
{
    public static bool Button(Rect position, GUIContent content) =>
        GUI.Button(position, content, EditorStyles.toolbarIconButton);

    public static bool Toggle(Rect position, bool value, GUIContent content)
    {
        if (GUI.Button(position, content, value ? EditorStyles.toolbarIconButtonSelected :
                EditorStyles.toolbarIconButton)) value = !value;
        return value;
    }

    public static bool Button(GUIContent content, params GUILayoutOption[] options) =>
        GUILayout.Button(content, EditorStyles.toolbarIconButton, options);

    public static bool Toggle(bool value, GUIContent content, params GUILayoutOption[] options)
    {
        if (GUILayout.Button(content, value ? EditorStyles.toolbarIconButtonSelected :
                EditorStyles.toolbarIconButton, options)) value = !value;
        return value;
    }

    public static bool IconButton(string icon, string tooltip, params GUILayoutOption[] options) =>
        Button(new GUIContent(string.Empty, icon, tooltip), options);

    public static bool IconToggle(bool value, string icon, string tooltip, params GUILayoutOption[] options) =>
        Toggle(value, new GUIContent(string.Empty, icon, tooltip), options);

    public static string SearchField(string value, params GUILayoutOption[] options) =>
        SearchField(value, string.Empty, options);

    public static string SearchField(string value, string placeholder, params GUILayoutOption[] options)
    {
        GUILayout.BeginHorizontal(options);
        GUILayout.Label(new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Search, "Search"),
            GUILayout.Width(22));
        var clearWidth = string.IsNullOrEmpty(value) ? Fix64.Zero : (Fix64)26;
        value = GUILayout.TextField(value, EditorStyles.toolbarSearchField, GUILayout.Width(Fix64.Max(30,
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
