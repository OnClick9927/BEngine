using BEngine.ProjectSystem;

namespace BEngine.Editor;

public sealed class AssetBundlePackagesWindow : EditorWindow
{
    private AssetBundlePackageSettingsDocument _settings = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly EditorScrollView _scrollView = new();
    private Vector2 _scroll;
    private string _project = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _message = string.Empty;
    private string _loadError = string.Empty;
    private bool _dirty;
    private bool _forceRebuild;

    [MenuItem("Assets/AssetBundle Packages...", false, 1119)]
    public static void Open() => GetWindow<AssetBundlePackagesWindow>("AssetBundle Packages").Show();

    protected override void OnEnable()
    {
        minSize = new Vector2(540, 320);
        Reload();
    }

    protected override void OnProjectChange()
    {
        if (!_project.Equals(EditorApplication.projectPath, StringComparison.OrdinalIgnoreCase)) Reload();
        else Repaint();
    }

    protected override void OnGUI()
    {
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath))
        {
            EditorGUILayout.HelpBox("Open a project to configure AssetBundle packages.");
            return;
        }
        if (!_project.Equals(EditorApplication.projectPath, StringComparison.OrdinalIgnoreCase)) Reload();
        if (_loadError.Length > 0)
        {
            EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            if (GUILayout.Button("Reload Settings")) Reload();
            return;
        }
        EditorGUILayout.HelpBox("AssetBundle packages group build work and update/repair operations. " +
                                "Asset loading keeps using the same address. The longest matching asset path owns each asset.");
        _scroll = _scrollView.Begin(_scroll);
        try
        {
            using (new EditorGUI.DisabledScope(AssetBundleMenuCommands.IsBuilding))
            {
                var defaultName = EditorGUILayout.TextField("Default Package", _settings.DefaultPackageName);
                if (defaultName != _settings.DefaultPackageName)
                {
                    if (_selected.Remove(_settings.DefaultPackageName)) _selected.Add(defaultName);
                    _settings.DefaultPackageName = defaultName;
                    _dirty = true;
                }
                DrawSelection(_settings.DefaultPackageName, "Unmatched assets");
                DrawSelection("bengine", "Generated assemblies and engine resources");
                for (var index = 0; index < _settings.Packages.Count; index++)
                {
                    var package = _settings.Packages[index];
                    GUILayout.Space(8);
                    var header = FullWidthRow();
                    var removeWidth = (Fix64)76;
                    GUI.Label(new Rect(header.x, header.y, Fix64.Max(0, header.width - removeWidth - 8),
                        header.height), $"AssetBundle Package {index + 1}", EditorStyles.boldLabel);
                    if (GUI.Button(new Rect(header.xMax - removeWidth, header.y, removeWidth, header.height), "Remove"))
                    {
                        if (!package.Name.Equals(_settings.DefaultPackageName, StringComparison.OrdinalIgnoreCase))
                            _selected.Remove(package.Name);
                        _settings.Packages.RemoveAt(index--);
                        _dirty = true;
                        continue;
                    }
                    var name = EditorGUILayout.TextField("Name", package.Name);
                    if (name != package.Name)
                    {
                        if (_selected.Remove(package.Name)) _selected.Add(name);
                        package.Name = name;
                        _dirty = true;
                    }
                    if (!name.Equals(_settings.DefaultPackageName, StringComparison.OrdinalIgnoreCase))
                        DrawSelection(name, "Build selection");
                    for (var pathIndex = 0; pathIndex < package.AssetPaths.Count; pathIndex++)
                    {
                        var row = FullWidthRow();
                        var labelWidth = Fix64.Min(92, row.width / 4);
                        var removePathWidth = (Fix64)26;
                        GUI.Label(new Rect(row.x, row.y, labelWidth, row.height), "Asset Path", EditorStyles.label);
                        var path = EditorGUI.TextField(new Rect(row.x + labelWidth, row.y,
                            Fix64.Max(0, row.width - labelWidth - removePathWidth - 6), row.height),
                            package.AssetPaths[pathIndex]);
                        if (path != package.AssetPaths[pathIndex])
                        {
                            package.AssetPaths[pathIndex] = path;
                            _dirty = true;
                        }
                        if (GUI.Button(new Rect(row.xMax - removePathWidth, row.y, removePathWidth, row.height), "-"))
                        {
                            package.AssetPaths.RemoveAt(pathIndex--);
                            _dirty = true;
                        }
                    }
                    if (GUILayout.Button("Add Asset Path"))
                    {
                        package.AssetPaths.Add("Assets/");
                        _dirty = true;
                    }
                }
                if (GUILayout.Button("Add AssetBundle Package"))
                {
                    var index = _settings.Packages.Count + 1;
                    while (_settings.Packages.Any(package => package.Name.Equals($"package-{index}",
                               StringComparison.OrdinalIgnoreCase))) index++;
                    _settings.Packages.Add(new AssetBundlePackageRule
                    {
                        Name = $"package-{index}", AssetPaths = ["Assets/"]
                    });
                    _dirty = true;
                }
                GUILayout.Space(8);
                SplitButtonRow(out var reload, out var save);
                if (GUI.Button(reload, "Reload Settings")) Reload();
                using (new EditorGUI.DisabledScope(!_dirty))
                    if (GUI.Button(save, "Save Settings")) Save();
                GUILayout.Space(8);
                _outputDirectory = EditorGUILayout.TextField("Build Output", _outputDirectory);
                _forceRebuild = GUILayout.Toggle(_forceRebuild, "Force Rebuild Selected", GUILayout.ExpandWidth(true));
                SplitButtonRow(out var buildAll, out var buildSelected);
                if (GUI.Button(buildAll, "Build All Packages")) Build(all: true);
                using (new EditorGUI.DisabledScope(_selected.Count == 0))
                    if (GUI.Button(buildSelected, "Build Selected Packages")) Build(all: false);
            }
            EditorGUILayout.HelpBox("Run Build All once to establish the incremental cache. " +
                                    "A selected build reuses unchanged, verified packages; changed or missing unselected packages cause a clear build error.");
            if (_message.Length > 0) EditorGUILayout.HelpBox(_message);
        }
        finally { _scrollView.End(); }
    }

    private void DrawSelection(string name, string description)
    {
        var selected = _selected.Contains(name);
        var next = GUILayout.Toggle(selected, $"{name} — {description}", GUILayout.ExpandWidth(true));
        if (next) _selected.Add(name);
        else _selected.Remove(name);
    }

    private static Rect FullWidthRow() => GUILayoutUtility.GetControlRect(
        EditorGUIUtility.singleLineHeight + 4, GUILayout.ExpandWidth(true));

    private static void SplitButtonRow(out Rect left, out Rect right)
    {
        var row = FullWidthRow();
        var width = Fix64.Max(0, (row.width - 8) / 2);
        left = new Rect(row.x, row.y, width, row.height);
        right = new Rect(row.x + width + 8, row.y, width, row.height);
    }

    private void Reload()
    {
        _project = EditorApplication.projectPath;
        _dirty = false;
        _selected.Clear();
        _message = string.Empty;
        _loadError = string.Empty;
        _settings = new AssetBundlePackageSettingsDocument();
        try
        {
            if (string.IsNullOrWhiteSpace(_project)) return;
            var workspace = EditorServiceAccess.GetRequiredService<ProjectWorkspace>();
            _settings = AssetBundlePackageSettings.Load(workspace);
            _outputDirectory = EditorHostServices.Paths.Combine(workspace.RootPath, "Build", "AssetBundles");
        }
        catch (Exception exception) { _loadError = exception.Message; }
    }

    private bool Save()
    {
        try
        {
            AssetBundlePackageSettings.Save(EditorServiceAccess.GetRequiredService<ProjectWorkspace>(), _settings);
            _dirty = false;
            _message = $"Saved ProjectSettings/{AssetBundlePackageSettingsDocument.FileName}.";
            return true;
        }
        catch (Exception exception)
        {
            _message = exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    private void Build(bool all)
    {
        if (!Save()) return;
        _message = "Building AssetBundle packages...";
        AssetBundleMenuCommands.StartBuild(_outputDirectory, [],
            all ? null : _selected.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            _forceRebuild, (result, failure) =>
            {
                _message = failure is not null ? failure.Message : result is not null
                    ? $"Built {result.BuiltBundleCount}, reused {result.ReusedBundleCount} bundles. " +
                      string.Join("; ", result.Packages.Select(static package =>
                          $"{package.Name}: built {package.BuiltBundleCount}, reused {package.ReusedBundleCount}"))
                    : "Build did not produce a result.";
                Repaint();
            });
    }
}
