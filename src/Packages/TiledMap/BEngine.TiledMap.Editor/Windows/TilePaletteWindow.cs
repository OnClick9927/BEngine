using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.TiledMap.Editor;

[EditorWindowIcon("Icons/Assets/AssetImage.png")]
public sealed partial class TilePaletteWindow : EditorWindow<TilePaletteWindowModel, TilePaletteWindowController>
{
    public TilePaletteWindow()
    {
        titleContent = new GUIContent("Tile Palette", EditorBuiltinIcons.Assets.Image, "Tile Palette");
        minSize = new Vector2(430, 400); position = new Rect(100, 80, 650, 740);
    }
    protected override TilePaletteWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override TilePaletteWindowController CreateWindowController(EditorWindowMvcContext context,
        TilePaletteWindowModel windowModel) => new(context, windowModel);
    [MenuItem("Window/2D/Tile Palette", false, 220)]
    public static void Open() => GetWindow<TilePaletteWindow>("Tile Palette").Show();
    protected override void OnSelectionChange()
    {
        if (Selection.activeGameObject?.GetComponent<Tilemap>() is { isDestroyed: false } map)
            controller.SetActiveTilemap(map);
        Repaint();
    }
    protected override void OnGUI()
    {
        if (GUIUtility.currentViewWidth < 330 || GUIUtility.currentViewHeight < 350)
        { GUILayout.Label("Resize the Tile Palette window to edit tiles.", EditorStyles.wordWrappedLabel); return; }
        if (model.Palette.isDestroyed) controller.NewPalette();
        HandleShortcuts();
        using var readOnly = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        DrawToolbar();
        DrawTargetToolbar();
        DrawPaletteToolbar();
        var prefabBrush = EditorGUILayout.ObjectField("Prefab Brush", model.PrefabBrush, false);
        if (prefabBrush != model.PrefabBrush) controller.SetPrefabBrush(prefabBrush);
        if (model.PrefabBrush is not null)
            GUILayout.Label("Prefab brush: Paint, Erase and Box. Clear this field to paint tiles.", EditorStyles.miniLabel);
        var available = Fix64.Max(64, GUIUtility.currentViewHeight - GUILayoutUtility.GetLastRect().yMax - 88);
        if (model.EditPalette)
        {
            var inspectorHeight = Fix64.Clamp(available * Fix64.FromDecimal(0.4m), 96, 210);
            DrawPaletteCanvas(GUILayoutUtility.GetControlRect(Fix64.Max(64, available - inspectorHeight)));
            DrawPaletteInspectorPanel(inspectorHeight);
        }
        else DrawPaletteCanvas(GUILayoutUtility.GetControlRect(available));
        GUILayout.Label($"Brush: {controller.BrushWidth} x {controller.BrushHeight} | {model.BrushMode} | [ ] rotate, Shift+[ ] flip", EditorStyles.miniLabel);
        GUILayout.Label("Left drag: select tiles in palette; paint in Scene. Middle drag: pan; wheel: zoom.", EditorStyles.miniLabel);
        GUILayout.Label(model.Message, EditorStyles.miniLabel);
    }
    private void DrawToolbar()
    {
        var row = GUILayoutUtility.GetControlRect(23);
        var tools = new[] {
            (TilePaletteBrushMode.Select,"Select S"), (TilePaletteBrushMode.Move,"Move M"),
            (TilePaletteBrushMode.Paint,"Paint B"), (TilePaletteBrushMode.BoxFill,"Box U"),
            (TilePaletteBrushMode.Pick,"Pick I"), (TilePaletteBrushMode.Erase,"Erase D"),
            (TilePaletteBrushMode.Flood,"Fill G") };
        var toolWidth = (row.width - 24) / 7;
        for (var index = 0; index < tools.Length; index++)
        {
            var (mode, title) = tools[index];
            var caption = row.width < 600 ? title[..title.LastIndexOf(' ')] : title;
            if (GUI.Button(new Rect(row.x + index * (toolWidth + 4), row.y, toolWidth, row.height),
                new GUIContent(caption, string.Empty, title), model.BrushMode == mode ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton))
                controller.SelectTool(mode);
        }
        row = GUILayoutUtility.GetControlRect(23);
        var rotationWidth = row.width < 600 ? (Fix64)62 : 110;
        if (GUI.Button(new Rect(row.x, row.y, rotationWidth, row.height), row.width < 600 ? "Left [" : "Rotate Left [")) controller.RotateBrush(false);
        if (GUI.Button(new Rect(row.x + rotationWidth + 4, row.y, rotationWidth, row.height), row.width < 600 ? "Right ]" : "Rotate Right ]")) controller.RotateBrush(true);
        var offset = rotationWidth * 2 + 8;
        if (GUI.Button(new Rect(row.x + offset, row.y, 57, row.height), "Flip X")) controller.FlipBrush(true);
        if (GUI.Button(new Rect(row.x + offset + 61, row.y, 57, row.height), "Flip Y")) controller.FlipBrush(false);
        var enabled = model.ScenePaintingEnabled && Tools.current == Tool.None;
        if (GUI.Button(new Rect(row.x + offset + 122, row.y, row.width - offset - 122, row.height),
            enabled ? "Painting in Scene" : "Paint in Scene", enabled ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton))
        { model.ScenePaintingEnabled = !enabled; Tools.current = !enabled ? Tool.None : Tool.Move; SceneView.RepaintAll(); }
    }
    private void DrawTargetToolbar()
    {
        var maps = BObject.FindObjectsByType<Tilemap>().Where(map => !map.isDestroyed && map.gameObject.scene is not null).ToArray();
        var current = model.ActiveTilemap;
        var names = new[] { "None" }.Concat(maps.Select(map => map.gameObject.name)).ToArray();
        var index = Array.IndexOf(maps, current) + 1;
        var row = GUILayoutUtility.GetControlRect(23);
        GUI.Label(new Rect(row.x, row.y, 105, row.height), "Active Tilemap");
        var fieldWidth = Fix64.Max(50, row.width - 310);
        var chosen = EditorGUI.Popup(new Rect(row.x + 109, row.y, fieldWidth, row.height), "", index, names);
        if (chosen != index) controller.SetActiveTilemap(chosen > 0 ? maps[chosen - 1] : null);
        var buttons = row.x + 113 + fieldWidth;
        using (new EditorGUI.DisabledScope(model.ActiveTilemap is null))
        {
            if (GUI.Button(new Rect(buttons, row.y, 44, row.height), "Ping") && model.ActiveTilemap is { } ping)
                EditorUtility.PingObject(ping.gameObject);
            if (GUI.Button(new Rect(buttons + 48, row.y, 42, row.height), "Eye") && model.ActiveTilemap is { } visible)
            { SceneVisibilityManager.instance.ToggleVisibility(visible.gameObject); SceneView.RepaintAll(); }
            if (GUI.Button(new Rect(buttons + 94, row.y, 50, row.height), "Frame") && model.ActiveTilemap is { } frame)
            { Selection.activeGameObject = frame.gameObject; SceneView.FrameSelected(); }
        }
        if (GUI.Button(new Rect(buttons + 148, row.y, 44, row.height), "New")) controller.CreateTilemap();
        if (model.PrefabBrush is null && model.ActiveTilemap is { } target && !controller.CompatiblePalette(target))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Target uses a different palette.", EditorStyles.miniLabel);
            if (GUILayout.Button("Use Target Palette") && target.palette is { } palette) controller.SelectPalette(palette);
            if (GUILayout.Button("Assign Palette")) controller.AssignPalette(target);
            GUILayout.EndHorizontal();
        }
    }
    private void DrawPaletteToolbar()
    {
        var row = GUILayoutUtility.GetControlRect(23);
        GUI.Label(new Rect(row.x, row.y, 61, row.height), "Palette");
        var selected = EditorGUI.ObjectField(new Rect(row.x + 65, row.y, row.width - 239, row.height), model.Palette, false);
        if (selected is not null && selected != model.Palette) controller.SelectPalette(selected);
        if (GUI.Button(new Rect(row.xMax - 170, row.y, 60, row.height), "Create")) controller.NewPalette();
        if (GUI.Button(new Rect(row.xMax - 106, row.y, 48, row.height), "Save")) controller.SavePalette();
        if (GUI.Button(new Rect(row.xMax - 54, row.y, 54, row.height), model.EditPalette ? "Editing" : "Edit",
            model.EditPalette ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton))
        { model.EditPalette = !model.EditPalette; controller.EndStroke(); }
    }
    private void DrawPaletteInspectorPanel(Fix64 height)
    {
        var viewport = GUILayoutUtility.GetControlRect(height);
        var contentWidth = Fix64.Max(1, viewport.width - 20);
        model.InspectorScroll = GUI.BeginScrollView(viewport, model.InspectorScroll,
            new Rect(0, 0, contentWidth + 8, 320));
        try
        {
            GUILayout.BeginArea(new Rect(4, 4, contentWidth, 312));
            try { DrawPaletteInspector(); }
            finally { GUILayout.EndArea(); }
        }
        finally { GUI.EndScrollView(); }
    }
    private void DrawPaletteInspector()
    {
        var palette = model.Palette;
        var name = EditorGUILayout.TextField("Name", palette.Name);
        model.AssetPath = EditorGUILayout.TextField("Path", model.AssetPath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture>(palette.Atlas);
        var selected = EditorGUILayout.ObjectField("Atlas Texture", texture, false);
        var columns = Math.Clamp(EditorGUILayout.IntField("Columns", model.Columns), 1, 128);
        var rows = Math.Clamp(EditorGUILayout.IntField("Rows", model.Rows), 1, 128);
        if (name != palette.Name || selected != texture)
        {
            Undo.RecordObject(palette, "Edit Tile Palette"); palette.Name = name;
            if (selected != texture) palette.Atlas = selected?.assetPath ?? "";
            EditorUtility.SetDirty(palette);
        }
        var atlas = EditorGUILayout.ObjectField<TextureAtlas>("Sprite Atlas", null, false);
        if (atlas is not null)
        {
            try { Undo.RecordObject(palette, "Import Tile Atlas"); palette.ImportAtlas(atlas); EditorUtility.SetDirty(palette); controller.ResetBrush(); }
            catch (Exception exception) { model.Message = exception.Message; }
        }
        var tile = palette.Find(model.SelectedTileId);
        if (tile is not null)
        {
            var collider = EditorGUILayout.Toggle("Collider", tile.HasCollider);
            if (collider != tile.HasCollider) { Undo.RecordObject(palette, "Edit Tile Collider"); tile.HasCollider = collider; EditorUtility.SetDirty(palette); }
            if (GUILayout.Button("Edit Rules and Animation in Inspector")) Selection.activeObject = palette;
        }
        model.Columns = columns; model.Rows = rows;
        if (GUILayout.Button("Slice Atlas into Tiles"))
        {
            Undo.IncrementCurrentGroup(); Undo.RecordObject(palette, "Slice Tile Palette");
            palette.SliceAtlas(columns, rows); EditorUtility.SetDirty(palette); controller.ResetBrush();
        }
    }
    internal static void Edit(Tilemap tilemap, string operation, Action action)
    {
        if (tilemap.isDestroyed || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Undo.IncrementCurrentGroup(); Undo.RecordObject(tilemap, operation); action();
        EditorUtility.SetDirty(tilemap); SceneView.RepaintAll();
    }
    internal static TilePalette CreatePalette() { var palette = new TilePalette(); palette.SliceAtlas(1, 1); return palette; }
    internal static string ResolvePath(string path)
    {
        var paths = EditorServices.GetRequiredService<PathUtility>();
        return paths.GetFullPath(paths.IsPathRooted(path) ? path : paths.Combine(EditorApplication.projectPath,
            path.Replace('/', paths.DirectorySeparatorChar)));
    }
    internal static string ToProjectPath(string path) => EditorServices.GetRequiredService<PathUtility>()
        .GetRelativePath(EditorApplication.projectPath, ResolvePath(path)).Replace('\\', '/');
}

public enum TilePaletteBrushMode { Paint, Erase, Flood, Pick, Select, Move, BoxFill }

public sealed class TilePaletteWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public TilePalette Palette { get; internal set; } = TilePaletteWindow.CreatePalette();
    public string AssetPath { get; internal set; } = string.Empty;
    public int SelectedTileId { get; internal set; } = 1;
    public int Columns { get; internal set; } = 1;
    public int Rows { get; internal set; } = 1;
    public int BoxMinX { get; internal set; }
    public int BoxMinY { get; internal set; }
    public int BoxMaxX { get; internal set; } = 1;
    public int BoxMaxY { get; internal set; } = 1;
    public int GridRadius { get; internal set; } = 6;
    public TileTransformFlags Flags { get; internal set; }
    public TilePaletteBrushMode BrushMode { get; internal set; }
    public Tilemap? ActiveTilemap { get; internal set; }
    public PrefabAsset? PrefabBrush { get; internal set; }
    public bool EditPalette { get; internal set; }
    public bool ScenePaintingEnabled { get; internal set; }
    internal int CellPixels = 42;
    internal Vector2 PaletteScroll { get; set; }
    internal Vector2 InspectorScroll { get; set; }
    public bool Dirty { get; internal set; }
    public string Message { get; internal set; } = "Select tiles in the palette, then paint in the Scene view.";
}

