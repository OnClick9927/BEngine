using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

[EditorWindowIcon("Icons/Assets/AssetImage.png")]
public sealed class TilePaletteWindow : EditorWindow
{
    private enum BrushMode { Paint, Erase, Flood }

    private TilePalette _palette = CreatePalette();
    private string _assetPath = string.Empty;
    private int _selectedTileId = 1;
    private int _columns = 1;
    private int _rows = 1;
    private int _boxMinX;
    private int _boxMinY;
    private int _boxMaxX = 1;
    private int _boxMaxY = 1;
    private int _gridRadius = 6;
    private TileTransformFlags _flags;
    private BrushMode _brushMode;
    private Vector2 _paletteScroll;
    private bool _dirty;
    private string _message = string.Empty;

    public TilePaletteWindow()
    {
        titleContent = new GUIContent("Tile Palette", EditorBuiltinIcons.Assets.Image, "Tile Palette");
        minSize = new Vector2(820, 540);
        position = new Rect(100, 80, 980, 680);
    }

    [MenuItem("Window/2D/Tile Palette", false, 220)]
    public static void Open() => GetWindow<TilePaletteWindow>("Tile Palette", true).ShowAuxWindow();

    protected override void OnSelectionChange() => Repaint();

    protected override void OnGUI()
    {
        DrawToolbar();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(310));
        DrawPaletteInspector();
        DrawPaletteList();
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        DrawBrushToolbar();
        DrawTargetGrid();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(StatusText(), EditorStyles.miniLabel);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New tile palette", GUILayout.Width(26)))
            NewPalette();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save, "Save tile palette", GUILayout.Width(26)))
            SavePalette();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Reload tile palette", GUILayout.Width(26)))
            LoadPalette();
        _assetPath = EditorGUILayout.TextField("Palette", _assetPath);
        if (GUILayout.Button("Load", GUILayout.Width(64))) LoadPalette();
        GUILayout.EndHorizontal();
    }

    private void DrawPaletteInspector()
    {
        GUILayout.Label("Palette", EditorStyles.boldLabel);
        var name = EditorGUILayout.TextField("Name", _palette.Name);
        var atlas = EditorGUILayout.TextField("Atlas", _palette.Atlas);
        var columns = Math.Max(1, EditorGUILayout.IntField("Columns", _columns));
        var rows = Math.Max(1, EditorGUILayout.IntField("Rows", _rows));
        if (!name.Equals(_palette.Name, StringComparison.Ordinal) ||
            !atlas.Equals(_palette.Atlas, StringComparison.Ordinal))
        {
            _palette.Name = name;
            _palette.Atlas = atlas;
            _dirty = true;
        }
        _columns = columns;
        _rows = rows;
        if (GUILayout.Button("Slice Atlas"))
        {
            _palette.SliceAtlas(_columns, _rows);
            _selectedTileId = _palette.Tiles.FirstOrDefault()?.Id ?? 0;
            _dirty = true;
        }
        if (_palette.Atlas.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase) &&
            GUILayout.Button("Import Texture Atlas"))
        {
            try
            {
                _palette.ImportAtlas(TextureAtlas.Load(ResolvePath(_palette.Atlas)));
                _selectedTileId = _palette.Tiles.FirstOrDefault()?.Id ?? 0;
                _dirty = true;
                _message = "Texture atlas imported";
            }
            catch (Exception exception) { _message = exception.Message; }
        }

        var selected = _palette.Find(_selectedTileId);
        if (selected is null) return;
        GUILayout.Space(6);
        GUILayout.Label($"Selected Tile {selected.Id}", EditorStyles.boldLabel);
        var tileName = EditorGUILayout.TextField("Tile Name", selected.Name);
        var collider = EditorGUILayout.Toggle("Collider", selected.HasCollider);
        if (!tileName.Equals(selected.Name, StringComparison.Ordinal) || collider != selected.HasCollider)
        {
            selected.Name = tileName;
            selected.HasCollider = collider;
            _dirty = true;
        }
    }

    private void DrawPaletteList()
    {
        GUILayout.Space(6);
        GUILayout.Label("Tiles", EditorStyles.boldLabel);
        var height = Fix64.Max(120, GUIUtility.currentViewHeight - 330);
        var viewport = GUILayoutUtility.GetControlRect(height);
        var rowHeight = (Fix64)24;
        _paletteScroll = GUI.BeginScrollView(viewport, _paletteScroll,
            new Rect(0, 0, Fix64.Max(280, viewport.width - 14),
                Fix64.Max(height, _palette.Tiles.Count * rowHeight + 4)));
        try
        {
            for (var index = 0; index < _palette.Tiles.Count; index++)
            {
                var tile = _palette.Tiles[index];
                var label = $"{tile.Id}: {tile.Name}  [{tile.UvX:0.###}, {tile.UvY:0.###}]";
                var style = tile.Id == _selectedTileId
                    ? EditorStyles.toolbarIconButtonSelected
                    : EditorStyles.toolbarButton;
                if (GUI.Button(new Rect(2, index * rowHeight, Fix64.Max(120, viewport.width - 20),
                        rowHeight - 2), new GUIContent(label), style))
                    _selectedTileId = tile.Id;
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawBrushToolbar()
    {
        var tilemap = SelectedTilemap();
        GUILayout.Label("Tilemap Painting", EditorStyles.boldLabel);
        GUILayout.Label(tilemap is null
            ? "Select a GameObject with a Tilemap component."
            : $"Target: {tilemap.gameObject.name} | Cells: {tilemap.cellCount}", EditorStyles.miniLabel);
        GUILayout.BeginHorizontal();
        DrawModeButton(BrushMode.Paint);
        DrawModeButton(BrushMode.Erase);
        DrawModeButton(BrushMode.Flood);
        GUILayout.Space(10);
        _gridRadius = Math.Clamp(EditorGUILayout.IntField("Radius", _gridRadius, GUILayout.Width(130)), 2, 12);
        GUILayout.EndHorizontal();
        _flags = (TileTransformFlags)EditorGUILayout.EnumPopup("Transform", _flags);
        if (tilemap is not null && !string.IsNullOrWhiteSpace(_assetPath) &&
            !tilemap.paletteAsset.Equals(ToProjectPath(_assetPath), StringComparison.OrdinalIgnoreCase) &&
            GUILayout.Button("Assign Current Palette", GUILayout.Width(180)))
        {
            Edit(tilemap, "Assign Tile Palette", () => tilemap.paletteAsset = ToProjectPath(_assetPath));
        }
    }

    private void DrawTargetGrid()
    {
        var tilemap = SelectedTilemap();
        EditorGUI.BeginDisabledGroup(tilemap is null || _selectedTileId <= 0);
        for (var y = _gridRadius; y >= -_gridRadius; y--)
        {
            GUILayout.BeginHorizontal();
            for (var x = -_gridRadius; x <= _gridRadius; x++)
            {
                var position = new TileCoordinate(x, y);
                var tileId = tilemap?.GetTile(position) ?? 0;
                var label = tileId == 0 ? "." : tileId.ToString();
                if (GUILayout.Button(new GUIContent(label, $"Cell ({x}, {y})"),
                        GUILayout.Width(31), GUILayout.Height(25)) && tilemap is not null)
                    ApplyBrush(tilemap, position);
            }
            GUILayout.EndHorizontal();
        }
        EditorGUI.EndDisabledGroup();

        if (tilemap is null) return;
        GUILayout.Space(6);
        GUILayout.Label("Box Fill", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        _boxMinX = EditorGUILayout.IntField("Min X", _boxMinX, GUILayout.Width(115));
        _boxMinY = EditorGUILayout.IntField("Min Y", _boxMinY, GUILayout.Width(115));
        _boxMaxX = EditorGUILayout.IntField("Max X", _boxMaxX, GUILayout.Width(115));
        _boxMaxY = EditorGUILayout.IntField("Max Y", _boxMaxY, GUILayout.Width(115));
        if (GUILayout.Button("Fill", GUILayout.Width(58))) Edit(tilemap, "Box Fill Tilemap", () =>
            tilemap.BoxFill(new TileCoordinate(_boxMinX, _boxMinY),
                new TileCoordinate(_boxMaxX, _boxMaxY), _selectedTileId, _flags));
        if (GUILayout.Button("Clear All", GUILayout.Width(76)))
            Edit(tilemap, "Clear Tilemap", tilemap.ClearAllTiles);
        GUILayout.EndHorizontal();
    }

    private void DrawModeButton(BrushMode mode)
    {
        if (GUILayout.Button(mode.ToString(), _brushMode == mode
                ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton,
                GUILayout.Width(68))) _brushMode = mode;
    }

    private void ApplyBrush(Tilemap tilemap, TileCoordinate position)
    {
        Edit(tilemap, $"{_brushMode} Tilemap", () =>
        {
            switch (_brushMode)
            {
                case BrushMode.Paint: tilemap.SetTile(position, _selectedTileId, _flags); break;
                case BrushMode.Erase: tilemap.ClearTile(position); break;
                case BrushMode.Flood: tilemap.FloodFill(position, _selectedTileId, _flags); break;
                default: throw new ArgumentOutOfRangeException();
            }
        });
    }

    private static void Edit(Tilemap tilemap, string operation, Action action)
    {
        Undo.RecordObject(tilemap, operation);
        action();
        EditorUtility.SetDirty(tilemap);
        SceneView.RepaintAll();
    }

    private void NewPalette()
    {
        _palette = CreatePalette();
        _assetPath = string.Empty;
        _columns = 1;
        _rows = 1;
        _selectedTileId = 1;
        _dirty = true;
        _message = "New palette";
    }

    private void LoadPalette()
    {
        if (string.IsNullOrWhiteSpace(_assetPath)) return;
        try
        {
            _palette = TilePalette.Load(ResolvePath(_assetPath));
            _columns = _palette.Columns;
            _rows = _palette.Rows;
            _selectedTileId = _palette.Tiles.FirstOrDefault()?.Id ?? 0;
            _dirty = false;
            _message = "Palette loaded";
        }
        catch (Exception exception) { _message = exception.Message; }
    }

    private void SavePalette()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_assetPath))
            {
                var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
                _assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder.TrimEnd('/', '\\')}/New Tile Palette.tilepalette.yaml");
            }
            var fullPath = ResolvePath(_assetPath);
            _palette.Save(fullPath);
            _assetPath = ToProjectPath(fullPath);
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
            _dirty = false;
            _message = "Palette saved";
        }
        catch (Exception exception) { _message = exception.Message; }
    }

    private string StatusText() =>
        $"{(_dirty ? "Modified" : "Saved")} | {(string.IsNullOrWhiteSpace(_assetPath) ? "New palette" : _assetPath)}" +
        (string.IsNullOrWhiteSpace(_message) ? string.Empty : $" | {_message}");

    private static Tilemap? SelectedTilemap() => Selection.activeGameObject?.GetComponent<Tilemap>();

    private static TilePalette CreatePalette()
    {
        var palette = new TilePalette();
        palette.SliceAtlas(1, 1);
        return palette;
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(EditorApplication.projectPath,
            path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ToProjectPath(string path) => Path.GetRelativePath(
        EditorApplication.projectPath, ResolvePath(path)).Replace('\\', '/');
}
