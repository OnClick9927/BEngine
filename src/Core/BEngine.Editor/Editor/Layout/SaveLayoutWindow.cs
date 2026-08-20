namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
internal sealed class SaveLayoutWindow : EditorWindow
{
    private Action<string>? _accepted;
    private string _layoutName = "Layout";

    public static void Open(string currentName, Action<string> accepted)
    {
        var window = CreateWindow<SaveLayoutWindow>();
        window.saveToLayout = false;
        window.titleContent = new GUIContent("Save Layout", "Icons/Windows/Window.png", "Save editor layout");
        window._layoutName = currentName.Equals("Last Session", StringComparison.OrdinalIgnoreCase)
            ? "Layout" : currentName;
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
        _layoutName = EditorGUILayout.TextField("Layout Name", _layoutName);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
        if (GUILayout.Button("Save", GUILayout.Width(80))) Accept();
        GUILayout.EndHorizontal();
    }

    private void Accept()
    {
        if (_accepted is { } accepted)
            EditorFeatureGuard.Invoke(this, "Accept", () => accepted(EditorLayoutStore.NormalizeName(_layoutName)));
        Close();
    }
}
