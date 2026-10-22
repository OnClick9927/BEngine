namespace BEngine.Editor;

internal sealed class PlayerBuildProfileNameWindow : EditorWindow
{
    private string _value = string.Empty;
    private Func<string, bool>? _accept;
    private string _error = string.Empty;

    public PlayerBuildProfileNameWindow() => saveToLayout = false;

    internal static void Open(
        string title,
        string initialValue,
        Rect anchor,
        Func<string, bool> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        var window = CreateWindow<PlayerBuildProfileNameWindow>();
        window.titleContent = new GUIContent(title, EditorBuiltinIcons.Toolbar.New, title);
        window._value = initialValue;
        window._accept = accept;
        window.minSize = new Vector2(300, 116);
        window.maxSize = new Vector2(460, 116);
        window.ShowAsDropDown(anchor, new Vector2(360, 116));
        window.Focus();
    }

    protected override void OnGUI()
    {
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        GUI.Label(new Rect(12, 10, width - 24, 20), "Profile Name", EditorStyles.boldLabel);
        GUI.SetNextControlName("BuildProfileName");
        _value = GUI.TextField(new Rect(12, 34, width - 24, 24), _value);
        if (_error.Length > 0)
            GUI.Label(new Rect(12, 61, width - 126, 20), _error, EditorStyles.miniLabel);
        if (GUI.Button(new Rect(width - 110, 72, 46, 28), "Cancel")) Close();
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_value)))
            if (GUI.Button(new Rect(width - 58, 72, 46, 28), "OK")) Accept();

        if (Event.current.type != EventType.KeyDown) return;
        if (Event.current.keyCode == KeyCode.Escape)
        {
            Event.current.Use();
            Close();
        }
        else if (Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
        {
            Event.current.Use();
            Accept();
        }
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_value) || _accept is null) return;
        try
        {
            if (_accept(_value.Trim())) Close();
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            Repaint();
        }
    }
}
