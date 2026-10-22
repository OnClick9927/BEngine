using BEngine.Content;

namespace BEngine.Editor;

internal sealed class PlayerBuildEncryptionWindow : EditorWindow
{
    private string _key = string.Empty;
    private string _confirmation = string.Empty;
    private string _error = string.Empty;
    private bool _show;
    private Action<string>? _accept;

    public PlayerBuildEncryptionWindow() => saveToLayout = false;

    internal static void Open(Action<string> accept)
    {
        var window = CreateWindow<PlayerBuildEncryptionWindow>();
        window.titleContent = new GUIContent("Build Encryption Key");
        window._accept = accept;
        window.minSize = new Vector2(460, 252);
        window.maxSize = new Vector2(460, 252);
        window.ShowAsDropDown(new Rect(180, 100, 460, 1), new Vector2(460, 252));
        window.Focus();
    }

    protected override void OnGUI()
    {
        GUI.Label(new Rect(12, 8, 436, 40),
            "Use the same Key for the main Player and its Hot Resources. Keep it for future updates.", EditorStyles.wordWrappedLabel);
        GUI.Label(new Rect(12, 53, 90, 22), "Key");
        _key = _show ? GUI.TextField(new Rect(104, 52, 344, 24), _key, 1024)
            : GUI.PasswordField(new Rect(104, 52, 344, 24), _key, '*', 1024);
        GUI.Label(new Rect(12, 85, 90, 22), "Confirm Key");
        _confirmation = _show ? GUI.TextField(new Rect(104, 84, 344, 24), _confirmation, 1024)
            : GUI.PasswordField(new Rect(104, 84, 344, 24), _confirmation, '*', 1024);
        _show = GUI.Toggle(new Rect(12, 119, 120, 24), _show, "Show Key");
        if (GUI.Button(new Rect(318, 116, 130, 28), "Generate Key"))
            _confirmation = _key = ContentEncryptionKey.GeneratePassphrase();
        GUI.Label(new Rect(12, 151, 436, 30), "16-1024 UTF-8 bytes. The Key is used for this build only.", EditorStyles.wordWrappedMiniLabel);
        GUI.Label(new Rect(12, 182, 436, 24), _error, EditorStyles.wordWrappedMiniLabel);
        if (GUI.Button(new Rect(262, 214, 88, 28), "Cancel")) Close();
        if (GUI.Button(new Rect(358, 214, 90, 28), "Continue")) Accept();
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        { Event.current.Use(); Close(); }
    }

    private void Accept()
    {
        try
        {
            if (!_key.Equals(_confirmation, StringComparison.Ordinal))
                throw new ArgumentException("The two Keys do not match.");
            _ = ContentEncryptionKey.FromPassphrase(_key);
            var key = _key;
            var accept = _accept;
            _key = _confirmation = string.Empty;
            Close();
            accept?.Invoke(key);
        }
        catch (Exception exception) { _error = exception.Message; Repaint(); }
    }

    protected override void OnDisable() { _key = _confirmation = string.Empty; _accept = null; }
}
