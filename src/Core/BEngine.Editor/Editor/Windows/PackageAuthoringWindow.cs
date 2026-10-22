using BEngine.ProjectSystem;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/PackageManager.png")]
internal sealed class PackageAuthoringWindow : EditorWindow<PackageAuthoringWindowModel, PackageAuthoringWindowController>
{
    private readonly EditorScrollView _scrollView = new();
    private Vector2 _scrollPosition;
    // Native progress dialogs temporarily take focus from this form.
    internal override bool dismissPopupOnFocusLost => false;

    protected override PackageAuthoringWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override PackageAuthoringWindowController CreateWindowController(EditorWindowMvcContext context,
        PackageAuthoringWindowModel windowModel) => new(context, windowModel);

    internal static PackageAuthoringWindow OpenCreate(ProjectWorkspace workspace, Action<string> accepted, Rect? anchor = null) =>
        Open(workspace, false, string.Empty, accepted, anchor);

    internal static PackageAuthoringWindow OpenExport(ProjectWorkspace workspace, string? source, Action<string> accepted, Rect? anchor = null) =>
        Open(workspace, true, source ?? string.Empty, accepted, anchor);

    private static PackageAuthoringWindow Open(ProjectWorkspace workspace, bool export, string source, Action<string> accepted, Rect? anchor)
    {
        var window = CreateWindow<PackageAuthoringWindow>();
        window.saveToLayout = false;
        var size = new Vector2(620, export ? 300 : 480);
        window.position = new Rect(220, 140, size.x, size.y);
        window.minSize = new Vector2(440, export ? 260 : 400);
        window.maxSize = new Vector2(680, export ? 360 : 560);
        window.controller.Configure(workspace, export, source, accepted);
        if (anchor is { } button) window.ShowAsDropDown(button, size);
        else window.ShowPopup();
        return window;
    }

    protected override void OnGUI()
    {
        var width = GUIUtility.currentViewWidth;
        var height = GUIUtility.currentViewHeight;
        var buttonHeight = Fix64.Max(EditorGUIUtility.singleLineHeight, GUI.skin.button.fixedHeight);
        var footerY = Fix64.Max(0, height - buttonHeight - 14);
        GUI.Label(new Rect(16, 10, width - 32, 24), titleContent, EditorStyles.boldLabel);
        using (GUILayout.Area(new Rect(8, 38, Fix64.Max(1, width - 16), Fix64.Max(1, footerY - 44))))
        {
            _scrollPosition = _scrollView.Begin(_scrollPosition);
            try { DrawForm(); }
            finally { _scrollView.End(); }
        }
        if (GUI.Button(new Rect(width - 200, footerY, 90, buttonHeight), "Cancel")) controller.Cancel();
        var oldEnabled = GUI.enabled;
        GUI.enabled = oldEnabled && EditorAssetWritePolicy.CanWrite && model.Valid;
        if (GUI.Button(new Rect(width - 102, footerY, 90, buttonHeight), model.Export ? "Export" : "Create"))
            controller.Accept();
        GUI.enabled = oldEnabled;
    }

