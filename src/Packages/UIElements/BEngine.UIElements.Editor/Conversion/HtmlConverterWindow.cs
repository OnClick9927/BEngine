using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[EditorWindowIcon("Icons/Windows/HtmlConverter.png")]
internal sealed class HtmlConverterWindow : EditorWindow
{
    private string _source = string.Empty;
    private string _destination = string.Empty;
    private bool _copyResources = true;
    private bool _generateBindings = true;
    private string _summary = "Ready";
    private HtmlConversionDiagnostic[] _diagnostics = [];

    [MenuItem("Tools/UIElements/HTML Converter", false, 200)]
    public static void Open() => GetWindow<HtmlConverterWindow>("HTML Converter").ShowAuxWindow();
    public static void Open(string sourcePath) { var window = GetWindow<HtmlConverterWindow>("HTML Converter"); window.SetSource(sourcePath); }
    [MenuItem("Assets/Convert HTML to UIElements", false, 220)]
    private static void ConvertSelected() { if (SelectedHtmlPath() is { } path) Open(path); }
    [MenuItem("Assets/Convert HTML to UIElements", true)]
    private static bool ValidateConvertSelected() => SelectedHtmlPath() is not null;

    protected override void OnEnable()
    {
        titleContent = new GUIContent("HTML Converter", "Icons/Windows/HtmlConverter.png");
        _source = EditorPrefs.GetString("BEngine.HtmlConverter.Source");
        _destination = EditorPrefs.GetString("BEngine.HtmlConverter.Destination");
    }
    protected override void OnGUI()
    {
        GUILayout.Label("HTML5 to BEngine UIElements", EditorStyles.largeLabel);
        EditorGUILayout.HelpBox("HTML DOM is converted to UXML and CSS declarations to USS.", MessageType.Info);
        _source = EditorGUILayout.TextField("HTML", _source);
        if (GUILayout.Button(new GUIContent("Select HTML", EditorBuiltinIcons.Toolbar.Browse,
                "Select source HTML"))) PickSource();
        _destination = EditorGUILayout.TextField("UXML", _destination);
        if (GUILayout.Button(new GUIContent("Select Destination", EditorBuiltinIcons.Toolbar.Browse,
                "Select destination UXML"))) PickDestination();
        _copyResources = EditorGUILayout.Toggle("Copy local resources", _copyResources);
        _generateBindings = EditorGUILayout.Toggle("Generate C# binding", _generateBindings);
        if (GUILayout.Button(new GUIContent("Convert", EditorBuiltinIcons.Toolbar.Html,
                "Convert HTML to UIElements"))) ConvertCurrent();
        GUILayout.Label(_summary, EditorStyles.helpBox);
        foreach (var diagnostic in _diagnostics) GUILayout.Label($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
    }
    private void PickSource() => EditorFileDialog.Open("Select HTML", Application.dataPath,
        "HTML files (*.html;*.htm)|*.html;*.htm", SetSource);
    private void PickDestination() => EditorFileDialog.Save("Save UXML", Application.dataPath,
        "UXML files (*.uxml)|*.uxml", Path.GetFileNameWithoutExtension(_source) + ".uxml",
        path => _destination = path);
    private void SetSource(string path)
    {
        _source = Path.GetFullPath(path); _destination = Path.ChangeExtension(_source, ".uxml");
        EditorPrefs.SetString("BEngine.HtmlConverter.Source", _source);
        EditorPrefs.SetString("BEngine.HtmlConverter.Destination", _destination);
    }
    private void ConvertCurrent()
    {
        try
        {
            var result = HtmlToUIElementsConverter.ConvertFile(_source, _destination, new HtmlConversionOptions
            { CopyLocalResources = _copyResources, GenerateBindingScript = _generateBindings });
            _summary = $"DOM {result.Report.DomPreservationPercent:0.##}% | CSS " +
                       $"{result.Report.ConvertedCssDeclarationCount}/{result.Report.CssDeclarationCount}";
            _diagnostics = result.Report.Diagnostics.ToArray(); AssetDatabase.Refresh();
        }
        catch (Exception exception) { _summary = $"Conversion failed: {exception.Message}"; }
    }
    private static string? SelectedHtmlPath()
    {
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
            ? Resolve(path) : null;
    }
    private static string Resolve(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var root = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
