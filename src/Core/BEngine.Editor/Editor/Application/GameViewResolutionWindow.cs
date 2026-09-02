namespace BEngine.Editor;

internal sealed class GameViewResolutionWindow : EditorWindow
{
    private Action<string, int, int>? _accepted;
    private string _name = "Custom";
    private int _width = 1920;
    private int _height = 1080;
    private string _validationMessage = string.Empty;

    public GameViewResolutionWindow()
    {
        titleContent = new GUIContent("Add Game View Resolution");
        saveToLayout = false;
        minSize = new Vector2(330, 165);
        maxSize = new Vector2(520, 220);
    }

    public static void Open(Rect rootAnchor, Action<string, int, int> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var window = CreateWindow<GameViewResolutionWindow>();
        window._accepted = accepted;
        window.position = new Rect(rootAnchor.x, rootAnchor.yMax, 390, 180);
        window.ShowPopup();
        window.Focus();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        _name = EditorGUILayout.TextField("Name", _name);
        _width = EditorGUILayout.IntField("Width", _width);
        _height = EditorGUILayout.IntField("Height", _height);
        if (_validationMessage.Length > 0)
            GUILayout.Label(_validationMessage, EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(76))) Close();
        if (GUILayout.Button("Add", GUILayout.Width(76))) Accept();
        GUILayout.EndHorizontal();
    }

    private void Accept()
    {
        if (_width is <= 0 or > GameViewResolutionSettings.MaximumDimension ||
            _height is <= 0 or > GameViewResolutionSettings.MaximumDimension)
        {
            _validationMessage = $"Width and height must be between 1 and " +
                                 $"{GameViewResolutionSettings.MaximumDimension}.";
            return;
        }

        var accepted = _accepted;
        _accepted = null;
        accepted?.Invoke(_name, _width, _height);
        Close();
    }
}