    private void DrawForm()
    {
        EditorGUI.BeginChangeCheck();
        if (model.Export)
        {
            var enabled = GUI.enabled;
            GUI.enabled = enabled && model.PackageDirectories.Length > 0;
            var selected = Array.IndexOf(model.PackageDirectories, model.SourceDirectory);
            var next = EditorGUILayout.Popup("Source package", selected,
                model.PackageNames.Length > 0 ? model.PackageNames : ["No packages found"]);
            GUI.enabled = enabled;
            if (next != selected && model.PackageDirectories.Length > 0) controller.SelectSource(next);
            if (model.PackageDirectories.Length == 0)
                EditorGUILayout.HelpBox("No packages found in Assets/Packages. Create a package first.", MessageType.Info);
            else
                GUILayout.Label("Source: Assets/Packages/" + EditorHostServices.Paths.GetFileName(model.SourceDirectory),
                    EditorStyles.wordWrappedLabel);
            GUILayout.Label("Exports are saved in Packages-Exported next to Assets, grouped by package name and version.",
                EditorStyles.wordWrappedLabel);
        }
        else
        {
            var name = EditorGUILayout.TextField("Package name", model.Options.Name);
            if (name != model.Options.Name) controller.SetName(name);
            model.Options.Id = EditorGUILayout.TextField("Package ID", model.Options.Id);
            model.Options.DisplayName = EditorGUILayout.TextField("Display name", model.Options.DisplayName);
            model.Options.RootNamespace = EditorGUILayout.TextField("Root namespace", model.Options.RootNamespace);
            model.Options.PackageVersion = EditorGUILayout.TextField("Version", model.Options.PackageVersion);
            var minimumVersion = EditorGUILayout.Popup("Min. engine version",
                Array.IndexOf(model.EngineVersions, model.Options.MinimumEngineVersion), model.EngineVersions);
            model.Options.MinimumEngineVersion = model.EngineVersions[minimumVersion];
            GUILayout.Label("Description", EditorStyles.label);
            model.Options.Description = EditorGUILayout.TextArea(model.Options.Description, GUILayout.Height(64));
            GUILayout.Label("The template is created only in this project's Assets/Packages directory.", EditorStyles.wordWrappedLabel);
        }
        if (EditorGUI.EndChangeCheck()) controller.Validate();
        GUILayout.Space(8);
        if (!string.IsNullOrEmpty(model.PreviewPath))
        {
            GUILayout.Label(model.Export ? "Export path" : "Create path", EditorStyles.boldLabel);
            GUILayout.Label(model.PreviewPath, EditorStyles.wordWrappedLabel);
            if (!model.Export)
                GUILayout.Label($"Assemblies: {model.Options.RootNamespace.Trim()} / " +
                    $"{model.Options.RootNamespace.Trim()}.Editor", EditorStyles.wordWrappedLabel);
        }
        if (!string.IsNullOrEmpty(model.Error)) EditorGUILayout.HelpBox(model.Error, MessageType.Error);
    }

    protected override void OnDisable() => controller.Release();
}

internal sealed class PackageAuthoringWindowModel(string windowId) : EditorWindowModel(windowId)
{
    internal ProjectWorkspace? Workspace { get; set; }
    internal BPackageTemplateOptions Options { get; } = new();
    internal string[] EngineVersions { get; set; } = [EngineVersion.Current];
    internal bool Export { get; set; }
    internal string SourceDirectory { get; set; } = string.Empty;
    internal string[] PackageDirectories { get; set; } = [];
    internal string[] PackageNames { get; set; } = [];
    internal string PreviewPath { get; set; } = string.Empty;
    internal string Error { get; set; } = string.Empty;
    internal bool Valid { get; set; }
    internal Action<string>? Accepted { get; set; }
}

internal sealed class PackageAuthoringWindowController : EditorWindowController<PackageAuthoringWindow, PackageAuthoringWindowModel>
{
    internal PackageAuthoringWindowController(EditorWindowMvcContext context, PackageAuthoringWindowModel model) : base(context, model) { }

