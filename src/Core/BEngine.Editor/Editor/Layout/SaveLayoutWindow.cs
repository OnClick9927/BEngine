namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
internal sealed class SaveLayoutWindow : EditorWindow
{
    private Action<string>? _accepted;
    private string _layoutName = "Layout";
    private string _fieldLabel = "Layout Name";
    private string _actionLabel = "Save";

    public static void Open(string currentName, Action<string> accepted) =>
        OpenCore("Save Layout", "Save editor layout", "Save",
            currentName.Equals(EditorLayoutStore.LastSessionName, StringComparison.OrdinalIgnoreCase)
                ? "Custom Layout" : currentName,
            accepted);

    public static void OpenRename(string currentName, Action<string> accepted) =>
        OpenCore("Rename Layout", "Rename editor layout", "Rename", currentName, accepted);

    private static void OpenCore(
        string title,
        string tooltip,
        string actionLabel,
        string currentName,
        Action<string> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var window = CreateWindow<SaveLayoutWindow>();
        window.saveToLayout = false;
        window.titleContent = new GUIContent(title, "Icons/Windows/Window.png", tooltip);
        window._layoutName = EditorLayoutStore.NormalizeName(currentName);
        window._actionLabel = actionLabel;
        window._accepted = accepted;
        window.position = new Rect(240, 180, 420, 130);
        window.minSize = new Vector2(360, 120);
        window.maxSize = new Vector2(700, 180);
        window.ShowAuxWindow();
        window.Focus();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        _layoutName = EditorGUILayout.TextField(_fieldLabel, _layoutName);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
        if (GUILayout.Button(_actionLabel, GUILayout.Width(80))) Accept();
        GUILayout.EndHorizontal();
    }

    private void Accept()
    {
        if (_accepted is { } accepted)
            EditorFeatureGuard.Invoke(this, nameof(Accept),
                () => accepted(EditorLayoutStore.NormalizeName(_layoutName)));
        Close();
    }
}
