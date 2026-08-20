namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetImage.png")]
public sealed class TextureAtlasWindow : EditorWindow
{
    private TextureAtlas _atlas = new();
    private string _assetPath = string.Empty;
    private string _sourcePath = string.Empty;
    private bool _dirty;
    private string _message = string.Empty;
    private Vector2 _sourceScroll;

    public TextureAtlasWindow()
    {
        titleContent = new GUIContent("Texture Atlas", EditorBuiltinIcons.Assets.Image, "Texture Atlas");
        minSize = new Vector2(940, 560);
        position = new Rect(120, 90, 1080, 700);
    }

    [MenuItem("Window/2D/Texture Atlas", false, 210)]
    public static void Open() => GetWindow<TextureAtlasWindow>("Texture Atlas", true).ShowAuxWindow();

    protected override void OnSelectionChange()
    {
        var selected = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (selected.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase) &&
            !selected.Equals(_assetPath, StringComparison.OrdinalIgnoreCase))
        {
            _assetPath = selected;
            LoadAtlas();
        }
        Repaint();
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(620));
        DrawSettings();
        DrawSources();
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        DrawPreview();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(StatusText(), EditorStyles.miniLabel);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New texture atlas", GUILayout.Width(26)))
            NewAtlas();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save, "Save texture atlas", GUILayout.Width(26)))
            SaveAtlas();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Reload texture atlas", GUILayout.Width(26)))
            LoadAtlas();
        _assetPath = EditorGUILayout.TextField("Atlas", _assetPath);
        if (GUILayout.Button("Load", GUILayout.Width(60))) LoadAtlas();
        if (GUILayout.Button("Build", GUILayout.Width(64))) BuildAtlas();
        GUILayout.EndHorizontal();
    }

    private void DrawSettings()
    {
        GUILayout.Label("Packing", EditorStyles.boldLabel);
        var maxSize = Math.Clamp(EditorGUILayout.IntField("Max Size", _atlas.MaxSize), 32, 16384);
        maxSize = PreviousPowerOfTwo(maxSize);
        var padding = Math.Clamp(EditorGUILayout.IntField("Padding", _atlas.Padding), 0, 64);
        var extrude = Math.Clamp(EditorGUILayout.IntField("Extrude", _atlas.Extrude), 0, padding);
        if (maxSize != _atlas.MaxSize || padding != _atlas.Padding || extrude != _atlas.Extrude)
        {
            _atlas.MaxSize = maxSize;
            _atlas.Padding = padding;
            _atlas.Extrude = extrude;
            _dirty = true;
        }
        GUILayout.Label(_atlas.Sprites.Count == 0
            ? "Not built"
            : $"Output: {_atlas.Width} x {_atlas.Height} | {_atlas.Sprites.Count} sprites",
            EditorStyles.miniLabel);
    }

    private void DrawSources()
    {
        GUILayout.Space(8);
        GUILayout.Label("Sources", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        _sourcePath = EditorGUILayout.TextField("PNG", _sourcePath);
        if (GUILayout.Button("Add", GUILayout.Width(52))) AddSource(_sourcePath);
        if (GUILayout.Button("Add Selected", GUILayout.Width(96))) AddSelected();
        GUILayout.EndHorizontal();

        var viewportHeight = Fix64.Max(220, GUIUtility.currentViewHeight - 255);
        var viewport = GUILayoutUtility.GetControlRect(viewportHeight);
        var rowHeight = (Fix64)27;
        var contentWidth = Fix64.Max(590, viewport.width - 14);
        var contentHeight = Fix64.Max(viewportHeight, (_atlas.Sources.Count + 1) * rowHeight + 4);
        _sourceScroll = GUI.BeginScrollView(viewport, _sourceScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        var remove = -1;
        GUI.Label(new Rect(4, 2, 126, rowHeight - 2), "Name", EditorStyles.miniLabel);
        GUI.Label(new Rect(134, 2, contentWidth - 278, rowHeight - 2), "Path", EditorStyles.miniLabel);
        GUI.Label(new Rect(contentWidth - 140, 2, 54, rowHeight - 2), "Pivot X", EditorStyles.miniLabel);
        GUI.Label(new Rect(contentWidth - 82, 2, 54, rowHeight - 2), "Pivot Y", EditorStyles.miniLabel);
        for (var index = 0; index < _atlas.Sources.Count; index++)
        {
            var source = _atlas.Sources[index];
            var y = (index + 1) * rowHeight;
            var name = EditorGUI.TextField(new Rect(4, y, 126, rowHeight - 3), source.Name);
            var path = EditorGUI.TextField(new Rect(134, y, contentWidth - 278, rowHeight - 3), source.Path);
            var pivotX = Math.Clamp(EditorGUI.FloatField(
                new Rect(contentWidth - 140, y, 54, rowHeight - 3), source.PivotX), 0, 1);
            var pivotY = Math.Clamp(EditorGUI.FloatField(
                new Rect(contentWidth - 82, y, 54, rowHeight - 3), source.PivotY), 0, 1);
            if (GUI.Button(new Rect(contentWidth - 24, y, 22, rowHeight - 3),
                    new GUIContent(EditorBuiltinIcons.Toolbar.Delete, tooltip: $"Remove {source.Name}"),
                    EditorStyles.toolbarIconButton)) remove = index;
            if (!name.Equals(source.Name, StringComparison.Ordinal) ||
                !path.Equals(source.Path, StringComparison.Ordinal) ||
                pivotX != source.PivotX || pivotY != source.PivotY)
            {
                source.Name = name;
                source.Path = path;
                source.PivotX = pivotX;
                source.PivotY = pivotY;
                _dirty = true;
            }
        }
        GUI.EndScrollView();
        if (remove >= 0)
        {
            _atlas.Sources.RemoveAt(remove);
            _dirty = true;
        }
    }

    private void DrawPreview()
    {
        GUILayout.Label("Atlas Preview", EditorStyles.boldLabel);
        var available = Fix64.Max(180, GUIUtility.currentViewHeight - 170);
        var rect = GUILayoutUtility.GetControlRect(available);
        if (!string.IsNullOrWhiteSpace(_atlas.Texture))
        {
            var atlasWidth = Math.Max(1, _atlas.Width);
            var atlasHeight = Math.Max(1, _atlas.Height);
            var scale = Fix64.Min(rect.width / atlasWidth, rect.height / atlasHeight);
            var previewWidth = atlasWidth * scale;
            var previewHeight = atlasHeight * scale;
            var preview = new Rect(rect.x + (rect.width - previewWidth) * Fix64.Half,
                rect.y + (rect.height - previewHeight) * Fix64.Half, previewWidth, previewHeight);
            GUI.DrawTexture(preview, ResolvePreviewPath(_atlas.Texture));
        }
        else GUI.Box(rect, new GUIContent("No generated atlas"), EditorStyles.helpBox);
    }

    private void AddSelected()
    {
        foreach (var path in Selection.objects.Select(AssetDatabase.GetAssetPath)
                     .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            AddSource(path);
    }

    private void AddSource(string path)
    {
        path = path.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) ||
            _atlas.Sources.Any(source => source.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        _atlas.Sources.Add(new TextureAtlasSource
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path
        });
        _sourcePath = string.Empty;
        _dirty = true;
    }

    private void NewAtlas()
    {
        _atlas = new TextureAtlas();
        _assetPath = string.Empty;
        _dirty = true;
        _message = "New atlas";
    }

    private void LoadAtlas()
    {
        if (string.IsNullOrWhiteSpace(_assetPath)) return;
        try
        {
            _atlas = TextureAtlas.Load(ResolvePath(_assetPath));
            _dirty = false;
            _message = "Atlas loaded";
        }
        catch (Exception exception) { _message = exception.Message; }
    }

    private void SaveAtlas()
    {
        try
        {
            EnsureAssetPath();
            _atlas.Save(ResolvePath(_assetPath));
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
            _dirty = false;
            _message = "Atlas saved";
        }
        catch (Exception exception) { _message = exception.Message; }
    }

    private void BuildAtlas()
    {
        try
        {
            EnsureAssetPath();
            var result = TextureAtlasBuilder.Build(_atlas, ResolvePath(_assetPath));
            _assetPath = result.AtlasAssetPath;
            _dirty = false;
            _message = $"Built {result.SpriteCount} sprites at {result.Width} x {result.Height}";
            Repaint();
        }
        catch (Exception exception) { _message = exception.Message; }
    }

    private void EnsureAssetPath()
    {
        if (!string.IsNullOrWhiteSpace(_assetPath)) return;
        var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
        _assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder.TrimEnd('/', '\\')}/New Texture Atlas.atlas.yaml");
    }

    private string StatusText() =>
        $"{(_dirty ? "Modified" : "Saved")} | " +
        $"{(string.IsNullOrWhiteSpace(_assetPath) ? "New atlas" : _assetPath)}" +
        (string.IsNullOrWhiteSpace(_message) ? string.Empty : $" | {_message}");

    private static int PreviousPowerOfTwo(int value)
    {
        var result = 1;
        while (result <= value / 2) result *= 2;
        return Math.Max(32, result);
    }

    private static string ResolvePath(string path) => Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : AssetDatabase.ResolveAssetPath(path);

    private static string ResolvePreviewPath(string path) => Path.IsPathRooted(path)
        ? path
        : Path.Combine(EditorApplication.projectPath, path.Replace('/', Path.DirectorySeparatorChar));
}
