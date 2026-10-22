using BEngine.UIElements;

namespace BEngine.Editor;

public sealed class SdfFontWindowModel(string id) : EditorWindowModel(id)
{
    public string Family { get; set; } = "Microsoft YaHei UI";
    public string Characters { get; set; } = SdfFontBuilder.DefaultCharacters;
    public int SamplingSize { get; set; } = 64;
    public int DistanceRange { get; set; } = 8;
    public string Path { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Dynamic { get; set; }
    public string PrimaryFontPath { get; set; } = "";
    public List<string> FallbackFontPaths { get; } = [];
}

public sealed class SdfFontWindowController(EditorWindowMvcContext context, SdfFontWindowModel model)
    : EditorWindowController<SdfFontWindow, SdfFontWindowModel>(context, model)
{
    public void Select(SdfFontAsset asset)
    {
        Model.Family = asset.fontFamily; Model.Characters = asset.characters; Model.SamplingSize = asset.samplingSize;
        Model.DistanceRange = asset.distanceRange; Model.Path = asset.assetPath; Model.Message = "";
        Model.Dynamic = asset.supportsAdvancedText;
    }
    public bool Save(string path)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Bake SDF font");
        SdfFontAsset? font = null;
        try
        {
            var paths = RuntimeServiceAccess.PathService;
            var root = paths.GetFullPath(EditorApplication.projectPath);
            var full = paths.GetFullPath(paths.IsPathRooted(path) ? path : paths.Combine(root, path));
            var relative = paths.GetRelativePath(root, full).Replace('\\', '/');
            if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || !relative.EndsWith(".sdfont", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a .sdfont path inside this project's Assets directory.");
            font = Model.Dynamic
                ? SdfFontBuilder.CreateDynamic(Model.PrimaryFontPath, Model.FallbackFontPaths)
                : SdfFontBuilder.Bake(Model.Characters, Model.Family, Model.SamplingSize, Model.DistanceRange);
            Undo.IncrementCurrentGroup();
            Undo.RegisterFileUndo(full, relative, "Bake SDF font");
            font.Save(full); AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            Model.Path = relative; Model.Message = $"Saved {font.glyphCount} glyphs, {font.atlasWidth}×{font.atlasHeight}: {relative}";
            View.Repaint(); return true;
        }
        catch (Exception error) { Model.Message = error.Message; return false; }
        finally { if (font is not null) BObject.DestroyImmediate(font); }
    }
    public void SelectPrimaryFont() => EditorFileDialog.Open("Select primary OpenType font", "",
        "OpenType fonts|*.ttf;*.otf;*.ttc", path => { Model.PrimaryFontPath = path; View.Repaint(); });
    public void AddFallbackFont() => EditorFileDialog.Open("Add fallback OpenType font", "",
        "OpenType fonts|*.ttf;*.otf;*.ttc", path =>
        {
            if (!Model.FallbackFontPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) Model.FallbackFontPaths.Add(path);
            View.Repaint();
        });
}

[EditorWindowIcon("Icons/Assets/AssetFont.png")]
public sealed class SdfFontWindow : EditorWindow<SdfFontWindowModel, SdfFontWindowController>
{
    public SdfFontWindow() { titleContent = new("SDF Font"); minSize = new(520, 400); }
    protected override SdfFontWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override SdfFontWindowController CreateWindowController(EditorWindowMvcContext context, SdfFontWindowModel model) => new(context, model);
    [MenuItem("Window/UI Toolkit/SDF Font", false, 310)]
    public static void Open() => GetWindow<SdfFontWindow>("SDF Font").ShowUtility();
    [OnOpenAsset]
    private static bool OpenAsset(string path)
    {
        if (!path.EndsWith(".sdfont", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadAssetAtPath<SdfFontAsset>(path) is not { } font) return false;
        var window = GetWindow<SdfFontWindow>("SDF Font"); window.controller.Select(font); window.ShowUtility(); return true;
    }
    protected override void OnGUI()
    {
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            model.Dynamic = EditorGUILayout.Toggle("Dynamic Advanced Text", model.Dynamic);
            if (model.Dynamic)
            {
                EditorGUILayout.TextField("Primary Font", model.PrimaryFontPath);
                if (GUILayout.Button("Select Primary Font...")) controller.SelectPrimaryFont();
                GUILayout.Label("Fallback fonts (in order)");
                foreach (var path in model.FallbackFontPaths.ToArray())
                {
                    GUILayout.BeginHorizontal(); EditorGUILayout.TextField("", path);
                    if (GUILayout.Button("Remove", GUILayout.Width(72))) model.FallbackFontPaths.Remove(path);
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add Fallback Font...")) controller.AddFallbackFont();
            }
            else
            {
                model.Family = EditorGUILayout.TextField("Installed Font Family", model.Family);
                model.SamplingSize = EditorGUILayout.IntField("Sampling Size (16–256)", model.SamplingSize);
                model.DistanceRange = EditorGUILayout.IntField("Distance Range (2–32)", model.DistanceRange);
                GUILayout.Label("Characters to include");
                model.Characters = EditorGUILayout.TextArea(model.Characters, GUILayout.Height(150));
            }
            GUILayout.BeginHorizontal();
            if (!model.Dynamic && GUILayout.Button("Add Basic Latin")) model.Characters += SdfFontBuilder.DefaultCharacters;
            if (GUILayout.Button("Bake and Save..."))
                EditorFileDialog.Save("Save SDF Font", RuntimeServiceAccess.PathService.Combine(EditorApplication.projectPath, "Assets"),
                    "SDF Font|*.sdfont", "Font.sdfont", path => controller.Save(path));
            using (new EditorGUI.DisabledScope(model.Path.Length == 0))
                if (GUILayout.Button("Rebake")) controller.Save(model.Path);
            GUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox(model.Dynamic
            ? "The font files are embedded in the asset. HarfBuzz shapes OpenType features, mixed-direction text and the ordered fallback chain at runtime."
            : "Static SDF mode is compact and deterministic. Add every required character before baking.");
        if (model.Message.Length > 0) EditorGUILayout.HelpBox(model.Message);
    }
}
