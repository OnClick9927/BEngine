namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetPrefab.png")]
public sealed class PrefabOverridesWindow : EditorWindow<PrefabOverridesModel, PrefabOverridesController>
{
    private readonly EditorScrollView _scroll = new();
    public PrefabOverridesWindow() => saveToLayout = false;
    protected override PrefabOverridesModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override PrefabOverridesController CreateWindowController(EditorWindowMvcContext context, PrefabOverridesModel state) => new(context, state);
    protected override void OnHierarchyChange() => controller.Refresh();

    public static PrefabOverridesWindow Open(GameObject root)
    {
        var window = CreateWindow<PrefabOverridesWindow>();
        window.model.Root = PrefabUtility.GetNearestPrefabInstanceRoot(root) ?? root;
        window.titleContent = new GUIContent("Prefab Overrides", EditorBuiltinIcons.Assets.Prefab);
        window.position = new Rect(180, 140, 680, 440);
        window.minSize = new Vector2(460, 260);
        window.controller.Refresh(); window.ShowPopup(); return window;
    }

    protected override void OnGUI()
    {
        if (model.Root is null || model.Root.isDestroyed) { GUILayout.Label("The prefab instance is no longer available."); return; }
        using var disabled = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || model.Root.IsRuntimeOnly);
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(model.Root.name, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) controller.Refresh();
        if (GUILayout.Button("Apply All", EditorStyles.toolbarButton, GUILayout.Width(80))) controller.ApplyAll();
        if (GUILayout.Button("Revert All", EditorStyles.toolbarButton, GUILayout.Width(80))) controller.RevertAll();
        GUILayout.EndHorizontal();
        if (model.Error.Length > 0) EditorGUILayout.HelpBox(model.Error, MessageType.Error);
        model.Scroll = _scroll.Begin(model.Scroll);
        if (model.Items.Count == 0) GUILayout.Label("No overrides.");
        foreach (var item in model.Items.ToArray())
        {
            GUILayout.BeginHorizontal();
            var owner = item.Target is Component component ? component.gameObject : item.Target;
            var label = $"{owner.name} / {item.Target.GetType().Name} / {item.PropertyPath}";
            if (GUILayout.Button(new GUIContent(label, item.Kind.ToString()), EditorStyles.label, GUILayout.ExpandWidth(true)))
                Selection.activeObject = item.Target;
            GUILayout.Label(item.Kind == PrefabOverrideKind.Property ? "Modified" : item.Kind.ToString(), GUILayout.Width(132));
            if (GUILayout.Button(new GUIContent("Apply", "Apply only this override"), GUILayout.Width(58))) controller.Apply(item);
            if (GUILayout.Button(new GUIContent("Revert", "Revert only this override"), GUILayout.Width(58))) controller.Revert(item);
            GUILayout.EndHorizontal();
        }
        _scroll.End();
    }
}

public sealed class PrefabOverridesModel(string windowId) : EditorWindowModel(windowId)
{
    public GameObject? Root { get; internal set; }
    public IReadOnlyList<PrefabOverride> Items { get; internal set; } = [];
    public string Error { get; internal set; } = string.Empty;
    internal Vector2 Scroll;
}

public sealed class PrefabOverridesController(EditorWindowMvcContext context, PrefabOverridesModel model)
    : EditorWindowController<PrefabOverridesWindow, PrefabOverridesModel>(context, model)
{
    public void Refresh() => Run(() => Model.Items = Model.Root is { isDestroyed: false } root ? PrefabUtility.GetOverrides(root, true) : []);
    public void Apply(PrefabOverride item) => Change(() => PrefabUtility.ApplyOverride(item));
    public void Revert(PrefabOverride item) => Change(() => PrefabUtility.RevertOverride(item));
    public void ApplyAll() => Change(() => PrefabUtility.ApplyPrefabInstance(Model.Root!));
    public void RevertAll() => Change(() => PrefabUtility.RevertPrefabInstance(Model.Root!));
    private void Change(Action action) => Run(() => { action(); Model.Items = PrefabUtility.GetOverrides(Model.Root!, true); });
    private void Run(Action action)
    {
        try { action(); Model.Error = string.Empty; }
        catch (Exception error) { Model.Error = error.Message; }
    }
}
