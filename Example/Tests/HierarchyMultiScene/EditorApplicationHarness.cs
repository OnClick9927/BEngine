using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal sealed class EditorApplicationHarness : IDisposable
{
    private readonly Assembly _editorAssembly = typeof(EditorWindow).Assembly;
    private readonly Type _applicationType;
    private readonly object _nativeWindow;
    private readonly object[] _windows;
    private readonly BPackageManager _packages;
    private bool _disposed;

    public object Application { get; }
    public object HierarchyWindow { get; }
    public object ProjectWindow { get; }
    public object SceneWindow { get; }
    public object GameWindow { get; }
    public object InspectorWindow { get; }
    public object DockWorkspace { get; }
    public Scene InitialScene { get; }

    public EditorApplicationHarness(SceneFixture fixture)
    {
        _applicationType = TestAssert.RequireType(_editorAssembly, "BEngine.Editor.GpuEditorApplication");
        Application = RuntimeHelpers.GetUninitializedObject(_applicationType);
        var services = new RuntimeSceneServiceProvider();
        var assets = new ProjectAssetDatabase(fixture.Workspace);
        assets.Refresh();
        _packages = (BPackageManager)(Activator.CreateInstance(typeof(BPackageManager),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, [fixture.Workspace, null, false], culture: null) ??
                                     throw new InvalidOperationException("Could not create Package Manager."));
        InitialScene = Document.LoadBObject<SceneDocument, Scene>(fixture.FirstScenePath, services);
        SetProperty(InitialScene, "path", fixture.FirstScenePath);

        var openSceneType = TestAssert.RequireType(_editorAssembly, "BEngine.Editor.EditorOpenScene");
        var initialEntry = Activator.CreateInstance(openSceneType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [InitialScene, fixture.FirstScenePath, ToAssetPath(fixture, fixture.FirstScenePath), true],
            culture: null) ?? throw new InvalidOperationException("Could not create the initial editor Scene entry.");
        var openScenes = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(openSceneType)) ??
                                 throw new InvalidOperationException("Could not create the open Scene collection."));
        openScenes.Add(initialEntry);

        DockWorkspace = Create("BEngine.Editor.ImGuiDockWorkspace");
        _nativeWindow = Create("BEngine.Editor.ImGuiNativeWindow", "Hierarchy Test", 900, 640, false);
        HierarchyWindow = CreateNested("ImGuiHierarchyWindow", Application);
        SceneWindow = CreateNested("ImGuiSceneWindow", Application);
        GameWindow = CreateNested("ImGuiGameWindow", Application);
        ProjectWindow = CreateNested("ImGuiProjectWindow", Application);
        InspectorWindow = CreateNested("ImGuiInspectorWindow", Application);
        _windows = [HierarchyWindow, SceneWindow, GameWindow, ProjectWindow, InspectorWindow];

        SetField("_workspace", fixture.Workspace);
        SetField("_services", services);
        SetField("_assets", assets);
        SetField("_packages", _packages);
        SetField("_scene", InitialScene);
        SetField("_scenePath", fixture.FirstScenePath);
        SetField("_openScenes", openScenes);
        SetField("_loadedSceneSnapshot", new[] { InitialScene });
        SetField("_dock", DockWorkspace);
        SetField("_mainWindow", _nativeWindow);
        SetField("_hierarchy", HierarchyWindow);
        SetField("_sceneView", SceneWindow);
        SetField("_gameView", GameWindow);
        SetField("_project", ProjectWindow);
        SetField("_inspector", InspectorWindow);
        SetField("_layoutStore", Create("BEngine.Editor.EditorLayoutStore", fixture.Workspace));
        SetField("_activeLayoutName", "Last Session");
        var instanceLogPath = Path.Combine(fixture.Workspace.LibraryPath, "Logs", "HierarchyMultiScene.log");
        Directory.CreateDirectory(Path.GetDirectoryName(instanceLogPath)!);
        File.WriteAllText(instanceLogPath, "HierarchyMultiScene test log");
        SetField("_instanceLogPath", instanceLogPath);
        InitializeMenuRegistry();
        InitializeField("_editorPanels");
        InitializeField("_windowLayer");
        InitializeField("_builtInWindows");
        InitializeField("_runtimes");
        InitializeField("_scriptSourceCache");

        AddBuiltIn(HierarchyWindow, "Left", true);
        AddBuiltIn(SceneWindow, "Center", true);
        AddBuiltIn(GameWindow, "Center", true);
        AddBuiltIn(InspectorWindow, "Right", true);
        AddBuiltIn(ProjectWindow, "Bottom", true);
        AttachBridge();
    }

    public void ExpandScene(Scene scene)
    {
        var expanded = GetField(HierarchyWindow, "_expandedScenes") ??
                       throw new InvalidOperationException("Hierarchy has no expanded Scene state.");
        expanded.GetType().GetMethod("Add", [typeof(Guid)])!.Invoke(expanded, [scene.Id]);
    }

    public void SetPlaying(bool value) => SetField("_playing", value);

    public void FocusHierarchy() => typeof(EditorWindow).GetMethod("FocusInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(HierarchyWindow, null);

    public void FocusWindow(object window) => typeof(EditorWindow).GetMethod("FocusInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    public void SetCameraPosition(System.Numerics.Vector2 value) => SetField("_editorCameraPosition", value);

    public System.Numerics.Vector2 CameraPosition =>
        (System.Numerics.Vector2)(GetField(Application, "_editorCameraPosition") ?? default(System.Numerics.Vector2));
    public Tool CurrentTool => (Tool)(GetField(Application, "_tool") ?? Tool.None);

    public void SelectGameObject(GameObject gameObject) => Selection.activeGameObject = gameObject;

    public void HandleGlobalKeyboard(Event evt)
    {
        var commands = new List<GpuCanvasCommand>();
        var beginFrame = typeof(GUI).GetMethod("BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
        var endFrame = typeof(GUI).GetMethod("EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
        beginFrame.Invoke(null, [evt, 640, 480, commands]);
        try
        {
            _applicationType.GetMethod("HandleGlobalKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Application, null);
        }
        finally { endFrame.Invoke(null, null); }
    }

    public string? PingedAssetPath => GetField(ProjectWindow, "_pingedAssetPath") as string;

    public GameObject? SelectedGameObject => GetField(Application, "_selected") as GameObject;

    public bool IsSceneWindowSelected() => (bool)(DockWorkspace.GetType()
        .GetMethod("IsSelected", BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(DockWorkspace, [SceneWindow]) ?? false);

    public void SetHierarchySearch(string value) =>
        RequireField(HierarchyWindow.GetType(), "_search").SetValue(HierarchyWindow, value);

    public Guid? HierarchyRenamingId =>
        RequireField(HierarchyWindow.GetType(), "_renamingId").GetValue(HierarchyWindow) as Guid?;

    public void SetHierarchyRenameValue(string value) =>
        RequireField(HierarchyWindow.GetType(), "_renameValue").SetValue(HierarchyWindow, value);

    public IReadOnlyList<GpuCanvasCommand> RenderHierarchy(Event evt, int width = 520, int height = 640)
        => RenderWindow(HierarchyWindow, evt, width, height);

    public IReadOnlyList<GpuCanvasCommand> RenderScene(Event evt, int width = 640, int height = 480)
        => RenderWindow(SceneWindow, evt, width, height);

    private static IReadOnlyList<GpuCanvasCommand> RenderWindow(
        object window,
        Event evt,
        int width,
        int height)
    {
        var commands = new List<GpuCanvasCommand>();
        var beginFrame = typeof(GUI).GetMethod("BeginFrame", BindingFlags.Static | BindingFlags.NonPublic) ??
                         throw new MissingMethodException(typeof(GUI).FullName, "BeginFrame");
        var endFrame = typeof(GUI).GetMethod("EndFrame", BindingFlags.Static | BindingFlags.NonPublic) ??
                       throw new MissingMethodException(typeof(GUI).FullName, "EndFrame");
        beginFrame.Invoke(null, [evt, width, height, commands]);
        try
        {
            typeof(EditorWindow).GetMethod("OnGUIInternal",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        }
        finally
        {
            endFrame.Invoke(null, null);
        }
        return commands;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var scenes = ((IEnumerable)(GetField(Application, "_openScenes") ?? Array.Empty<object>()))
            .Cast<object>()
            .Select(entry => entry.GetType().GetProperty("Scene")?.GetValue(entry) as Scene)
            .Where(scene => scene is not null)
            .Cast<Scene>()
            .Append(InitialScene)
            .Distinct()
            .ToArray();
        DetachBridge();
        foreach (var window in _windows)
        {
            try
            {
                typeof(EditorWindow).GetMethod("CloseInternal", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
            }
            catch (TargetInvocationException) { }
        }
        foreach (var scene in scenes)
            if (scene.world.IsCreated) scene.world.Dispose();
        _packages.Dispose();
        try { ((IDisposable)_nativeWindow).Dispose(); }
        catch (InvalidOperationException) { }
    }

    private void AddBuiltIn(object window, string area, bool select)
    {
        var method = _applicationType.GetMethod("AddBuiltIn", BindingFlags.Instance | BindingFlags.NonPublic) ??
                     throw new MissingMethodException(_applicationType.FullName, "AddBuiltIn");
        var areaType = method.GetParameters()[1].ParameterType;
        method.Invoke(Application, [window, Enum.Parse(areaType, area), select]);
    }

    private object Create(string fullName, params object[] arguments)
    {
        var type = TestAssert.RequireType(_editorAssembly, fullName);
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null, arguments, culture: null) ??
               throw new InvalidOperationException($"Could not create {fullName}.");
    }

    private object CreateNested(string name, params object[] arguments)
    {
        var type = _applicationType.GetNestedType(name, BindingFlags.NonPublic) ??
                   throw new TypeLoadException($"{_applicationType.FullName}+{name}");
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null, arguments, culture: null) ??
               throw new InvalidOperationException($"Could not create {type.FullName}.");
    }

    private void InitializeField(string name)
    {
        var field = RequireField(_applicationType, name);
        SetField(name, Activator.CreateInstance(field.FieldType) ??
                       throw new InvalidOperationException($"Could not initialize {name}."));
    }

    private void InitializeMenuRegistry()
    {
        var registryType = TestAssert.RequireType(_editorAssembly, "BEngine.Editor.MenuItemRegistry");
        var discover = registryType.GetMethod("Discover", BindingFlags.Static | BindingFlags.Public |
                                                          BindingFlags.NonPublic) ??
                       throw new MissingMethodException(registryType.FullName, "Discover");
        SetField("_menuItems", discover.Invoke(null, null) ??
                               throw new InvalidOperationException("MenuItem discovery returned null."));
    }

    private void SetField(string name, object? value) => RequireField(_applicationType, name).SetValue(Application, value);

    private static object? GetField(object target, string name) =>
        RequireField(target.GetType(), name).GetValue(target);

    private static FieldInfo RequireField(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
        throw new MissingFieldException(type.FullName, name);

    private static void SetProperty(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                       throw new MissingMemberException(target.GetType().FullName, name);
        property.SetValue(target, value);
    }

    private static string ToAssetPath(SceneFixture fixture, string sourcePath) =>
        Path.GetRelativePath(fixture.Workspace.RootPath, sourcePath).Replace('\\', '/');

    private void AttachBridge()
    {
        var bridge = TestAssert.RequireType(_editorAssembly, "BEngine.Editor.EditorBridge");
        bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Application]);
    }

    private void DetachBridge()
    {
        var bridge = TestAssert.RequireType(_editorAssembly, "BEngine.Editor.EditorBridge");
        bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Application]);
    }
}