public sealed partial class TilePaletteWindowController : EditorWindowController<TilePaletteWindow, TilePaletteWindowModel>
{
    public TilePaletteWindowController(EditorWindowMvcContext context, TilePaletteWindowModel model) : base(context, model) { }
    public void NewPalette()
    {
        EndStroke(); Model.Palette = TilePaletteWindow.CreatePalette(); Model.Palette.Name = "New Tile Palette";
        Model.AssetPath = ""; Model.Columns = Model.Rows = 1; Model.EditPalette = true;
        EditorUtility.SetDirty(Model.Palette); ResetBrush(); Model.Message = "Choose an atlas texture, slice it, then save the palette."; Repaint();
    }
    public void LoadPalette()
    {
        try { if (!string.IsNullOrWhiteSpace(Model.AssetPath)) SelectPalette(TilePalette.Load(TilePaletteWindow.ResolvePath(Model.AssetPath))); }
        catch (Exception exception) { Model.Message = exception.Message; }
        Repaint();
    }
    public void SelectPalette(TilePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.isDestroyed) return;
        EndStroke(); Model.Palette = palette; Model.AssetPath = palette.assetPath;
        Model.Columns = palette.Columns; Model.Rows = palette.Rows; Model.EditPalette = false;
        ResetBrush(); Repaint();
    }
    public void SavePalette()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Model.AssetPath))
            {
                var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
                if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) folder = "Assets";
                Model.AssetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/', '\\')}/New Tile Palette.tilepalette.yaml");
            }
            var relative = TilePaletteWindow.ToProjectPath(Model.AssetPath);
            if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || !relative.EndsWith(".tilepalette.yaml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Save palettes under Assets with the .tilepalette.yaml extension.");
            Model.Palette.Save(TilePaletteWindow.ResolvePath(relative)); Model.AssetPath = relative;
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            EditorUtility.ClearDirty(Model.Palette); Model.Message = $"Saved {relative}";
        }
        catch (Exception exception) { Model.Message = exception.Message; }
        Repaint();
    }
    public void SetActiveTilemap(Tilemap? map)
    {
        EndStroke(); Model.ActiveTilemap = map; SelectionBounds = TileBounds.Empty;
        if (map?.palette is { } palette && string.IsNullOrWhiteSpace(Model.Palette.Atlas) && !EditorUtility.IsDirty(Model.Palette)) SelectPalette(palette);
        Repaint();
    }
    public void SelectTool(TilePaletteBrushMode mode)
    {
        EndStroke();
        Model.BrushMode = mode; Model.ScenePaintingEnabled = true; Tools.current = Tool.None; Repaint(); SceneView.RepaintAll();
    }
    public bool CompatiblePalette(Tilemap map) => ReferenceEquals(map.palette, Model.Palette) ||
        map.palette is { isDestroyed: false } palette && !string.IsNullOrWhiteSpace(palette.assetPath) &&
        string.Equals(palette.assetPath, Model.Palette.assetPath, StringComparison.OrdinalIgnoreCase);
    public void AssignPalette(Tilemap map)
    {
        if (string.IsNullOrWhiteSpace(Model.Palette.assetPath)) SavePalette();
        if (!string.IsNullOrWhiteSpace(Model.Palette.assetPath)) TilePaletteWindow.Edit(map, "Assign Tile Palette", () => map.palette = Model.Palette);
    }
    public void CreateTilemap()
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        if (EditorSceneManager.GetActiveScene() is not { } scene) { Model.Message = "Open a scene before creating a Tilemap."; return; }
        if (Model.PrefabBrush is null && string.IsNullOrWhiteSpace(Model.Palette.assetPath)) SavePalette();
        if (Model.PrefabBrush is null && string.IsNullOrWhiteSpace(Model.Palette.assetPath)) return;
        Undo.IncrementCurrentGroup();
        var gameObject = scene.CreateGameObject("Tilemap");
        var map = gameObject.AddComponent<Tilemap>(); gameObject.AddComponent<TilemapRenderer>();
        if (!string.IsNullOrWhiteSpace(Model.Palette.assetPath)) map.palette = Model.Palette;
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Tilemap");
        EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = gameObject;
        SetActiveTilemap(map); SelectTool(TilePaletteBrushMode.Paint);
    }
}
