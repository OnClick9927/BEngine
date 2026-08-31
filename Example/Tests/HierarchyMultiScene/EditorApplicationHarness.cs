using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.SceneManagement;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal sealed class EditorApplicationHarness : IDisposable
{
    private readonly Assembly _editorAssembly = typeof(EditorWindow).Assembly;
    private readonly Type _applicationType;
    private readonly object _nativeWindow;
    private readonly List<object> _windows;
    private readonly BPackageManager _packages;
    private bool _disposed;

    public object Application { get; }
    public object HierarchyWindow { get; }
    public object ProjectWindow { get; }
    public object SceneWindow { get; }
    public object GameWindow { get; }
    public object InspectorWindow { get; }
    public object ConsoleWindow { get; }
    public object PackageManagerWindow { get; }
    public object DockWorkspace { get; }
    public Scene InitialScene { get; }
    public Scene ActiveScene => EditorSceneManager.activeScene ??
                                throw new InvalidOperationException("The editor has no active Scene.");
    public IReadOnlyList<Scene> OpenScenes => Enumerable.Range(0, EditorSceneManager.sceneCount)
        .Select(EditorSceneManager.GetSceneAt).ToArray();
    public bool IsPlaying => EditorApplication.isPlaying;
    public IRuntimeSceneManager RuntimeSceneManager =>
        (IRuntimeSceneManager)(GetField(Application, "_runtimeSceneManager") ??
                               throw new InvalidOperationException("The editor has no runtime Scene manager."));
    public int RuntimeCount => ((ICollection)(GetField(Application, "_runtimes") ??
                                              throw new InvalidOperationException(
                                                  "The editor has no runtime collection."))).Count;

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
        _nativeWindow = Create("BEngine.Editor.ImGuiNativeWindow", "Hierarchy Test", 900, 640,
            false, true, 320, 200, int.MaxValue, int.MaxValue);
        HierarchyWindow = CreateNested("ImGuiHierarchyWindow", Application);
        SceneWindow = CreateNested("ImGuiSceneWindow", Application);
        GameWindow = CreateNested("ImGuiGameWindow", Application);
        ProjectWindow = CreateNested("ImGuiProjectWindow", Application);
        InspectorWindow = CreateNested("ImGuiInspectorWindow", Application);
        ConsoleWindow = CreateNested("ImGuiConsoleWindow", Application);
        PackageManagerWindow = CreateNested("ImGuiPackageManagerWindow", Application);
        _windows =
        [
            HierarchyWindow, SceneWindow, GameWindow, ProjectWindow, InspectorWindow, ConsoleWindow,
            PackageManagerWindow
        ];

        SetField("_workspace", fixture.Workspace);
        SetField("_services", services);
        SetField("_sceneRuntimeFactory", new SceneRuntimeFactory());
        SetField("_runtimeSceneManager", services.SceneManager);
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
        SetField("_console", ConsoleWindow);
        SetField("_packageManager", PackageManagerWindow);
        SetField("_tool", Tool.Move);
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
        InitializeField("_closedWindowPlacements");
        InitializeField("_runtimes");
        InitializeField("_scriptSourceCache");
        InitializeField("_nativeFloatingWindows");
        InitializeField("_nativeTransientOwners");
        InitializeField("_nativeDockTrackers");
        InitializeField("_nativeFloatingCarries");
        InitializeField("_pendingUndocks");
        InitializeField("_pendingNativeDocks");
        InitializeField("_pendingNativeCloses");

        AddBuiltIn(HierarchyWindow, "Left", true);
        AddBuiltIn(SceneWindow, "Center", true);
        AddBuiltIn(GameWindow, "Center", true);
        AddBuiltIn(InspectorWindow, "Right", true);
        AddBuiltIn(ProjectWindow, "Bottom", true);
        AddBuiltIn(ConsoleWindow, "Bottom", false);
        AddBuiltIn(PackageManagerWindow, "Center", false);
        AttachBridge();
    }

    public void ExpandScene(Scene scene)
    {
        var expanded = GetField(HierarchyWindow, "_expandedScenes") ??
                       throw new InvalidOperationException("Hierarchy has no expanded Scene state.");
        expanded.GetType().GetMethod("Add", [typeof(Guid)])!.Invoke(expanded, [scene.Id]);
    }

    public void ExpandGameObject(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        var expanded = GetField(HierarchyWindow, "_expanded") ??
                       throw new InvalidOperationException("Hierarchy has no expanded GameObject state.");
        expanded.GetType().GetMethod("Add", [typeof(Guid)])!.Invoke(expanded, [gameObject.Id]);
    }

    public bool IsGameObjectExpanded(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        var expanded = GetField(HierarchyWindow, "_expanded") ??
                       throw new InvalidOperationException("Hierarchy has no expanded GameObject state.");
        return (bool)(expanded.GetType().GetMethod("Contains", [typeof(Guid)])!
            .Invoke(expanded, [gameObject.Id]) ?? false);
    }

    public Vector2 HierarchyScrollPosition
    {
        get
        {
            var scroll = GetField(HierarchyWindow, "_scroll") ??
                         throw new InvalidOperationException("Hierarchy has no scroll region.");
            return (Vector2)(GetField(scroll, "_position") ?? Vector2.zero);
        }
    }

    public void SetPlaying(bool value) => SetField("_playing", value);

    public void EnterPlay()
    {
        EditorApplication.isPlaying = true;
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("The editor did not enter Play Mode.");
    }

    public void ExitPlay()
    {
        EditorApplication.isPlaying = false;
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("The editor did not exit Play Mode.");
    }

    public bool IsSceneDirty(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var entries = (IEnumerable)(GetField(Application, "_openScenes") ?? Array.Empty<object>());
        foreach (var entry in entries.Cast<object>())
        {
            var entryScene = entry.GetType().GetProperty("Scene")?.GetValue(entry) as Scene;
            if (!ReferenceEquals(entryScene, scene)) continue;
            return (bool)(entry.GetType().GetProperty("IsDirty")?.GetValue(entry) ?? false);
        }
        return false;
    }

    public void FocusHierarchy() => typeof(EditorWindow).GetMethod("FocusInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(HierarchyWindow, null);

    public void FocusWindow(object window) => typeof(EditorWindow).GetMethod("FocusInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    public void SelectDockedWindow(object window)
    {
        var panels = (IDictionary)(GetField(Application, "_editorPanels") ??
                                   throw new InvalidOperationException("The editor has no panel registry."));
        var panel = panels[window] ??
                    throw new InvalidOperationException("The requested window is not docked.");
        var id = (string)(panel.GetType().GetProperty("Id")?.GetValue(panel) ??
                          throw new InvalidOperationException("The dock panel has no persistent id."));
        DockWorkspace.GetType().GetMethod("Show", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(DockWorkspace, [id]);
    }

    public bool IsWindowSelected(object window) => (bool)(DockWorkspace.GetType()
        .GetMethod("IsSelected", BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(DockWorkspace, [window]) ?? false);

    public bool IsWindowOpen(object window) => (bool)(typeof(EditorWindow)
        .GetProperty("IsOpen", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(window) ?? false);

    public void FloatWindow(object window) => ((EditorWindow)window).ShowAuxWindow();

    public void CloseWindow(object window) => RequireMethod("CloseEditorWindow").Invoke(Application, [window]);

    public object OpenAdditionalBuiltIn(object prototype, string area)
    {
        var panels = (IDictionary)(GetField(Application, "_editorPanels") ??
                                   throw new InvalidOperationException("The editor has no panel registry."));
        var before = panels.Keys.Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance);
        var method = RequireMethod("OpenBuiltInInstance");
        var areaType = method.GetParameters()[1].ParameterType;
        method.Invoke(Application, [prototype, Enum.Parse(areaType, area)]);
        var added = panels.Keys.Cast<object>().Where(window => !before.Contains(window)).ToArray();
        if (added.Length != 1)
            throw new InvalidOperationException($"Opening an additional built-in added {added.Length} windows.");
        if (!_windows.Any(window => ReferenceEquals(window, added[0]))) _windows.Add(added[0]);
        return added[0];
    }

    public bool SupportsMultipleBuiltIn(object window)
    {
        var method = _applicationType.GetMethod("SupportsMultipleBuiltIn",
            BindingFlags.Static | BindingFlags.NonPublic) ??
                     throw new MissingMethodException(_applicationType.FullName, "SupportsMultipleBuiltIn");
        return (bool)(method.Invoke(null, [window]) ?? false);
    }

    public IReadOnlyList<(string Label, bool Checked)> WindowMenuItems()
    {
        var entries = (IEnumerable)(RequireMethod("MenuItems").Invoke(Application, ["Window"]) ??
                                    throw new InvalidOperationException("Window menu could not be enumerated."));
        return entries.Cast<object>().Select(entry =>
        {
            var type = entry.GetType();
            var label = (string)(type.GetProperty("Label")?.GetValue(entry) ?? string.Empty);
            var isChecked = (bool)(type.GetProperty("Checked")?.GetValue(entry) ?? false);
            return (label, isChecked);
        }).ToArray();
    }

    public IReadOnlyList<string> AddNewTabMenuPaths(object? sourceWindow = null)
    {
        var menu = new GenericMenu();
        RequireMethod("PopulateAddNewTabMenu").Invoke(Application, [menu, sourceWindow ?? InspectorWindow]);
        var items = (IEnumerable)(typeof(GenericMenu).GetProperty("Items",
                                      BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(menu) ??
                                  throw new MissingMemberException(typeof(GenericMenu).FullName, "Items"));
        return items.Cast<object>().Select(item =>
                (string)(item.GetType().GetProperty("Path")?.GetValue(item) ?? string.Empty))
            .ToArray();
    }

    public object OpenAddNewTab(object sourceWindow, string menuPath)
    {
        var panels = (IDictionary)(GetField(Application, "_editorPanels") ??
                                   throw new InvalidOperationException("The editor has no panel registry."));
        var before = panels.Keys.Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance);
        var menu = new GenericMenu();
        RequireMethod("PopulateAddNewTabMenu").Invoke(Application, [menu, sourceWindow]);
        var items = (IEnumerable)(typeof(GenericMenu).GetProperty("Items",
                                      BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(menu) ??
                                  throw new MissingMemberException(typeof(GenericMenu).FullName, "Items"));
        var item = items.Cast<object>().Single(value =>
            string.Equals((string?)value.GetType().GetProperty("Path")?.GetValue(value), menuPath,
                StringComparison.Ordinal));
        var action = item.GetType().GetProperty("Action")?.GetValue(item) as Action ??
                     throw new InvalidOperationException($"Add new tab item '{menuPath}' has no action.");
        action();
        var added = panels.Keys.Cast<object>().Where(window => !before.Contains(window)).ToArray();
        if (added.Length != 1)
            throw new InvalidOperationException($"Add new tab created {added.Length} windows.");
        if (!_windows.Any(window => ReferenceEquals(window, added[0]))) _windows.Add(added[0]);
        return added[0];
    }

    public string WindowId(object window) =>
        (string)(typeof(EditorWindow).GetProperty("PersistentId",
                         BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) ??
                 throw new MissingMemberException(typeof(EditorWindow).FullName, "PersistentId"));

    public bool IsWindowDocked(object window) =>
        ((IDictionary)(GetField(Application, "_editorPanels") ??
                       throw new InvalidOperationException("The editor has no panel registry."))).Contains(window);

    public int DockIndex(object window)
    {
        var panels = (IDictionary)(GetField(Application, "_editorPanels") ??
                                   throw new InvalidOperationException("The editor has no panel registry."));
        var panel = panels[window] ?? throw new InvalidOperationException("The window is not docked.");
        var group = panel.GetType().GetProperty("Group",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(panel) ??
                    throw new InvalidOperationException("The dock panel has no group.");
        var groupPanels = (IList)(group.GetType().GetProperty("Panels",
                                  BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                              ?.GetValue(group) ??
                          throw new InvalidOperationException("The dock group has no panel list."));
        return groupPanels.IndexOf(panel);
    }

    public bool SharesDockGroup(object first, object second)
    {
        var panels = (IDictionary)(GetField(Application, "_editorPanels") ??
                                   throw new InvalidOperationException("The editor has no panel registry."));
        var firstPanel = panels[first] ?? throw new InvalidOperationException("The first window is not docked.");
        var secondPanel = panels[second] ?? throw new InvalidOperationException("The second window is not docked.");
        var groupProperty = firstPanel.GetType().GetProperty("Group",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                            throw new MissingMemberException(firstPanel.GetType().FullName, "Group");
        return ReferenceEquals(groupProperty.GetValue(firstPanel), groupProperty.GetValue(secondPanel));
    }

    public bool ToggleMaximize(object window) => (bool)(DockWorkspace.GetType()
        .GetMethod("ToggleMaximize", BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(DockWorkspace, [window]) ?? false);

    public bool IsMaximized(object window) => (bool)(DockWorkspace.GetType()
        .GetMethod("IsMaximized", BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(DockWorkspace, [window]) ?? false);

    public EditorLayoutDocument CaptureLayout() =>
        (EditorLayoutDocument)(RequireMethod("CaptureLayout").Invoke(Application, ["Test Capture"]) ??
                               throw new InvalidOperationException("The editor did not capture a layout."));

    public void ApplyLayout(EditorLayoutDocument document) =>
        RequireMethod("ApplyLayout").Invoke(Application, [document]);

    public bool LayoutSaved
    {
        get => (bool)(GetField(Application, "_layoutSaved") ?? false);
        set => SetField("_layoutSaved", value);
    }

    public void PrimeProjectCache(object window)
    {
        var field = RequireField(window.GetType(), "_cache");
        field.SetValue(window, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
    }

    public bool IsProjectCacheInvalidated(object window) =>
        RequireField(window.GetType(), "_cache").GetValue(window) is null;

    public void InvalidateProjectWindows() => RequireMethod("InvalidateProjectWindows").Invoke(Application, null);

    public void SetCameraPosition(System.Numerics.Vector2 value) => SetField("_editorCameraPosition", value);
    public void SetCameraSize(float value) => SetField("_editorCameraSize", value);

    public RenderCamera ResolveEditorCamera(float viewportHeight) =>
        (RenderCamera)(RequireMethod("EditorCamera").Invoke(Application, [viewportHeight]) ??
                       throw new InvalidOperationException("The editor Scene camera was not resolved."));

    public float ResolveEditorWorldUnitsPerPixel(float viewportHeight) =>
        (float)(RequireMethod("EditorWorldUnitsPerPixel").Invoke(Application, [viewportHeight]) ??
                throw new InvalidOperationException("The editor Scene scale was not resolved."));

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

    public BObject? SelectedAsset => GetField(Application, "_selectedAsset") as BObject;

    public BObject? InspectorTarget => GetField(InspectorWindow, "_lastTarget") as BObject;

    public BObject? InspectorTargetFor(object inspector) => GetField(inspector, "_lastTarget") as BObject;

    public void LockInspector(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ((EditorWindow)InspectorWindow).isLocked = true;
        RequireField(InspectorWindow.GetType(), "_lastTarget").SetValue(InspectorWindow, target);
    }

    public void LockInspector(object inspector, BObject target)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(target);
        ((EditorWindow)inspector).isLocked = true;
        RequireField(inspector.GetType(), "_lastTarget").SetValue(inspector, target);
    }

    public bool IsWindowLocked(object window) => ((EditorWindow)window).isLocked;

    public void SetWindowLocked(object window, bool value) => ((EditorWindow)window).isLocked = value;

    public IDisposable AttachRuntimeSceneCallbacks()
    {
        var loaded = (Action<Scene, LoadSceneMode>)RequireMethod("OnRuntimeSceneLoaded")
            .CreateDelegate(typeof(Action<Scene, LoadSceneMode>), Application);
        var unloaded = (Action<Scene>)RequireMethod("OnRuntimeSceneUnloaded")
            .CreateDelegate(typeof(Action<Scene>), Application);
        var activeChanged = (Action<Scene?, Scene?>)RequireMethod("OnRuntimeActiveSceneChanged")
            .CreateDelegate(typeof(Action<Scene?, Scene?>), Application);
        RuntimeSceneManager.SceneLoaded += loaded;
        RuntimeSceneManager.SceneUnloaded += unloaded;
        RuntimeSceneManager.ActiveSceneChanged += activeChanged;
        return new RuntimeSceneCallbackSubscription(
            RuntimeSceneManager, loaded, unloaded, activeChanged);
    }

    public bool IsSceneWindowSelected() => (bool)(DockWorkspace.GetType()
        .GetMethod("IsSelected", BindingFlags.Instance | BindingFlags.Public)!
        .Invoke(DockWorkspace, [SceneWindow]) ?? false);

    public void SetHierarchySearch(string value) =>
        RequireField(HierarchyWindow.GetType(), "_search").SetValue(HierarchyWindow, value);

    public Guid? HierarchyRenamingId =>
        RequireField(HierarchyWindow.GetType(), "_renamingId").GetValue(HierarchyWindow) as Guid?;

    public void SetHierarchyRenameValue(string value) =>
        RequireField(HierarchyWindow.GetType(), "_renameValue").SetValue(HierarchyWindow, value);

    public string HierarchyRenameValue =>
        (string)(RequireField(HierarchyWindow.GetType(), "_renameValue").GetValue(HierarchyWindow) ??
                 string.Empty);

    public string GuiFocusState
    {
        get
        {
            var gui = typeof(GUI);
            var members = BindingFlags.Static | BindingFlags.NonPublic;
            return $"focused='{gui.GetField("_focusedControlName", members)!.GetValue(null)}'," +
                   $"pending='{gui.GetField("_pendingFocusControlName", members)!.GetValue(null)}'," +
                   $"active={gui.GetField("_activeTextControl", members)!.GetValue(null)}," +
                   $"keyboard={GUIUtility.keyboardControl}";
        }
    }

    public IReadOnlyList<GpuCanvasCommand> RenderHierarchy(Event evt, int width = 520, int height = 640)
        => RenderWindow(HierarchyWindow, evt, width, height);

    public IReadOnlyList<GpuCanvasCommand> RenderScene(Event evt, int width = 640, int height = 480)
        => RenderWindow(SceneWindow, evt, width, height);

    public void LoseSceneFocus() => SceneWindow.GetType()
        .GetMethod("OnLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(SceneWindow, null);

    public int SceneNavigationButton =>
        (int)(GetField(SceneWindow, "_navigationButton") ?? -1);

    public IReadOnlyList<GpuCanvasCommand> RenderInspector(
        object inspector,
        Event evt,
        int width = 520,
        int height = 640) => RenderWindow(inspector, evt, width, height);

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
            if (scene.isCreated) scene.Dispose();
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

    private MethodInfo RequireMethod(string name) =>
        _applicationType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException(_applicationType.FullName, name);

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

    private sealed class RuntimeSceneCallbackSubscription(
        IRuntimeSceneManager manager,
        Action<Scene, LoadSceneMode> loaded,
        Action<Scene> unloaded,
        Action<Scene?, Scene?> activeChanged) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            manager.SceneLoaded -= loaded;
            manager.SceneUnloaded -= unloaded;
            manager.ActiveSceneChanged -= activeChanged;
        }
    }
}
