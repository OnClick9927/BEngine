namespace BEngine.Editor;

internal sealed class GameViewResolutionWindow :
    EditorWindow<GameViewResolutionWindowModel, GameViewResolutionWindowController>
{
    public GameViewResolutionWindow()
    {
        titleContent = new GUIContent("Add Game View Resolution");
        saveToLayout = false;
        minSize = new Vector2(330, 165);
        maxSize = new Vector2(520, 220);
    }

    protected override GameViewResolutionWindowModel CreateWindowModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override GameViewResolutionWindowController CreateWindowController(
        EditorWindowMvcContext context,
        GameViewResolutionWindowModel windowModel) => new(context, windowModel);

    public static void Open(Rect rootAnchor, Action<string, int, int> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        var window = CreateWindow<GameViewResolutionWindow>();
        window.controller.Configure(accepted);
        window.position = new Rect(rootAnchor.x, rootAnchor.yMax, 390, 180);
        window.ShowPopup();
        window.Focus();
    }

    protected override void OnGUI()
    {
        GUILayout.Space(8);
        model.Name = EditorGUILayout.TextField("Name", model.Name);
        model.Width = EditorGUILayout.IntField("Width", model.Width);
        model.Height = EditorGUILayout.IntField("Height", model.Height);
        if (model.ValidationMessage.Length > 0)
            GUILayout.Label(model.ValidationMessage, EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(76))) controller.Cancel();
        if (GUILayout.Button("Add", GUILayout.Width(76))) controller.Accept();
        GUILayout.EndHorizontal();
    }
}

internal sealed class GameViewResolutionWindowModel(string windowId) : EditorWindowModel(windowId)
{
    internal Action<string, int, int>? Accepted { get; set; }
    internal string Name { get; set; } = "Custom";
    internal int Width { get; set; } = 1920;
    internal int Height { get; set; } = 1080;
    internal string ValidationMessage { get; set; } = string.Empty;
}

internal sealed class GameViewResolutionWindowController :
    EditorWindowController<GameViewResolutionWindow, GameViewResolutionWindowModel>
{
    internal GameViewResolutionWindowController(
        EditorWindowMvcContext context,
        GameViewResolutionWindowModel model) : base(context, model) { }

    internal void Configure(Action<string, int, int> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        Model.Accepted = accepted;
        Model.ValidationMessage = string.Empty;
    }

    internal void Cancel()
    {
        Model.Accepted = null;
        Close();
    }

    internal void Accept()
    {
        if (Model.Width is <= 0 or > GameViewResolutionSettings.MaximumDimension ||
            Model.Height is <= 0 or > GameViewResolutionSettings.MaximumDimension)
        {
            Model.ValidationMessage = $"Width and height must be between 1 and " +
                                      $"{GameViewResolutionSettings.MaximumDimension}.";
            return;
        }

        var accepted = Model.Accepted;
        Model.Accepted = null;
        accepted?.Invoke(Model.Name, Model.Width, Model.Height);
        Close();
    }
}
