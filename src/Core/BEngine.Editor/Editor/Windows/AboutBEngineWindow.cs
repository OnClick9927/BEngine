namespace BEngine.Editor;

[EditorWindowIcon("Icons/BEngine.png")]
public sealed class AboutBEngineWindow :
    EditorWindow<AboutBEngineWindowModel, AboutBEngineWindowController>
{
    private const string WindowTitle = "About BEngine";
    private const string IconPath = "Icons/BEngine.png";

    protected override AboutBEngineWindowModel CreateWindowModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override AboutBEngineWindowController CreateWindowController(
        EditorWindowMvcContext context,
        AboutBEngineWindowModel windowModel) => new(context, windowModel);

    internal static void Open()
    {
        var window = (AboutBEngineWindow)GetWindow(typeof(AboutBEngineWindow), utility: true,
            WindowTitle, focus: false);
        window.position = new Rect(320, 180, 520, 284);
        window.controller.RefreshInformation();
        window.Focus();
    }

    protected override void OnEnable()
    {
        saveToLayout = false;
        titleContent = new GUIContent(WindowTitle, IconPath, WindowTitle);
        minSize = new Vector2(340, 250);
        maxSize = new Vector2(760, 420);
        controller.RefreshInformation();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        GUILayout.Label(new GUIContent("BEngine", IconPath, "BEngine game engine"),
            EditorStyles.largeLabel, GUILayout.Height(34));
        GUILayout.Space(3);

        DrawInformationRow("Version", model.EngineVersion);
        DrawInformationRow(".NET", model.FrameworkDescription);
        DrawInformationRow("OS", model.OperatingSystemDescription);
        DrawInformationRow("Project", model.ProjectPath);

        GUILayout.FlexibleSpace();
        DrawActions();
        GUILayout.Space(5);
    }

    internal static string BuildSystemInfo() => CommandController().BuildSystemInfo();

    internal static void CopySystemInfo() => CommandController().CopySystemInfo();

    internal static bool canOpenDocumentation => CommandController().CanOpenDocumentation;

    internal static void OpenDocumentation() => CommandController().OpenDocumentation();

    private void DrawActions()
    {
        if (GUIUtility.currentViewWidth < 390)
        {
            if (GUILayout.Button("Copy System Info", GUILayout.ExpandWidth(true)))
                controller.CopySystemInfo();
            DrawDocumentationButton(expand: true);
            if (GUILayout.Button("Close", GUILayout.ExpandWidth(true))) controller.Close();
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy System Info", GUILayout.Width(124))) controller.CopySystemInfo();
        DrawDocumentationButton(expand: false);
        if (GUILayout.Button("Close", GUILayout.Width(72))) controller.Close();
        GUILayout.EndHorizontal();
    }

    private void DrawDocumentationButton(bool expand)
    {
        var previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && model.DocumentationPath is not null;
        var content = new GUIContent("Open Documentation", model.DocumentationPath ??
            "BEngine documentation was not found in this installation.");
        var clicked = expand
            ? GUILayout.Button(content, GUILayout.ExpandWidth(true))
            : GUILayout.Button(content, GUILayout.Width(136));
        GUI.enabled = previousEnabled;
        if (clicked)
            EditorFeatureGuard.Invoke(this, nameof(AboutBEngineWindowController.OpenDocumentation),
                controller.OpenDocumentation);
    }

    private static void DrawInformationRow(string label, string value)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight));
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(72));
        GUILayout.Label(new GUIContent(value, value), EditorStyles.label, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }

    private static AboutBEngineWindowController CommandController()
    {
        var existing = EditorWindowRegistry.GetControllers<AboutBEngineWindowController>()
            .FirstOrDefault();
        var controller = existing ?? CreateWindow<AboutBEngineWindow>().controller;
        controller.RefreshInformation();
        return controller;
    }
}
