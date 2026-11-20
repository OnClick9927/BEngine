using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.UIElements.Editor;

[EditorWindowIcon("Icons/Windows/HtmlConverter.png")]
public sealed class HtmlConverterWindow :
    EditorWindow<HtmlConverterWindowModel, HtmlConverterWindowController>
{
    private readonly EditorScrollView _scroll = new();
    private string _source { get => model.Source; set => model.Source = value; }
    private string _destination { get => model.Destination; set => model.Destination = value; }
    private bool _copyResources { get => model.CopyResources; set => model.CopyResources = value; }
    private bool _generateBindings { get => model.GenerateBindings; set => model.GenerateBindings = value; }
    private string _summary => model.Summary;
    private HtmlConversionDiagnostic[] _diagnostics => model.Diagnostics;

    protected override HtmlConverterWindowModel CreateWindowModel(EditorWindowMvcContext context) =>
        new(context.WindowId);

    protected override HtmlConverterWindowController CreateWindowController(
        EditorWindowMvcContext context,
        HtmlConverterWindowModel windowModel) => new(
            context,
            windowModel,
            EditorServices.GetRequiredService<FileUtility>(),
            EditorServices.GetRequiredService<PathUtility>(),
            EditorServices.GetRequiredService<PlatformUtility>());

    [MenuItem("Window/UIElement/HTML Converter", false, 232)]
    public static void Open() => GetWindow<HtmlConverterWindow>("HTML Converter").ShowAuxWindow();
    public static void Open(string sourcePath)
    {
        var window = GetWindow<HtmlConverterWindow>("HTML Converter");
        window.controller.SetSource(sourcePath);
        window.ShowAuxWindow();
    }
    [MenuItem("Assets/Convert HTML to UIElements", false, 220)]
    private static void ConvertSelected() { if (SelectedHtmlPath() is { } path) Open(path); }
    [MenuItem("Assets/Convert HTML to UIElements", true)]
    private static bool ValidateConvertSelected() => SelectedHtmlPath() is not null;

    protected override void OnEnable()
    {
        titleContent = new GUIContent("HTML Converter", "Icons/Windows/HtmlConverter.png");
        minSize = new Vector2(500, 400);
        controller.Activate();
    }
    protected override void OnGUI()
    {
        model.Scroll = _scroll.Begin(model.Scroll);
        GUILayout.Label("HTML5 to BEngine UIElements", EditorStyles.largeLabel);
        EditorGUILayout.HelpBox("HTML DOM is converted to BXML and CSS declarations to BSS.", MessageType.Info);
        _source = EditorGUILayout.TextField("HTML", _source);
        if (GUILayout.Button(new GUIContent("Select HTML", EditorBuiltinIcons.Toolbar.Browse,
                "Select source HTML"))) controller.PickSource();
        _destination = EditorGUILayout.TextField("BXML", _destination);
        if (GUILayout.Button(new GUIContent("Select Destination", EditorBuiltinIcons.Toolbar.Browse,
                "Select destination BXML"))) controller.PickDestination();
        _copyResources = EditorGUILayout.Toggle("Copy local resources", _copyResources);
        _generateBindings = EditorGUILayout.Toggle("Generate C# binding", _generateBindings);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_source) || string.IsNullOrWhiteSpace(_destination)))
        if (GUILayout.Button(new GUIContent("Convert", "Icons/Toolbar/Html.png",
                "Convert HTML to UIElements"))) controller.Convert();
        GUILayout.Label(_summary, EditorStyles.wordWrappedLabel);
        foreach (var diagnostic in _diagnostics) GUILayout.Label($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}", EditorStyles.wordWrappedLabel);
        _scroll.End();
    }
    private static string? SelectedHtmlPath()
    {
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
            ? Resolve(path) : null;
    }
    private static string Resolve(string path)
    {
        var paths = EditorServices.GetRequiredService<PathUtility>();
        var platform = EditorServices.GetRequiredService<PlatformUtility>();
        if (paths.IsPathRooted(path)) return paths.GetFullPath(path);
        var root = paths.GetDirectoryName(paths.GetFullPath(Application.dataPath)) ?? platform.CurrentDirectory;
        return paths.GetFullPath(paths.Combine(root, path.Replace('/', paths.DirectorySeparatorChar)));
    }
}

