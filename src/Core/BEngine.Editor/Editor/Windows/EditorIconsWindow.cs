namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetImage.png")]
public sealed class EditorIconsWindow : EditorBuiltinBrowserWindow
{
    private string[] _icons = [];

    [MenuItem("Tools/EditorIcons", false, 281)]
    public static void Open() => GetWindow<EditorIconsWindow>("Editor Icons");

    protected override void OnEnable()
    {
        minSize = new Vector2(400, 300);
        titleContent = new GUIContent("Editor Icons", EditorBuiltinIcons.Assets.Image);
        var logo = EditorResource.FindPath("Icons/BEngine.png");
        if (logo is null) return;
        var directory = EditorHostServices.Paths.GetDirectoryName(logo)!;
        _icons = EditorHostServices.FileSystem.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories)
            .Select(path => "Icons/" + EditorHostServices.Paths.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    protected override void OnGUI()
    {
        DrawSearch("Search icon name or folder");
        var matches = _icons.Where(path => path.Contains(model.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        GUILayout.Label($"{matches.Length} icons · click to copy resource path", EditorStyles.miniLabel);
        if (model.Selected.Length > 0) GUILayout.Label(model.Selected, EditorStyles.wordWrappedMiniLabel);
        model.Scroll = _scroll.Begin(model.Scroll);
        var columns = Math.Max(1, (int)(GUIUtility.currentViewWidth - 24) / 150);
        for (var row = 0; row < matches.Length; row += columns)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(104));
            foreach (var path in matches.Skip(row).Take(columns))
            {
                GUILayout.BeginVertical(GUILayout.Width(142), GUILayout.Height(100));
                var rect = GUILayoutUtility.GetRect(142, 64);
                if (GUI.Button(rect, new GUIContent(string.Empty, tooltip: path), EditorStyles.helpBox))
                { model.Selected = path; GUIUtility.systemCopyBuffer = path; }
                GUI.DrawTexture(new Rect(rect.x + (rect.width - 48) / 2, rect.y + 8, 48, 48), path);
                GUILayout.Label(EditorHostServices.Paths.GetFileNameWithoutExtension(path), EditorStyles.centeredMiniLabel, GUILayout.Height(24));
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }
        _scroll.End();
    }
}
