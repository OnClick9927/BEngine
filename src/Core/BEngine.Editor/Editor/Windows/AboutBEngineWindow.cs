using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/BEngine.png")]
internal sealed class AboutBEngineWindow : EditorWindow
{
    private const string WindowTitle = "About BEngine";
    private const string IconPath = "Icons/BEngine.png";
    private string? _documentationPath;

    internal static void Open()
    {
        var window = (AboutBEngineWindow)GetWindow(typeof(AboutBEngineWindow), utility: true,
            WindowTitle, focus: false);
        window.position = new Rect(320, 180, 520, 284);
        window.Focus();
    }

    protected override void OnEnable()
    {
        saveToLayout = false;
        titleContent = new GUIContent(WindowTitle, IconPath, WindowTitle);
        minSize = new Vector2(340, 250);
        maxSize = new Vector2(760, 420);
        _documentationPath = FindDocumentationPath();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        GUILayout.Label(new GUIContent("BEngine", IconPath, "BEngine game engine"),
            EditorStyles.largeLabel, GUILayout.Height(34));
        GUILayout.Space(3);

        DrawInformationRow("Version", EngineVersion);
        DrawInformationRow(".NET", RuntimeInformation.FrameworkDescription);
        DrawInformationRow("OS", RuntimeInformation.OSDescription);
        DrawInformationRow("Project", ProjectPath);

        GUILayout.FlexibleSpace();
        DrawActions();
        GUILayout.Space(5);
    }

    internal static string BuildSystemInfo() => string.Join(Environment.NewLine,
    [
        $"BEngine {EngineVersion}",
        $".NET: {RuntimeInformation.FrameworkDescription}",
        $"OS: {RuntimeInformation.OSDescription}",
        $"OS Architecture: {RuntimeInformation.OSArchitecture}",
        $"Process Architecture: {RuntimeInformation.ProcessArchitecture}",
        $"Project: {ProjectPath}"
    ]);

    internal static void CopySystemInfo() => GUIUtility.systemCopyBuffer = BuildSystemInfo();

    internal static bool canOpenDocumentation => FindDocumentationPath() is not null;

    internal static void OpenDocumentation()
    {
        var path = FindDocumentationPath();
        if (path is null) return;
        Application.OpenURL(path);
    }

    private void DrawActions()
    {
        if (GUIUtility.currentViewWidth < 390)
        {
            if (GUILayout.Button("Copy System Info", GUILayout.ExpandWidth(true))) CopySystemInfo();
            DrawDocumentationButton(expand: true);
            if (GUILayout.Button("Close", GUILayout.ExpandWidth(true))) Close();
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy System Info", GUILayout.Width(124))) CopySystemInfo();
        DrawDocumentationButton(expand: false);
        if (GUILayout.Button("Close", GUILayout.Width(72))) Close();
        GUILayout.EndHorizontal();
    }

    private void DrawDocumentationButton(bool expand)
    {
        var previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && _documentationPath is not null;
        var content = new GUIContent("Open Documentation", _documentationPath ??
            "BEngine documentation was not found in this installation.");
        var clicked = expand
            ? GUILayout.Button(content, GUILayout.ExpandWidth(true))
            : GUILayout.Button(content, GUILayout.Width(136));
        GUI.enabled = previousEnabled;
        if (clicked)
            EditorFeatureGuard.Invoke(typeof(AboutBEngineWindow), nameof(OpenDocumentation),
                OpenDocumentation);
    }

    private static void DrawInformationRow(string label, string value)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight));
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(72));
        GUILayout.Label(new GUIContent(value, value), EditorStyles.label, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }

    private static string EngineVersion
    {
        get
        {
            var assembly = typeof(AboutBEngineWindow).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return !string.IsNullOrWhiteSpace(informational)
                ? informational
                : assembly.GetName().Version?.ToString(3) ?? Application.version;
        }
    }

    private static string ProjectPath => string.IsNullOrWhiteSpace(EditorApplication.projectPath)
        ? "No project is open"
        : Path.GetFullPath(EditorApplication.projectPath);

    private static string? FindDocumentationPath()
        => PackageDocumentationCatalog.FindCoreDocumentation();
}
