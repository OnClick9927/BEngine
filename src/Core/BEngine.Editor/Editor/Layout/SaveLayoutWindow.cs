namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
internal sealed class SaveLayoutWindow :
    EditorWindow<SaveLayoutWindowModel, SaveLayoutWindowController>
{
    protected override SaveLayoutWindowModel CreateWindowModel(EditorWindowMvcContext context) =>
        new(context.WindowId);

    protected override SaveLayoutWindowController CreateWindowController(
        EditorWindowMvcContext context,
        SaveLayoutWindowModel windowModel) => new(context, windowModel);

    public static void Open(string currentName, Action<string> accepted) =>
        OpenCore("Save Layout", "Save editor layout", "Save",
            currentName.Equals(EditorLayoutStore.LastSessionName, StringComparison.OrdinalIgnoreCase)
                ? "Custom Layout" : currentName,
            accepted);

    public static void OpenRename(string currentName, Action<string> accepted) =>
        OpenCore("Rename Layout", "Rename editor layout", "Rename", currentName, accepted);

    private static void OpenCore(
        string title,
        string tooltip,
        string actionLabel,
        string currentName,
        Action<string> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var window = CreateWindow<SaveLayoutWindow>();
        window.saveToLayout = false;
        window.controller.Configure(title, tooltip, actionLabel, currentName, accepted);
        window.position = new Rect(240, 180, 420, 130);
        window.minSize = new Vector2(360, 120);
        window.maxSize = new Vector2(700, 180);
        window.ShowAuxWindow();
        window.Focus();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        model.LayoutName = EditorGUILayout.TextField(model.FieldLabel, model.LayoutName);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(80))) controller.Cancel();
        if (GUILayout.Button(model.ActionLabel, GUILayout.Width(80))) controller.Accept();
        GUILayout.EndHorizontal();
    }
}

internal sealed class SaveLayoutWindowModel(string windowId) : EditorWindowModel(windowId)
{
    internal Action<string>? Accepted { get; set; }
    internal string LayoutName { get; set; } = "Layout";
    internal string FieldLabel { get; set; } = "Layout Name";
    internal string ActionLabel { get; set; } = "Save";
}

internal sealed class SaveLayoutWindowController :
    EditorWindowController<SaveLayoutWindow, SaveLayoutWindowModel>
{
    internal SaveLayoutWindowController(
        EditorWindowMvcContext context,
        SaveLayoutWindowModel model) : base(context, model) { }

    internal void Configure(
        string title,
        string tooltip,
        string actionLabel,
        string currentName,
        Action<string> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        View.titleContent = new GUIContent(title, "Icons/Windows/Window.png", tooltip);
        Model.LayoutName = EditorLayoutStore.NormalizeName(currentName);
        Model.ActionLabel = actionLabel;
        Model.Accepted = accepted;
    }

    internal void Cancel()
    {
        Model.Accepted = null;
        Close();
    }

    internal void Accept()
    {
        if (Model.Accepted is { } accepted)
            EditorFeatureGuard.Invoke(View, nameof(Accept),
                () => accepted(EditorLayoutStore.NormalizeName(Model.LayoutName)));
        Model.Accepted = null;
        Close();
    }
}