    internal void Configure(ProjectWorkspace workspace, bool export, string source, Action<string> accepted)
    {
        Model.Workspace = workspace;
        Model.Export = export;
        Model.Accepted = accepted;
        if (!export) Model.EngineVersions = DiscoverEngineVersions();
        View.titleContent = new GUIContent(export ? "Export Package" : "Create Package", "Icons/Windows/PackageManager.png");
        var parent = EditorHostServices.Paths.Combine(workspace.AssetsPath, "Packages");
        var name = "NewPackage";
        for (var index = 1; EditorHostServices.FileSystem.DirectoryExists(EditorHostServices.Paths.Combine(parent, name)) ||
                            EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(parent, name)); index++)
            name = "NewPackage" + index;
        SetName(name);
        if (export)
        {
            try
            {
                Model.PackageDirectories = BPackageAuthoring.DiscoverProjectPackages(workspace);
                Model.PackageNames = Model.PackageDirectories.Select(EditorHostServices.Paths.GetFileName).ToArray();
                var preferred = string.IsNullOrWhiteSpace(source) ? string.Empty : EditorHostServices.Paths.GetFullPath(
                    EditorHostServices.Paths.IsPathRooted(source) ? source : EditorHostServices.Paths.Combine(workspace.RootPath, source));
                Model.SourceDirectory = Model.PackageDirectories.FirstOrDefault(path =>
                    path.Equals(preferred, StringComparison.OrdinalIgnoreCase)) ?? Model.PackageDirectories.FirstOrDefault() ?? string.Empty;
            }
            catch (Exception exception) { Model.Error = exception.Message; return; }
        }
        Validate();
    }

    internal void SetName(string name)
    {
        var previous = Model.Options.Name;
        if (Model.Options.Id == DefaultId(previous)) Model.Options.Id = DefaultId(name);
        if (Model.Options.DisplayName == previous) Model.Options.DisplayName = name;
        if (Model.Options.RootNamespace == "Project." + previous) Model.Options.RootNamespace = "Project." + name;
        Model.Options.Name = name;
    }

    private static string DefaultId(string name) => "com.project." + name.Trim().ToLowerInvariant().Replace('_', '-');

    internal static string[] DiscoverEngineVersions(string? enginesRoot = null)
    {
        var paths = EditorHostServices.Paths;
        var files = EditorHostServices.FileSystem;
        var versions = new HashSet<string>(StringComparer.Ordinal) { EngineVersion.Current };
        var root = enginesRoot ?? paths.Combine(EditorDataPaths.rootPath, "BEgine");
        if (files.DirectoryExists(root))
            foreach (var directory in files.EnumerateDirectories(root, "BEgine-*", SearchOption.TopDirectoryOnly))
            {
                if (!files.FileExists(paths.Combine(directory, "BEngine.Editor.exe"))) continue;
                var version = paths.GetFileName(directory)["BEgine-".Length..];
                try { BPackageVersion.Validate(version); versions.Add(version); }
                catch (InvalidDataException) { }
            }
        return versions.OrderByDescending(version => version, BPackageVersion.Comparer).ToArray();
    }

    internal void SelectSource(int index)
    {
        if (index < 0 || index >= Model.PackageDirectories.Length) return;
        Model.SourceDirectory = Model.PackageDirectories[index];
        Validate();
    }

    internal void Validate()
    {
        Model.Error = string.Empty;
        Model.PreviewPath = string.Empty;
        Model.Valid = false;
        if (Model.Workspace is null) return;
        if (Model.Export && string.IsNullOrWhiteSpace(Model.SourceDirectory)) return;
        try
        {
            Model.PreviewPath = Model.Export
                ? BPackageAuthoring.GetProjectExportPath(Model.Workspace, Model.SourceDirectory)
                : BPackageAuthoring.GetTemplatePath(Model.Workspace, Model.Options);
            Model.Valid = true;
        }
        catch (Exception exception) { Model.Error = exception.Message; }
    }

    internal void Accept()
    {
        Validate();
        if (!Model.Valid || Model.Workspace is null) return;
        try
        {
            var path = Model.Export
                ? BPackageAuthoring.ExportProjectPackage(Model.Workspace, Model.SourceDirectory,
                    (completed, total, item) => EditorUtility.DisplayProgressBar("Validate package", item,
                        total == 0 ? 0f : (float)completed / total))
                : BPackageAuthoring.CreateTemplate(Model.Workspace, Model.Options);
            var accepted = Model.Accepted;
            Release();
            Close();
            if (accepted is not null) EditorFeatureGuard.Invoke(View, "Package completed", () => accepted(path));
        }
        catch (Exception exception) { Model.Error = exception.Message; Debug.LogException(exception); }
        finally { EditorUtility.ClearProgressBar(); }
    }

    internal void Cancel() { Release(); Close(); }
    internal void Release() { Model.Accepted = null; Model.Workspace = null; }
}