public sealed class HtmlConverterWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public Vector2 Scroll { get; internal set; }
    public string Source { get; internal set; } = string.Empty;
    public string Destination { get; internal set; } = string.Empty;
    public bool CopyResources { get; internal set; } = true;
    public bool GenerateBindings { get; internal set; } = true;
    public string Summary { get; internal set; } = "Ready";
    public HtmlConversionDiagnostic[] Diagnostics { get; internal set; } = [];
}

public sealed class HtmlConverterWindowController :
    EditorWindowController<HtmlConverterWindow, HtmlConverterWindowModel>
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;

    public HtmlConverterWindowController(
        EditorWindowMvcContext context,
        HtmlConverterWindowModel model) : this(
            context,
            model,
            EditorServices.GetRequiredService<FileUtility>(),
            EditorServices.GetRequiredService<PathUtility>(),
            EditorServices.GetRequiredService<PlatformUtility>()) { }

    public HtmlConverterWindowController(
        EditorWindowMvcContext context,
        HtmlConverterWindowModel model,
        FileUtility fileSystem,
        PathUtility paths,
        PlatformUtility platform) : base(context, model)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    public void Activate()
    {
        Model.Source = EditorPrefs.GetProjectString(ProjectRoot, "BEngine.HtmlConverter.Source");
        Model.Destination = EditorPrefs.GetProjectString(ProjectRoot, "BEngine.HtmlConverter.Destination");
    }

    public void PickSource() => EditorFileDialog.Open("Select HTML", Application.dataPath,
        "HTML files (*.html;*.htm)|*.html;*.htm", SetSource);

    public void PickDestination() => EditorFileDialog.Save("Save BXML", Application.dataPath,
        "BXML files (*.bxml)|*.bxml", _paths.GetFileNameWithoutExtension(Model.Source) + ".bxml",
        path =>
        {
            Model.Destination = path;
            EditorPrefs.SetProjectString(ProjectRoot, "BEngine.HtmlConverter.Destination", path);
            Repaint();
        });

    public void SetSource(string path)
    {
        Model.Source = _paths.GetFullPath(path);
        Model.Destination = ChangeExtension(Model.Source, ".bxml");
        EditorPrefs.SetProjectString(ProjectRoot, "BEngine.HtmlConverter.Source", Model.Source);
        EditorPrefs.SetProjectString(ProjectRoot, "BEngine.HtmlConverter.Destination", Model.Destination);
        Repaint();
    }

    private string ChangeExtension(string path, string extension)
    {
        var directory = _paths.GetDirectoryName(path);
        var fileName = _paths.GetFileNameWithoutExtension(path) + extension;
        return string.IsNullOrWhiteSpace(directory) ? fileName : _paths.Combine(directory, fileName);
    }

    public void Convert()
    {
        Model.Diagnostics = [];
        try
        {
            if (!_fileSystem.FileExists(Model.Source)) throw new FileNotFoundException("Select an existing HTML source.", Model.Source);
            if (!Model.Destination.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The destination must use the .bxml extension.");
            EditorPrefs.SetProjectString(ProjectRoot, "BEngine.HtmlConverter.Source", Model.Source);
            EditorPrefs.SetProjectString(ProjectRoot, "BEngine.HtmlConverter.Destination", Model.Destination);
            var result = HtmlToUIElementsConverter.ConvertFile(
                Model.Source,
                Model.Destination,
                new HtmlConversionOptions
                {
                    CopyLocalResources = Model.CopyResources,
                    GenerateBindingScript = Model.GenerateBindings
                },
                _fileSystem,
                _paths,
                _platform);
            Model.Summary = $"DOM {result.Report.DomPreservationPercent:0.##}% | CSS " +
                            $"{result.Report.ConvertedCssDeclarationCount}/" +
                            $"{result.Report.CssDeclarationCount}";
            Model.Diagnostics = result.Report.Diagnostics.ToArray();
            AssetDatabase.Refresh();
        }
        catch (Exception exception)
        {
            Model.Summary = $"Conversion failed: {exception.Message}";
        }
        Repaint();
    }

    private string ProjectRoot =>
        EditorBridge.Host?.ProjectRootPath ?? EditorInstanceContext.current?.projectRootPath ??
        _paths.GetDirectoryName(_paths.GetFullPath(Application.dataPath)) ??
        throw new InvalidDataException("Application.dataPath has no project root.");
}
