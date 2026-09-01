namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetAtlas.png")]
public sealed class TextureAtlasWindow : EditorWindow
{
    private TextureAtlas _atlas = new();
    private string _assetPath = string.Empty;
    private bool _dirty;
    private string _message = string.Empty;
    private Vector2 _sourceScroll;
    private Sprite? _pendingSource;

    public TextureAtlasWindow()
    {
        titleContent = new GUIContent("Texture Atlas", EditorBuiltinIcons.Assets.Atlas);
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
        else if (Selection.activeObject is Sprite sprite)
        {
            _pendingSource = sprite;
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
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save,
                    "Save texture atlas", GUILayout.Width(26)))
                SaveAtlas();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh,
                "Reload texture atlas", GUILayout.Width(26)))
            LoadAtlas();
        _assetPath = EditorGUILayout.TextField("Atlas", _assetPath);
        if (GUILayout.Button("Load", GUILayout.Width(60))) LoadAtlas();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (GUILayout.Button("Build", GUILayout.Width(64))) BuildAtlas();
        GUILayout.EndHorizontal();
    }

    private void DrawSettings()
    {
        GUILayout.Label("Packing", EditorStyles.boldLabel);
        var maxSize = PreviousPowerOfTwo(Math.Clamp(
            EditorGUILayout.IntField("Max Size", _atlas.MaxSize), 32, 16384));
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
        GUILayout.Label("Sprites", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        _pendingSource = EditorGUILayout.ObjectField("Sprite", _pendingSource,
            typeof(Sprite), false) as Sprite;
        using (new EditorGUI.DisabledScope(_pendingSource is null))
            if (GUILayout.Button("Add", GUILayout.Width(52)))
            {
                AddSource(_pendingSource!);
                _pendingSource = null;
            }
        if (GUILayout.Button("Add Selected", GUILayout.Width(96))) AddSelected();
        GUILayout.EndHorizontal();

        var viewportHeight = Fix64.Max(220, GUIUtility.currentViewHeight - 255);
        var viewport = GUILayoutUtility.GetControlRect(viewportHeight);
        var rowHeight = (Fix64)27;
        var contentWidth = Fix64.Max(590, viewport.width - 14);
        var contentHeight = Fix64.Max(viewportHeight, (_atlas.Sources.Length + 1) * rowHeight + 4);
        _sourceScroll = GUI.BeginScrollView(viewport, _sourceScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        GUI.Label(new Rect(4, 2, contentWidth - 34, rowHeight - 2),
            "Sprite Asset", EditorStyles.miniLabel);

        var removeIndex = -1;
        for (var index = 0; index < _atlas.Sources.Length; index++)
        {
            var source = _atlas.Sources[index];
            var y = (index + 1) * rowHeight;
            var selected = EditorGUI.ObjectField(
                new Rect(4, y, contentWidth - 34, rowHeight - 3),
                source, typeof(Sprite), false) as Sprite;
            if (selected is not null && !ReferenceEquals(selected, source) &&
                !ContainsSource(selected, index))
            {
                _atlas.Sources[index] = selected;
                _dirty = true;
            }
            if (GUI.Button(new Rect(contentWidth - 24, y, 22, rowHeight - 3),
                    new GUIContent(EditorBuiltinIcons.Toolbar.Delete, tooltip: $"Remove {source.name}"),
                    EditorStyles.toolbarIconButton))
                removeIndex = index;
        }
        GUI.EndScrollView();

        if (removeIndex >= 0)
        {
            _atlas.Sources = _atlas.Sources.Where((_, index) => index != removeIndex).ToArray();
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
        else
        {
            GUI.Box(rect, new GUIContent("No generated atlas"), EditorStyles.helpBox);
        }
    }

    private void AddSelected()
    {
        var sprites = Selection.objects.OfType<Sprite>().ToList();
        foreach (var texture in Selection.objects.OfType<Texture>())
            if (AssetDatabase.LoadAssetAtPath<Sprite>(texture.assetPath) is { } sprite)
                sprites.Add(sprite);
        foreach (var sprite in sprites) AddSource(sprite);
    }

    private void AddSource(Sprite sprite)
    {
        if (ContainsSource(sprite)) return;
        _atlas.Sources = [.. _atlas.Sources, sprite];
        _dirty = true;
    }

    private bool ContainsSource(Sprite sprite, int ignoredIndex = -1)
    {
        var identity = TextureAtlas.SourceIdentity(sprite);
        return _atlas.Sources.Where((_, index) => index != ignoredIndex)
            .Any(source => TextureAtlas.SourceIdentity(source)
                .Equals(identity, StringComparison.OrdinalIgnoreCase));
    }

    private void NewAtlas()
    {
        _atlas = new TextureAtlas();
        _assetPath = string.Empty;
        _pendingSource = null;
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
        catch (Exception exception)
        {
            _message = exception.Message;
        }
    }

    private void SaveAtlas()
    {
        try
        {
            EditorAssetWritePolicy.EnsureCanWrite("Saving texture atlases");
            EnsureAssetPath();
            _atlas.Save(ResolvePath(_assetPath));
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
            _dirty = false;
            _message = "Atlas saved";
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
    }

    private void BuildAtlas()
    {
        try
        {
            EditorAssetWritePolicy.EnsureCanWrite("Building texture atlases");
            EnsureAssetPath();
            var result = TextureAtlasBuilder.Build(_atlas, ResolvePath(_assetPath));
            _assetPath = result.AtlasAssetPath;
            _dirty = false;
            _message = $"Built {result.SpriteCount} sprites at {result.Width} x {result.Height}";
            Repaint();
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
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

    private static string ResolvePreviewPath(string path)
    {
        if (path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
            BAsset.Load<Texture>(path) is { } texture)
        {
            var importedPath = string.IsNullOrWhiteSpace(texture.artifactPath)
                ? texture.sourcePath
                : texture.artifactPath;
            if (File.Exists(importedPath)) return importedPath;
        }
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(EditorApplication.projectPath, path.Replace('/', Path.DirectorySeparatorChar));
    }
}
