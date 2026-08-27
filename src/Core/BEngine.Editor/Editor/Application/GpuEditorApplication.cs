using System.Diagnostics;
using System.Reflection;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.DependencyInjection;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using UnityEditor.IMGUI.Controls;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

/// <summary>GPU-native editor. Every panel is redrawn through Unity-style immediate-mode OnGUI calls.</summary>
internal sealed class GpuEditorApplication : IDisposable, IEditorHost
{
    private static readonly string[] BuiltInMenuRoots =
        ["File", "Edit", "Assets", "GameObject", "Component", "Window", "Help"];
    private readonly ProjectWorkspace _workspace;
    private readonly IServiceProvider _services;
    private readonly ServiceProvider? _ownedServices;
    private readonly bool _ownsProjectDependencies;
    private readonly ISceneRuntimeFactory _sceneRuntimeFactory;
    private readonly IRuntimeSceneManager _runtimeSceneManager;
    private readonly IEditorTaskScheduler _tasks;
    private readonly bool _ownsTaskScheduler;
    private readonly ProjectAssetDatabase _assets;
    private readonly ProjectSourceChangeMonitor _sourceChanges;
    private readonly BPackageManager _packages;
    private readonly EditorLayoutStore _layoutStore;
    private MenuItemRegistry _menuItems;
    private readonly ImGuiDockWorkspace _dock = new();
    private readonly Dictionary<EditorWindow, ImGuiDockPanel> _editorPanels = [];
    private readonly EditorWindowLayer _windowLayer = new();
    private readonly Dictionary<EditorWindow, NativeFloatingEditorWindow> _nativeFloatingWindows = [];
    private readonly Dictionary<EditorWindow, NativeFloatingEditorWindow> _nativeTransientOwners = [];
    private readonly Dictionary<NativeFloatingEditorWindow, NativeWindowDockTracker> _nativeDockTrackers = [];
    private readonly Dictionary<NativeFloatingEditorWindow, Vector2> _nativeFloatingCarries = [];
    private readonly List<(EditorWindow Window, DockArea Area)> _builtInWindows = [];
    private readonly Queue<PendingUndock> _pendingUndocks = [];
    private readonly Queue<(NativeFloatingEditorWindow Presentation, Vector2 ScreenPoint, bool RequireTarget)>
        _pendingNativeDocks = [];
    private readonly HashSet<EditorWindow> _pendingNativeCloses = [];
    private readonly string _instanceLogPath;
    private readonly EditorSessionLogWriter _sessionLogWriter;
    private string _scenePath;
    private readonly ImGuiNativeWindow _mainWindow;
    private readonly ImGuiHierarchyWindow _hierarchy;
    private readonly ImGuiSceneWindow _sceneView;
    private readonly ImGuiGameWindow _gameView;
    private readonly ImGuiInspectorWindow _inspector;
    private readonly ImGuiProjectWindow _project;
    private readonly ImGuiConsoleWindow _console;
    private readonly ImGuiPackageManagerWindow _packageManager;
    private readonly List<EditorOpenScene> _openScenes = [];
    private Scene[] _loadedSceneSnapshot = [];
    private Scene _scene;
    private Scene? _mainScene;
    private GameObject? _mainSelection;
    private PrefabStage? _prefabStage;
    private readonly List<SceneRuntime> _runtimes = [];
    private GameObject? _selected;
    private BObject? _selectedAsset;
    private string? _selectedAssetPath;
    private bool _playing;
    private bool _paused;
    private bool _enteringPlayMode;
    private bool _exitingPlayMode;
    private bool _registeringPlayModeScenes;
    private bool _dirty;
    private bool _mainSceneDirty;
    private EditorPlayModeSession? _playModeSession;
    private bool _scriptCompilationFailed;
    private bool _consoleClearedForPackageReload;
    private int _errorPauseRequested;
    private readonly SemaphoreSlim _shaderCompilationGate = new(1, 1);
    private CancellationTokenSource? _shaderCompilationCancellation;
    private int _shaderCompilationGeneration;
    private readonly SemaphoreSlim _scriptCompilationGate = new(1, 1);
    private CancellationTokenSource? _scriptCompilationCancellation;
    private int _scriptCompilationGeneration;
    private bool _backgroundCompilationActive;
    private bool _scriptCompilationDeferred;
    private readonly SemaphoreSlim _assetRefreshGate = new(1, 1);
    private CancellationTokenSource? _assetRefreshCancellation;
    private int _assetRefreshGeneration;
    private bool _pendingRefreshScripts;
    private bool _pendingRefreshShaders;
    private bool _disposed;
    private bool _closing;
    private bool _applicationFocusLossPending;
    private bool _closePendingAfterPlayModeTransition;
    private Tool _tool = Tool.Move;
    private readonly Dictionary<IGraphicsDevice, PortableSceneRenderer> _sceneRenderers =
        new(ReferenceEqualityComparer.Instance);
    private int _lastWidth;
    private int _lastHeight;
    private NVector2 _editorCameraPosition;
    private float _editorCameraSize = 5;
    private float _editorCameraReferenceHeight;
    private NVector2 _editorCameraPivot;
    private string? _openMenu;
    private Rect _openMenuAnchor;
    private readonly ImGuiPopupMenu _mainMenuPopup = new();
    private readonly ImGuiPopupMenu _genericMenuPopup = new();
    private readonly ImGuiAdvancedDropdown _advancedDropdown = new();
    private Rect? _genericMenuAnchor;
    private EditorProgressInfo _progress;
    private Rect _dockBounds;
    private string _activeLayoutName = "Last Session";
    private bool _layoutSaved;
    private GameObject? _copiedGameObject;
    private string? _copiedAssetPath;
    private readonly Dictionary<Type, string?> _scriptSourceCache = [];

    public GpuEditorApplication(string projectPath, bool openEditorStatusOnStart, bool openUiBuilderOnStart)
        : this(
            new EditorLaunchOptions(projectPath, openEditorStatusOnStart, openUiBuilderOnStart),
            null,
            null,
            null,
            null,
            null,
            null) { }

    internal GpuEditorApplication(
        EditorLaunchOptions options,
        IServiceProvider? services,
        ProjectWorkspace? workspace,
        EditorLayoutStore? layoutStore,
        ProjectAssetDatabase? assets,
        BPackageManager? packages,
        ISceneRuntimeFactory? sceneRuntimeFactory)
        : this(options, services, workspace, layoutStore, assets, packages, sceneRuntimeFactory, null) { }

    internal GpuEditorApplication(
        EditorLaunchOptions options,
        IServiceProvider? services,
        ProjectWorkspace? workspace,
        EditorLayoutStore? layoutStore,
        ProjectAssetDatabase? assets,
        BPackageManager? packages,
        ISceneRuntimeFactory? sceneRuntimeFactory,
        IEditorTaskScheduler? taskScheduler)
    {
        EditorLogStore.Initialize();
        var projectPath = Path.GetFullPath(options.ProjectPath);
        var initialWorkspace = workspace ?? ProjectWorkspace.Open(projectPath);
        ProjectRuntimeSettings.LoadAndApply(initialWorkspace);
        var openEditorStatusOnStart = options.OpenEditorStatusOnStart;
        var openUiBuilderOnStart = options.OpenUiBuilderOnStart;
        if (services is null)
        {
            _ownedServices = new ServiceCollection()
                .AddBEngine(new EngineServiceContext(EngineHostKind.Editor, Path.GetFullPath(projectPath)),
                    [typeof(BObject).Assembly, typeof(EditorWindow).Assembly])
                .AddSingleton<ISceneLoader>(new EditorProjectSceneLoader(projectPath))
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            _services = _ownedServices;
        }
        else
            _services = services;
        _sceneRuntimeFactory = sceneRuntimeFactory ?? new SceneRuntimeFactory();
        _runtimeSceneManager = _services.GetRequiredService<IRuntimeSceneManager>();
        _tasks = taskScheduler ?? new EditorTaskScheduler();
        _ownsTaskScheduler = taskScheduler is null;
        EditorUtility.DisplayProgressBar("打开项目", "读取工程配置...", 0.05f);
        _workspace = initialWorkspace;
        _layoutStore = layoutStore ?? new EditorLayoutStore(_workspace);
        _ownsProjectDependencies = workspace is null;
        Directory.SetCurrentDirectory(_workspace.RootPath);
        _instanceLogPath = Path.Combine(EditorInstanceContext.current?.logsPath ?? _workspace.LogsPath,
            "Editor.log");
        _sessionLogWriter = new EditorSessionLogWriter(_instanceLogPath, _tasks);
        Application.isEditor = true;
        Application.isPlaying = false;
        Application.dataPath = _workspace.AssetsPath;
        Application.productName = _workspace.Project.Name;
        RegisterResourceRoot();
        EditorPreferences.Initialize();
        Time.fixedDeltaTime = Fix64.Parse(_workspace.Project.FixedDeltaTime);

        EditorProjectSettings.Initialize(_workspace.ProjectSettingsFilePath, _workspace.Project.Name);

        EditorUtility.DisplayProgressBar("打开项目", "扫描资源...", 0.16f);
        _assets = assets ?? new ProjectAssetDatabase(_workspace);
        _assets.assetsChanged += OnAssetsChanged;
        _assets.Refresh(progress => EditorUtility.DisplayProgressBar("打开项目",
            $"导入资源 {progress.Completed}/{progress.Total}: {progress.AssetPath}",
            0.16f + progress.Ratio * 0.19f));
        _sourceChanges = new ProjectSourceChangeMonitor(_workspace);

        EditorUtility.DisplayProgressBar("打开项目", "加载扩展包...", 0.38f);
        _packages = packages ?? new BPackageManager(_workspace);
        _packages.packagesReloading += OnPackagesReloading;
        _packages.packagesUnloading += OnPackagesUnloading;
        CompileScripts();
        CompileShaders(_assets.assets.Where(asset => asset.AssetType == "Shader")
            .Select(asset => asset.AssetPath));
        _packages.BeginDynamicEditorInitialization();
        EditorUtility.DisplayProgressBar("打开项目", "构建运行时与编辑器反射缓存...", 0.68f);
        TypeCache.Warmup();
        _menuItems = DiscoverMenuItems(MenuItemRegistry.Empty());

        _scenePath = ResolveInitialScene();
        EditorUtility.DisplayProgressBar("打开项目", "载入场景...", 0.76f);
        _scene = Document.LoadBObject<SceneDocument, Scene>(_scenePath, _services);
        _scene.path = _scenePath;
        _openScenes.Add(new EditorOpenScene(_scene, _scenePath, ToAssetPath(_scenePath)));
        RefreshLoadedSceneSnapshot();
        _selected = _scene.gameObjects.FirstOrDefault();

        _hierarchy = new ImGuiHierarchyWindow(this);
        _sceneView = new ImGuiSceneWindow(this);
        _gameView = new ImGuiGameWindow(this);
        _inspector = new ImGuiInspectorWindow(this);
        _project = new ImGuiProjectWindow(this);
        _packages.packagesChanged += OnPackagesChanged;
        _console = new ImGuiConsoleWindow(this);
        _packageManager = new ImGuiPackageManagerWindow(this);
        AddBuiltIn(_hierarchy, DockArea.Left);
        AddBuiltIn(_sceneView, DockArea.Center);
        AddBuiltIn(_gameView, DockArea.Center, false);
        AddBuiltIn(_inspector, DockArea.Right);
        AddBuiltIn(_project, DockArea.Bottom);
        AddBuiltIn(_console, DockArea.Bottom, false);
        AddBuiltIn(_packageManager, DockArea.Center, false);
        if (_scriptCompilationFailed) _dock.Show(_console.PersistentId);

        _mainWindow = new ImGuiNativeWindow(BuildTitle(), 1500, 920);
        _mainWindow.gui += OnGUI;
        _mainWindow.updating += OnUpdate;
        _mainWindow.closing += OnClosing;
        _mainWindow.focusChanged += OnMainWindowFocusChanged;
        _dock.UndockRequested += QueueUndock;
        _windowLayer.WindowClosed += CloseEditorWindow;
        _windowLayer.DockRequested += DockFloatingWindow;
        _mainWindow.renderBackground = RenderSceneBackground;
        GenericMenuDispatcher.Handler = OpenDispatchedMenu;

        EditorBridge.Attach(this);
        EditorUtility.progressChanged += OnProgressChanged;
        Debug.MessageLogged += OnLog;
        Undo.undoRedoPerformed += OnUndoRedo;
        _runtimeSceneManager.SceneLoaded += OnRuntimeSceneLoaded;
        _runtimeSceneManager.SceneUnloaded += OnRuntimeSceneUnloaded;
        _runtimeSceneManager.ActiveSceneChanged += OnRuntimeActiveSceneChanged;
        EditorFeatureGuard.Invoke("EditorInitialization.Run", () => EditorInitialization.Run());
        AssemblyReloadEvents.RaiseAfterAssemblyReload();
        RestoreLastLayout();
        InitializeEditorCameraNavigation();
        EditorUtility.DisplayProgressBar("打开项目", "编辑器初始化完成", 1);
        EditorUtility.ClearProgressBar();
        if (openEditorStatusOnStart) EditorStatusWindow.Open();
        if (openUiBuilderOnStart) _menuItems.Execute("Window/UI Builder");
    }

    public void Run(Action? firstFrameRendered = null)
    {
        Trace("Entering GPU IMGUI editor message loop.");
        if (firstFrameRendered is not null)
            _mainWindow.firstFrameRendered += () =>
            {
                firstFrameRendered();
                _mainWindow.Focus();
            };
        _mainWindow.Run();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_playing || _playModeSession is not null)
            ExitPlayMode(raiseStateEvents: false, processDeferredCompilation: false);
        CloseTransientMenus();
        _disposed = true;
        SaveLastLayout();
        Debug.MessageLogged -= OnLog;
        Undo.undoRedoPerformed -= OnUndoRedo;
        _runtimeSceneManager.SceneLoaded -= OnRuntimeSceneLoaded;
        _runtimeSceneManager.SceneUnloaded -= OnRuntimeSceneUnloaded;
        _runtimeSceneManager.ActiveSceneChanged -= OnRuntimeActiveSceneChanged;
        _packages.packagesChanged -= OnPackagesChanged;
        _packages.packagesReloading -= OnPackagesReloading;
        _packages.packagesUnloading -= OnPackagesUnloading;
        _assets.assetsChanged -= OnAssetsChanged;
        _sourceChanges.Dispose();
        CancelAssetRefresh();
        CancelShaderCompilation();
        CancelScriptCompilation();
        _sessionLogWriter.Dispose();
        if (_ownsTaskScheduler) _tasks.Dispose();
        EditorUtility.progressChanged -= OnProgressChanged;
        GenericMenuDispatcher.Handler = null;
        _dock.UndockRequested -= QueueUndock;
        _windowLayer.WindowClosed -= CloseEditorWindow;
        _windowLayer.DockRequested -= DockFloatingWindow;
        foreach (var presentation in _windowLayer.Presentations.ToArray())
        {
            _windowLayer.Remove(presentation.Window);
            presentation.Window.CloseInternal();
        }
        foreach (var renderer in _sceneRenderers.Values) renderer.Dispose();
        _sceneRenderers.Clear();
        foreach (var presentation in _nativeFloatingWindows.Values.ToArray())
            RemoveNativeFloatingWindow(presentation, closeWindow: true);
        foreach (var editorWindow in _editorPanels.Keys.ToArray()) editorWindow.CloseInternal();
        StopAllRuntimes();
        foreach (var scene in _openScenes.Select(item => item.Scene)
                     .Append(_scene).Distinct().ToArray())
            if (scene.isCreated) scene.Dispose();
        _scriptSourceCache.Clear();
        ComponentClipboard.Clear();
        Undo.ClearAll();
        ProjectScriptCompiler.ReleaseLoadContexts();
        if (_ownsProjectDependencies) _packages.Dispose();
        EditorBridge.Detach(this);
        _mainWindow.Dispose();
        _ownedServices?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AddBuiltIn(EditorWindow window, DockArea area, bool select = true)
    {
        window.OpenInternal();
        var id = window.GetType().Name.Replace("ImGui", string.Empty, StringComparison.Ordinal)
            .Replace("Window", string.Empty, StringComparison.Ordinal);
        var title = HumanizeIdentifier(id);
        window.titleContent = new GUIContent(title, $"Icons/Windows/{id}.png", title);
        window.PersistentId = id;
        var panel = _dock.Add(id, window, area, select);
        _editorPanels[window] = panel;
        _builtInWindows.Add((window, area));
        window.windowState = EditorWindowState.Normal;
        window.docked = true;
    }

    private void ShowBuiltIn(EditorWindow window, DockArea area)
    {
        if (_editorPanels.TryGetValue(window, out var panel))
        {
            _dock.Show(panel.Id);
            return;
        }
        if (_windowLayer.Contains(window))
        {
            _windowLayer.Focus(window);
            return;
        }
        if (_nativeFloatingWindows.TryGetValue(window, out var native))
        {
            native.Focus();
            return;
        }

        window.OpenInternal();
        window.windowState = EditorWindowState.Normal;
        window.docked = true;
        _editorPanels[window] = _dock.Add(window.PersistentId, window, area, true);
        window.FocusInternal();
    }

    private void FocusGameViewIfOpen()
    {
        if (_editorPanels.TryGetValue(_gameView, out var panel))
        {
            _dock.Show(panel.Id);
            return;
        }
        if (_nativeFloatingWindows.TryGetValue(_gameView, out var native))
        {
            native.Focus();
            return;
        }
        _windowLayer.Focus(_gameView);
    }

    private static string HumanizeIdentifier(string value)
    {
        if (value.Length < 2) return value;
        var result = new System.Text.StringBuilder(value.Length + 4).Append(value[0]);
        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1])) result.Append(' ');
            result.Append(value[index]);
        }
        return result.ToString();
    }

    private void OnGUI()
    {
        var width = GUIUtility.currentViewWidth;
        var height = GUIUtility.currentViewHeight;
        var menuHeight = Fix64.Max(EditorStyles.toolbar.fixedHeight,
            EditorStyles.toolbarButton.fixedHeight + 4);
        var toolbarHeight = Fix64.Max(EditorStyles.toolbar.fixedHeight + 2,
            EditorStyles.toolbarIconButton.fixedHeight + 8);
        var statusHeight = Fix64.Max(EditorStyles.statusBar.fixedHeight + 2, 20);
        var prefabBarHeight = _prefabStage is null
            ? Fix64.Zero
            : Fix64.Max(EditorStyles.toolbar.fixedHeight + 2,
                EditorStyles.toolbarIconButton.fixedHeight + 6);
        var baseContentY = menuHeight + toolbarHeight;
        var contentY = baseContentY + prefabBarHeight;
        _dockBounds = new Rect(0, contentY, width, Fix64.Max(1, height - contentY - statusHeight));
        var inputPass = ImGuiPopupMenu.IsInputEvent(Event.current.type);
        if (inputPass)
        {
            // Context menus are the top-most input layer. Give an already-open menu the event
            // before the menu bar, window overlays, toolbar, or docked panels can react to it.
            DrawGenericPopup();
            if (Event.current.type == EventType.Used) return;
            if (_windowLayer.HasModal)
            {
                _windowLayer.Draw(_dockBounds, inputPass: true);
                if (Event.current.type == EventType.Used) return;
            }
            DrawMenuBar(new Rect(0, 0, width, menuHeight));
            DrawMenuPopup();
            DrawGenericPopup();
            if (Event.current.type == EventType.Used) return;
            _windowLayer.Draw(_dockBounds, inputPass: true);
            if (Event.current.type == EventType.Used) return;
        }
        else DrawMenuBar(new Rect(0, 0, width, menuHeight));
        DrawToolbar(new Rect(0, menuHeight, width, toolbarHeight));
        if (_prefabStage is not null)
        {
            DrawPrefabStageBar(new Rect(0, baseContentY, width, prefabBarHeight));
        }
        _dock.HostIsInteractive = _mainWindow.isFocused;
        _dock.OnGUI(_dockBounds);
        if (Event.current.type != EventType.Used) HandleGlobalKeyboard();
        DrawStatusBar(new Rect(0, height - statusHeight, width, statusHeight));
        if (!inputPass)
        {
            _windowLayer.Draw(_dockBounds, inputPass: false);
            DrawMenuPopup();
            DrawGenericPopup();
        }
    }

    private void DrawMenuBar(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
        var roots = BuiltInMenuRoots
            .Concat(_menuItems.Roots).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var x = (Fix64)4;
        foreach (var root in roots)
        {
            var content = new GUIContent(root);
            var width = Fix64.Max(42, EditorStyles.toolbarButton.CalcSize(content).x + 18);
            var buttonHeight = Fix64.Min(rect.height - 2,
                Fix64.Max(18, EditorStyles.toolbarButton.fixedHeight));
            var rootRect = new Rect(x, rect.y + (rect.height - buttonHeight) / 2, width, buttonHeight);
            if (_openMenu is not null && Event.current.type == EventType.MouseMove &&
                rootRect.Contains(Event.current.mousePosition) && !_openMenu.Equals(root, StringComparison.Ordinal))
                OpenMainMenu(root, rootRect);
            var rootStyle = _openMenu == root
                ? EditorStyles.toolbarIconButtonSelected
                : EditorStyles.toolbarButton;
            if (GUI.Button(rootRect, content, rootStyle))
            {
                if (_openMenu == root)
                {
                    _openMenu = null;
                    _mainMenuPopup.Close();
                }
                else OpenMainMenu(root, rootRect);
            }
            x += width;
        }
    }

    private void DrawMenuPopup()
    {
        if (_openMenu is null) return;
        if (!_mainMenuPopup.Draw(_openMenuAnchor)) _openMenu = null;
    }

    private void OpenMainMenu(string root, Rect rootRect)
    {
        _openMenu = root;
        _openMenuAnchor = new Rect(rootRect.x, rootRect.y, rootRect.width, rootRect.height + 1);
        _mainMenuPopup.Open(MenuItems(root).Select(item => new GenericMenuItem(
            item.Label, item.Checked, item.Enabled, false, item.Action)),
            new Vector2(rootRect.x, rootRect.yMax));
    }

    private IEnumerable<MenuEntry> MenuItems(string root)
    {
        var registeredEmitted = false;
        if (root.Equals("Component", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in ComponentRootMenuItems())
                yield return new(MenuDisplayLabel(root, item.Label), item.Enabled, item.Action, item.Checked);
            registeredEmitted = true;
        }
        else if (root.Equals("Window", StringComparison.OrdinalIgnoreCase))
        {
            yield return new("Layouts/Save Current", true, SaveCurrentLayout);
            yield return new("Layouts/Save As...", true,
                () => SaveLayoutWindow.Open(_activeLayoutName, SaveNamedLayout));
            yield return new("Layouts/Load Last Session", _layoutStore.HasLastSession, RestoreLastLayout,
                _activeLayoutName.Equals("Last Session", StringComparison.OrdinalIgnoreCase));
            foreach (var name in _layoutStore.Names)
            {
                var capturedName = name;
                yield return new($"Layouts/Switch/{name}", true, () => LoadNamedLayout(capturedName),
                    _activeLayoutName.Equals(name, StringComparison.OrdinalIgnoreCase));
                yield return new($"Layouts/Delete/{name}", true, () => DeleteNamedLayout(capturedName));
            }
            foreach (var builtIn in _builtInWindows.OrderBy(item => BuiltInWindowMenuPath(item.Window),
                         StringComparer.OrdinalIgnoreCase))
            {
                var captured = builtIn;
                var visible = _editorPanels.ContainsKey(captured.Window) ||
                              _windowLayer.Contains(captured.Window);
                yield return new(BuiltInWindowMenuPath(captured.Window), true,
                    () => ShowBuiltIn(captured.Window, captured.Area), visible);
            }
            var builtInSet = _builtInWindows.Select(item => item.Window).ToHashSet();
            foreach (var panel in _dock.Panels.Where(panel => !builtInSet.Contains(panel.Window)))
            {
                var captured = panel;
                yield return new(panel.Window.titleContent.text, true, () => _dock.Show(captured.Id),
                    panel.Visible);
            }
        }
        if (registeredEmitted) yield break;
        foreach (var node in FlattenMenu(_menuItems.GetRoot(root)))
            yield return new(MenuDisplayLabel(root, node.Label), node.Enabled, node.Action, node.Checked);
    }

    private static string MenuDisplayLabel(string root, string label)
    {
        var shortcut = $"{root}/{label}" switch
        {
            "File/New Scene" => "Ctrl+N",
            "File/Open Scene..." => "Ctrl+O",
            "File/Save Scene" => "Ctrl+S",
            "Edit/Undo" => "Ctrl+Z",
            "Edit/Redo" => "Ctrl+Y",
            "Edit/Copy" => "Ctrl+C",
            "Edit/Paste" => "Ctrl+V",
            "Edit/Duplicate" => "Ctrl+D",
            "Edit/Rename" => "F2",
            "Edit/Delete" => "Del",
            "Edit/Select All" => "Ctrl+A",
            "Edit/Frame Selected" => "F",
            "Edit/Play" => "Ctrl+P",
            "Edit/Pause" => "Ctrl+Shift+P",
            "Edit/Step" => "Ctrl+Alt+P",
            _ => string.Empty
        };
        return shortcut.Length == 0 ? label : $"{label.PadRight(30)}{shortcut}";
    }

    private static string BuiltInWindowMenuPath(EditorWindow window)
    {
        var title = window.titleContent.text;
        return window is ImGuiConsoleWindow or ImGuiGameWindow or ImGuiHierarchyWindow or
            ImGuiProjectWindow or ImGuiInspectorWindow or ImGuiSceneWindow
            ? $"General/{title}"
            : title;
    }

    private static IEnumerable<MenuEntry> FlattenMenu(IEnumerable<MenuItemRegistry.MenuNode> nodes,
        string prefix = "")
    {
        foreach (var node in nodes)
        {
            var path = string.IsNullOrEmpty(prefix) ? node.Name : $"{prefix}/{node.Name}";
            if (node.Children.Count > 0)
            {
                foreach (var child in FlattenMenu(node.Children, path)) yield return child;
            }
            else yield return new MenuEntry(path, node.Enabled, node.Execute, node.Checked);
        }
    }

    private IEnumerable<MenuEntry> ComponentMenuItems()
    {
        var selected = _selected;
        var components = TypeCache.GetTypesDerivedFrom<Component>()
            .Where(type => !type.IsAbstract && type != typeof(Transform) && type != typeof(MissingComponent))
            .Select(type => (Type: type, Menu: type.GetCustomAttribute<AddComponentMenuAttribute>()))
            .Select(item => (item.Type, Path: ComponentMenuPath(item.Type, item.Menu),
                Order: item.Menu?.componentOrder ?? 1000))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();
        foreach (var component in components)
        {
            var disallowMultiple = component.Type.IsDefined(typeof(DisallowMultipleComponentAttribute), false);
            var enabled = selected is not null &&
                          (!disallowMultiple || selected.GetComponent(component.Type) is null);
            var type = component.Type;
            yield return new MenuEntry(component.Path, enabled, () => AddSelectedComponent(type));
        }
    }

    private IEnumerable<MenuEntry> ComponentRootMenuItems(BObject? context = null)
    {
        foreach (var node in FlattenMenu(_menuItems.GetRoot("Component", context)))
            yield return new MenuEntry(node.Label, node.Enabled, node.Action, node.Checked);
        foreach (var item in ComponentMenuItems()) yield return item;
    }

    private static string ComponentMenuPath(Type componentType, AddComponentMenuAttribute? attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute?.componentMenu))
            return attribute.componentMenu.Replace('\\', '/').Trim('/');
        return typeof(MonoBehaviour).IsAssignableFrom(componentType)
            ? $"Scripts/{componentType.Name}"
            : componentType.Name;
    }

    private void AddSelectedComponent(Type componentType)
    {
        if (_selected is null) return;
        try
        {
            ObjectFactory.AddComponent(_selected, componentType);
            MarkDirty(_selected.scene ?? _scene);
            _inspector.RebuildEditor();
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report($"Add component {componentType.FullName}", exception);
        }
    }

    private void ShowAddComponentMenu()
    {
        var menu = new GenericMenu();
        foreach (var entry in ComponentRootMenuItems(_selected))
        {
            if (entry.Enabled && entry.Action is not null)
                menu.AddItem(new GUIContent(entry.Label), entry.Checked, entry.Action.Invoke);
            else
                menu.AddDisabledItem(new GUIContent(entry.Label), entry.Checked);
        }
        menu.ShowAsAdvancedDropdown();
    }

    private void DrawToolbar(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
        var buttonHeight = Fix64.Min(rect.height - 6,
            Fix64.Max(18, EditorStyles.toolbarIconButton.fixedHeight));
        var buttonY = rect.y + (rect.height - buttonHeight) / 2;
        var toolWidth = Fix64.Max(25, EditorStyles.toolbarIconButton.fixedWidth + 3);
        var x = rect.x + 6;
        foreach (var (icon, tooltip, tool) in new[]
                 {
                     (EditorBuiltinIcons.Toolbar.Move, "Move Tool (W)", Tool.Move),
                     (EditorBuiltinIcons.Toolbar.Rotate, "Rotate Tool (E)", Tool.Rotate),
                     (EditorBuiltinIcons.Toolbar.Scale, "Scale Tool (R)", Tool.Scale)
                 })
        {
            if (EditorToolbar.Toggle(new Rect(x, buttonY, toolWidth, buttonHeight), _tool == tool,
                    new GUIContent(string.Empty, icon, tooltip))) _tool = tool;
            x += toolWidth + 2;
        }
        var transportWidth = Fix64.Max(32, toolWidth + 7);
        var transportGap = (Fix64)4;
        var transportGroupWidth = transportWidth * 3 + transportGap * 2;
        var center = rect.x + (rect.width - transportGroupWidth) / 2;
        if (EditorToolbar.Button(new Rect(center, buttonY, transportWidth, buttonHeight),
                new GUIContent(string.Empty, _playing ? EditorBuiltinIcons.Toolbar.Stop : EditorBuiltinIcons.Toolbar.Play,
                    _playing ? "Stop" : "Play"))) TogglePlay();
        var old = GUI.enabled; GUI.enabled = _playing;
        _paused = EditorToolbar.Toggle(new Rect(center + transportWidth + transportGap, buttonY,
                transportWidth, buttonHeight), _paused,
            new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Pause, _paused ? "Resume" : "Pause"));
        if (EditorToolbar.Button(new Rect(center + (transportWidth + transportGap) * 2, buttonY,
                transportWidth, buttonHeight),
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Step, "Step"))) Step();
        GUI.enabled = old;
    }

    private void DrawPrefabStageBar(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.notificationBackground);
        GUI.Box(new Rect(rect.x, rect.yMax - 1, rect.width, 1), GUIContent.none,
            EditorStyles.separator);
        var buttonHeight = Fix64.Min(rect.height - 4,
            Fix64.Max(18, EditorStyles.toolbarIconButton.fixedHeight));
        var buttonY = rect.y + (rect.height - buttonHeight) / 2;
        if (EditorToolbar.Button(new Rect(rect.x + 5, buttonY, 25, buttonHeight),
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Up, "Back to Scene"))) ClosePrefabStage();
        GUI.Label(new Rect(rect.x + 38, rect.y, rect.width - 150, rect.height),
            new GUIContent($"Prefab  >  {_prefabStage!.prefabContentsRoot.name}",
                EditorBuiltinIcons.Assets.Prefab, _prefabStage.assetPath), EditorStyles.boldLabel);
        if (EditorToolbar.Button(new Rect(rect.xMax - 106, buttonY, 100, buttonHeight),
                new GUIContent("Save", EditorBuiltinIcons.Toolbar.Save, "Save Prefab"))) SavePrefabStage();
    }

    private void DrawStatusBar(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.statusBar);
        var text = _progress.IsVisible
            ? $"{_progress.Title}: {_progress.Info} ({_progress.Progress:P0})"
            : $"IMGUI | {_mainWindow.backend} | {Event.current.type}" +
              (_tasks.PendingBackgroundCount + _tasks.PendingMainThreadCount > 0
                  ? $" | Background: {_tasks.PendingBackgroundCount} | Apply: {_tasks.PendingMainThreadCount}"
                  : string.Empty);
        GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 12, rect.height), text, EditorStyles.statusBar);
        if (_progress.IsVisible)
            GUI.DrawRect(new Rect(rect.x, rect.y, rect.width * (Fix64)_progress.Progress, 2),
                EditorStyles.progressBarBar.normal.backgroundColor);
    }

    private void DrawGenericPopup()
    {
        if (_advancedDropdown.isOpen) _advancedDropdown.Draw();
        else if (!_genericMenuPopup.Draw(_genericMenuAnchor)) _genericMenuAnchor = null;
    }

    private void OpenDispatchedMenu(IReadOnlyList<GenericMenuItem> items)
    {
        var presentation = GenericMenuDispatcher.CurrentPresentation;
        Rect? rootAnchor = null;
        Vector2 position;
        if (presentation.HasAnchor)
        {
            var topLeft = GUI.GUIToRootPoint(presentation.Anchor.position);
            var bottomRight = GUI.GUIToRootPoint(new Vector2(
                presentation.Anchor.xMax, presentation.Anchor.yMax));
            rootAnchor = new Rect(topLeft.x, topLeft.y,
                Fix64.Max(0, bottomRight.x - topLeft.x),
                Fix64.Max(0, bottomRight.y - topLeft.y));
            position = new Vector2(rootAnchor.Value.x, rootAnchor.Value.yMax);
        }
        else
            position = GUI.GUIToRootPoint(Event.current.mousePosition);

        if (presentation.IsAdvanced)
        {
            _genericMenuPopup.Close();
            _genericMenuAnchor = null;
            _advancedDropdown.Open(items, position, rootAnchor);
        }
        else
        {
            _advancedDropdown.Close();
            _genericMenuAnchor = rootAnchor;
            _genericMenuPopup.Open(items, position);
        }
    }

    private void CloseTransientMenus()
    {
        _openMenu = null;
        _mainMenuPopup.Close();
        _genericMenuPopup.Close();
        _genericMenuAnchor = null;
        _advancedDropdown.Close();
    }

    private void OnUpdate(double deltaSeconds)
    {
        _tasks.PumpMainThread(TimeSpan.FromMilliseconds(2));
        var delta = Math.Clamp(deltaSeconds, 0, 0.1);
        Time.deltaTime = (Fix64)delta;
        Time.time += Time.deltaTime;
        if (Interlocked.Exchange(ref _errorPauseRequested, 0) != 0 && _playing)
            EditorApplication.isPaused = true;
        EditorFeatureGuard.Invoke("EditorApplication.update", EditorApplication.RaiseUpdate);
        if (_playing && !_paused)
            foreach (var runtime in _runtimes.ToArray())
                EditorFeatureGuard.Invoke(runtime, "SceneRuntime.Tick", () => runtime.Tick(Time.deltaTime));
        foreach (var window in EditorWindow.EnumerateOpenWindows()) window.UpdateInternal();
        EditorFeatureGuard.Invoke("DockWorkspace.ProcessPendingUndocks", ProcessPendingUndocks);
        EditorFeatureGuard.Invoke("NativeFloatingWindows.Pump", PumpNativeFloatingWindows);
        ApplyPendingApplicationFocusLoss();
        ProcessProjectSourceChanges();
    }

    private void OnMainWindowFocusChanged(bool focused)
    {
        if (!focused)
        {
            CloseTransientMenus();
            _windowLayer.DismissPopups();
        }
        if (focused && EditorWindow.focusedWindow is { } focusedWindow &&
            (_nativeFloatingWindows.ContainsKey(focusedWindow) ||
             _nativeTransientOwners.ContainsKey(focusedWindow)))
            focusedWindow.LoseFocusInternal();
        if (focused && _windowLayer.TopModalWindow is { } modal)
            _windowLayer.Focus(modal);
        UpdateApplicationFocus();
    }

    private void OnNativeWindowFocusChanged(NativeFloatingEditorWindow presentation, bool focused)
    {
        if (focused && _windowLayer.TopModalWindow is { } modal)
        {
            _windowLayer.Focus(modal);
            _mainWindow.Focus();
        }
        else if (focused && !(EditorWindow.focusedWindow is { } focusedWindow &&
                              _nativeTransientOwners.TryGetValue(focusedWindow, out var owner) &&
                              ReferenceEquals(owner, presentation)))
            presentation.Window.FocusInternal();
        UpdateApplicationFocus();
    }

    private void UpdateApplicationFocus()
    {
        if (_mainWindow.isFocused || _nativeFloatingWindows.Values.Any(window => window.IsFocused))
        {
            _applicationFocusLossPending = false;
            return;
        }
        _applicationFocusLossPending = true;
    }

    private void ApplyPendingApplicationFocusLoss()
    {
        if (!_applicationFocusLossPending || _mainWindow.isFocused ||
            _nativeFloatingWindows.Values.Any(window => window.IsFocused)) return;
        _applicationFocusLossPending = false;
        GUIUtility.ReleaseInputFocus();
        _dock.CancelInteractions();
        _windowLayer.CancelInteractions();
        DragAndDrop.Cancel();
        CloseTransientMenus();
        EditorWindow.focusedWindow?.LoseFocusInternal();
    }

    private void OnClosing()
    {
        if (_closing) return;
        _closing = true;
        if (_enteringPlayMode || _exitingPlayMode)
        {
            _closePendingAfterPlayModeTransition = true;
            return;
        }
        CompleteClose();
    }

    private void CompleteClose()
    {
        _closePendingAfterPlayModeTransition = false;
        CloseTransientMenus();
        if (_playing || _playModeSession is not null)
            ExitPlayMode(processDeferredCompilation: false);
        EditorFeatureGuard.Invoke("EditorLayout.SaveLastLayout", SaveLastLayout);
        if (_prefabStage is not null)
            EditorFeatureGuard.Invoke("PrefabStage.Close", ClosePrefabStage);
        EditorFeatureGuard.Invoke("Scene.SaveOnExit", SaveAllOpenScenes);
        EditorFeatureGuard.Invoke("EditorApplication.quitting", EditorApplication.RaiseQuitting);
        foreach (var window in EditorWindow.EnumerateOpenWindows().ToArray()) window.CloseInternal();
    }

    private void RenderSceneBackground(IGraphicsDevice device, int width, int height)
    {
        var renderer = SceneRendererFor(device);

        if (_dock.IsSelected(_sceneView) && !_windowLayer.Contains(_sceneView))
            RenderEditorSceneViewport(renderer, device, width, height);
        if (_dock.IsSelected(_gameView) && !_windowLayer.Contains(_gameView))
            RenderGameViewport(renderer, device, width, height);

        foreach (var presentation in _windowLayer.Presentations)
        {
            if (ReferenceEquals(presentation.Window, _sceneView))
                RenderEditorSceneViewport(renderer, device, width, height);
            else if (ReferenceEquals(presentation.Window, _gameView))
                RenderGameViewport(renderer, device, width, height);
        }
    }

    private void RenderNativeWindowBackground(NativeFloatingEditorWindow presentation,
        IGraphicsDevice device, int width, int height)
    {
        if (ReferenceEquals(presentation.Window, _sceneView))
            RenderEditorSceneViewport(SceneRendererFor(device), device, width, height);
        else if (ReferenceEquals(presentation.Window, _gameView))
            RenderGameViewport(SceneRendererFor(device), device, width, height);
    }

    private PortableSceneRenderer SceneRendererFor(IGraphicsDevice device)
    {
        if (_sceneRenderers.TryGetValue(device, out var renderer)) return renderer;
        renderer = new PortableSceneRenderer(device);
        _sceneRenderers.Add(device, renderer);
        return renderer;
    }

    private void RenderEditorSceneViewport(PortableSceneRenderer renderer, IGraphicsDevice device,
        int frameWidth, int frameHeight)
    {
        if (!TryGetRenderViewport(_sceneView, device, frameWidth, frameHeight, out var viewport)) return;
        _lastWidth = viewport.Width;
        _lastHeight = viewport.Height;
        var scenes = LoadedScenes();
        var renderScale = Math.Max(0.01f, (float)RenderScaleFor(_sceneView));
        var editorCamera = EditorCamera(viewport.Height / renderScale);
        renderer.RenderViewport(scenes, _scene, editorCamera, viewport,
            initializeColor: true, drawGrid: true, drawUi: false, drawGizmos: false,
            objectFilter: SceneVisibilityManager.instance.IsVisible);
        if (!SceneGizmoVisibility.Enabled) return;
        var gizmos = SceneGizmoPass.Collect(scenes, _selected, viewport.Width, viewport.Height);
        renderer.DrawGizmos(gizmos.Lines, editorCamera, viewport);
    }

    private void RenderGameViewport(PortableSceneRenderer renderer, IGraphicsDevice device,
        int frameWidth, int frameHeight)
    {
        if (!TryGetRenderViewport(_gameView, device, frameWidth, frameHeight, out var availableViewport)) return;
        var viewport = _gameView.FitRenderViewport(availableViewport);
        if (viewport != availableViewport)
            renderer.FillViewport(availableViewport, new NVector4(0.025f, 0.028f, 0.032f, 1));
        var targetSize = _gameView.ApplyTargetSize(viewport);
        var cameras = EngineRenderer.ResolveGameCameras(LoadedScenes());
        renderer.RenderCameras(LoadedScenes(), _scene, cameras, viewport,
            targetSize.Width, targetSize.Height, drawUi: true);
        _gameView.UpdateRenderStatistics(renderer.LastRenderStatistics);
    }

    private bool TryGetRenderViewport(EditorWindow window, IGraphicsDevice device,
        int frameWidth, int frameHeight, out GraphicsRect viewport)
    {
        var rect = _nativeFloatingWindows.TryGetValue(window, out var native)
            ? native.ContentRect(frameWidth, frameHeight)
            : _windowLayer.TryGetContentRect(window, out var floatingContent)
                ? floatingContent
                : window.position;
        var toolbarHeight = EditorStyles.toolbar.fixedHeight;
        var scale = Math.Max(0.01f, (float)RenderScaleFor(window));
        var left = Math.Clamp((int)MathF.Floor((float)rect.x * scale), 0, frameWidth);
        var top = Math.Clamp((int)MathF.Floor((float)(rect.y + toolbarHeight) * scale), 0, frameHeight);
        var right = Math.Clamp((int)MathF.Ceiling((float)rect.xMax * scale), left, frameWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling((float)rect.yMax * scale), top, frameHeight);
        var viewportWidth = right - left;
        var viewportHeight = bottom - top;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            viewport = default;
            return false;
        }

        var graphicsY = device.Backend == GraphicsBackend.OpenGL
            ? frameHeight - bottom
            : top;
        viewport = new GraphicsRect(left, graphicsY, viewportWidth, viewportHeight);
        return true;
    }

    private Fix64 RenderScaleFor(EditorWindow window) =>
        _nativeFloatingWindows.TryGetValue(window, out var native)
            ? native.RenderScale
            : _mainWindow.renderScale;

    private RenderCamera EditorCamera(float viewportHeight)
    {
        viewportHeight = NormalizeEditorViewportHeight(viewportHeight);
        var referenceHeight = EnsureEditorCameraReferenceHeight(viewportHeight);
        var effectiveSize = _editorCameraSize * viewportHeight / referenceHeight;
        return new RenderCamera(
            new Vector2((Fix64)_editorCameraPosition.X, (Fix64)_editorCameraPosition.Y),
            Fix64.Zero, (Fix64)effectiveSize,
            new NVector4(0.055f, 0.071f, 0.09f, 1));
    }

    private float EnsureEditorCameraReferenceHeight(float viewportHeight)
    {
        viewportHeight = NormalizeEditorViewportHeight(viewportHeight);
        if (!float.IsFinite(_editorCameraReferenceHeight) || _editorCameraReferenceHeight <= 0)
            _editorCameraReferenceHeight = viewportHeight;
        return _editorCameraReferenceHeight;
    }

    private static float NormalizeEditorViewportHeight(float viewportHeight) =>
        float.IsFinite(viewportHeight) ? Math.Max(1, viewportHeight) : 1;

    private void InitializeEditorCameraNavigation()
    {
        _editorCameraPivot = _editorCameraPosition;
    }

    private void ZoomEditorCamera(float wheelDelta)
    {
        if (!float.IsFinite(wheelDelta) || MathF.Abs(wheelDelta) < 0.0001f) return;
        _editorCameraSize = Math.Clamp(_editorCameraSize * MathF.Exp(wheelDelta * 0.14f), 0.01f, 100000f);
    }

    private void PanEditorCamera(float deltaX, float deltaY, float viewportHeight)
    {
        if (!float.IsFinite(deltaX) || !float.IsFinite(deltaY)) return;
        var worldPerPixel = EditorWorldUnitsPerPixel(viewportHeight);
        var translation = new NVector2(-deltaX, deltaY) * worldPerPixel;
        _editorCameraPosition += translation;
        _editorCameraPivot += translation;
    }

    private float EditorWorldUnitsPerPixel(float viewportHeight) =>
        2 * _editorCameraSize / EnsureEditorCameraReferenceHeight(viewportHeight);

    private bool TryProjectEditorPoint(NVector2 worldPosition, Rect viewport, out Vector2 point)
    {
        var viewportPoint = EditorCamera((float)viewport.height).WorldToViewport(
            new Vector2((Fix64)worldPosition.X, (Fix64)worldPosition.Y),
            Math.Max(1, (int)viewport.width), Math.Max(1, (int)viewport.height));
        point = new Vector2(viewport.x + (Fix64)viewportPoint.X, viewport.y + (Fix64)viewportPoint.Y);
        return float.IsFinite(viewportPoint.X) && float.IsFinite(viewportPoint.Y);
    }

    private void OnProgressChanged(EditorProgressInfo progress) => _progress = progress;
    private void OnLog(LogEntry entry)
    {
        if (ConsoleLogController.ShouldPauseOnError(entry, _playing))
            Interlocked.Exchange(ref _errorPauseRequested, 1);
        Trace($"[{entry.Type}] {entry.Message}{(string.IsNullOrWhiteSpace(entry.StackTrace) ? string.Empty :
            $"{Environment.NewLine}{entry.StackTrace}")}");
    }
    private void OnAssetsChanged(IReadOnlyList<AssetChange> changes)
    {
        BAsset.ClearLoadedAssets();
        TextureAtlasResolver.Clear();
        AssetPreview.ClearTemporaryAssetPreviews();
        var selectedSkin = EditorPreferences.current.EditorSkin?.Trim() ?? string.Empty;
        if (selectedSkin.Length > 0 &&
            !selectedSkin.StartsWith(EditorSkinPreferences.BuiltInPrefix, StringComparison.OrdinalIgnoreCase) &&
            changes.LastOrDefault(change => PathsEqual(change.AssetPath, selectedSkin) ||
                                            PathsEqual(change.PreviousPath, selectedSkin)) is { } skinChange)
        {
            if (skinChange.Kind == AssetChangeKind.Moved && PathsEqual(skinChange.PreviousPath, selectedSkin))
            {
                EditorPreferences.current.EditorSkin = skinChange.AssetPath;
                EditorPreferences.Save();
            }
            else if (skinChange.Kind == AssetChangeKind.Deleted)
            {
                EditorSkinPreferences.Select(EditorPreferences.current,
                    EditorAppearance.builtInSkins.First(skin =>
                        skin.name.Equals(nameof(EditorTheme.Dark), StringComparison.OrdinalIgnoreCase)));
                EditorPreferences.Save();
            }
            else EditorAppearance.Apply(EditorPreferences.current);
        }
        ReloadChangedAssetSelection(changes);
        _scriptSourceCache.Clear();
        _project?.Invalidate();
    }

    private void ReloadChangedAssetSelection(IReadOnlyList<AssetChange> changes)
    {
        if (_selected is not null || string.IsNullOrWhiteSpace(_selectedAssetPath)) return;
        var change = changes.LastOrDefault(candidate =>
            PathsEqual(candidate.AssetPath, _selectedAssetPath) ||
            PathsEqual(candidate.PreviousPath, _selectedAssetPath));
        if (change is null) return;

        var previous = _selectedAsset;
        if (change.Kind == AssetChangeKind.Deleted)
        {
            _selectedAsset = null;
            _selectedAssetPath = null;
        }
        else
        {
            _selectedAssetPath = change.AssetPath;
            _selectedAsset = AssetDatabase.LoadMainAssetAtPath(change.AssetPath);
        }

        if (previous is not null && ReferenceEquals(_inspector.LockedTarget, previous))
            _inspector.RemapLockedTarget(target => ReferenceEquals(target, previous) ? _selectedAsset : target);
        else
            _inspector.RebuildEditor();
        Selection.NotifyHostSelectionChanged(_selectedAsset);
    }
    private void OnUndoRedo() { MarkDirty(_selected?.scene ?? _scene); _inspector.RebuildEditor(); }
    private void OnPackagesReloading()
    {
        CloseTransientMenus();
        if (_playing || _playModeSession is not null) ExitPlayMode();
        _consoleClearedForPackageReload = ConsoleLogController.ClearIfEnabled(ConsoleClearTrigger.Recompile);
        _scriptSourceCache.Clear();
        ComponentClipboard.Clear();
        Undo.ClearAll();
        ProjectScriptCompiler.ReleaseLoadContexts();
    }
    private void OnPackagesUnloading(IReadOnlyList<BPackageDefinition> _)
    {
        if (!_playing && _playModeSession is null) return;
        ExitPlayMode();
    }
    private void OnPackagesChanged()
    {
        QueueScriptCompilation();
        _project.Invalidate();
    }

    private void OnRuntimeSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_playing) return;
        scene.MarkRuntimeOnly();
        _playModeSession?.RuntimeScenes.Add(scene);
        if (_exitingPlayMode) return;
        try
        {
            scene.path = Path.GetFullPath(scene.path);
            var entry = FindSceneEntry(scene);
            if (entry is null)
            {
                entry = new EditorOpenScene(scene, scene.path, ToAssetPath(scene.path));
                _openScenes.Add(entry);
            }
            entry.IsLoaded = true;
            if (_runtimes.All(item => !ReferenceEquals(item.Scene, scene)))
                StartRuntime(scene);
            SetActiveEditorScene(scene);
            SelectInitialSceneObject(scene);
            RefreshLoadedSceneSnapshot();
            EditorApplication.RaiseHierarchyChanged();
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report($"Runtime Scene loaded ({mode})", exception);
        }
    }

    private void OnRuntimeSceneUnloaded(Scene scene)
    {
        if (!_playing) return;
        if (_exitingPlayMode) return;
        try
        {
            _runtimes.RemoveAll(item => ReferenceEquals(item.Scene, scene));
            if (FindSceneEntry(scene) is { } entry) _openScenes.Remove(entry);
            if (_selected is not null &&
                (_selected.scene is null || ReferenceEquals(_selected.scene, scene)))
            {
                _selected = null;
                Selection.NotifyHostSelectionChanged(null);
                _inspector.RebuildEditor();
            }
            RefreshLoadedSceneSnapshot();
            EditorApplication.RaiseHierarchyChanged();
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Runtime Scene unloaded", exception);
        }
    }

    private void OnRuntimeActiveSceneChanged(Scene? _, Scene? current)
    {
        if (!_playing || _registeringPlayModeScenes || _exitingPlayMode ||
            current is null || !current.isCreated) return;
        try
        {
            if (FindSceneEntry(current) is not { IsLoaded: true }) return;
            SetActiveEditorScene(current);
            RefreshLoadedSceneSnapshot();
            EditorApplication.RaiseHierarchyChanged();
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Runtime active Scene changed", exception);
        }
    }

    internal Scene Scene => _scene;
    internal IReadOnlyList<EditorOpenScene> OpenSceneEntries => _openScenes;
    internal GameObject? Selected => _selected;
    internal BObject? SelectedAsset => _selectedAsset;
    internal string? SelectedAssetPath => _selectedAssetPath;
    internal IReadOnlyList<AssetRecord> Assets => _assets.assets.OrderBy(item => item.AssetPath).ToArray();
    internal IReadOnlyList<LogEntry> Logs => EditorLogStore.Snapshot();
    internal BPackageManager Packages => _packages;
    internal void ClearLogs() => EditorLogStore.Clear();

    internal void Select(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        _selected = gameObject; _selectedAsset = null; _selectedAssetPath = null;
        _hierarchy?.RevealSelection(gameObject);
        Selection.NotifyHostSelectionChanged(gameObject); _inspector.RebuildEditor();
    }

    internal void ClearSelection()
    {
        if (_selected is null && _selectedAsset is null && _selectedAssetPath is null) return;
        _selected = null;
        _selectedAsset = null;
        _selectedAssetPath = null;
        Selection.NotifyHostSelectionChanged(null);
        _inspector.RebuildEditor();
    }

    internal void Select(AssetRecord record)
    {
        _selected = null; _selectedAssetPath = record.AssetPath;
        _selectedAsset = AssetDatabase.LoadMainAssetAtPath(record.AssetPath);
        Selection.NotifyHostSelectionChanged(((IEditorHost)this).ActiveObject); _inspector.RebuildEditor();
    }

    internal void Select(ProjectBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Asset is { } record)
        {
            Select(record);
            return;
        }

        _selected = null;
        _selectedAssetPath = item.NormalizedPath;
        _selectedAsset = ProjectBrowserSelection.CreateReadOnlyAsset(item);
        Selection.NotifyHostSelectionChanged(_selectedAsset);
        _inspector.RebuildEditor();
    }

    internal void OpenAsset(AssetRecord record)
    {
        if (record.IsDirectory) { _project.Toggle(record.AssetPath); return; }
        if (record.AssetPath.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
        {
            OpenEditorScene(record.SourcePath, OpenSceneMode.Single);
        }
        else if (record.AssetPath.EndsWith(".prefab.yaml", StringComparison.OrdinalIgnoreCase))
            OpenPrefabStage(record.AssetPath);
        else OpenExternal(record.SourcePath);
    }

    internal void SelectScript(MonoBehaviour component, bool open)
    {
        var sourcePath = FindScriptSource(component);
        if (sourcePath is null)
        {
            Debug.LogWarning($"Could not locate the source file for {component.GetType().FullName}.");
            return;
        }
        var record = Assets.FirstOrDefault(item =>
            Path.GetFullPath(item.SourcePath).Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase));
        if (record is not null)
        {
            _project.Ping(record.AssetPath);
            _dock.Show(_project.PersistentId);
            if (open) AssetDatabase.OpenAsset(sourcePath);
        }
        else if (open) AssetDatabase.OpenAsset(sourcePath);
    }

    internal string? FindScriptSource(MonoBehaviour component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var type = component.GetType();
        if (_scriptSourceCache.TryGetValue(type, out var cached)) return cached;
        var identity = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        var sourcePath = ProjectScriptSourceLocator.Find(_workspace, identity);
        _scriptSourceCache[type] = sourcePath;
        return sourcePath;
    }

    internal void MarkDirty() => MarkDirty(_scene);
    internal bool MarkDirty(Scene scene)
    {
        if (_prefabStage is not null && ReferenceEquals(scene, _scene))
        {
            _dirty = true;
            _mainWindow.SetTitle(BuildTitle());
            return true;
        }
        var entry = FindSceneEntry(scene);
        if (entry is null || !entry.IsLoaded) return false;
        entry.IsDirty = true;
        if (ReferenceEquals(scene, _scene)) _dirty = true;
        _mainWindow.SetTitle(BuildTitle());
        return true;
    }
    internal void RemoveComponent(Component component)
    {
        if (component is Transform) return;
        if (!component.gameObject.CanRemoveComponent(component))
        {
            Debug.LogWarning($"{component.GetType().Name} is required by another component and cannot be removed.");
            return;
        }
        Undo.DestroyObjectImmediate(component);
        MarkDirty(component.gameObject.scene ?? _scene);
        _inspector.RebuildEditor();
    }

    private GameObject CreateGameObject(Transform? parent = null)
    {
        var owner = parent?.gameObject.scene ?? _scene;
        var gameObject = owner.CreateGameObject("GameObject");
        if (parent is not null) gameObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(gameObject, "Create GameObject");
        Select(gameObject); MarkDirty(owner); EditorApplication.RaiseHierarchyChanged(); return gameObject;
    }

    private GameObject CreateConfiguredGameObject(string name, Action<GameObject> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var gameObject = _scene.CreateGameObject(name);
        configure(gameObject);
        Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
        Select(gameObject);
        MarkDirty(_scene);
        EditorApplication.RaiseHierarchyChanged();
        _inspector.RebuildEditor();
        return gameObject;
    }

    private void CreateEmptyParent(GameObject child)
    {
        var scene = child.scene ?? throw new InvalidOperationException("GameObject does not belong to a Scene.");
        var oldParent = child.transform.parent;
        var oldSiblingIndex = child.transform.GetSiblingIndex();
        var parent = scene.CreateGameObject("Parent");
        if (oldParent is not null) parent.transform.SetParent(oldParent, false);
        parent.transform.localPosition = child.transform.localPosition;
        parent.transform.localRotation = Fix64.Zero;
        parent.transform.localScale = Vector2.one;
        parent.transform.SetSiblingIndex(oldSiblingIndex);
        var parentPosition = parent.transform.localPosition;
        var parentRotation = parent.transform.localRotation;
        var parentScale = parent.transform.localScale;
        child.transform.SetParent(parent.transform, true);

        Undo.RegisterOperation("Create Empty Parent",
            () =>
            {
                child.transform.SetParent(oldParent, true);
                child.transform.SetSiblingIndex(oldSiblingIndex);
                if (parent.scene is { } owner) owner.Destroy(parent);
                Select(child);
                MarkDirty(scene);
                EditorApplication.RaiseHierarchyChanged();
            },
            () =>
            {
                if (parent.scene is null) scene.Add(parent);
                if (oldParent is not null) parent.transform.SetParent(oldParent, false);
                parent.transform.localPosition = parentPosition;
                parent.transform.localRotation = parentRotation;
                parent.transform.localScale = parentScale;
                parent.transform.SetSiblingIndex(oldSiblingIndex);
                child.transform.SetParent(parent.transform, true);
                Select(parent);
                MarkDirty(scene);
                EditorApplication.RaiseHierarchyChanged();
            });
        Select(parent);
        MarkDirty(scene);
        EditorApplication.RaiseHierarchyChanged();
    }

    private bool CanExecuteGameObjectCommand(GameObjectCommand command, GameObject? target)
    {
        var activeSceneReady = _scene.isLoaded && _scene.isCreated;
        var targetExists = target?.scene is { isLoaded: true } && target.scene.isCreated;
        var editable = targetExists && (target!.hideFlags & HideFlags.NotEditable) == 0;
        var siblingIndex = target?.transform.GetSiblingIndex() ?? -1;
        var siblingCount = target is null ? 0 : SiblingCount(target);
        return command switch
        {
            GameObjectCommand.CreateEmpty or GameObjectCommand.CreateSprite or
                GameObjectCommand.CreateParticleSystem or GameObjectCommand.CreateCamera2D => activeSceneReady,
            GameObjectCommand.CreateEmptyChild or GameObjectCommand.CreateEmptyParent => editable,
            GameObjectCommand.SetActive => editable && !target!.activeSelf,
            GameObjectCommand.SetInactive => editable && target!.activeSelf,
            GameObjectCommand.ResetTransform or GameObjectCommand.ResetPosition or
                GameObjectCommand.ResetRotation or GameObjectCommand.ResetScale => editable,
            GameObjectCommand.SelectParent => targetExists && target!.transform.parent is not null,
            GameObjectCommand.SelectChildren or GameObjectCommand.CenterOnChildren =>
                targetExists && target!.transform.childCount > 0,
            GameObjectCommand.MoveToRoot => editable && target!.transform.parent is not null,
            GameObjectCommand.MoveUp or GameObjectCommand.SetAsFirstSibling => editable && siblingIndex > 0,
            GameObjectCommand.MoveDown or GameObjectCommand.SetAsLastSibling =>
                editable && siblingIndex >= 0 && siblingIndex < siblingCount - 1,
            GameObjectCommand.FrameSelected or GameObjectCommand.CopyHierarchyPath => targetExists,
            GameObjectCommand.Rename or GameObjectCommand.Duplicate or GameObjectCommand.Delete => editable,
            _ => false
        };
    }

    private bool CanExecuteComponentCommand(ComponentCommand command)
    {
        var target = _selected;
        if (target?.scene is not { isLoaded: true } scene || !scene.isCreated ||
            (target.hideFlags & HideFlags.NotEditable) != 0 ||
            target.components.Any(component => (component.hideFlags & HideFlags.NotEditable) != 0))
            return false;

        return command switch
        {
            ComponentCommand.RemoveMissingComponents =>
                target.components.Any(component => component is MissingComponent &&
                                                   target.CanRemoveComponent(component)),
            _ => false
        };
    }

    private bool ExecuteComponentCommand(ComponentCommand command)
    {
        if (!CanExecuteComponentCommand(command) || _selected is not { } target) return false;

        switch (command)
        {
            case ComponentCommand.RemoveMissingComponents:
                RemoveMissingComponents(target);
                break;
            default:
                return false;
        }

        if (target.scene is { } scene) MarkDirty(scene);
        _inspector.RebuildEditor();
        return true;
    }

    private static void RemoveMissingComponents(GameObject target)
    {
        var states = target.components
            .Where(component => component is MissingComponent && target.CanRemoveComponent(component))
            .Select(component => StructuralObjectState.Capture(component))
            .ToArray();
        for (var index = states.Length - 1; index >= 0; index--) states[index].Remove();
        Undo.RegisterOperation("Remove Missing Components",
            () =>
            {
                foreach (var state in states) state.Restore();
            },
            () =>
            {
                for (var index = states.Length - 1; index >= 0; index--) states[index].Remove();
            });
        EditorUtility.SetDirty(target);
        EditorApplication.RaiseHierarchyChanged();
    }

    private static bool CanExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera)
    {
        if (command is not (CameraViewCommand.AlignWithView or CameraViewCommand.MoveToView)) return false;
        var gameObject = camera.gameObject;
        return gameObject.scene is { isLoaded: true } scene && scene.isCreated &&
               gameObject.components.Any(component => ReferenceEquals(component, camera)) &&
               (gameObject.hideFlags & HideFlags.NotEditable) == 0 &&
               (camera.hideFlags & HideFlags.NotEditable) == 0 &&
               (gameObject.transform.hideFlags & HideFlags.NotEditable) == 0;
    }

    private bool ExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera)
    {
        if (!CanExecuteCameraViewCommand(command, camera)) return false;
        var transform = camera.transform;
        Undo.RecordObject(transform, command == CameraViewCommand.AlignWithView
            ? "Align Camera With View"
            : "Move Camera To View");

        if (command == CameraViewCommand.AlignWithView)
        {
            transform.position = ToEngineVector(_editorCameraPosition);
            transform.rotation = Fix64.Zero;
        }
        else
        {
            transform.position = ToEngineVector(_editorCameraPivot);
        }

        EditorUtility.SetDirty(transform);
        if (camera.gameObject.scene is { } scene) MarkDirty(scene);
        _inspector.RebuildEditor();
        _sceneView.Repaint();
        return true;
    }

    private static Vector2 ToEngineVector(NVector2 value) =>
        new((Fix64)value.X, (Fix64)value.Y);

    private bool CanExecuteMainMenuCommand(MainMenuCommand command)
    {
        var focused = EditorWindow.focusedWindow;
        return command switch
        {
            MainMenuCommand.NewScene or MainMenuCommand.OpenScene or MainMenuCommand.OpenSceneAdditive =>
                !_closing && !_playing && !_enteringPlayMode && !_exitingPlayMode,
            MainMenuCommand.SaveScene => !_playing && !_enteringPlayMode && !_exitingPlayMode &&
                (_prefabStage is not null || FindSceneEntry(_scene) is { IsLoaded: true }),
            MainMenuCommand.SaveAllScenes => !_playing && !_enteringPlayMode && !_exitingPlayMode &&
                (_prefabStage is not null || _openScenes.Any(item => item.IsLoaded)),
            MainMenuCommand.ShowProjectInExplorer => Directory.Exists(_workspace.RootPath),
            MainMenuCommand.Exit => !_closing,
            MainMenuCommand.Undo => Undo.canUndo,
            MainMenuCommand.Redo => Undo.canRedo,
            MainMenuCommand.Copy or MainMenuCommand.Duplicate => CanCopySelection(),
            MainMenuCommand.Paste => CanPasteSelection(),
            MainMenuCommand.Rename => focused is ImGuiProjectWindow
                ? _project.CanExecuteCommand(ProjectAssetCommand.Rename)
                : CanExecuteGameObjectCommand(GameObjectCommand.Rename, _selected),
            MainMenuCommand.Delete => focused is ImGuiProjectWindow
                ? _project.CanExecuteCommand(ProjectAssetCommand.Delete)
                : CanExecuteGameObjectCommand(GameObjectCommand.Delete, _selected),
            MainMenuCommand.SelectAll => _scene.isLoaded && _scene.gameObjects.Count > 0,
            MainMenuCommand.DeselectAll => Selection.count > 0 || _selected is not null || _selectedAsset is not null,
            MainMenuCommand.FrameSelected => _selected is not null,
            MainMenuCommand.Play => !_closing && !EditorApplication.isCompiling,
            MainMenuCommand.Pause or MainMenuCommand.Step => _playing,
            MainMenuCommand.RecompileScripts => !_playing && !EditorApplication.isCompiling &&
                                                 !EditorApplication.isAssemblyReloadLocked,
            MainMenuCommand.CloseFocusedWindow or MainMenuCommand.ToggleLockFocusedWindow => focused is not null,
            MainMenuCommand.ToggleMaximizeFocusedWindow => focused is not null &&
                                                            _editorPanels.ContainsKey(focused),
            MainMenuCommand.NextWindow or MainMenuCommand.PreviousWindow => OpenEditorWindows().Length > 1,
            MainMenuCommand.Documentation => AboutBEngineWindow.canOpenDocumentation,
            MainMenuCommand.ViewEditorLog => File.Exists(_instanceLogPath),
            MainMenuCommand.RevealLogsFolder => Directory.Exists(Path.GetDirectoryName(_instanceLogPath)),
            MainMenuCommand.CopySystemInfo or MainMenuCommand.About => true,
            _ => false
        };
    }

    private bool ExecuteMainMenuCommand(MainMenuCommand command)
    {
        if (!CanExecuteMainMenuCommand(command)) return false;
        switch (command)
        {
            case MainMenuCommand.NewScene:
                CreateNewScene();
                break;
            case MainMenuCommand.OpenScene:
                ShowOpenSceneDialog(OpenSceneMode.Single);
                break;
            case MainMenuCommand.OpenSceneAdditive:
                ShowOpenSceneDialog(OpenSceneMode.Additive);
                break;
            case MainMenuCommand.SaveScene:
                SaveScene();
                break;
            case MainMenuCommand.SaveAllScenes:
                SaveAllOpenScenes();
                break;
            case MainMenuCommand.ShowProjectInExplorer:
                ShowInExplorer(_workspace.RootPath);
                break;
            case MainMenuCommand.Exit:
                RequestExit();
                break;
            case MainMenuCommand.Undo:
                Undo.PerformUndo();
                break;
            case MainMenuCommand.Redo:
                Undo.PerformRedo();
                break;
            case MainMenuCommand.Copy:
                CopySelection();
                break;
            case MainMenuCommand.Paste:
                PasteSelection();
                break;
            case MainMenuCommand.Duplicate:
                DuplicateSelection();
                break;
            case MainMenuCommand.Rename:
                RenameSelection();
                break;
            case MainMenuCommand.Delete:
                DeleteCurrentSelection();
                break;
            case MainMenuCommand.SelectAll:
                Selection.objects = _scene.gameObjects.Cast<BObject>().ToArray();
                break;
            case MainMenuCommand.DeselectAll:
                Selection.objects = [];
                _selected = null;
                _selectedAsset = null;
                _selectedAssetPath = null;
                _inspector.RebuildEditor();
                break;
            case MainMenuCommand.FrameSelected:
                FrameSelectedInScene();
                break;
            case MainMenuCommand.Play:
                TogglePlay();
                break;
            case MainMenuCommand.Pause:
                EditorApplication.isPaused = !_paused;
                break;
            case MainMenuCommand.Step:
                EditorApplication.isPaused = true;
                Step();
                break;
            case MainMenuCommand.RecompileScripts:
                QueueScriptCompilation();
                break;
            case MainMenuCommand.CloseFocusedWindow:
                CloseEditorWindow(EditorWindow.focusedWindow!);
                break;
            case MainMenuCommand.ToggleLockFocusedWindow:
                EditorWindow.focusedWindow!.isLocked = !EditorWindow.focusedWindow.isLocked;
                break;
            case MainMenuCommand.ToggleMaximizeFocusedWindow:
                _dock.ToggleMaximize(EditorWindow.focusedWindow!);
                break;
            case MainMenuCommand.NextWindow:
                CycleFocusedWindow(1);
                break;
            case MainMenuCommand.PreviousWindow:
                CycleFocusedWindow(-1);
                break;
            case MainMenuCommand.Documentation:
                AboutBEngineWindow.OpenDocumentation();
                break;
            case MainMenuCommand.ViewEditorLog:
                OpenExternal(_instanceLogPath);
                break;
            case MainMenuCommand.RevealLogsFolder:
                ShowInExplorer(Path.GetDirectoryName(_instanceLogPath));
                break;
            case MainMenuCommand.CopySystemInfo:
                AboutBEngineWindow.CopySystemInfo();
                break;
            case MainMenuCommand.About:
                AboutBEngineWindow.Open();
                break;
            default:
                return false;
        }
        return true;
    }

    private bool IsMainMenuCommandChecked(MainMenuCommand command) => command switch
    {
        MainMenuCommand.Play => _playing,
        MainMenuCommand.Pause => _paused,
        MainMenuCommand.ToggleLockFocusedWindow => EditorWindow.focusedWindow?.isLocked == true,
        MainMenuCommand.ToggleMaximizeFocusedWindow =>
            EditorWindow.focusedWindow is { } focused && _dock.IsMaximized(focused),
        _ => false
    };

    private void CreateNewScene()
    {
        var assetPath = ProjectAssetCreation.CreateScene("Assets/Scenes");
        _project.SelectCreatedAsset(assetPath, beginRename: false);
        OpenEditorScene(ResolveAssetPath(assetPath), OpenSceneMode.Single);
    }

    private void ShowOpenSceneDialog(OpenSceneMode mode)
    {
        EditorFileDialog.Open(mode == OpenSceneMode.Single ? "Open Scene" : "Open Scene Additive",
            _workspace.ScenesPath, "BEngine Scene|*.scene.yaml", path =>
            {
                if (!IsProjectScenePath(path))
                {
                    Debug.LogError($"Scene must be inside the project's Assets folder: {path}");
                    return;
                }
                OpenEditorScene(path, mode);
            });
    }

    private bool IsProjectScenePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var assetsRoot = Path.GetFullPath(_workspace.AssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase) &&
               fullPath.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private void RenameSelection()
    {
        if (EditorWindow.focusedWindow is ImGuiProjectWindow)
            _project.ExecuteCommand(ProjectAssetCommand.Rename);
        else if (_selected is not null)
            _hierarchy.BeginRename(_selected);
    }

    private void DeleteCurrentSelection()
    {
        if (EditorWindow.focusedWindow is ImGuiProjectWindow)
            _project.ExecuteCommand(ProjectAssetCommand.Delete);
        else
            DeleteSelected();
    }

    private EditorWindow[] OpenEditorWindows() => _editorPanels.Keys
        .Where(window => _editorPanels[window].Visible)
        .Concat(_windowLayer.Presentations.Select(item => item.Window))
        .Distinct()
        .ToArray();

    private void CycleFocusedWindow(int direction)
    {
        var windows = OpenEditorWindows();
        if (windows.Length == 0) return;
        var current = EditorWindow.focusedWindow is { } focused ? Array.IndexOf(windows, focused) : -1;
        var next = current < 0 ? 0 : (current + direction + windows.Length) % windows.Length;
        var window = windows[next];
        if (_editorPanels.TryGetValue(window, out var panel)) _dock.Show(panel.Id);
        else _windowLayer.Focus(window);
    }

    private void RequestExit()
    {
        if (EditorApplication.RaiseWantsToQuit()) _mainWindow.Close();
    }

    private bool ExecuteGameObjectCommand(GameObjectCommand command, GameObject? target)
    {
        if (!CanExecuteGameObjectCommand(command, target)) return false;
        switch (command)
        {
            case GameObjectCommand.CreateEmpty:
                CreateGameObject();
                break;
            case GameObjectCommand.CreateEmptyChild:
                CreateGameObject(target!.transform);
                break;
            case GameObjectCommand.CreateEmptyParent:
                CreateEmptyParent(target!);
                break;
            case GameObjectCommand.CreateSprite:
                CreateConfiguredGameObject("Sprite", gameObject => gameObject.AddComponent<SpriteRenderer>());
                break;
            case GameObjectCommand.CreateParticleSystem:
                CreateConfiguredGameObject("Particle System 2D",
                    gameObject => gameObject.AddComponent<ParticleSystem2D>());
                break;
            case GameObjectCommand.CreateCamera2D:
                CreateConfiguredGameObject("Camera 2D", gameObject => gameObject.AddComponent<Camera2D>());
                break;
            case GameObjectCommand.SetActive:
                SetGameObjectActive(target!, true);
                break;
            case GameObjectCommand.SetInactive:
                SetGameObjectActive(target!, false);
                break;
            case GameObjectCommand.ResetTransform:
                ResetTransform(target!, true, true, true);
                break;
            case GameObjectCommand.ResetPosition:
                ResetTransform(target!, true, false, false);
                break;
            case GameObjectCommand.ResetRotation:
                ResetTransform(target!, false, true, false);
                break;
            case GameObjectCommand.ResetScale:
                ResetTransform(target!, false, false, true);
                break;
            case GameObjectCommand.SelectParent:
                Select(target!.transform.parent!.gameObject);
                break;
            case GameObjectCommand.SelectChildren:
                Selection.objects = target!.transform.children.Select(item => (BObject)item.gameObject).ToArray();
                break;
            case GameObjectCommand.MoveToRoot:
                Undo.SetTransformParent(target!.transform, null, "Move GameObject To Root");
                FinishGameObjectEdit(target);
                break;
            case GameObjectCommand.MoveUp:
                SetSiblingIndex(target!, target!.transform.GetSiblingIndex() - 1, "Move GameObject Up");
                break;
            case GameObjectCommand.MoveDown:
                SetSiblingIndex(target!, target!.transform.GetSiblingIndex() + 1, "Move GameObject Down");
                break;
            case GameObjectCommand.SetAsFirstSibling:
                SetSiblingIndex(target!, 0, "Set As First Sibling");
                break;
            case GameObjectCommand.SetAsLastSibling:
                SetSiblingIndex(target!, SiblingCount(target!) - 1, "Set As Last Sibling");
                break;
            case GameObjectCommand.CenterOnChildren:
                CenterOnChildren(target!);
                break;
            case GameObjectCommand.FrameSelected:
                Select(target!);
                FrameSelectedInScene();
                break;
            case GameObjectCommand.CopyHierarchyPath:
                GUIUtility.systemCopyBuffer = HierarchyPath(target!);
                break;
            case GameObjectCommand.Rename:
                Select(target!);
                _hierarchy.BeginRename(target!);
                break;
            case GameObjectCommand.Duplicate:
                Select(target!);
                DuplicateSelected();
                break;
            case GameObjectCommand.Delete:
                Select(target!);
                DeleteSelected();
                break;
            default:
                return false;
        }
        return true;
    }

    private void SetGameObjectActive(GameObject gameObject, bool active)
    {
        Undo.RecordObject(gameObject, active ? "Set GameObject Active" : "Set GameObject Inactive");
        gameObject.SetActive(active);
        FinishGameObjectEdit(gameObject);
    }

    private void ResetTransform(GameObject gameObject, bool position, bool rotation, bool scale)
    {
        Undo.RecordObject(gameObject.transform, "Reset Transform");
        if (position) gameObject.transform.localPosition = Vector2.zero;
        if (rotation) gameObject.transform.localRotation = Fix64.Zero;
        if (scale) gameObject.transform.localScale = Vector2.one;
        FinishGameObjectEdit(gameObject);
    }

    private void SetSiblingIndex(GameObject gameObject, int index, string operationName)
    {
        var transform = gameObject.transform;
        var oldIndex = transform.GetSiblingIndex();
        transform.SetSiblingIndex(index);
        var newIndex = transform.GetSiblingIndex();
        Undo.RegisterOperation(operationName,
            () => RestoreSiblingIndex(gameObject, oldIndex),
            () => RestoreSiblingIndex(gameObject, newIndex));
        FinishGameObjectEdit(gameObject);
    }

    private void RestoreSiblingIndex(GameObject gameObject, int index)
    {
        gameObject.transform.SetSiblingIndex(index);
        FinishGameObjectEdit(gameObject);
    }

    private void CenterOnChildren(GameObject gameObject)
    {
        var children = gameObject.transform.children.ToArray();
        var worldPositions = children.Select(child => child.position).ToArray();
        Undo.RecordObjects(children.Cast<BObject>().Prepend(gameObject.transform).ToArray(), "Center On Children");
        var center = worldPositions.Aggregate(Vector2.zero, static (sum, item) => sum + item) /
                     (Fix64)worldPositions.Length;
        gameObject.transform.position = center;
        for (var index = 0; index < children.Length; index++) children[index].position = worldPositions[index];
        FinishGameObjectEdit(gameObject);
    }

    private void FinishGameObjectEdit(GameObject gameObject)
    {
        EditorUtility.SetDirty(gameObject);
        EditorUtility.SetDirty(gameObject.transform);
        if (gameObject.scene is { } scene) MarkDirty(scene);
        EditorApplication.RaiseHierarchyChanged();
        _inspector.RebuildEditor();
    }

    private static int SiblingCount(GameObject gameObject) =>
        gameObject.transform.parent?.childCount ?? gameObject.scene?.rootCount ?? 0;

    private static string HierarchyPath(GameObject gameObject)
    {
        var segments = new Stack<string>();
        for (var current = gameObject.transform; current is not null; current = current.parent)
            segments.Push(current.gameObject.name);
        if (!string.IsNullOrWhiteSpace(gameObject.scene?.name)) segments.Push(gameObject.scene.name);
        return string.Join('/', segments);
    }

    private void DuplicateSelected()
    {
        if (_selected is null) return;
        var sourceParent = _selected.transform.parent;
        var sourceSiblingIndex = _selected.transform.GetSiblingIndex();
        var owner = _selected.scene ?? _scene;
        var copy = (GameObject)BObject.Instantiate(_selected);
        if (copy.scene is null) owner.Add(copy);
        else if (!ReferenceEquals(copy.scene, owner)) Scene.MoveGameObjectToScene(copy, owner);
        if (sourceParent is not null) copy.transform.SetParent(sourceParent, false);
        copy.transform.SetSiblingIndex(sourceSiblingIndex + 1);
        copy.name = _selected.name + " (1)";
        Undo.RegisterCreatedObjectUndo(copy, "Duplicate GameObject");
        Select(copy); MarkDirty(owner); EditorApplication.RaiseHierarchyChanged();
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        var owner = _selected.scene ?? _scene;
        Undo.DestroyObjectImmediate(_selected); _selected = owner.gameObjects.FirstOrDefault(); MarkDirty(owner);
        Selection.NotifyHostSelectionChanged(_selected);
        _inspector.RebuildEditor();
    }

    private bool CanCopySelection() => _selected is not null ||
        EditorWindow.focusedWindow is ImGuiProjectWindow && _project.CanCopySelected();

    private bool CanPasteSelection() => _copiedGameObject is not null ||
        _copiedAssetPath is not null && EditorWindow.focusedWindow is ImGuiProjectWindow;

    private void CopySelection()
    {
        if (EditorWindow.focusedWindow is ImGuiProjectWindow && _project.CopySelectedPath() is { } assetPath)
        {
            _copiedAssetPath = assetPath;
            _copiedGameObject = null;
            GUIUtility.systemCopyBuffer = assetPath;
            return;
        }
        if (_selected is null) return;
        _copiedGameObject = _selected;
        _copiedAssetPath = null;
        GUIUtility.systemCopyBuffer = _selected.name;
    }

    private void PasteSelection()
    {
        if (EditorWindow.focusedWindow is ImGuiProjectWindow && _copiedAssetPath is { } assetPath)
        {
            _project.PasteAsset(assetPath);
            return;
        }
        if (_copiedGameObject is null) return;
        var owner = _selected?.scene ?? _scene;
        var copy = (GameObject)BObject.Instantiate(_copiedGameObject);
        if (copy.scene is null) owner.Add(copy);
        else if (!ReferenceEquals(copy.scene, owner)) Scene.MoveGameObjectToScene(copy, owner);
        copy.name = _copiedGameObject.name + " (Copy)";
        if (_selected?.transform.parent is { } parent) copy.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(copy, "Paste GameObject");
        Select(copy);
        MarkDirty(owner);
        EditorApplication.RaiseHierarchyChanged();
    }

    private void DuplicateSelection()
    {
        if (EditorWindow.focusedWindow is ImGuiProjectWindow && _project.CopySelectedPath() is { } assetPath)
        {
            _project.PasteAsset(assetPath);
            return;
        }
        DuplicateSelected();
    }

    private void HandleGlobalKeyboard()
    {
        var current = Event.current;
        if (current.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;
        var actionModifier = current.control || current.command;
        if (!actionModifier && !current.alt && !current.shift)
        {
            var tool = current.keyCode switch
            {
                KeyCode.W => Tool.Move,
                KeyCode.E => Tool.Rotate,
                KeyCode.R => Tool.Scale,
                _ => (Tool?)null
            };
            if (tool is { } selectedTool)
            {
                _tool = selectedTool;
                current.Use();
                return;
            }
        }
        MainMenuCommand? command = null;
        if (actionModifier)
        {
            command = current.keyCode switch
            {
                KeyCode.N => MainMenuCommand.NewScene,
                KeyCode.O => MainMenuCommand.OpenScene,
                KeyCode.S => MainMenuCommand.SaveScene,
                KeyCode.Z when current.shift => MainMenuCommand.Redo,
                KeyCode.Z => MainMenuCommand.Undo,
                KeyCode.Y => MainMenuCommand.Redo,
                KeyCode.C => MainMenuCommand.Copy,
                KeyCode.V => MainMenuCommand.Paste,
                KeyCode.D => MainMenuCommand.Duplicate,
                KeyCode.A => MainMenuCommand.SelectAll,
                KeyCode.P when current.alt => MainMenuCommand.Step,
                KeyCode.P when current.shift => MainMenuCommand.Pause,
                KeyCode.P => MainMenuCommand.Play,
                _ => null
            };
        }
        else
        {
            command = current.keyCode switch
            {
                KeyCode.Delete => MainMenuCommand.Delete,
                KeyCode.F2 => MainMenuCommand.Rename,
                KeyCode.F => MainMenuCommand.FrameSelected,
                _ => null
            };
        }
        if (command is not { } selectedCommand || !CanExecuteMainMenuCommand(selectedCommand)) return;
        EditorFeatureGuard.Invoke($"Editor keyboard command {selectedCommand}",
            () => ExecuteMainMenuCommand(selectedCommand));
        current.Use();
    }

    private void SaveScene()
    {
        if (_prefabStage is not null) { SavePrefabStage(); return; }
        SaveEditorScene(_scene);
    }

    private bool SaveEditorScene(Scene scene)
    {
        if (_playing || _enteringPlayMode || _exitingPlayMode) return false;
        var entry = FindSceneEntry(scene);
        if (entry is null || !entry.IsLoaded) return false;
        if (!entry.IsDirty) return true;
        Document.SaveBObject<SceneDocument>(scene, entry.SourcePath);
        entry.IsDirty = false;
        if (ReferenceEquals(scene, _scene)) _dirty = false;
        _mainWindow.SetTitle(BuildTitle());
        return true;
    }

    private void SaveAllOpenScenes()
    {
        if (_prefabStage is not null)
        {
            SavePrefabStage();
            return;
        }
        foreach (var entry in _openScenes.Where(item => item.IsLoaded).ToArray())
            SaveEditorScene(entry.Scene);
    }

    private void TogglePlay()
    {
        if (_enteringPlayMode || _exitingPlayMode) return;
        if (_playing || _playModeSession is not null) ExitPlayMode();
        else EnterPlayMode();
    }

    private void EnterPlayMode()
    {
        if (_enteringPlayMode || _exitingPlayMode || _playing || _playModeSession is not null) return;
        if (_prefabStage is not null)
        {
            Debug.LogWarning("Play Mode is unavailable while editing Prefab contents. Close the Prefab Stage first.");
            return;
        }
        _enteringPlayMode = true;
        try { EnterPlayModeCore(); }
        finally
        {
            _enteringPlayMode = false;
            if (_closePendingAfterPlayModeTransition) CompleteClose();
        }
    }

    private void EnterPlayModeCore()
    {
        EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
        ConsoleLogController.ClearIfEnabled(ConsoleClearTrigger.Play);

        var undoHistory = Undo.CaptureAndClear();
        var componentClipboard = ComponentClipboard.CaptureAndClear();
        var dirtyState = EditorUtility.CaptureDirtyState();
        var runtimeState = EditorRuntimeStateSnapshot.Capture();
        var copiedGameObject = _copiedGameObject;
        _copiedGameObject = null;
        EditorPlayModeSession? session = null;
        try
        {
            session = EditorPlayModeSession.Create(
                _services,
                _openScenes,
                _scene,
                _scenePath,
                _mainScene,
                _mainSelection,
                _prefabStage,
                _selected,
                _selectedAsset,
                _selectedAssetPath,
                _inspector.LockedTarget,
                _dirty,
                _mainSceneDirty,
                Selection.objects,
                Selection.activeContext,
                undoHistory,
                componentClipboard,
                dirtyState,
                copiedGameObject,
                runtimeState);
            _playModeSession = session;
            ApplyPlayModeSession(session);
            _playing = true;
            _paused = false;
            Application.isPlaying = true;

            var playScenes = LoadedScenes().ToArray();
            _registeringPlayModeScenes = true;
            try
            {
                foreach (var scene in playScenes)
                    _runtimeSceneManager.RegisterScene(scene);
            }
            finally { _registeringPlayModeScenes = false; }
            _runtimeSceneManager.SetActiveScene(_scene);
            session.InvokeAfterDeserializeCallbacks();
            foreach (var scene in playScenes)
            {
                if (!scene.isCreated || !_runtimeSceneManager.LoadedScenes.Contains(scene)) continue;
                StartRuntime(scene);
            }
            FocusGameViewIfOpen();
            EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
        }
        catch (Exception exception)
        {
            if (session is not null)
            {
                _exitingPlayMode = true;
                try { RestoreEditModeSession(session); }
                finally { _exitingPlayMode = false; }
            }
            else
            {
                Undo.Restore(undoHistory);
                ComponentClipboard.Restore(componentClipboard);
                EditorUtility.RestoreDirtyState(dirtyState);
                runtimeState.Restore();
                _copiedGameObject = copiedGameObject;
                _playing = false;
                _paused = false;
                Application.isPlaying = false;
            }
            EditorFeatureGuard.Report("Enter Play Mode", exception);
            EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }
    }

    private bool ExitPlayMode(
        bool raiseStateEvents = true,
        bool processDeferredCompilation = true)
    {
        if (_enteringPlayMode || _exitingPlayMode) return false;
        if (!_playing && _playModeSession is null) return false;
        _exitingPlayMode = true;
        try
        {
            if (raiseStateEvents)
                EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            if (_playModeSession is { } session)
                RestoreEditModeSession(session);
            else
            {
                StopAllRuntimes();
                _playing = false;
                _paused = false;
                Application.isPlaying = false;
            }

            if (raiseStateEvents)
                EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
            if (processDeferredCompilation && _scriptCompilationDeferred)
            {
                _scriptCompilationDeferred = false;
                QueueScriptCompilation();
            }
            return true;
        }
        finally
        {
            _exitingPlayMode = false;
            if (_closePendingAfterPlayModeTransition) CompleteClose();
        }
    }

    private void ApplyPlayModeSession(EditorPlayModeSession session)
    {
        _openScenes.Clear();
        _openScenes.AddRange(session.RuntimeOpenScenes);
        _scene = session.RuntimeScene;
        _mainScene = session.RuntimeMainScene;
        _mainSelection = session.RuntimeMainSelection;
        _prefabStage = session.RuntimePrefabStage;
        _selected = session.RuntimeSelected;
        _selectedAsset = session.RuntimeSelectedAsset;
        _selectedAssetPath = session.EditSelectedAssetPath;
        _dirty = session.EditDirty;
        _mainSceneDirty = session.EditMainSceneDirty;
        RefreshLoadedSceneSnapshot();
        Selection.RestoreHostSelection(
            session.RuntimeSelectionObjects,
            session.RuntimeSelectionContext,
            _selected ?? _selectedAsset);
        _inspector.RemapLockedTarget(session.ToRuntime);
        EditorApplication.RaiseHierarchyChanged();
        _mainWindow.SetTitle(BuildTitle());
    }

    private void RestoreEditModeSession(EditorPlayModeSession session)
    {
        var editScenes = session.EditOpenScenes.Select(item => item.Scene)
            .Append(session.EditScene)
            .Concat(session.EditMainScene is null ? [] : [session.EditMainScene])
            .Distinct()
            .ToArray();
        foreach (var scene in _openScenes.Select(item => item.Scene)
                     .Append(_scene)
                     .Concat(_runtimeSceneManager.LoadedScenes)
                     .Where(scene => !editScenes.Any(editScene => ReferenceEquals(editScene, scene))))
            session.RuntimeScenes.Add(scene);

        try
        {
            StopAllRuntimes(unregisterScenes: false);
            var disposedRuntimeScenes = new HashSet<Scene>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                var runtimeScenes = session.RuntimeScenes
                    .Concat(_runtimeSceneManager.LoadedScenes)
                    .Where(runtimeScene =>
                        !editScenes.Any(editScene => ReferenceEquals(editScene, runtimeScene)) &&
                        runtimeScene.isCreated && disposedRuntimeScenes.Add(runtimeScene))
                    .ToArray();
                if (runtimeScenes.Length == 0) break;
                foreach (var runtimeScene in runtimeScenes)
                    EditorFeatureGuard.Invoke(runtimeScene, "Dispose Play Mode Scene", runtimeScene.Dispose);
            }

            foreach (var scene in _runtimeSceneManager.LoadedScenes.ToArray())
                if (!editScenes.Any(editScene => ReferenceEquals(editScene, scene)))
                    EditorFeatureGuard.Invoke(scene, "Unregister Play Mode Scene",
                        () => _runtimeSceneManager.UnregisterScene(scene));

            _playing = false;
            _paused = false;
            Application.isPlaying = false;
        }
        finally
        {
            _openScenes.Clear();
            _openScenes.AddRange(session.EditOpenScenes);
            _scene = session.EditScene;
            _scenePath = session.EditScenePath;
            _mainScene = session.EditMainScene;
            _mainSelection = session.EditMainSelection;
            _prefabStage = session.EditPrefabStage;
            _selected = session.EditSelected;
            _selectedAsset = session.EditSelectedAsset;
            _selectedAssetPath = session.EditSelectedAssetPath;
            _dirty = session.EditDirty;
            _mainSceneDirty = session.EditMainSceneDirty;
            _playing = false;
            _paused = false;
            Application.isPlaying = false;
            _playModeSession = null;
            RefreshLoadedSceneSnapshot();
            Undo.Restore(session.UndoHistory);
            ComponentClipboard.Restore(session.ComponentClipboard);
            EditorUtility.RestoreDirtyState(session.DirtyState);
            _copiedGameObject = session.CopiedGameObject;
            session.RuntimeState.Restore();
            Selection.RestoreHostSelection(
                session.EditSelectionObjects,
                session.EditSelectionContext,
                _selected ?? _selectedAsset);
            _inspector.RemapLockedTarget(session.ToEdit);
            EditorApplication.RaiseHierarchyChanged();
            _mainWindow.SetTitle(BuildTitle());
        }
    }

    private void Step()
    {
        if (!_playing || _runtimes.Count == 0) return;
        _paused = true;
        foreach (var runtime in _runtimes.ToArray())
            EditorFeatureGuard.Invoke(runtime, "SceneRuntime.Step", () => runtime.Tick(Time.fixedDeltaTime));
    }

    private IReadOnlyList<Scene> LoadedScenes() => _loadedSceneSnapshot;

    private void RefreshLoadedSceneSnapshot() => _loadedSceneSnapshot = _prefabStage is not null
        ? [_scene]
        : _openScenes.Where(item => item.IsLoaded).Select(item => item.Scene).ToArray();

    private EditorOpenScene? FindSceneEntry(Scene scene) =>
        _openScenes.FirstOrDefault(item => ReferenceEquals(item.Scene, scene));

    private Scene? OpenEditorScene(string scenePath, OpenSceneMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenePath);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (_enteringPlayMode || _exitingPlayMode) return null;
        var fullPath = Path.GetFullPath(scenePath);
        if (!File.Exists(fullPath)) return null;
        if (_prefabStage is not null) ClosePrefabStage();

        var existing = _openScenes.FirstOrDefault(item =>
            item.SourcePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (mode == OpenSceneMode.Single)
        {
            StopPlayingForSceneChange();
            existing = _openScenes.FirstOrDefault(item =>
                item.SourcePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
            SaveAllOpenScenes();
            ClearSceneObjectHistory();
            foreach (var previousEntry in _openScenes.Where(item => !ReferenceEquals(item, existing)).ToArray())
            {
                _openScenes.Remove(previousEntry);
                previousEntry.Scene.isLoaded = false;
                if (previousEntry.Scene.isCreated) previousEntry.Scene.Dispose();
                EditorSceneManager.RaiseSceneClosed(previousEntry.Scene);
            }
            if (existing is not null && !existing.IsLoaded)
            {
                if (existing.Scene.isCreated) existing.Scene.Dispose();
                _openScenes.Remove(existing);
                existing = null;
            }
        }
        else if (mode == OpenSceneMode.Additive && existing is { IsLoaded: true })
        {
            SetActiveEditorScene(existing.Scene);
            return existing.Scene;
        }

        if (existing is { IsLoaded: false })
        {
            if (existing.Scene.isCreated) existing.Scene.Dispose();
            _openScenes.Remove(existing);
            existing = null;
        }
        if (existing is not null)
        {
            SetActiveEditorScene(existing.Scene);
            if (mode == OpenSceneMode.Single) SelectInitialSceneObject(existing.Scene);
            RefreshLoadedSceneSnapshot();
            return existing.Scene;
        }

        var assetPath = ToAssetPath(fullPath);
        if (mode == OpenSceneMode.AdditiveWithoutLoading)
        {
            var placeholder = new Scene(SceneDisplayName(fullPath), _services)
            {
                path = fullPath,
                isLoaded = false
            };
            var unloaded = new EditorOpenScene(placeholder, fullPath, assetPath, false);
            _openScenes.Add(unloaded);
            RefreshLoadedSceneSnapshot();
            EditorSceneManager.RaiseSceneOpened(placeholder, mode);
            EditorApplication.RaiseHierarchyChanged();
            return placeholder;
        }

        var scene = Document.LoadBObject<SceneDocument, Scene>(fullPath, _services);
        scene.path = fullPath;
        scene.isLoaded = true;
        var entry = new EditorOpenScene(scene, fullPath, assetPath);
        _openScenes.Add(entry);
        SetActiveEditorScene(scene);
        if (mode == OpenSceneMode.Single) SelectInitialSceneObject(scene);
        RefreshLoadedSceneSnapshot();
        if (_playing)
        {
            StartRuntime(scene);
            _runtimeSceneManager.SetActiveScene(scene);
        }
        EditorSceneManager.RaiseSceneOpened(scene, mode);
        EditorApplication.RaiseHierarchyChanged();
        return scene;
    }

    private void SelectInitialSceneObject(Scene scene)
    {
        _selected = scene.gameObjects.FirstOrDefault();
        _selectedAsset = null;
        _selectedAssetPath = null;
        Selection.NotifyHostSelectionChanged(_selected);
        _inspector.RebuildEditor();
    }

    private bool CloseEditorScene(Scene scene, bool removeScene)
    {
        if (_enteringPlayMode || _exitingPlayMode) return false;
        if (_prefabStage is not null || FindSceneEntry(scene) is not { } entry) return false;
        var otherLoaded = _openScenes.FirstOrDefault(item => !ReferenceEquals(item, entry) && item.IsLoaded);
        if (entry.IsLoaded && otherLoaded is null) return false;
        if (entry.IsDirty && !SaveEditorScene(scene)) return false;
        ClearSceneObjectHistory();
        StopRuntime(scene);

        if (removeScene)
            _openScenes.Remove(entry);
        else
            entry.IsLoaded = false;
        scene.isLoaded = false;
        if (scene.isCreated) scene.Dispose();

        if (ReferenceEquals(_scene, scene) && otherLoaded is not null)
            SetActiveEditorScene(otherLoaded.Scene);
        if (ReferenceEquals(_selected?.scene, scene))
        {
            _selected = null;
            Selection.NotifyHostSelectionChanged(null);
            _inspector.RebuildEditor();
        }
        RefreshLoadedSceneSnapshot();
        EditorSceneManager.RaiseSceneClosed(scene);
        EditorApplication.RaiseHierarchyChanged();
        return true;
    }

    private bool SetActiveEditorScene(Scene scene)
    {
        if (FindSceneEntry(scene) is not { IsLoaded: true } entry) return false;
        var previous = _scene;
        _scene = scene;
        _scenePath = entry.SourcePath;
        _dirty = entry.IsDirty;
        _mainWindow.SetTitle(BuildTitle());
        if (_playing && _services.GetService<IRuntimeSceneManager>() is { } sceneManager)
            sceneManager.SetActiveScene(scene);
        if (!ReferenceEquals(previous, scene)) EditorSceneManager.RaiseActiveSceneChanged(previous, scene);
        return true;
    }

    private void StopPlayingForSceneChange()
    {
        ExitPlayMode();
    }

    private SceneRuntime StartRuntime(Scene scene)
    {
        var runtime = _sceneRuntimeFactory.Create(scene);
        _runtimes.Add(runtime);
        try
        {
            runtime.Start();
            return runtime;
        }
        catch
        {
            _runtimes.Remove(runtime);
            throw;
        }
    }

    private void StopRuntime(Scene scene)
    {
        var runtime = _runtimes.FirstOrDefault(item => ReferenceEquals(item.Scene, scene));
        if (runtime is null) return;
        EditorFeatureGuard.Invoke(runtime, "SceneRuntime.Stop", runtime.Stop);
        _runtimes.Remove(runtime);
        if (_services.GetService<IRuntimeSceneManager>() is { } sceneManager)
            sceneManager.UnregisterScene(scene);
    }

    private void StopAllRuntimes(bool unregisterScenes = true)
    {
        var runtimes = _runtimes.ToArray();
        _runtimes.Clear();
        for (var index = runtimes.Length - 1; index >= 0; index--)
            EditorFeatureGuard.Invoke(runtimes[index], "SceneRuntime.Stop", runtimes[index].Stop);
        if (!unregisterScenes) return;
        foreach (var scene in runtimes.Select(item => item.Scene).Distinct())
            EditorFeatureGuard.Invoke(scene, "Unregister Runtime Scene",
                () => _runtimeSceneManager.UnregisterScene(scene));
    }

    private void PingSceneAsset(EditorOpenScene entry)
    {
        var record = _assets.GetRecord(entry.AssetPath);
        if (record is null) return;
        _project.Ping(record.AssetPath);
        _dock.Show(_project.PersistentId);
        Select(record);
    }

    private GameObject? FindGameObject(Guid id) => _openScenes
        .Where(item => item.IsLoaded)
        .Select(item => item.Scene.Find(id))
        .FirstOrDefault(item => item is not null);

    private string ToAssetPath(string sourcePath) => Path.GetRelativePath(_workspace.RootPath,
        Path.GetFullPath(sourcePath)).Replace('\\', '/');

    private static string SceneDisplayName(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        return fileName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scene.yaml".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private void RefreshAssets()
    {
        CancelAssetRefresh();
        var changes = _assets.Refresh();
        ProcessAssetChanges(changes, false, false);
    }

    private void ProcessProjectSourceChanges()
    {
        if (!_sourceChanges.TryDequeue(out var scriptsChanged, out var shadersChanged)) return;
        QueueAssetRefresh(scriptsChanged, shadersChanged);
    }

    private void QueueAssetRefresh(bool scriptsChanged, bool shadersChanged)
    {
        _pendingRefreshScripts |= scriptsChanged;
        _pendingRefreshShaders |= shadersChanged;
        CancelAssetRefresh(clearPending: false);
        var cancellation = new CancellationTokenSource();
        _assetRefreshCancellation = cancellation;
        var generation = ++_assetRefreshGeneration;
        var refresh = _tasks.ScheduleAsync(
            "Refresh project assets",
            async token =>
            {
                await _assetRefreshGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    return _assets.PrepareRefresh(cancellationToken: token);
                }
                finally { _assetRefreshGate.Release(); }
            },
            EditorTaskPriority.Critical,
            cancellation.Token);
        _ = CompleteAssetRefreshAsync(refresh, generation, cancellation.Token);
    }

    private async Task CompleteAssetRefreshAsync(
        Task<AssetRefreshSnapshot> refresh,
        int generation,
        CancellationToken cancellationToken)
    {
        AssetRefreshSnapshot snapshot;
        try { snapshot = await refresh.ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch { return; }
        try
        {
            _tasks.Post(() => ApplyAssetRefresh(generation, snapshot), "Apply project asset refresh");
        }
        catch (ObjectDisposedException) { }
    }

    private void ApplyAssetRefresh(int generation, AssetRefreshSnapshot snapshot)
    {
        if (generation != _assetRefreshGeneration) return;
        _assetRefreshCancellation?.Dispose();
        _assetRefreshCancellation = null;
        var scriptsChanged = _pendingRefreshScripts;
        var shadersChanged = _pendingRefreshShaders;
        _pendingRefreshScripts = false;
        _pendingRefreshShaders = false;
        if (!_assets.TryApplyRefresh(snapshot, out var changes))
        {
            QueueAssetRefresh(scriptsChanged, shadersChanged);
            return;
        }
        ProcessAssetChanges(changes, scriptsChanged, shadersChanged);
    }

    private void CancelAssetRefresh(bool clearPending = true)
    {
        _assetRefreshGeneration++;
        var cancellation = _assetRefreshCancellation;
        _assetRefreshCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        if (!clearPending) return;
        _pendingRefreshScripts = false;
        _pendingRefreshShaders = false;
    }

    private void ProcessAssetChanges(
        IReadOnlyList<AssetChange> changes,
        bool scriptsChanged,
        bool shadersChanged)
    {
        _project.Invalidate();
        EditorApplication.RaiseProjectChanged();
        scriptsChanged |= changes.Any(change =>
            ProjectSourceChangeMonitor.IsScriptPath(change.AssetPath) ||
            change.PreviousPath is { } previous && ProjectSourceChangeMonitor.IsScriptPath(previous));
        var shaderPaths = changes
            .SelectMany(change => change.PreviousPath is { } previous
                ? new[] { change.AssetPath, previous }
                : [change.AssetPath])
            .Where(ProjectShaderCompiler.IsShaderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (shadersChanged && shaderPaths.Length == 0)
            shaderPaths = _assets.assets.Where(asset => asset.AssetType == "Shader")
                .Select(asset => asset.AssetPath).ToArray();
        if (scriptsChanged) QueueScriptCompilation();
        if (shadersChanged || shaderPaths.Length > 0) QueueShaderCompilation(shaderPaths);
    }

    private void CompileShaders(IEnumerable<string> assetPaths)
    {
        var paths = assetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        var emptyResult = new ShaderCompilationResult(
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>());
        if (!EditorFeatureGuard.TryInvoke("ProjectShaderCompiler.CompileChanged",
                () => ProjectShaderCompiler.CompileChanged(_workspace, paths), emptyResult, out var result)) return;
        foreach (var error in result.Errors)
            Debug.LogError($"Shader compilation failed for '{error.Key}': {error.Value}");
        if (result.CompiledArtifacts.Count > 0)
            Debug.Log($"Compiled {result.CompiledArtifacts.Count} changed shader(s).");
    }

    private void QueueShaderCompilation(IEnumerable<string> assetPaths)
    {
        var paths = assetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        CancelShaderCompilation();
        var cancellation = new CancellationTokenSource();
        _shaderCompilationCancellation = cancellation;
        var generation = ++_shaderCompilationGeneration;
        var compilation = _tasks.ScheduleAsync(
            "Compile changed shaders",
            async token =>
            {
                await _shaderCompilationGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    return ProjectShaderCompiler.CompileChangedInBackground(_workspace, paths, token);
                }
                finally { _shaderCompilationGate.Release(); }
            },
            EditorTaskPriority.Normal,
            cancellation.Token);
        _ = CompleteShaderCompilationAsync(compilation, generation, cancellation.Token);
    }

    private async Task CompleteShaderCompilationAsync(
        Task<ShaderCompilationResult> compilation,
        int generation,
        CancellationToken cancellationToken)
    {
        ShaderCompilationResult result;
        try { result = await compilation.ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch { return; }
        try
        {
            _tasks.Post(() => ApplyShaderCompilation(generation, result), "Apply changed shaders");
        }
        catch (ObjectDisposedException) { }
    }

    private void ApplyShaderCompilation(int generation, ShaderCompilationResult result)
    {
        if (generation != _shaderCompilationGeneration) return;
        _shaderCompilationCancellation?.Dispose();
        _shaderCompilationCancellation = null;
        ProjectShaderCompiler.ApplyCompilationResult(result);
        foreach (var error in result.Errors)
            Debug.LogError($"Shader compilation failed for '{error.Key}': {error.Value}");
        if (result.CompiledArtifacts.Count > 0)
            Debug.Log($"Compiled {result.CompiledArtifacts.Count} changed shader(s) in the background.");
    }

    private void CancelShaderCompilation()
    {
        _shaderCompilationGeneration++;
        var cancellation = _shaderCompilationCancellation;
        _shaderCompilationCancellation = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void CompileScripts()
    {
        if (_consoleClearedForPackageReload)
            _consoleClearedForPackageReload = false;
        else
            ConsoleLogController.ClearIfEnabled(ConsoleClearTrigger.Recompile);
        var reloading = EditorBridge.Host is not null;
        if (reloading)
        {
            CloseTransientMenus();
            AssemblyReloadEvents.RaiseBeforeAssemblyReload();
        }
        _scriptSourceCache.Clear();
        ComponentClipboard.Clear();
        Undo.ClearAll();
        EditorFeatureGuard.Invoke("ProjectScriptCompiler.ReleaseLoadContexts",
            ProjectScriptCompiler.ReleaseLoadContexts);
        EditorApplication.isCompiling = true;
        CompilationPipeline.RaiseCompilationStarted(_workspace);
        try
        {
            EditorUtility.DisplayProgressBar("打开项目", "编译运行时脚本...", 0.48f);
            Assembly? game = null;
            var runtimeSucceeded = true;
            try
            {
                game = ProjectScriptCompiler.CompileAndLoad(_workspace);
            }
            catch (Exception exception)
            {
                runtimeSucceeded = false;
                RecordScriptCompilationFailure("Project runtime assemblies", "ScriptCompilation.log", exception);
            }

            if (runtimeSucceeded)
            {
                EditorUtility.DisplayProgressBar("打开项目", "编译编辑器脚本...", 0.62f);
                try
                {
                    EditorProjectScriptCompiler.CompileAndLoad(
                        _workspace, game, typeof(MenuItemAttribute).Assembly.Location);
                }
                catch (Exception exception)
                {
                    RecordScriptCompilationFailure(
                        "Project editor assemblies", "EditorScriptCompilation.log", exception);
                }
            }

            if (reloading)
            {
                TypeCache.Refresh();
                EditorFeatureGuard.Invoke("EditorInitialization.Run", () => EditorInitialization.Run());
            }
        }
        finally
        {
            CompilationPipeline.RaiseCompilationFinished(_workspace);
            EditorApplication.isCompiling = false;
            if (reloading) AssemblyReloadEvents.RaiseAfterAssemblyReload();
        }
    }

    private void QueueScriptCompilation()
    {
        if (_playing)
        {
            _scriptCompilationDeferred = true;
            return;
        }
        if (EditorApplication.isAssemblyReloadLocked)
        {
            EditorApplication.RequestReloadWhenUnlocked();
            return;
        }
        _scriptCompilationDeferred = false;
        if (_consoleClearedForPackageReload)
            _consoleClearedForPackageReload = false;
        else
            ConsoleLogController.ClearIfEnabled(ConsoleClearTrigger.Recompile);

        Dictionary<string, string> editorReferences;
        IReadOnlyDictionary<string, string> runtimeReferences;
        try
        {
            var preferred = ProjectScriptCompiler.PreferredAssemblyPaths(
                typeof(MenuItemAttribute).Assembly.Location);
            editorReferences = EditorProjectScriptCompiler.PackageReferences(
                _workspace, _packages, preferred);
            runtimeReferences = RuntimePackageLoader.ResolveEnabledReferences(
                _workspace, _packages.definitions).AssemblyReferences;
        }
        catch (Exception exception)
        {
            RecordScriptCompilationFailure("Project assembly preparation", "ScriptCompilation.log", exception);
            return;
        }

        CancelScriptCompilation(endTransaction: false);
        var cancellation = new CancellationTokenSource();
        _scriptCompilationCancellation = cancellation;
        var generation = ++_scriptCompilationGeneration;
        if (!_backgroundCompilationActive)
        {
            _backgroundCompilationActive = true;
            EditorApplication.isCompiling = true;
            CompilationPipeline.RaiseCompilationStarted(_workspace);
        }
        EditorUtility.DisplayProgressBar("编译脚本", "后台编译运行时与编辑器程序集...", 0.2f);
        var build = _tasks.ScheduleAsync(
            "Compile project scripts",
            async token =>
            {
                await _scriptCompilationGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    return ProjectScriptBuildPipeline.Build(
                        _workspace, runtimeReferences, editorReferences, token);
                }
                finally { _scriptCompilationGate.Release(); }
            },
            EditorTaskPriority.Critical,
            cancellation.Token);
        _ = CompleteScriptCompilationAsync(build, generation, cancellation.Token);
    }

    private async Task CompleteScriptCompilationAsync(
        Task<ProjectScriptBuildResult> build,
        int generation,
        CancellationToken cancellationToken)
    {
        ProjectScriptBuildResult? result = null;
        Exception? failure = null;
        try { result = await build.ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception exception) { failure = exception; }

        try
        {
            _tasks.Post(() => ApplyScriptCompilation(generation, result, failure),
                "Apply project script compilation");
        }
        catch (ObjectDisposedException) { }
    }

    private void ApplyScriptCompilation(
        int generation,
        ProjectScriptBuildResult? build,
        Exception? failure)
    {
        if (generation != _scriptCompilationGeneration) return;
        _scriptCompilationCancellation?.Dispose();
        _scriptCompilationCancellation = null;
        if (_playing)
        {
            _scriptCompilationDeferred = true;
            EndBackgroundCompilation();
            return;
        }
        if (EditorApplication.isAssemblyReloadLocked)
        {
            EditorApplication.RequestReloadWhenUnlocked();
            EndBackgroundCompilation();
            return;
        }
        var reloading = EditorBridge.Host is not null;
        var reloadStarted = false;
        try
        {
            if (failure is not null || build is null)
            {
                RecordScriptCompilationFailure("Project assemblies", "ScriptCompilation.log",
                    failure ?? new InvalidOperationException("The background compiler produced no result."));
                return;
            }

            if (reloading)
            {
                CloseTransientMenus();
                AssemblyReloadEvents.RaiseBeforeAssemblyReload();
                reloadStarted = true;
            }
            _scriptSourceCache.Clear();
            ComponentClipboard.Clear();
            Undo.ClearAll();
            ProjectScriptBuildPipeline.Apply(_workspace, build);
            _scriptCompilationFailed = false;
            if (reloading)
            {
                TypeCache.Refresh();
                EditorFeatureGuard.Invoke("EditorInitialization.Run", () => EditorInitialization.Run());
                _menuItems = DiscoverMenuItems(_menuItems);
                ReloadSelectionAfterScriptReload();
                _project.Invalidate();
            }
            Debug.Log($"Compiled {build.Runtime.CompiledAssemblies.Count} runtime and " +
                      $"{build.Editor.CompiledAssemblies.Count} editor assembly(s) in the background.");
        }
        catch (Exception exception)
        {
            RecordScriptCompilationFailure("Project assembly reload", "ScriptCompilation.log", exception);
        }
        finally
        {
            EndBackgroundCompilation();
            if (reloadStarted) AssemblyReloadEvents.RaiseAfterAssemblyReload();
        }
    }

    private void ReloadSelectionAfterScriptReload()
    {
        var previousSelectedAsset = _selectedAsset;
        if (_selected is null && !string.IsNullOrWhiteSpace(_selectedAssetPath))
            _selectedAsset = AssetDatabase.LoadMainAssetAtPath(_selectedAssetPath);

        var lockedTarget = _inspector.LockedTarget;
        if (lockedTarget is not null)
        {
            BObject? reloadedLockedTarget = lockedTarget;
            if (lockedTarget is not GameObject)
            {
                var lockedPath = AssetDatabase.GetAssetPath(lockedTarget);
                if (!string.IsNullOrWhiteSpace(lockedPath))
                    reloadedLockedTarget = previousSelectedAsset is not null &&
                                           ReferenceEquals(lockedTarget, previousSelectedAsset) &&
                                           PathsEqual(lockedPath, _selectedAssetPath)
                        ? _selectedAsset
                        : AssetDatabase.LoadMainAssetAtPath(lockedPath);
            }
            _inspector.RemapLockedTarget(target => ReferenceEquals(target, lockedTarget)
                ? reloadedLockedTarget
                : target);
        }
        else
            _inspector.RebuildEditor(force: true);

        if (_selected is null && !ReferenceEquals(previousSelectedAsset, _selectedAsset))
            Selection.NotifyHostSelectionChanged(_selectedAsset);
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        left.Replace('\\', '/').Equals(right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private void CancelScriptCompilation(bool endTransaction = true)
    {
        _scriptCompilationGeneration++;
        var cancellation = _scriptCompilationCancellation;
        _scriptCompilationCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        if (endTransaction) EndBackgroundCompilation();
    }

    private void EndBackgroundCompilation()
    {
        if (!_backgroundCompilationActive) return;
        _backgroundCompilationActive = false;
        CompilationPipeline.RaiseCompilationFinished(_workspace);
        EditorApplication.isCompiling = false;
        EditorUtility.ClearProgressBar();
    }

    private void RecordScriptCompilationFailure(string assemblyName, string logFileName, Exception exception)
    {
        _scriptCompilationFailed = true;
        var logPath = Path.Combine(EditorInstanceContext.current?.logsPath ?? _workspace.LogsPath, logFileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath,
                $"{Environment.NewLine}[{DateTimeOffset.Now:O}] {assemblyName}{Environment.NewLine}{exception}" +
                Environment.NewLine);
        }
        catch (Exception logException)
        {
            EditorFeatureGuard.Report($"{assemblyName} compilation log", logException);
        }
        Debug.LogException(new InvalidOperationException(
            $"{assemblyName} 编译失败。编辑器已继续启动，请查看 {logPath}", exception));
    }

    private static MenuItemRegistry DiscoverMenuItems(MenuItemRegistry fallback)
    {
        EditorFeatureGuard.TryInvoke("MenuItemRegistry.Discover", MenuItemRegistry.Discover,
            fallback, out var registry);
        return registry;
    }

    private string ResolveInitialScene()
    {
        var settingsPath = Path.Combine(_workspace.ProjectSettingsPath, "EditorSettings.yaml");
        if (!File.Exists(settingsPath)) return _workspace.StartupScenePath;
        try
        {
            var document = Document.Load<EditorSettingsDocument>(settingsPath);
            var path = _workspace.ResolveInside(document.LastScene);
            return File.Exists(path) ? path : _workspace.StartupScenePath;
        }
        catch { return _workspace.StartupScenePath; }
    }

    private static void RegisterResourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var core = Path.Combine(directory.FullName, "src", "Core");
            if (!Directory.Exists(Path.Combine(core, EditorResource.DirectoryName))) continue;
            Resources.RegisterResourceRoot(core); return;
        }
    }

    private string BuildTitle() => _prefabStage is null
        ? $"BEngine - {_workspace.Project.Name}{(_openScenes.Any(item => item.IsDirty) ? " *" : string.Empty)}"
        : $"BEngine - {_workspace.Project.Name} - {_prefabStage.prefabContentsRoot.name} (Prefab){(_dirty ? " *" : string.Empty)}";
    private void Trace(string message)
        => _sessionLogWriter.Write(message);
    private static void OpenExternal(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private static void ShowInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
                { UseShellExecute = true });
        else if (Directory.Exists(fullPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{fullPath}\"")
                { UseShellExecute = true });
    }
    private static Color C(float r, float g, float b, float a = 1) =>
        new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);

    private void FrameSelectedInScene()
    {
        if (_nativeFloatingWindows.TryGetValue(_sceneView, out var nativeScene)) nativeScene.Focus();
        else _dock.Show(_sceneView.PersistentId);
        if (!TryGetSelectedBounds(out var pivot, out var radius)) return;
        _editorCameraPivot = pivot;
        var framedSize = Math.Max(0.01f, radius * 1.2f);
        if (_lastHeight > 0 && _editorCameraReferenceHeight > 0)
        {
            var renderScale = Math.Max(0.01f, (float)RenderScaleFor(_sceneView));
            var viewportHeight = _lastHeight / renderScale;
            framedSize *= _editorCameraReferenceHeight / viewportHeight;
        }
        _editorCameraSize = Math.Clamp(framedSize, 0.01f, 100000f);
        _editorCameraPosition = pivot;
    }

    private bool TryGetSelectedBounds(out NVector2 pivot, out float radius)
    {
        pivot = default;
        radius = 0;
        if (_selected is null) return false;
        var minimum = new NVector2(float.MaxValue);
        var maximum = new NVector2(float.MinValue);
        foreach (var transform in _selected.GetComponentsInChildren<Transform>(true))
        {
            var position = transform.position;
            var scale = transform.lossyScale;
            var center = new NVector2((float)position.x, (float)position.y);
            var extent = new NVector2(MathF.Abs((float)scale.x), MathF.Abs((float)scale.y)) * 0.5f;
            extent = NVector2.Max(extent, new NVector2(0.25f));
            minimum = NVector2.Min(minimum, center - extent);
            maximum = NVector2.Max(maximum, center + extent);
        }

        if (!float.IsFinite(minimum.X)) return false;
        pivot = (minimum + maximum) * 0.5f;
        radius = Math.Max(0.5f, (maximum - minimum).Length() * 0.5f);
        return true;
    }

    IEditorTaskScheduler IEditorHost.TaskScheduler => _tasks;
    Scene IEditorHost.ActiveScene => _scene;
    IReadOnlyList<Scene> IEditorHost.OpenScenes => _openScenes.Select(item => item.Scene).ToArray();
    BObject? IEditorHost.ActiveObject
    {
        get => _selected ?? (BObject?)_selectedAsset;
        set
        {
            if (value is GameObject gameObject) Select(gameObject);
            else if (value is Component component) Select(component.gameObject);
            else { _selected = null; _selectedAsset = value; }
        }
    }
    GameObject? IEditorHost.ActiveGameObject { get => _selected; set { if (value is null) _selected = null; else Select(value); } }
    void IEditorHost.MarkSceneDirty() => MarkDirty();
    bool IEditorHost.MarkSceneDirty(Scene scene) => MarkDirty(scene);
    void IEditorHost.FrameSelected() => FrameSelectedInScene();
    bool IEditorHost.SaveActiveScene() => _prefabStage is not null ? SavePrefabStage() : SaveEditorScene(_scene);
    bool IEditorHost.SaveScene(Scene scene) => SaveEditorScene(scene);
    Scene? IEditorHost.OpenScene(string scenePath, OpenSceneMode mode) => OpenEditorScene(scenePath, mode);
    bool IEditorHost.CloseScene(Scene scene, bool removeScene) => CloseEditorScene(scene, removeScene);
    bool IEditorHost.SetActiveScene(Scene scene) => SetActiveEditorScene(scene);
    bool IEditorHost.IsPlaying { get => _playing; set { if (_playing != value) TogglePlay(); } }
    bool IEditorHost.IsChangingPlayMode => _enteringPlayMode || _exitingPlayMode;
    bool IEditorHost.IsPaused { get => _paused; set => _paused = _playing && value; }
    void IEditorHost.ShowWindow(EditorWindow window) => ShowEditorWindow(window);
    void IEditorHost.FocusWindow(EditorWindow window) => FocusEditorWindow(window);
    void IEditorHost.CloseWindow(EditorWindow window) => CloseEditorWindow(window);
    void IEditorHost.RepaintWindow(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.IsOpen) return;
        if (_nativeFloatingWindows is not null &&
            _nativeFloatingWindows.TryGetValue(window, out var native)) native.Repaint();
        else if (_nativeTransientOwners is not null &&
                 _nativeTransientOwners.TryGetValue(window, out var owner)) owner.Repaint();
        else if (_mainWindow is not null) _mainWindow.Repaint();
    }

    void IEditorHost.RepaintAllWindows()
    {
        if (_mainWindow is not null) _mainWindow.Repaint();
        if (_nativeFloatingWindows is null) return;
        foreach (var native in _nativeFloatingWindows.Values) native.Repaint();
    }
    Tool IEditorHost.CurrentTool { get => _tool; set => _tool = value; }
    bool IEditorHost.ExecuteMenuItem(string itemName) => _menuItems.Execute(itemName);
    string? IEditorHost.ActiveProjectAssetPath => _project.SelectedAssetPath;
    string? IEditorHost.ActiveProjectFolderPath => _project.SelectedAssetsFolder();
    void IEditorHost.RevealProjectAsset(string assetPath, bool beginRename) =>
        _project.SelectCreatedAsset(assetPath, beginRename);
    bool IEditorHost.CanExecuteProjectAssetCommand(ProjectAssetCommand command) =>
        _project.CanExecuteCommand(command);
    bool IEditorHost.ExecuteProjectAssetCommand(ProjectAssetCommand command) =>
        _project.ExecuteCommand(command);
    bool IEditorHost.CanExecuteGameObjectCommand(GameObjectCommand command, GameObject? target) =>
        CanExecuteGameObjectCommand(command, target);
    bool IEditorHost.ExecuteGameObjectCommand(GameObjectCommand command, GameObject? target) =>
        ExecuteGameObjectCommand(command, target);
    bool IEditorHost.CanExecuteComponentCommand(ComponentCommand command) =>
        CanExecuteComponentCommand(command);
    bool IEditorHost.ExecuteComponentCommand(ComponentCommand command) =>
        ExecuteComponentCommand(command);
    bool IEditorHost.CanExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera) =>
        CanExecuteCameraViewCommand(command, camera);
    bool IEditorHost.ExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera) =>
        ExecuteCameraViewCommand(command, camera);
    bool IEditorHost.CanExecuteMainMenuCommand(MainMenuCommand command) =>
        CanExecuteMainMenuCommand(command);
    bool IEditorHost.ExecuteMainMenuCommand(MainMenuCommand command) =>
        ExecuteMainMenuCommand(command);
    bool IEditorHost.IsMainMenuCommandChecked(MainMenuCommand command) =>
        IsMainMenuCommandChecked(command);
    void IEditorHost.Exit(int exitCode) => RequestExit();
    string IEditorHost.ProjectRootPath => _workspace.RootPath;
    string IEditorHost.AssetsRootPath => _workspace.AssetsPath;
    EditorAssetRecord[] IEditorHost.FindAssets(string search) => _assets.FindAssets(search).Select(ToRecord).ToArray();
    EditorAssetRecord? IEditorHost.GetAsset(string assetPath) => _assets.GetRecord(assetPath) is { } item ? ToRecord(item) : null;
    EditorAssetRecord? IEditorHost.GetAsset(Guid guid) => _assets.GetRecord(guid) is { } item ? ToRecord(item) : null;
    void IEditorHost.RefreshAssets() => RefreshAssets();
    void IEditorHost.ImportAsset(string assetPath)
    {
        CancelAssetRefresh();
        _assets.ImportAsset(ResolveAssetPath(assetPath));
    }
    void IEditorHost.RequestScriptCompilation() => QueueScriptCompilation();
    string IEditorHost.CreateAssetFolder(string parentFolder, string newFolderName)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Creating project folders");
        var path = Path.Combine(ResolveAssetPath(parentFolder), newFolderName); Directory.CreateDirectory(path);
        CancelAssetRefresh();
        _assets.ImportAsset(path); return Path.GetRelativePath(_workspace.RootPath, path).Replace('\\', '/');
    }
    bool IEditorHost.DeleteAsset(string assetPath)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Deleting project assets");
        var path = ResolveAssetPath(assetPath);
        if (File.Exists(path)) File.Delete(path); else if (Directory.Exists(path)) Directory.Delete(path, true); else return false;
        var metadataPath = path + ".meta";
        if (File.Exists(metadataPath)) File.Delete(metadataPath);
        RefreshAssets(); return true;
    }
    string IEditorHost.MoveAsset(string oldPath, string newPath)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Moving project assets");
        var result = AssetFileOperations.Move(ResolveAssetPath(oldPath), ResolveAssetPath(newPath));
        if (result.Length == 0) RefreshAssets();
        return result;
    }
    PrefabStage? IEditorHost.CurrentPrefabStage => _prefabStage;
    bool IEditorHost.OpenPrefabStage(string assetPath) => OpenPrefabStage(assetPath);
    bool IEditorHost.SavePrefabStage() => SavePrefabStage();
    void IEditorHost.ClosePrefabStage() => ClosePrefabStage();

    private bool OpenPrefabStage(string assetPath)
    {
        if (_playing || _enteringPlayMode || _exitingPlayMode) return false;
        var record = _assets.GetRecord(assetPath);
        if (record is null || record.AssetType != "Prefab") return false;
        if (_prefabStage is not null) ClosePrefabStage();
        var prefab = AssetDatabase.LoadAssetAtPath<PrefabAsset>(record.AssetPath);
        if (prefab is null) return false;
        ClearSceneObjectHistory();
        _mainScene = _scene;
        _mainSelection = _selected;
        _mainSceneDirty = _dirty;
        var root = PrefabDocumentOperations.LoadContents(prefab);
        var stageScene = new Scene(prefab.name + " (Prefab)");
        foreach (var gameObject in PrefabDocumentOperations.Traverse(root))
            if (gameObject.scene is null) stageScene.Add(gameObject);
        _scene = stageScene;
        _prefabStage = new PrefabStage(record.AssetPath, stageScene, root);
        RefreshLoadedSceneSnapshot();
        _selected = root;
        _selectedAsset = null;
        _selectedAssetPath = null;
        _dirty = false;
        Selection.NotifyHostSelectionChanged(root);
        _inspector.RebuildEditor();
        EditorApplication.RaiseHierarchyChanged();
        _mainWindow.SetTitle(BuildTitle());
        return true;
    }

    private bool SavePrefabStage()
    {
        if (_playing || _enteringPlayMode || _exitingPlayMode || _prefabStage is null) return false;
        var record = _assets.GetRecord(_prefabStage.assetPath);
        if (record is null) return false;
        var assetId = record.Guid;
        var document = Document.FromBObject<PrefabDocument>(_prefabStage.prefabContentsRoot,
            new DocumentConversionContext(record.SourcePath));
        document.Id = assetId;
        document.Save(record.SourcePath);
        CancelAssetRefresh();
        _assets.ImportAsset(record.SourcePath);
        _project.Invalidate();
        _dirty = false;
        _mainWindow.SetTitle(BuildTitle());
        return true;
    }

    private void ClosePrefabStage()
    {
        if (_playing || _enteringPlayMode || _exitingPlayMode ||
            _prefabStage is null || _mainScene is null) return;
        if (_dirty) SavePrefabStage();
        ClearSceneObjectHistory();
        var stageScene = _scene;
        _scene = _mainScene;
        _selected = _mainSelection ?? _scene.gameObjects.FirstOrDefault();
        _mainScene = null;
        _mainSelection = null;
        _prefabStage = null;
        _dirty = _mainSceneDirty;
        _mainSceneDirty = false;
        RefreshLoadedSceneSnapshot();
        if (stageScene.isCreated) stageScene.Dispose();
        Selection.NotifyHostSelectionChanged(_selected);
        _inspector.RebuildEditor();
        EditorApplication.RaiseHierarchyChanged();
        _mainWindow.SetTitle(BuildTitle());
    }

    private void ClearSceneObjectHistory()
    {
        ComponentClipboard.Clear();
        Undo.ClearAll();
        _copiedGameObject = null;
    }

    private void SaveCurrentLayout()
    {
        if (_activeLayoutName.Equals("Last Session", StringComparison.OrdinalIgnoreCase))
        {
            _layoutSaved = false;
            SaveLastLayout();
            Debug.Log("Saved the current editor layout as Last Session.");
            return;
        }
        SaveNamedLayout(_activeLayoutName);
    }

    private void SaveNamedLayout(string name)
    {
        try
        {
            var document = CaptureLayout(name);
            _activeLayoutName = _layoutStore.Save(name, document);
            document.ActiveLayout = _activeLayoutName;
            _layoutStore.SaveLastSession(document);
            _layoutSaved = false;
            Debug.Log($"Saved editor layout '{_activeLayoutName}'.");
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report($"Save editor layout {name}", exception);
        }
    }

    private void LoadNamedLayout(string name)
    {
        try
        {
            var document = _layoutStore.Load(name);
            ApplyLayout(document);
            _activeLayoutName = name;
            var lastSession = CaptureLayout("Last Session");
            lastSession.ActiveLayout = name;
            _layoutStore.SaveLastSession(lastSession);
            _layoutSaved = false;
            Debug.Log($"Loaded editor layout '{name}'.");
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report($"Load editor layout {name}", exception);
        }
    }

    private void DeleteNamedLayout(string name)
    {
        try
        {
            if (!_layoutStore.Delete(name)) return;
            if (_activeLayoutName.Equals(name, StringComparison.OrdinalIgnoreCase))
                _activeLayoutName = "Last Session";
            Debug.Log($"Deleted editor layout '{name}'.");
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report($"Delete editor layout {name}", exception);
        }
    }

    private void RestoreLastLayout()
    {
        if (!_layoutStore.HasLastSession) return;
        try
        {
            var document = _layoutStore.LoadLastSession();
            ApplyLayout(document);
            _activeLayoutName = string.IsNullOrWhiteSpace(document.ActiveLayout)
                ? "Last Session" : document.ActiveLayout;
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Restore previous editor layout", exception);
        }
    }

    private void SaveLastLayout()
    {
        if (_layoutSaved) return;
        try
        {
            var document = CaptureLayout("Last Session");
            document.ActiveLayout = _activeLayoutName;
            _layoutStore.SaveLastSession(document);
            _layoutSaved = true;
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Save previous editor layout", exception);
        }
    }

    private EditorLayoutDocument CaptureLayout(string name)
    {
        EditorLayoutDocument document;
        try
        {
            document = _layoutStore.HasLastSession
                ? _layoutStore.LoadLastSession()
                : new EditorLayoutDocument();
        }
        catch
        {
            document = new EditorLayoutDocument();
        }

        var mainPosition = _mainWindow.screenPosition;
        var mainSize = _mainWindow.windowSize;
        document.Version = 2;
        document.Name = name;
        document.ActiveLayout = _activeLayoutName;
        document.WindowX = (int)mainPosition.x;
        document.WindowY = (int)mainPosition.y;
        document.WindowWidth = Math.Max(640, (int)mainSize.x);
        document.WindowHeight = Math.Max(480, (int)mainSize.y);
        document.WindowMaximized = _mainWindow.isMaximized;
        document.HierarchyWidth = (float)_hierarchy.position.width;
        document.InspectorWidth = (float)_inspector.position.width;
        document.BottomHeight = (float)_project.position.height;
        _project.CaptureLayout(document);
        document.SceneCameraPositionX = _editorCameraPosition.X;
        document.SceneCameraPositionY = _editorCameraPosition.Y;
        document.SceneCameraRotation = 0;
        document.SceneCameraSize = _editorCameraSize;
        document.ShowHierarchy = _editorPanels.ContainsKey(_hierarchy);
        document.ShowInspector = _editorPanels.ContainsKey(_inspector);
        document.ShowSceneView = _editorPanels.ContainsKey(_sceneView);
        document.ShowGameView = _editorPanels.ContainsKey(_gameView);
        document.ShowProject = _editorPanels.ContainsKey(_project);
        document.ShowConsole = _editorPanels.ContainsKey(_console);
        document.DockRoot = _dock.CaptureLayout();
        document.MaximizedPanelId = _dock.MaximizedPanelId;
        document.FocusedWindowId = EditorWindow.focusedWindow is { saveToLayout: true } focused
            ? focused.PersistentId : null;
        document.Windows = [];
        foreach (var window in _editorPanels.Keys.Where(window => window.saveToLayout))
            document.Windows.Add(CaptureWindow(window, true));
        foreach (var presentation in _windowLayer.Presentations)
        {
            if (!presentation.Window.saveToLayout ||
                presentation.State is EditorWindowState.Pop or EditorWindowState.Modal)
                continue;
            document.Windows.Add(CaptureWindow(presentation.Window, false));
        }
        foreach (var presentation in _nativeFloatingWindows.Values)
        {
            if (!presentation.Window.saveToLayout) continue;
            presentation.Window.position = presentation.ScreenBounds;
            document.Windows.Add(CaptureWindow(presentation.Window, false));
        }
        return document;
    }

    private static EditorWindowLayoutDocument CaptureWindow(EditorWindow window, bool docked)
    {
        var position = window.position.position;
        var size = window.position.size;
        return new EditorWindowLayoutDocument
        {
            Id = window.PersistentId,
            TypeName = window.GetType().AssemblyQualifiedName ?? window.GetType().FullName ?? window.GetType().Name,
            State = window.windowState.ToString(),
            Docked = docked,
            X = (float)position.x,
            Y = (float)position.y,
            Width = (float)size.x,
            Height = (float)size.y
        };
    }

    private void ApplyLayout(EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _project.ApplyLayout(document);
        _editorCameraPosition = new NVector2(document.SceneCameraPositionX, document.SceneCameraPositionY);
        _editorCameraSize = Math.Clamp(document.SceneCameraSize, 0.01f, 100000f);
        _mainWindow.SetMaximized(false);
        _mainWindow.Move(document.WindowX, document.WindowY);
        _mainWindow.Resize(document.WindowWidth, document.WindowHeight);
        if (document.WindowMaximized) _mainWindow.SetMaximized(true);
        if (document.Version < 2 || document.DockRoot is null || document.Windows.Count == 0) return;

        var existing = _editorPanels.Keys
            .Concat(_windowLayer.Presentations.Select(item => item.Window))
            .Concat(_nativeFloatingWindows.Keys)
            .GroupBy(window => window.PersistentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var desiredRecords = document.Windows
            .Where(record => Enum.TryParse<EditorWindowState>(record.State, true, out var state) &&
                             state is EditorWindowState.Normal or EditorWindowState.Aux)
            .ToDictionary(record => record.Id, StringComparer.Ordinal);
        foreach (var window in existing.Values.Where(window => !desiredRecords.ContainsKey(window.PersistentId))
                     .ToArray())
            CloseEditorWindow(window);

        var resolved = new Dictionary<string, (EditorWindow Window, EditorWindowLayoutDocument Record,
            EditorWindowState State)>(StringComparer.Ordinal);
        foreach (var record in desiredRecords.Values)
        {
            if (!Enum.TryParse<EditorWindowState>(record.State, true, out var state)) continue;
            var window = ResolveLayoutWindow(record, existing);
            if (window is null) continue;
            window.PersistentId = record.Id;
            window.position = new Rect((Fix64)record.X, (Fix64)record.Y,
                Fix64.Max(window.minSize.x, (Fix64)record.Width),
                Fix64.Max(window.minSize.y, (Fix64)record.Height));
            window.OpenInternal();
            resolved[record.Id] = (window, record, state);
        }

        foreach (var (_, item) in resolved)
        {
            _windowLayer.Remove(item.Window);
            if (_nativeFloatingWindows.TryGetValue(item.Window, out var native))
                RemoveNativeFloatingWindow(native, closeWindow: false);
        }
        var dockedWindows = resolved.Where(pair => pair.Value.Record.Docked &&
                                                   pair.Value.State == EditorWindowState.Normal)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Window, StringComparer.Ordinal);
        _editorPanels.Clear();
        foreach (var pair in _dock.RestoreLayout(document.DockRoot, dockedWindows))
        {
            pair.Key.windowState = EditorWindowState.Normal;
            pair.Key.docked = true;
            _editorPanels[pair.Key] = pair.Value;
        }
        _dock.RestoreMaximizedPanel(document.MaximizedPanelId);

        foreach (var item in resolved.Values.Where(item => !item.Record.Docked ||
                                                            item.State == EditorWindowState.Aux))
            ShowFloating(item.Window, item.State, positionIsScreenSpace: true);
        if (document.FocusedWindowId is { } focusedId && resolved.TryGetValue(focusedId, out var focused))
            focused.Window.Focus();
    }

    private static EditorWindow? ResolveLayoutWindow(EditorWindowLayoutDocument record,
        IReadOnlyDictionary<string, EditorWindow> existing)
    {
        if (existing.TryGetValue(record.Id, out var window)) return window;
        Type? type = null;
        try { type = Type.GetType(record.TypeName, throwOnError: false); }
        catch { }
        var fullName = record.TypeName.Split(',')[0].Trim();
        type ??= AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(candidate => candidate is not null);
        if (type is null || type.IsAbstract || !typeof(EditorWindow).IsAssignableFrom(type))
        {
            Debug.LogWarning($"Layout window type '{record.TypeName}' is unavailable.");
            return null;
        }
        return (EditorWindow)ScriptableObject.CreateInstance(type);
    }

    private void ShowEditorWindow(EditorWindow window)
    {
        var requestedState = window.ConsumeRequestedState();
        if (_editorPanels.TryGetValue(window, out var panel))
        {
            if (requestedState == EditorWindowState.Normal)
            {
                _dock.Show(panel.Id);
                window.windowState = requestedState;
                window.FocusInternal();
                return;
            }
            _editorPanels.Remove(window);
            _dock.Remove(panel.Id);
        }
        else if (_windowLayer.TryGet(window, out var floating))
        {
            if (floating.State == requestedState ||
                floating.State == EditorWindowState.Normal && requestedState == EditorWindowState.Normal)
            {
                _windowLayer.Focus(window);
                return;
            }
            _windowLayer.Remove(window);
        }
        else if (_nativeTransientOwners.TryGetValue(window, out var transientOwner))
        {
            if (requestedState == EditorWindowState.Pop && transientOwner.ContainsTransient(window))
            {
                transientOwner.FocusTransient(window);
                return;
            }
            transientOwner.RemoveTransient(window);
            _nativeTransientOwners.Remove(window);
        }
        else if (_nativeFloatingWindows.TryGetValue(window, out var native))
        {
            if (native.State == requestedState ||
                native.State == EditorWindowState.Normal && requestedState == EditorWindowState.Normal)
            {
                native.Focus();
                return;
            }
            RemoveNativeFloatingWindow(native, closeWindow: false);
        }

        window.OpenInternal();
        var id = string.IsNullOrWhiteSpace(window.PersistentId)
            ? $"EditorWindow:{window.GetType().Assembly.GetName().Name}:{window.GetType().FullName}" : window.PersistentId;
        window.PersistentId = id;
        window.windowState = requestedState;
        if (requestedState == EditorWindowState.Normal)
        {
            _editorPanels[window] = _dock.Add(id, window, DockArea.Center, true); window.docked = true;
        }
        else ShowFloating(window, requestedState);
        window.FocusInternal();
    }

    private void FocusEditorWindow(EditorWindow window)
    {
        if (_editorPanels.TryGetValue(window, out var panel))
        {
            _dock.Show(panel.Id);
            window.FocusInternal();
            _mainWindow.Focus();
            return;
        }
        if (_nativeFloatingWindows.TryGetValue(window, out var native))
        {
            native.Focus();
            return;
        }
        if (_nativeTransientOwners.TryGetValue(window, out var transientOwner) &&
            transientOwner.FocusTransient(window))
            return;
        if (_windowLayer.Focus(window))
        {
            _mainWindow.Focus();
            return;
        }
        window.FocusInternal();
    }

    private void ShowFloating(EditorWindow window, EditorWindowState state,
        bool positionIsScreenSpace = false)
    {
        if (state == EditorWindowState.Modal)
        {
            CloseTransientMenus();
            foreach (var native in _nativeFloatingWindows.Values) native.CancelTransientUi();
        }
        if (state is EditorWindowState.Normal or EditorWindowState.Aux)
        {
            if (!positionIsScreenSpace)
            {
                var screenPosition = _mainWindow.GUIToScreen(window.position.position,
                    GUIUtility.pixelsPerPoint);
                var clientSize = ImGuiNativeWindow.GUIToClient(window.position.size,
                    GUIUtility.pixelsPerPoint);
                window.position = new Rect(screenPosition.x, screenPosition.y,
                    clientSize.x, clientSize.y);
            }
            ShowNativeFloating(window, state);
            return;
        }
        if (state == EditorWindowState.Pop &&
            TryGetNativeOwner(EditorWindow.currentDrawingWindow ?? EditorWindow.focusedWindow,
                out var transientOwner))
        {
            ShowNativeTransient(transientOwner, window, state);
            return;
        }
        _windowLayer.Show(window, state);
        _windowLayer.Focus(window);
        if (state == EditorWindowState.Modal) _mainWindow.Focus();
    }

    private void CloseEditorWindow(EditorWindow window)
    {
        if (_editorPanels.Remove(window, out var panel)) _dock.Remove(panel.Id);
        if (_nativeTransientOwners.Remove(window, out var transientOwner))
        {
            transientOwner.RemoveTransient(window);
            window.CloseInternal();
            return;
        }
        _windowLayer.Remove(window);
        if (_nativeFloatingWindows.ContainsKey(window))
        {
            _pendingNativeCloses.Add(window);
            return;
        }
        window.CloseInternal();
    }

    private void QueueUndock(ImGuiDockPanel panel, Vector2 pointer)
    {
        if (!_editorPanels.TryGetValue(panel.Window, out var registered) || !ReferenceEquals(panel, registered))
            return;
        _pendingUndocks.Enqueue(new PendingUndock(panel, pointer));
    }

    private void ProcessPendingUndocks()
    {
        while (_pendingUndocks.TryDequeue(out var pending))
        {
            var window = pending.Panel.Window;
            if (!_editorPanels.Remove(window, out var panel) || !ReferenceEquals(panel, pending.Panel)) continue;
            var dockedBounds = panel.Group?.Bounds ?? window.position;
            _dock.Remove(panel.Id);
            var screenPoint = _mainWindow.GUIToScreen(pending.CanvasPosition, GUIUtility.pixelsPerPoint);
            var dockedScreenOrigin = _mainWindow.GUIToScreen(dockedBounds.position,
                GUIUtility.pixelsPerPoint);
            var clientSize = ImGuiNativeWindow.GUIToClient(new Vector2(
                    Fix64.Max(window.minSize.x, dockedBounds.width),
                    Fix64.Max(window.minSize.y, dockedBounds.height)),
                GUIUtility.pixelsPerPoint);
            window.position = new Rect(dockedScreenOrigin.x, dockedScreenOrigin.y,
                clientSize.x, clientSize.y);
            ShowNativeFloating(window, EditorWindowState.Normal);
            if (_nativeFloatingWindows.TryGetValue(window, out var presentation) &&
                presentation.LeftMouseButtonPressed)
                _nativeFloatingCarries[presentation] = screenPoint - presentation.ScreenPosition;
        }
    }

    private void DockFloatingWindow(EditorWindow window, Vector2 pointer)
    {
        if (!_windowLayer.Remove(window)) return;
        window.windowState = EditorWindowState.Normal;
        window.docked = true;
        _editorPanels[window] = _dock.DockExternal(window.PersistentId, window, pointer);
        _dock.SetExternalDragPoint(null);
    }

    private void ShowNativeFloating(EditorWindow window, EditorWindowState state)
    {
        if (_nativeFloatingWindows.TryGetValue(window, out var existing))
        {
            existing.Focus();
            return;
        }

        var presentation = new NativeFloatingEditorWindow(window, state, window.position);
        presentation.CloseRequested += QueueNativeClose;
        presentation.DockRequested += QueueNativeDock;
        presentation.FocusChanged += OnNativeWindowFocusChanged;
        presentation.TransientClosed += OnNativeTransientClosed;
        presentation.RenderBackground = (device, width, height) =>
            RenderNativeWindowBackground(presentation, device, width, height);
        _nativeFloatingWindows.Add(window, presentation);
        _nativeDockTrackers.Add(presentation, new NativeWindowDockTracker());
        window.windowState = state;
        window.docked = false;
        try
        {
            presentation.Initialize();
            _nativeDockTrackers[presentation].Reset(
                presentation.ScreenPosition, presentation.ScreenBounds.size);
            presentation.Focus();
        }
        catch
        {
            RemoveNativeFloatingWindow(presentation, closeWindow: false);
            throw;
        }
    }

    private void QueueNativeClose(NativeFloatingEditorWindow presentation) =>
        _pendingNativeCloses.Add(presentation.Window);

    private void QueueNativeDock(NativeFloatingEditorWindow presentation, Vector2 screenPoint) =>
        _pendingNativeDocks.Enqueue((presentation, screenPoint, false));

    private void OnNativeTransientClosed(NativeFloatingEditorWindow presentation, EditorWindow window)
    {
        if (!_nativeTransientOwners.TryGetValue(window, out var owner) ||
            !ReferenceEquals(owner, presentation)) return;
        _nativeTransientOwners.Remove(window);
        window.CloseInternal();
    }

    private void PumpNativeFloatingWindows()
    {
        var previewActive = false;
        foreach (var presentation in _nativeFloatingWindows.Values.ToArray())
        {
            if (!_nativeDockTrackers.TryGetValue(presentation, out var tracker)) continue;
            presentation.InputBlocked = _windowLayer.HasModal;
            var hasPointer = ImGuiNativeWindow.TryGetPointerScreenPosition(out var screenPointer);
            var dockPoint = hasPointer ? MainDockPoint(screenPointer) : null;
            var pointerDown = presentation.LeftMouseButtonPressed;

            if (_nativeFloatingCarries.TryGetValue(presentation, out var pointerOffset))
            {
                if (hasPointer && pointerDown)
                {
                    presentation.Move(screenPointer - pointerOffset);
                    if (dockPoint is not null)
                    {
                        _dock.SetExternalDragPoint(dockPoint);
                        previewActive = true;
                    }
                }
                else if (!pointerDown)
                {
                    _nativeFloatingCarries.Remove(presentation);
                    tracker.Reset(presentation.ScreenPosition, presentation.ScreenBounds.size);
                    if (hasPointer && dockPoint is not null)
                        _pendingNativeDocks.Enqueue((presentation, screenPointer, true));
                }

                presentation.Pump();
                if (presentation.IsClosing) _pendingNativeCloses.Add(presentation.Window);
                continue;
            }

            if (presentation.State != EditorWindowState.Normal || presentation.InputBlocked)
            {
                tracker.Reset(presentation.ScreenPosition, presentation.ScreenBounds.size);
                presentation.Pump();
                if (presentation.IsClosing) _pendingNativeCloses.Add(presentation.Window);
                continue;
            }
            var pointerOperation = pointerDown && tracker.NeedsPointerOperation
                ? presentation.PointerOperation
                : NativeWindowPointerOperation.Unknown;
            var update = tracker.Observe(presentation.ScreenPosition, presentation.ScreenBounds.size,
                pointerDown, dockPoint, pointerOperation);
            if (update.Phase == NativeDockDragPhase.Preview)
            {
                _dock.SetExternalDragPoint(update.DockPoint);
                previewActive = true;
            }
            else if (update.Phase == NativeDockDragPhase.Drop && update.DockPoint is { } point)
                _pendingNativeDocks.Enqueue((presentation, MainScreenPoint(point), true));

            presentation.Pump();
            if (presentation.IsClosing) _pendingNativeCloses.Add(presentation.Window);
        }

        if (!previewActive) _dock.SetExternalDragPoint(null);
        ProcessPendingNativeCloses();
        ProcessPendingNativeDocks();
    }

    private void ProcessPendingNativeCloses()
    {
        foreach (var window in _pendingNativeCloses.ToArray())
        {
            _pendingNativeCloses.Remove(window);
            if (!_nativeFloatingWindows.TryGetValue(window, out var presentation)) continue;
            RemoveNativeFloatingWindow(presentation, closeWindow: true);
        }
    }

    private void ProcessPendingNativeDocks()
    {
        while (_pendingNativeDocks.TryDequeue(out var pending))
        {
            if (!_nativeFloatingWindows.TryGetValue(pending.Presentation.Window, out var current) ||
                !ReferenceEquals(current, pending.Presentation)) continue;
            var dockPoint = MainDockPoint(pending.ScreenPoint);
            if (dockPoint is null && pending.RequireTarget) continue;
            var window = pending.Presentation.Window;
            RemoveNativeFloatingWindow(pending.Presentation, closeWindow: false);
            window.windowState = EditorWindowState.Normal;
            window.docked = true;
            _editorPanels[window] = _dock.DockExternal(window.PersistentId, window,
                dockPoint ?? _dockBounds.center);
            _dock.SetExternalDragPoint(null);
            window.FocusInternal();
            _mainWindow.Focus();
        }
    }

    private void RemoveNativeFloatingWindow(NativeFloatingEditorWindow presentation, bool closeWindow)
    {
        presentation.CloseRequested -= QueueNativeClose;
        presentation.DockRequested -= QueueNativeDock;
        presentation.FocusChanged -= OnNativeWindowFocusChanged;
        presentation.TransientClosed -= OnNativeTransientClosed;
        foreach (var transient in _nativeTransientOwners
                     .Where(pair => ReferenceEquals(pair.Value, presentation))
                     .Select(pair => pair.Key).ToArray())
        {
            presentation.RemoveTransient(transient);
            _nativeTransientOwners.Remove(transient);
            transient.CloseInternal();
        }
        _nativeFloatingWindows.Remove(presentation.Window);
        _nativeDockTrackers.Remove(presentation);
        _nativeFloatingCarries.Remove(presentation);
        _pendingNativeCloses.Remove(presentation.Window);
        if (presentation.GraphicsDevice is { } device && _sceneRenderers.Remove(device, out var renderer))
            renderer.Dispose();
        presentation.Dispose();
        if (closeWindow) presentation.Window.CloseInternal();
    }

    private bool TryGetNativeOwner(EditorWindow? source, out NativeFloatingEditorWindow owner)
    {
        if (source is not null)
        {
            if (_nativeFloatingWindows.TryGetValue(source, out owner!)) return true;
            if (_nativeTransientOwners.TryGetValue(source, out owner!)) return true;
        }
        owner = null!;
        return false;
    }

    private void ShowNativeTransient(NativeFloatingEditorWindow owner, EditorWindow window,
        EditorWindowState state)
    {
        _nativeTransientOwners[window] = owner;
        owner.ShowTransient(window, state);
    }

    private Vector2? MainDockPoint(Vector2 screenPoint)
    {
        var point = _mainWindow.ScreenToGUI(screenPoint, GUIUtility.pixelsPerPoint);
        return _dockBounds.Contains(point) && _dock.CanDockAt(point) ? point : null;
    }

    private Vector2 MainScreenPoint(Vector2 dockPoint) =>
        _mainWindow.GUIToScreen(dockPoint, GUIUtility.pixelsPerPoint);

    private string ResolveAssetPath(string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) :
        _workspace.ResolveInside(path.Replace('\\', '/'));
    private static EditorAssetRecord ToRecord(AssetRecord record) =>
        new(record.Guid, record.AssetPath, record.SourcePath, record.AssetType, record.IsDirectory);

    private readonly record struct MenuEntry(string Label, bool Enabled, Action? Action, bool Checked = false);

    private sealed class ImGuiScrollRegion
    {
        private Vector2 _position;
        private Fix64 _contentHeight = 1;
        private Fix64 _viewportHeight = 1;
        internal Vector2 position { get => _position; set => _position = value; }
        public void Begin(Fix64 minimumContentWidth = default)
        {
            var viewport = GUILayoutUtility.GetControlRect(60, GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            _viewportHeight = Fix64.Max(1, viewport.height);
            var contentWidth = Fix64.Max(Fix64.Max(1, viewport.width - 11), minimumContentWidth);
            var contentHeight = Fix64.Max(viewport.height, _contentHeight);
            _position = GUI.BeginScrollView(viewport, _position,
                new Rect(0, 0, contentWidth, contentHeight));
            GUILayout.BeginContainer(new Rect(0, 0, contentWidth, contentHeight));
        }

        public bool Reveal(Rect contentRect)
        {
            var nextY = _position.y;
            if (contentRect.y < nextY)
                nextY = contentRect.y;
            else if (contentRect.yMax > nextY + _viewportHeight)
                nextY = contentRect.yMax - _viewportHeight;
            nextY = Fix64.Max(0, nextY);
            if (nextY == _position.y) return false;
            _position = new Vector2(_position.x, nextY);
            return true;
        }

        public void End()
        {
            _contentHeight = Fix64.Max(1, GUILayout.CurrentContentHeight + 4);
            GUILayout.EndContainer();
            GUI.EndScrollView();
        }
    }

    private static void DrawWindowToolbarBackground()
    {
        var height = EditorStyles.toolbar.fixedHeight + 8;
        GUI.Box(new Rect(0, 0, GUIUtility.currentViewWidth, height), GUIContent.none,
            EditorStyles.toolbar);
    }

    private static GUIStyle TreeRowStyle(bool selected) =>
        selected ? EditorStyles.treeViewRowSelected : EditorStyles.treeViewRow;

    private sealed class ImGuiHierarchyWindow(GpuEditorApplication app) : EditorWindow
    {
        private GpuEditorApplication Application => app;
        private static readonly Guid DontDestroyOnLoadId = new("D0D0D0D0-0000-0000-0000-000000000001");
        private string _search = string.Empty;
        private readonly HashSet<Guid> _expanded = [];
        private readonly HashSet<Guid> _expandedScenes = [];
        private readonly HashSet<Guid> _knownScenes = [];
        private readonly ImGuiScrollRegion _scroll = new();
        private Guid? _renamingId;
        private string _renameValue = string.Empty;
        private Guid? _dragCandidateId;
        private Guid? _draggedId;
        private Guid? _dropTargetId;
        private Vector2 _dragStart;
        private bool _showRowActions = true;
        private Guid? _pendingRevealId;
        private readonly TreeViewState<Guid> _treeState = new();
        private HierarchyTreeView? _treeView;
        private bool _treeDirty = true;
        public ImGuiHierarchyWindow() : this(null!) { }
        protected override void OnGUI()
        {
            if (_draggedId is not null && !DragAndDrop.isDragging) ClearDrag();
            HandleKeyboard();
            UpdateResponsiveState();
            var toolbarHeight = EditorStyles.toolbar.fixedHeight;
            GUI.Box(new Rect(0, 0, GUIUtility.currentViewWidth, toolbarHeight),
                GUIContent.none, EditorStyles.toolbar);
            GUILayout.BeginHorizontal(GUILayout.Height(toolbarHeight));
            var createPressed = EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Add,
                "Create GameObject", GUILayout.Width(24));
            var createRect = GUILayoutUtility.GetLastRect();
            var createMenuPressed = GUILayout.Button(new GUIContent(string.Empty,
                    EditorBuiltinIcons.Toolbar.FoldoutOpen, "Create GameObject menu"),
                EditorStyles.hierarchyAction, GUILayout.Width(16), GUILayout.Height(toolbarHeight));
            var createMenuRect = GUILayoutUtility.GetLastRect();
            if (createPressed || createMenuPressed)
                ShowCreateMenu(createPressed ? createRect : createMenuRect);
            _search = EditorToolbar.SearchField(_search, "All", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            var treeTop = GUILayoutUtility.GetLastRect().yMax;
            var treeRect = new Rect(0, treeTop, GUIUtility.currentViewWidth,
                Fix64.Max(1, GUIUtility.currentViewHeight - treeTop));
            DrawTreeView(treeRect);
            if ((Event.current.type is EventType.MouseUp or EventType.DragPerform or EventType.DragExited) &&
                _draggedId is not null) ClearDrag();
        }

        protected override void OnHierarchyChange()
        {
            _treeDirty = true;
            Repaint();
        }

        private void DrawTreeView(Rect rect)
        {
            _treeView ??= new HierarchyTreeView(this, _treeState);
            if (_treeDirty)
            {
                _treeState.searchString = _search;
                _treeState.expandedIDs = [.. _expandedScenes, .. _expanded];
                _treeView.Reload();
                _treeDirty = false;
            }
            else
                _treeView.searchString = _search;
            _treeView.SetExpanded([.. _expandedScenes, .. _expanded]);
            _treeView.SetSelection(app.Selected is { } selected ? [selected.Id] : []);
            if (_pendingRevealId is { } reveal)
            {
                _treeView.FrameItem(reveal);
                _pendingRevealId = null;
            }
            _treeView.OnGUI(rect);
            SynchronizeExpandedState();
            _scroll.position = _treeState.scrollPos;
        }

        private void SynchronizeExpandedState()
        {
            if (_treeView is null) return;
            var expanded = _treeView.GetExpanded().ToHashSet();
            _expandedScenes.RemoveWhere(id => !expanded.Contains(id));
            _expanded.RemoveWhere(id => !expanded.Contains(id));
            foreach (var id in expanded)
            {
                if (_treeView.IsScene(id)) _expandedScenes.Add(id);
                else _expanded.Add(id);
            }
        }

        private HierarchyTreeItem BuildTreeRoot()
        {
            var root = new HierarchyTreeItem(Guid.Empty, -1, "Hierarchy", HierarchyNodeKind.Root);
            if (app._prefabStage is not null)
                root.AddChild(BuildSceneTree(app.Scene, null, app.Scene.name, true, app._dirty));
            else
                foreach (var entry in app.OpenSceneEntries)
                    root.AddChild(BuildSceneTree(entry.Scene, entry, entry.DisplayName, entry.IsLoaded,
                        entry.IsDirty));

            if (app._playing)
            {
                var persistentRoots = app.OpenSceneEntries.Where(item => item.IsLoaded)
                    .SelectMany(item => item.Scene.rootGameObjects)
                    .Where(item => item.isDontDestroyOnLoad).ToArray();
                if (persistentRoots.Length > 0)
                {
                    if (_knownScenes.Add(DontDestroyOnLoadId)) _expandedScenes.Add(DontDestroyOnLoadId);
                    var persistent = new HierarchyTreeItem(DontDestroyOnLoadId, 0, "DontDestroyOnLoad",
                        HierarchyNodeKind.VirtualScene) { Loaded = true };
                    foreach (var item in persistentRoots) persistent.AddChild(BuildGameObjectTree(item));
                    root.AddChild(persistent);
                }
            }
            return root;
        }

        private HierarchyTreeItem BuildSceneTree(Scene scene, EditorOpenScene? entry, string displayName,
            bool loaded, bool dirty)
        {
            if (_knownScenes.Add(scene.Id) && loaded) _expandedScenes.Add(scene.Id);
            var node = new HierarchyTreeItem(scene.Id, 0, displayName, HierarchyNodeKind.Scene)
            {
                Scene = scene,
                SceneEntry = entry,
                Loaded = loaded,
                Dirty = dirty
            };
            if (!loaded) return node;
            foreach (var item in scene.rootGameObjects.Where(item => !app._playing || !item.isDontDestroyOnLoad))
                node.AddChild(BuildGameObjectTree(item));
            return node;
        }

        private static HierarchyTreeItem BuildGameObjectTree(GameObject item)
        {
            var node = new HierarchyTreeItem(item.Id, 0, item.name, HierarchyNodeKind.GameObject)
                { GameObject = item };
            foreach (var child in item.transform.children) node.AddChild(BuildGameObjectTree(child.gameObject));
            return node;
        }

        private void DrawHierarchyTreeRow(TreeView<Guid>.RowGUIArgs args, HierarchyTreeView tree)
        {
            if (args.item is not HierarchyTreeItem item) return;
            var rowRect = args.rowRect;
            var rowHeight = rowRect.height;
            if (item.Kind is HierarchyNodeKind.Scene or HierarchyNodeKind.VirtualScene)
            {
                DrawSceneTreeRow(item, rowRect, tree);
                return;
            }
            if (item.GameObject is not { } gameObject) return;

            if (!args.selected)
                GUI.Box(rowRect, GUIContent.none, EditorStyles.treeViewRow);
            if (_dropTargetId == gameObject.Id)
                GUI.Box(rowRect, GUIContent.none, EditorStyles.treeViewRowSelected);
            HandleDrag(gameObject, rowRect);

            const int sceneStateButtonWidth = 18;
            const int sceneStateActionWidth = sceneStateButtonWidth * 2;
            var treeLeft = rowRect.x + sceneStateActionWidth;
            const int minimumLabelWidth = 13;
            var objectDepth = Math.Max(0, item.depth - 1);
            var desiredFoldoutX = treeLeft + objectDepth * 14;
            var maximumFoldoutX = Fix64.Max(treeLeft, rowRect.xMax - 18 - minimumLabelWidth);
            var foldoutRect = new Rect(Fix64.Min(desiredFoldoutX, maximumFoldoutX), rowRect.y, 18, rowHeight);
            if (item.hasChildren)
            {
                var expanded = tree.IsExpanded(item.id);
                var foldoutIcon = expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen :
                    EditorBuiltinIcons.Toolbar.FoldoutClosed;
                if (GUI.Button(foldoutRect,
                        new GUIContent(string.Empty, foldoutIcon, expanded ? "Collapse" : "Expand"),
                        EditorStyles.foldout))
                    tree.SetItemExpanded(item.id, !expanded);
            }

            var itemLabelRect = new Rect(foldoutRect.xMax, rowRect.y,
                Fix64.Max(1, rowRect.xMax - foldoutRect.xMax), rowHeight);
            if (_renamingId == gameObject.Id)
            {
                var commit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                var cancel = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
                GUI.SetNextControlName("HierarchyRename");
                _renameValue = GUI.TextField(itemLabelRect, _renameValue);
                if (commit) CommitRename(gameObject);
                else if (cancel) CancelRename();
            }
            else
            {
                var sceneHidden = SceneVisibilityManager.instance.IsHidden(gameObject);
                var style = args.selected ? EditorStyles.hierarchyRowSelected :
                    gameObject.activeInHierarchy && !sceneHidden
                        ? EditorStyles.hierarchyRow
                        : EditorStyles.hierarchyRowInactive;
                var icon = PrefabUtility.IsPartOfPrefabInstance(gameObject)
                    ? EditorBuiltinIcons.Assets.Prefab : EditorBuiltinIcons.Components.GameObject;
                var visibleIcon = itemLabelRect.width >= 28 ? icon : string.Empty;
                GUI.Label(itemLabelRect, new GUIContent(gameObject.name, visibleIcon, string.Empty), style);
            }

            var sceneVisibility = SceneVisibilityManager.instance;
            var hidden = sceneVisibility.IsHidden(gameObject);
            var visibilityRect = new Rect(rowRect.x, rowRect.y, sceneStateButtonWidth, rowHeight);
            if (GUI.Button(visibilityRect, new GUIContent(string.Empty,
                    hidden ? EditorBuiltinIcons.Toolbar.Hidden : EditorBuiltinIcons.Toolbar.Visible,
                    hidden ? $"Show {gameObject.name} in Scene view" : $"Hide {gameObject.name} in Scene view"),
                    EditorStyles.hierarchyAction))
                sceneVisibility.ToggleVisibility(gameObject, includeDescendants: true);

            var pickingDisabled = sceneVisibility.IsPickingDisabled(gameObject);
            var pickingRect = new Rect(visibilityRect.xMax, rowRect.y, sceneStateButtonWidth, rowHeight);
            if (GUI.Button(pickingRect, new GUIContent(string.Empty,
                    pickingDisabled ? EditorBuiltinIcons.Toolbar.Lock : EditorBuiltinIcons.Toolbar.Unlock,
                    pickingDisabled ? $"Enable Scene picking for {gameObject.name}" :
                        $"Disable Scene picking for {gameObject.name}"), EditorStyles.hierarchyAction))
                sceneVisibility.TogglePicking(gameObject, includeDescendants: true);
        }

        private void DrawSceneTreeRow(HierarchyTreeItem item, Rect rowRect, HierarchyTreeView tree)
        {
            var scene = item.Scene;
            var active = scene is not null && ReferenceEquals(app.Scene, scene);
            GUI.Box(rowRect, GUIContent.none,
                active ? EditorStyles.hierarchySceneHeaderActive : EditorStyles.hierarchySceneHeader);
            GUI.Box(new Rect(rowRect.x, Fix64.Max(rowRect.y, rowRect.yMax - 1), rowRect.width, 1),
                GUIContent.none, EditorStyles.separator);
            if (item.SceneEntry is not null) HandleSceneDrop(item.SceneEntry, rowRect);

            var foldoutRect = new Rect(rowRect.x + 4, rowRect.y, 18, rowRect.height);
            if (item.hasChildren)
            {
                var expanded = tree.IsExpanded(item.id);
                var icon = expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen :
                    EditorBuiltinIcons.Toolbar.FoldoutClosed;
                if (GUI.Button(foldoutRect, new GUIContent(string.Empty, icon,
                            expanded ? "Collapse" : "Expand"), EditorStyles.foldout))
                    tree.SetItemExpanded(item.id, !expanded);
            }

            var showAction = item.SceneEntry is not null && _showRowActions;
            var labelRect = new Rect(foldoutRect.xMax, rowRect.y,
                Fix64.Max(1, rowRect.xMax - foldoutRect.xMax - (showAction ? 22 : 0)), rowRect.height);
            var suffix = item.Loaded ? item.Dirty ? " *" : string.Empty : " (Not Loaded)";
            var sceneIcon = labelRect.width >= 28 ? EditorBuiltinIcons.Assets.Scene : string.Empty;
            var tooltip = string.IsNullOrWhiteSpace(item.SceneEntry?.AssetPath)
                ? item.displayName : item.SceneEntry.AssetPath;
            var style = item.Loaded
                ? active ? EditorStyles.hierarchySceneHeaderActive : EditorStyles.hierarchySceneHeader
                : EditorStyles.hierarchyRowInactive;
            GUI.Label(labelRect, new GUIContent(item.displayName + suffix, sceneIcon, tooltip), style);

            if (!showAction) return;
            var moreRect = new Rect(rowRect.xMax - 22, rowRect.y, 22, rowRect.height);
            if (GUI.Button(moreRect, new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.More,
                    $"{item.displayName} options"), EditorStyles.hierarchyAction) && scene is not null)
                ShowSceneMenu(item.SceneEntry!, scene, item.Loaded, moreRect);
        }

        private sealed class HierarchyTreeView(ImGuiHierarchyWindow owner, TreeViewState<Guid> state)
            : TreeView<Guid>(state)
        {
            protected override TreeViewItem<Guid> BuildRoot() => owner.BuildTreeRoot();

            protected override IList<TreeViewItem<Guid>> BuildRows(TreeViewItem<Guid> root)
            {
                var rows = new List<TreeViewItem<Guid>>();
                if (root.children is null) return rows;
                foreach (var child in root.children) AddVisible(child, rows);
                return rows;
            }

            private void AddVisible(TreeViewItem<Guid> item, ICollection<TreeViewItem<Guid>> rows)
            {
                if (hasSearch && !BranchMatches(item)) return;
                rows.Add(item);
                if (item.children is null || (!hasSearch && !IsExpanded(item.id))) return;
                foreach (var child in item.children) AddVisible(child, rows);
            }

            private bool BranchMatches(TreeViewItem<Guid> item) =>
                item.displayName.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                item.children?.Any(BranchMatches) == true;

            protected override bool CanChangeExpandedState(TreeViewItem<Guid> item) => false;
            protected override bool CanMultiSelect(TreeViewItem<Guid> item) => false;
            protected override bool CanRename(TreeViewItem<Guid> item) => false;
            protected override void RowGUI(RowGUIArgs args) => owner.DrawHierarchyTreeRow(args, this);

            protected override void SelectionChanged(IList<Guid> selectedIds)
            {
                var id = selectedIds.LastOrDefault();
                if (id == Guid.Empty) return;
                if (FindItem(id, rootItem) is not HierarchyTreeItem item) return;
                if (item.GameObject is { } gameObject) owner.Application.Select(gameObject);
                else SetSelection(owner.Application.Selected is { } selected ? [selected.Id] : []);
            }

            protected override void SingleClickedItem(Guid id)
            {
                if (FindItem(id, rootItem) is HierarchyTreeItem
                    { Kind: HierarchyNodeKind.Scene, Loaded: true, Scene: { } scene })
                    owner.Application.SetActiveEditorScene(scene);
            }

            protected override void DoubleClickedItem(Guid id)
            {
                if (FindItem(id, rootItem) is HierarchyTreeItem { GameObject: not null })
                    owner.Application.FrameSelectedInScene();
            }

            protected override void ContextClickedItem(Guid id)
            {
                if (FindItem(id, rootItem) is not HierarchyTreeItem item) return;
                if (item.GameObject is { } gameObject)
                {
                    owner.Application.Select(gameObject);
                    owner.ShowItemMenu(gameObject);
                }
                else if (item is { SceneEntry: not null, Scene: { } scene })
                    owner.ShowSceneMenu(item.SceneEntry, scene, item.Loaded);
            }

            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args) =>
                DragAndDrop.visualMode;

            protected override void ExpandedStateChanged() => owner.SynchronizeExpandedState();
            internal void SetItemExpanded(Guid id, bool expanded) => SetExpanded(id, expanded);
            internal bool IsScene(Guid id) => FindItem(id, rootItem) is HierarchyTreeItem
                { Kind: HierarchyNodeKind.Scene or HierarchyNodeKind.VirtualScene };
        }

        private sealed class HierarchyTreeItem(Guid id, int depth, string name, HierarchyNodeKind kind)
            : TreeViewItem<Guid>(id, depth, name)
        {
            internal HierarchyNodeKind Kind { get; } = kind;
            internal Scene? Scene { get; init; }
            internal EditorOpenScene? SceneEntry { get; init; }
            internal GameObject? GameObject { get; init; }
            internal bool Loaded { get; init; }
            internal bool Dirty { get; init; }
        }

        private enum HierarchyNodeKind { Root, Scene, VirtualScene, GameObject }

        private void DrawScene(Scene scene, EditorOpenScene? entry, string displayName, bool loaded, bool dirty)
        {
            var roots = loaded
                ? scene.rootGameObjects.Where(item => !app._playing || !item.isDontDestroyOnLoad).ToArray()
                : [];
            var matches = SearchMatches(displayName) || roots.Any(MatchesSearch);
            if (!matches) return;
            var hasChildren = loaded && roots.Length > 0;
            if (_knownScenes.Add(scene.Id) && loaded) _expandedScenes.Add(scene.Id);
            if (!_expandedScenes.Contains(scene.Id) && !string.IsNullOrWhiteSpace(_search))
                _expandedScenes.Add(scene.Id);
            var expanded = _expandedScenes.Contains(scene.Id);
            var rowHeight = EditorTreeViewGUI.rowHeight;
            var rowRect = GUILayoutUtility.GetControlRect(rowHeight, GUILayout.ExpandWidth(true));
            var activeScene = ReferenceEquals(app.Scene, scene);
            DrawSceneRowBackground(rowRect, activeScene);
            if (entry is not null) HandleSceneDrop(entry, rowRect);
            var foldoutRect = new Rect(rowRect.x + 4, rowRect.y, 18, rowHeight);
            DrawSceneFoldout(foldoutRect, scene.Id, hasChildren, expanded);
            var suffix = loaded ? dirty ? " *" : string.Empty : " (Not Loaded)";
            var showAction = entry is not null && _showRowActions;
            var sceneLabelRect = new Rect(foldoutRect.xMax, rowRect.y,
                Fix64.Max(1, rowRect.xMax - foldoutRect.xMax - (showAction ? 22 : 0)), rowHeight);
            var sceneLabelWidth = sceneLabelRect.width;
            var sceneIcon = sceneLabelWidth >= 28 ? EditorBuiltinIcons.Assets.Scene : string.Empty;
            var content = new GUIContent(displayName + suffix, sceneIcon,
                string.IsNullOrWhiteSpace(entry?.AssetPath) ? displayName : entry.AssetPath);
            var sceneStyle = loaded
                ? activeScene ? EditorStyles.hierarchySceneHeaderActive : EditorStyles.hierarchySceneHeader
                : EditorStyles.hierarchyRowInactive;
            if (GUI.Button(sceneLabelRect, content, sceneStyle) && loaded)
                app.SetActiveEditorScene(scene);
            var morePressed = false;
            var moreRect = default(Rect);
            if (showAction)
            {
                moreRect = new Rect(rowRect.xMax - 22, rowRect.y, 22, rowHeight);
                morePressed = GUI.Button(moreRect, new GUIContent(string.Empty,
                    EditorBuiltinIcons.Toolbar.More, $"{displayName} options"), EditorStyles.hierarchyAction);
            }

            if (entry is not null && morePressed)
                ShowSceneMenu(entry, scene, loaded, moreRect);
            if (entry is not null && Event.current.type == EventType.ContextClick &&
                rowRect.Contains(Event.current.mousePosition))
            {
                ShowSceneMenu(entry, scene, loaded);
                Event.current.Use();
            }

            if (expanded && loaded)
                foreach (var root in roots) DrawItem(root, 1);
        }

        private void DrawVirtualScene(string displayName, IReadOnlyList<GameObject> roots)
        {
            if (!SearchMatches(displayName) && !roots.Any(MatchesSearch)) return;
            if (_knownScenes.Add(DontDestroyOnLoadId)) _expandedScenes.Add(DontDestroyOnLoadId);
            var expanded = _expandedScenes.Contains(DontDestroyOnLoadId);
            var rowHeight = EditorTreeViewGUI.rowHeight;
            var rowRect = GUILayoutUtility.GetControlRect(rowHeight, GUILayout.ExpandWidth(true));
            DrawSceneRowBackground(rowRect, false);
            var foldoutRect = new Rect(rowRect.x + 4, rowRect.y, 18, rowHeight);
            DrawSceneFoldout(foldoutRect, DontDestroyOnLoadId, roots.Count > 0, expanded);
            var labelRect = new Rect(foldoutRect.xMax, rowRect.y,
                Fix64.Max(1, rowRect.xMax - foldoutRect.xMax), rowHeight);
            var labelWidth = labelRect.width;
            var sceneIcon = labelWidth >= 28 ? EditorBuiltinIcons.Assets.Scene : string.Empty;
            GUI.Label(labelRect, new GUIContent(displayName, sceneIcon,
                    "Objects preserved when a scene is loaded in Single mode"),
                EditorStyles.hierarchySceneHeader);
            if (expanded)
                foreach (var root in roots) DrawItem(root, 1);
        }

        private void DrawSceneFoldout(Rect rect, Guid id, bool hasChildren, bool expanded)
        {
            if (!hasChildren) return;
            var icon = expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen : EditorBuiltinIcons.Toolbar.FoldoutClosed;
            if (GUI.Button(rect, new GUIContent(string.Empty, icon, expanded ? "Collapse" : "Expand"),
                    EditorStyles.foldout))
            {
                if (!_expandedScenes.Add(id)) _expandedScenes.Remove(id);
            }
        }

        private bool SearchMatches(string value) => string.IsNullOrWhiteSpace(_search) ||
            value.Contains(_search, StringComparison.OrdinalIgnoreCase);

        private bool MatchesSearch(GameObject item) => SearchMatches(item.name) ||
            item.transform.children.Any(child => MatchesSearch(child.gameObject));

        private void DrawItem(GameObject item, int depth)
        {
            if (!MatchesSearch(item)) return;
            var rowHeight = EditorTreeViewGUI.rowHeight;
            var rowRect = GUILayoutUtility.GetControlRect(rowHeight, GUILayout.ExpandWidth(true));
            if (_pendingRevealId == item.Id)
            {
                if (_scroll.Reveal(rowRect)) Repaint();
                _pendingRevealId = null;
            }
            var selected = ReferenceEquals(app.Selected, item) || _dropTargetId == item.Id;
            DrawObjectRowBackground(rowRect, selected);
            HandleDrag(item, rowRect);
            var children = item.transform.children.ToArray();
            const int sceneStateButtonWidth = 18;
            const int sceneStateActionWidth = sceneStateButtonWidth * 2;
            var treeLeft = rowRect.x + sceneStateActionWidth;
            const int minimumLabelWidth = 13;
            var desiredFoldoutX = treeLeft + depth * 14;
            var maximumFoldoutX = Fix64.Max(treeLeft,
                rowRect.xMax - 18 - minimumLabelWidth);
            var foldoutRect = new Rect(Fix64.Min(desiredFoldoutX, maximumFoldoutX), rowRect.y, 18, rowHeight);
            if (children.Length > 0)
            {
                var isExpanded = _expanded.Contains(item.Id);
                var foldoutIcon = isExpanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen :
                    EditorBuiltinIcons.Toolbar.FoldoutClosed;
                if (GUI.Button(foldoutRect,
                        new GUIContent(string.Empty, foldoutIcon, isExpanded ? "Collapse" : "Expand"),
                        EditorStyles.foldout))
                {
                    if (!_expanded.Add(item.Id)) _expanded.Remove(item.Id);
                }
            }
            var itemIcon = PrefabUtility.IsPartOfPrefabInstance(item)
                ? EditorBuiltinIcons.Assets.Prefab : EditorBuiltinIcons.Components.GameObject;
            var itemLabelRect = new Rect(foldoutRect.xMax, rowRect.y,
                Fix64.Max(1, rowRect.xMax - foldoutRect.xMax), rowHeight);
            var itemLabelWidth = itemLabelRect.width;
            var visibleItemIcon = itemLabelWidth >= 28 ? itemIcon : string.Empty;
            if (_renamingId == item.Id)
            {
                var commit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                var cancel = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
                GUI.SetNextControlName("HierarchyRename");
                _renameValue = GUI.TextField(itemLabelRect, _renameValue);
                if (commit) CommitRename(item);
                else if (cancel) CancelRename();
            }
            else
            {
                var doubleClick = Event.current.rawType == EventType.MouseUp && Event.current.button == 0 &&
                                  Event.current.clickCount >= 2;
                var sceneHidden = SceneVisibilityManager.instance.IsHidden(item);
                var rowStyle = selected
                    ? EditorStyles.hierarchyRowSelected
                    : item.activeInHierarchy && !sceneHidden
                        ? EditorStyles.hierarchyRow
                        : EditorStyles.hierarchyRowInactive;
                var itemContent = new GUIContent(item.name, visibleItemIcon, string.Empty);
                if (GUI.Button(itemLabelRect, GUIContent.none, rowStyle))
                {
                    app.Select(item);
                    if (doubleClick) app.FrameSelectedInScene();
                }
                GUI.Label(itemLabelRect, itemContent, rowStyle);
            }

            var sceneVisibility = SceneVisibilityManager.instance;
            var hidden = sceneVisibility.IsHidden(item);
            var visibilityRect = new Rect(rowRect.x, rowRect.y, sceneStateButtonWidth, rowHeight);
            if (GUI.Button(visibilityRect, new GUIContent(string.Empty,
                    hidden ? EditorBuiltinIcons.Toolbar.Hidden : EditorBuiltinIcons.Toolbar.Visible,
                    hidden ? $"Show {item.name} in Scene view" : $"Hide {item.name} in Scene view"),
                    EditorStyles.hierarchyAction))
                sceneVisibility.ToggleVisibility(item, includeDescendants: true);

            var pickingDisabled = sceneVisibility.IsPickingDisabled(item);
            var pickingRect = new Rect(visibilityRect.xMax, rowRect.y, sceneStateButtonWidth, rowHeight);
            if (GUI.Button(pickingRect, new GUIContent(string.Empty,
                    pickingDisabled ? EditorBuiltinIcons.Toolbar.Lock : EditorBuiltinIcons.Toolbar.Unlock,
                    pickingDisabled
                        ? $"Enable Scene picking for {item.name}"
                        : $"Disable Scene picking for {item.name}"), EditorStyles.hierarchyAction))
                sceneVisibility.TogglePicking(item, includeDescendants: true);
            if (Event.current.type == EventType.ContextClick && rowRect.Contains(Event.current.mousePosition))
            {
                app.Select(item);
                ShowItemMenu(item);
                Event.current.Use();
            }
            if (_expanded.Contains(item.Id) || !string.IsNullOrWhiteSpace(_search))
                foreach (var child in children) DrawItem(child.gameObject, depth + 1);
        }

        private void ShowCreateMenu(Rect anchor)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Create Empty"), false, () => app.CreateGameObject());
            if (app.Selected is { } selected)
                menu.AddItem(new GUIContent("Create Empty Child"), false,
                    () => app.CreateGameObject(selected.transform));
            else menu.AddDisabledItem(new GUIContent("Create Empty Child"));
            menu.DropDown(anchor);
        }

        private void ShowSceneMenu(EditorOpenScene entry, Scene scene, bool loaded, Rect? anchor = null)
        {
            var menu = new GenericMenu();
            if (!loaded)
                menu.AddItem(new GUIContent("Load Scene"), false,
                    () => app.OpenEditorScene(entry.SourcePath, OpenSceneMode.Additive));
            if (loaded && !ReferenceEquals(app.Scene, scene))
                menu.AddItem(new GUIContent("Set Active Scene"), false, () => app.SetActiveEditorScene(scene));
            else menu.AddDisabledItem(new GUIContent("Set Active Scene"));
            if (loaded) menu.AddItem(new GUIContent("Save Scene"), false, () => app.SaveEditorScene(scene));
            else menu.AddDisabledItem(new GUIContent("Save Scene"));
            menu.AddItem(new GUIContent("Select Scene Asset"), false, () => app.PingSceneAsset(entry));
            menu.AddSeparator(string.Empty);
            var canClose = app.OpenSceneEntries.Count > 1 &&
                           app.OpenSceneEntries.Any(item => !ReferenceEquals(item, entry) && item.IsLoaded);
            if (canClose)
                menu.AddItem(new GUIContent("Close Scene"), false, () => app.CloseEditorScene(scene, true));
            else menu.AddDisabledItem(new GUIContent("Close Scene"));
            if (anchor is { } position) menu.DropDown(position);
            else menu.ShowAsContext();
        }

        private void ShowItemMenu(GameObject item, Rect? anchor = null)
        {
            var menu = new GenericMenu();
            app._menuItems.PopulateRoot(menu, "GameObject", item);
            app._menuItems.PopulateContext(menu, item);
            if (anchor is { } position) menu.DropDown(position);
            else menu.ShowAsContext();
        }

        private void DrawSceneRowBackground(Rect rowRect, bool active)
        {
            var fullRow = FullRowRect(rowRect);
            GUI.Box(fullRow, GUIContent.none,
                active ? EditorStyles.hierarchySceneHeaderActive : EditorStyles.hierarchySceneHeader);
            GUI.Box(new Rect(fullRow.x, Fix64.Max(fullRow.y, fullRow.yMax - 1), fullRow.width, 1),
                GUIContent.none, EditorStyles.separator);
        }

        private void DrawObjectRowBackground(Rect rowRect, bool selected)
        {
            var fullRow = FullRowRect(rowRect);
            if (!selected)
            {
                GUI.Box(fullRow, GUIContent.none, EditorStyles.treeViewRow);
                return;
            }

            var wasEnabled = GUI.enabled;
            try
            {
                GUI.enabled = hasFocus;
                GUI.Box(fullRow, GUIContent.none, EditorStyles.treeViewRowSelected);
            }
            finally
            {
                GUI.enabled = wasEnabled;
            }
        }

        private static Rect FullRowRect(Rect rowRect) =>
            new(-4, rowRect.y, rowRect.width + 12, rowRect.height);

        private void UpdateResponsiveState()
        {
            var width = GUI.visibleViewWidth;
            if (_showRowActions && width < 128) _showRowActions = false;
            else if (!_showRowActions && width > 144) _showRowActions = true;
        }

        internal void RevealSelection(GameObject item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.scene is { } scene) _expandedScenes.Add(scene.Id);
            if (item.isDontDestroyOnLoad) _expandedScenes.Add(DontDestroyOnLoadId);
            for (var parent = item.transform.parent; parent is not null; parent = parent.parent)
                _expanded.Add(parent.gameObject.Id);
            _pendingRevealId = item.Id;
            Repaint();
        }

        private void HandleKeyboard()
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (Event.current.keyCode == KeyCode.F2 && !EditorGUIUtility.editingTextField &&
                app.Selected is { } selected)
            {
                BeginRename(selected);
                Event.current.Use();
            }
            else if (Event.current.keyCode == KeyCode.Escape && _renamingId is not null)
            {
                CancelRename();
                Event.current.Use();
            }
        }

        internal void BeginRename(GameObject item)
        {
            _renamingId = item.Id;
            _renameValue = item.name;
            app.Select(item);
            GUI.FocusControl("HierarchyRename");
            Repaint();
        }

        private void CommitRename(GameObject item)
        {
            var value = _renameValue.Trim();
            if (value.Length > 0 && value != item.name)
            {
                Undo.RecordObject(item, "Rename GameObject");
                item.name = value;
                if (item.scene is { } scene) app.MarkDirty(scene);
                _treeDirty = true;
            }
            CancelRename();
        }

        private void CancelRename()
        {
            _renamingId = null;
            _renameValue = string.Empty;
            GUIUtility.ReleaseInputFocus();
            Repaint();
        }

        private void HandleDrag(GameObject item, Rect rowRect)
        {
            var current = Event.current;
            var updating = current.type is EventType.MouseDrag or EventType.DragUpdated;
            var performing = current.type is EventType.MouseUp or EventType.DragPerform;
            if (current.type == EventType.MouseDown && current.button == 0 && rowRect.Contains(current.mousePosition))
            {
                _dragCandidateId = item.Id;
                _dragStart = current.mousePosition;
                return;
            }
            if (current.type == EventType.MouseDrag && _dragCandidateId is { } candidate &&
                _draggedId is null && (current.mousePosition - _dragStart).sqrMagnitude >= 16)
            {
                if (app.FindGameObject(candidate) is not { } draggedItem)
                {
                    ClearDrag();
                    return;
                }
                _draggedId = candidate;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = [draggedItem];
                DragAndDrop.paths = [];
                DragAndDrop.SetGenericData("BEngine.Hierarchy.GameObjectId", candidate);
                DragAndDrop.StartDrag(draggedItem.name);
            }
            if (updating && _draggedId is { } dragged &&
                dragged != item.Id && rowRect.Contains(current.mousePosition) && CanDrop(dragged, item))
            {
                _dropTargetId = item.Id;
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                Repaint();
                return;
            }
            if (!performing || _draggedId is not { } sourceId ||
                _dropTargetId != item.Id || !rowRect.Contains(current.mousePosition)) return;
            if (app.FindGameObject(sourceId) is { } source && CanDrop(sourceId, item))
            {
                Undo.SetTransformParent(source.transform, item.transform, "Reparent GameObject");
                _expanded.Add(item.Id);
                app.Select(source);
                if (source.scene is { } scene) app.MarkDirty(scene);
                DragAndDrop.AcceptDrag();
            }
            ClearDrag();
            current.Use();
        }

        private bool CanDrop(Guid sourceId, GameObject target) => app.FindGameObject(sourceId) is { } source &&
            ReferenceEquals(source.scene, target.scene) && !ReferenceEquals(source, target) &&
            !target.transform.IsChildOf(source.transform);

        private void HandleSceneDrop(EditorOpenScene entry, Rect rowRect)
        {
            var current = Event.current;
            var updating = current.type is EventType.MouseDrag or EventType.DragUpdated;
            var performing = current.type is EventType.MouseUp or EventType.DragPerform;
            if (updating && _draggedId is { } dragged &&
                rowRect.Contains(current.mousePosition) && CanDropOnScene(dragged, entry.Scene))
            {
                _dropTargetId = entry.Scene.Id;
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                Repaint();
                return;
            }
            if (!performing || _draggedId is not { } sourceId ||
                _dropTargetId != entry.Scene.Id || !rowRect.Contains(current.mousePosition)) return;
            if (app.FindGameObject(sourceId) is { } source && CanDropOnScene(sourceId, entry.Scene))
            {
                var sourceScene = source.scene!;
                if (ReferenceEquals(sourceScene, entry.Scene))
                    Undo.SetTransformParent(source.transform, null, "Move GameObject To Scene Root");
                else
                {
                    Undo.MoveGameObjectToScene(source, entry.Scene, "Move GameObject To Scene");
                    app.MarkDirty(sourceScene);
                }
                app.MarkDirty(entry.Scene);
                app.Select(source);
                DragAndDrop.AcceptDrag();
            }
            ClearDrag();
            current.Use();
        }

        private bool CanDropOnScene(Guid sourceId, Scene target) =>
            app.FindGameObject(sourceId) is { scene: not null } source &&
            (ReferenceEquals(source.scene, target) || source.transform.parent is null);

        private void ClearDrag()
        {
            _dragCandidateId = null;
            _draggedId = null;
            _dropTargetId = null;
            Repaint();
        }
    }

    private sealed class ImGuiSceneWindow(GpuEditorApplication app) : EditorWindow
    {
        private const float AxisHandleLength = 72;
        private const float RotationHandleRadius = 56;
        private const float RotationHandleHitWidth = 8;
        private static readonly (string Icon, string Tooltip, Tool Tool)[] ToolbarTools =
        [
            (EditorBuiltinIcons.Toolbar.Move, "Move Tool (W)", Tool.Move),
            (EditorBuiltinIcons.Toolbar.Rotate, "Rotate Tool (E)", Tool.Rotate),
            (EditorBuiltinIcons.Toolbar.Scale, "Scale Tool (R)", Tool.Scale)
        ];

        private int _navigationButton = -1;
        private SceneHandleAxis _handleAxis;
        private Tool _handleTool;
        private Vector2 _handleStartMouse;
        private Vector2 _handleStartWorldPosition;
        private Fix64 _handleStartRotation;
        private Vector2 _handleStartScale;
        private NVector2 _handleWorldAxis;
        private NVector2 _handleScreenDirection;
        private float _handleWorldPerPixel;
        private Vector2 _handleStartScreenOrigin;
        private Vector2 _handleStartWorldCenter;
        private float _handleLastScreenAngle;
        private float _handleAccumulatedRotation;
        private Transform? _handleTarget;
        private SceneGizmoToolbarMode _gizmoToolbarMode = SceneGizmoToolbarMode.Wide;
        private bool _gizmoToolbarLayoutInitialized;
        private GameObject[] _lastPickCandidates = [];
        private GameObject? _lastPickedObject;
        private Vector2 _lastPickPosition;
        private int _lastPickIndex = -1;

        public ImGuiSceneWindow() : this(null!) { }
        protected override void OnLostFocus()
        {
            if (_handleTarget is null) return;
            GUIUtility.hotControl = 0;
            ResetHandleInteraction();
        }

        protected override void OnGUI()
        {
            var toolbarHeight = EditorStyles.toolbar.fixedHeight;
            var viewport = new Rect(0, toolbarHeight, GUIUtility.currentViewWidth,
                Fix64.Max(0, GUIUtility.currentViewHeight - toolbarHeight));
            HandleNavigation(viewport);
            DrawWindowToolbarBackground();
            DrawToolbar(toolbarHeight);
            GUI.BeginClip(viewport);
            try
            {
                DrawSelectionHandles(viewport);
                HandleObjectPicking(viewport);
            }
            finally
            {
                GUI.EndClip();
            }
        }

        private void DrawToolbar(Fix64 toolbarHeight)
        {
            var width = GUIUtility.currentViewWidth;
            if (Event.current.type == EventType.Layout || !_gizmoToolbarLayoutInitialized)
            {
                _gizmoToolbarMode = ResolveGizmoToolbarMode(
                    _gizmoToolbarMode, width, _gizmoToolbarLayoutInitialized);
                _gizmoToolbarLayoutInitialized = true;
            }

            const int margin = 3;
            const int toolWidth = 26;
            var gizmoWidth = _gizmoToolbarMode switch
            {
                SceneGizmoToolbarMode.Wide => (Fix64)94,
                SceneGizmoToolbarMode.Compact => 48,
                _ => 26
            };
            var gizmoLeft = Fix64.Max(margin, width - gizmoWidth - margin);
            var x = (Fix64)margin;
            foreach (var (icon, tooltip, tool) in ToolbarTools)
            {
                if (x + toolWidth > gizmoLeft - 2) break;
                var rect = new Rect(x, 0, toolWidth, toolbarHeight);
                if (EditorToolbar.Toggle(rect, app._tool == tool,
                        new GUIContent(string.Empty, icon, tooltip))) app._tool = tool;
                x += toolWidth;
            }

            DrawGizmoControls(gizmoLeft, toolbarHeight);
        }

        private void DrawGizmoControls(Fix64 left, Fix64 height)
        {
            var enabled = SceneGizmoVisibility.Enabled;
            var icon = enabled ? EditorBuiltinIcons.Toolbar.Visible : EditorBuiltinIcons.Toolbar.Hidden;
            if (_gizmoToolbarMode == SceneGizmoToolbarMode.MenuOnly)
            {
                if (EditorToolbar.Button(new Rect(left, 0, 26, height),
                        new GUIContent(string.Empty, icon, "Gizmos"))) ShowGizmoMenu();
                return;
            }

            var toggleWidth = _gizmoToolbarMode == SceneGizmoToolbarMode.Wide ? (Fix64)74 : 28;
            var content = _gizmoToolbarMode == SceneGizmoToolbarMode.Wide
                ? new GUIContent("Gizmos", icon, "Toggle Scene Gizmos")
                : new GUIContent(string.Empty, icon, "Toggle Scene Gizmos");
            var toggled = EditorToolbar.Toggle(new Rect(left, 0, toggleWidth, height), enabled, content);
            if (toggled != enabled) SceneGizmoVisibility.Enabled = toggled;
            if (EditorToolbar.Button(new Rect(left + toggleWidth, 0, 20, height),
                    new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.FoldoutOpen,
                        "Choose visible Gizmos"))) ShowGizmoMenu();
        }

        private static void ShowGizmoMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Gizmos"), SceneGizmoVisibility.Enabled,
                () => SceneGizmoVisibility.Enabled = !SceneGizmoVisibility.Enabled);

            var drawableTypes = GizmoDrawerRegistry.DrawableTypes;
            if (drawableTypes.Count == 0)
            {
                menu.AddSeparator(string.Empty);
                menu.AddDisabledItem(new GUIContent("Components/No Gizmos"));
                menu.ShowAsAdvancedDropdown();
                return;
            }

            var componentTypes = drawableTypes.Select(item => item.ComponentType).ToArray();
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("All"), componentTypes.All(SceneGizmoVisibility.IsVisible),
                () => SceneGizmoVisibility.SetAll(componentTypes, true));
            menu.AddItem(new GUIContent("None"), componentTypes.All(type => !SceneGizmoVisibility.IsVisible(type)),
                () => SceneGizmoVisibility.SetAll(componentTypes, false));
            menu.AddSeparator(string.Empty);
            foreach (var item in drawableTypes)
            {
                var componentType = item.ComponentType;
                var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
                    ? ObjectNames.NicifyVariableName(componentType.Name)
                    : item.DisplayName.Replace('\\', '/').Trim('/');
                menu.AddItem(new GUIContent($"Components/{displayName}"),
                    SceneGizmoVisibility.IsVisible(componentType),
                    () => SceneGizmoVisibility.SetVisible(
                        componentType, !SceneGizmoVisibility.IsVisible(componentType)));
            }
            menu.ShowAsAdvancedDropdown();
        }

        private static SceneGizmoToolbarMode ResolveGizmoToolbarMode(
            SceneGizmoToolbarMode current, Fix64 width, bool initialized)
        {
            if (!initialized)
                return width >= 300 ? SceneGizmoToolbarMode.Wide :
                    width >= 180 ? SceneGizmoToolbarMode.Compact : SceneGizmoToolbarMode.MenuOnly;
            return current switch
            {
                SceneGizmoToolbarMode.Wide when width < 288 => SceneGizmoToolbarMode.Compact,
                SceneGizmoToolbarMode.Compact when width >= 312 => SceneGizmoToolbarMode.Wide,
                SceneGizmoToolbarMode.Compact when width < 168 => SceneGizmoToolbarMode.MenuOnly,
                SceneGizmoToolbarMode.MenuOnly when width >= 192 => SceneGizmoToolbarMode.Compact,
                _ => current
            };
        }

        private void DrawSelectionHandles(Rect viewport)
        {
            var id = GUIUtility.GetControlID("SceneSelectionHandles".GetHashCode(StringComparison.Ordinal),
                FocusType.Passive, viewport);
            var selected = app.Selected;
            if (selected is null || Tools.hidden || SceneVisibilityManager.instance.IsHidden(selected) ||
                app._tool is Tool.View or Tool.None)
            {
                CancelHandleInteraction(id);
                return;
            }
            var transform = selected.transform;
            var originWorld = Numerics.ToNumerics(SceneHandleUtility.GetHandlePosition(selected));
            if (!app.TryProjectEditorPoint(originWorld, viewport, out var origin))
            {
                CancelHandleInteraction(id);
                return;
            }

            var worldPerPixel = app.EditorWorldUnitsPerPixel((float)viewport.height);
            var localAngle = app._tool == Tool.Scale || Tools.pivotRotation == PivotRotation.Local
                ? (float)(transform.rotation * Fix64.Deg2Rad)
                : 0f;
            var localX = new NVector2(MathF.Cos(localAngle), MathF.Sin(localAngle));
            var axes = new[]
            {
                localX,
                new NVector2(-localX.Y, localX.X)
            };
            var endpoints = new Vector2[2];
            var visible = new[] { true, true };
            for (var index = 0; index < axes.Length; index++)
            {
                var screenDirection = new NVector2(
                    axes[index].X,
                    -axes[index].Y);
                screenDirection = NVector2.Normalize(screenDirection) * AxisHandleLength;
                endpoints[index] = origin + new Vector2(
                    (Fix64)screenDirection.X, (Fix64)screenDirection.Y);
            }

            HandleTransformInput(id, viewport, transform, origin, axes, endpoints, visible, worldPerPixel);

            if (Event.current.type != EventType.Repaint) return;
            var colors = new[] { C(0.95f, 0.24f, 0.22f), C(0.32f, 0.86f, 0.32f) };
            switch (app._tool)
            {
                case Tool.Rotate:
                    DrawRotationHandle(origin,
                        _handleAxis == SceneHandleAxis.Screen ? Color.white : C(1f, 0.72f, 0.16f));
                    break;
                case Tool.Scale:
                    DrawScaleHandles(origin, endpoints, visible, colors);
                    break;
                case Tool.Rect:
                    DrawRectHandle(origin);
                    break;
                default:
                    DrawMoveHandles(origin, endpoints, visible, colors);
                    break;
            }
        }

        private void DrawMoveHandles(Vector2 origin, IReadOnlyList<Vector2> endpoints,
            IReadOnlyList<bool> visible, IReadOnlyList<Color> colors)
        {
            for (var index = 0; index < endpoints.Count; index++)
            {
                if (!visible[index]) continue;
                var color = _handleAxis == (SceneHandleAxis)(index + 1) ? Color.white : colors[index];
                DrawHandleLine(origin, endpoints[index], color, 2);
                DrawArrowHead(origin, endpoints[index], color);
                DrawAxisLabel(endpoints[index], index);
            }

            var centerColor = _handleAxis == SceneHandleAxis.Screen
                ? Color.white
                : C(0.98f, 0.82f, 0.22f);
            var top = origin + new Vector2(0, -6);
            var right = origin + new Vector2(6, 0);
            var bottom = origin + new Vector2(0, 6);
            var left = origin + new Vector2(-6, 0);
            DrawHandleLine(top, right, centerColor, 2);
            DrawHandleLine(right, bottom, centerColor, 2);
            DrawHandleLine(bottom, left, centerColor, 2);
            DrawHandleLine(left, top, centerColor, 2);
            GUI.DrawRect(new Rect(origin.x - 2, origin.y - 2, 4, 4), centerColor);
        }

        private void DrawScaleHandles(Vector2 origin, IReadOnlyList<Vector2> endpoints,
            IReadOnlyList<bool> visible, IReadOnlyList<Color> colors)
        {
            for (var index = 0; index < endpoints.Count; index++)
            {
                if (!visible[index]) continue;
                var color = _handleAxis == (SceneHandleAxis)(index + 1) ? Color.white : colors[index];
                DrawHandleLine(origin, endpoints[index], color, 2);
                GUI.DrawRect(new Rect(endpoints[index].x - 5, endpoints[index].y - 5, 10, 10), color);
                DrawAxisLabel(endpoints[index], index);
            }
            var centerColor = _handleAxis == SceneHandleAxis.Screen
                ? Color.white
                : C(0.98f, 0.82f, 0.22f);
            GUI.DrawRect(new Rect(origin.x - 3, origin.y - 3, 6, 6), centerColor);
        }

        private static void DrawRotationHandle(Vector2 origin, Color color)
        {
            const int segments = 64;
            var previous = origin + new Vector2((Fix64)RotationHandleRadius, 0);
            for (var index = 1; index <= segments; index++)
            {
                var angle = MathF.Tau * index / segments;
                var next = origin + new Vector2(
                    (Fix64)(MathF.Cos(angle) * RotationHandleRadius),
                    (Fix64)(MathF.Sin(angle) * RotationHandleRadius));
                DrawHandleLine(previous, next, color, 2);
                previous = next;
            }

            var arrowAngle = -MathF.PI / 4;
            var tip = origin + new Vector2(
                (Fix64)(MathF.Cos(arrowAngle) * RotationHandleRadius),
                (Fix64)(MathF.Sin(arrowAngle) * RotationHandleRadius));
            var tangent = new Vector2(
                (Fix64)(-MathF.Sin(arrowAngle)), (Fix64)MathF.Cos(arrowAngle));
            DrawArrowHead(tip - tangent * 14, tip, color);
            DrawHandleLine(origin + new Vector2(-5, 0), origin + new Vector2(5, 0), color, 1);
            DrawHandleLine(origin + new Vector2(0, -5), origin + new Vector2(0, 5), color, 1);
        }

        private static void DrawRectHandle(Vector2 origin)
        {
            var color = C(1f, 0.78f, 0.15f);
            var rect = new Rect(origin.x - 14, origin.y - 14, 28, 28);
            DrawHandleLine(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), color, 2);
            DrawHandleLine(new Vector2(rect.xMax, rect.y), new Vector2(rect.xMax, rect.yMax), color, 2);
            DrawHandleLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.x, rect.yMax), color, 2);
            DrawHandleLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.x, rect.y), color, 2);
            GUI.DrawRect(new Rect(origin.x - 3, origin.y - 3, 6, 6), Color.white);
        }

        private static void DrawArrowHead(Vector2 start, Vector2 tip, Color color)
        {
            var direction = Normalize(tip - start);
            if (direction == NVector2.Zero) return;
            var perpendicular = new NVector2(-direction.Y, direction.X);
            var basePoint = tip - new Vector2((Fix64)(direction.X * 12), (Fix64)(direction.Y * 12));
            var left = basePoint + new Vector2((Fix64)(perpendicular.X * 5), (Fix64)(perpendicular.Y * 5));
            var right = basePoint - new Vector2((Fix64)(perpendicular.X * 5), (Fix64)(perpendicular.Y * 5));
            DrawHandleLine(tip, left, color, 2);
            DrawHandleLine(tip, right, color, 2);
        }

        private static void DrawAxisLabel(Vector2 endpoint, int index) =>
            GUI.Label(new Rect(endpoint.x + 6, endpoint.y - 9, 16, 18),
                index == 0 ? "X" : "Y", EditorStyles.boldLabel);

        private void HandleTransformInput(
            int id,
            Rect viewport,
            Transform transform,
            Vector2 origin,
            IReadOnlyList<NVector2> axes,
            IReadOnlyList<Vector2> endpoints,
            IReadOnlyList<bool> visible,
            float worldPerPixel)
        {
            var current = Event.current;
            if (_handleTarget is not null &&
                (GUIUtility.hotControl != id || !ReferenceEquals(_handleTarget, transform) ||
                 _handleTool != app._tool))
                CancelHandleInteraction(id);
            if (current.type == EventType.MouseLeaveWindow)
            {
                CancelHandleInteraction(id);
                return;
            }
            if (current.type == EventType.MouseDown && current.button == 0 &&
                viewport.Contains(current.mousePosition))
            {
                var axis = HitHandle(app._tool, current.mousePosition, origin, endpoints, visible);
                if (axis != SceneHandleAxis.None)
                {
                    _handleAxis = axis;
                    _handleTool = app._tool;
                    _handleStartMouse = current.mousePosition;
                    _handleStartWorldPosition = transform.position;
                    _handleStartRotation = transform.localRotation;
                    _handleStartScale = transform.localScale;
                    _handleWorldAxis = axis is >= SceneHandleAxis.X and <= SceneHandleAxis.Y
                        ? axes[(int)axis - 1]
                        : NVector2.Zero;
                    _handleScreenDirection = axis is >= SceneHandleAxis.X and <= SceneHandleAxis.Y
                        ? Normalize(endpoints[(int)axis - 1] - origin)
                        : NVector2.Zero;
                    _handleWorldPerPixel = worldPerPixel;
                    _handleStartScreenOrigin = origin;
                    _handleStartWorldCenter = SceneHandleUtility.GetHandlePosition(transform.gameObject);
                    _handleLastScreenAngle = ScreenAngle(origin, current.mousePosition);
                    _handleAccumulatedRotation = 0;
                    _handleTarget = transform;
                    Undo.RecordObject(transform, $"{_handleTool} {transform.gameObject.name}");
                    GUIUtility.hotControl = id;
                    Focus();
                    current.Use();
                    return;
                }
            }

            if (current.type == EventType.MouseDrag && GUIUtility.hotControl == id &&
                _handleAxis != SceneHandleAxis.None && ReferenceEquals(_handleTarget, transform))
            {
                var delta = current.mousePosition - _handleStartMouse;
                var deltaX = (float)delta.x;
                var deltaY = (float)delta.y;
                var along = deltaX * _handleScreenDirection.X + deltaY * _handleScreenDirection.Y;
                if (_handleTool == Tool.Rotate)
                {
                    var angle = ScreenAngle(_handleStartScreenOrigin, current.mousePosition);
                    _handleAccumulatedRotation += NormalizeAngle(angle - _handleLastScreenAngle);
                    _handleLastScreenAngle = angle;
                    transform.localRotation = _handleStartRotation + (Fix64)_handleAccumulatedRotation;
                    transform.position += _handleStartWorldCenter -
                                          SceneHandleUtility.GetHandlePosition(transform.gameObject);
                }
                else if (_handleTool == Tool.Scale && _handleAxis == SceneHandleAxis.Screen)
                {
                    var factor = Fix64.One + (Fix64)((deltaX - deltaY) / 160f);
                    transform.localScale = new Vector2(
                        _handleStartScale.x * factor,
                        _handleStartScale.y * factor);
                }
                else if (_handleAxis == SceneHandleAxis.Screen)
                {
                    var movement = new NVector2(deltaX, -deltaY) * _handleWorldPerPixel;
                    transform.position = _handleStartWorldPosition + ToVector2(movement);
                }
                else if (_handleTool is Tool.Move or Tool.Transform)
                {
                    var movement = _handleWorldAxis * (along * _handleWorldPerPixel);
                    transform.position = _handleStartWorldPosition + ToVector2(movement);
                }
                else if (_handleTool == Tool.Scale)
                {
                    var index = (int)_handleAxis - 1;
                    var start = index == 0 ? _handleStartScale.x : _handleStartScale.y;
                    var amount = (Fix64)(along / 80f) * Fix64.Max(Fix64.One, Fix64.Abs(start));
                    transform.localScale = AddAxis(_handleStartScale, _handleAxis, amount);
                }
                EditorUtility.SetDirty(transform);
                if (transform.gameObject.scene is { } scene) app.MarkDirty(scene);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0 && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                ResetHandleInteraction();
                current.Use();
            }
        }

        private void CancelHandleInteraction(int id)
        {
            if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
            ResetHandleInteraction();
        }

        private void ResetHandleInteraction()
        {
            _handleAxis = SceneHandleAxis.None;
            _handleTool = Tool.None;
            _handleTarget = null;
        }

        private static SceneHandleAxis HitAxis(Vector2 point, Vector2 origin,
            IReadOnlyList<Vector2> endpoints, IReadOnlyList<bool> visible)
        {
            var best = 9f;
            var result = SceneHandleAxis.None;
            for (var index = 0; index < endpoints.Count; index++)
            {
                if (!visible[index]) continue;
                var distance = DistanceToSegment(point, origin, endpoints[index]);
                if (distance >= best) continue;
                best = distance;
                result = (SceneHandleAxis)(index + 1);
            }
            return result;
        }

        private static SceneHandleAxis HitHandle(Tool tool, Vector2 point, Vector2 origin,
            IReadOnlyList<Vector2> endpoints, IReadOnlyList<bool> visible)
        {
            var distance = Distance(point, origin);
            if (tool == Tool.Rotate)
                return MathF.Abs(distance - RotationHandleRadius) <= RotationHandleHitWidth
                    ? SceneHandleAxis.Screen
                    : SceneHandleAxis.None;
            if (tool == Tool.Rect)
                return distance <= 18 ? SceneHandleAxis.Screen : SceneHandleAxis.None;
            if (tool == Tool.Scale && distance <= 8)
                return SceneHandleAxis.Screen;
            if (tool is Tool.Move or Tool.Transform && distance <= 8)
                return SceneHandleAxis.Screen;
            return HitAxis(point, origin, endpoints, visible);
        }

        private static float ScreenAngle(Vector2 center, Vector2 point) =>
            MathF.Atan2((float)(center.y - point.y), (float)(point.x - center.x)) *
            180f / MathF.PI;

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }

        private static Vector2 AddAxis(Vector2 value, SceneHandleAxis axis, Fix64 amount) => axis switch
        {
            SceneHandleAxis.X => new Vector2(value.x + amount, value.y),
            SceneHandleAxis.Y => new Vector2(value.x, value.y + amount),
            _ => value
        };

        private static NVector2 Normalize(Vector2 value)
        {
            var result = new NVector2((float)value.x, (float)value.y);
            return result.LengthSquared() <= 0.0001f ? NVector2.Zero : NVector2.Normalize(result);
        }

        private static Vector2 ToVector2(NVector2 value) =>
            new((Fix64)value.X, (Fix64)value.Y);

        private static float Distance(Vector2 left, Vector2 right)
        {
            var x = (float)(left.x - right.x);
            var y = (float)(left.y - right.y);
            return MathF.Sqrt(x * x + y * y);
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var px = (float)point.x;
            var py = (float)point.y;
            var ax = (float)start.x;
            var ay = (float)start.y;
            var bx = (float)end.x;
            var by = (float)end.y;
            var dx = bx - ax;
            var dy = by - ay;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.0001f) return Distance(point, start);
            var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
            var x = px - (ax + dx * t);
            var y = py - (ay + dy * t);
            return MathF.Sqrt(x * x + y * y);
        }

        private static void DrawHandleLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            var x = (float)(end.x - start.x);
            var y = (float)(end.y - start.y);
            var length = MathF.Sqrt(x * x + y * y);
            var steps = Math.Max(1, (int)MathF.Ceiling(length / 3));
            var size = (Fix64)Math.Max(1, thickness);
            for (var index = 0; index <= steps; index++)
            {
                var t = index / (float)steps;
                var px = start.x + (Fix64)(x * t) - size / 2;
                var py = start.y + (Fix64)(y * t) - size / 2;
                GUI.DrawRect(new Rect(px, py, size, size), color);
            }
        }

        private void HandleNavigation(Rect viewport)
        {
            var current = Event.current;
            var id = GUIUtility.GetControlID(nameof(ImGuiSceneWindow).GetHashCode(StringComparison.Ordinal),
                FocusType.Passive, viewport);
            if (current.type == EventType.ScrollWheel && viewport.Contains(current.mousePosition))
            {
                app.ZoomEditorCamera((float)current.delta.y);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 2 &&
                viewport.Contains(current.mousePosition))
            {
                _navigationButton = current.button;
                GUIUtility.hotControl = id;
                Focus();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                app.PanEditorCamera((float)current.delta.x, (float)current.delta.y,
                    (float)viewport.height);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && GUIUtility.hotControl == id &&
                current.button == _navigationButton)
            {
                GUIUtility.hotControl = 0;
                _navigationButton = -1;
                current.Use();
            }

            if (GUIUtility.hotControl == id)
                EditorGUIUtility.AddCursorRect(viewport, MouseCursor.Pan);
        }

        private void HandleObjectPicking(Rect viewport)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 ||
                !viewport.Contains(current.mousePosition)) return;

            var viewportPoint = current.mousePosition - new Vector2(viewport.x, viewport.y);
            var candidates = ScenePickingUtility.PickAll(
                app.LoadedScenes(), app.EditorCamera((float)viewport.height), viewportPoint,
                Math.Max(1, (int)viewport.width), Math.Max(1, (int)viewport.height)).ToArray();
            if (candidates.Length == 0)
            {
                app.ClearSelection();
                ResetPickCycle();
                Focus();
                current.Use();
                return;
            }

            var samePoint = (current.mousePosition - _lastPickPosition).sqrMagnitude <= 16;
            var sameCandidates = candidates.Length == _lastPickCandidates.Length &&
                                 candidates.SequenceEqual(_lastPickCandidates,
                                     ReferenceEqualityComparer.Instance);
            _lastPickIndex = samePoint && sameCandidates &&
                             ReferenceEquals(app.Selected, _lastPickedObject)
                ? (_lastPickIndex + 1) % candidates.Length
                : 0;
            _lastPickCandidates = candidates;
            _lastPickPosition = current.mousePosition;
            _lastPickedObject = candidates[_lastPickIndex];
            app.Select(_lastPickedObject);
            Focus();
            current.Use();
        }

        private void ResetPickCycle()
        {
            _lastPickCandidates = [];
            _lastPickedObject = null;
            _lastPickIndex = -1;
        }

    }

    private enum SceneHandleAxis
    {
        None,
        X,
        Y,
        Screen
    }

    private enum SceneGizmoToolbarMode
    {
        Wide,
        Compact,
        MenuOnly
    }

    private sealed class ImGuiGameWindow(GpuEditorApplication app) : EditorWindow
    {
        private const int StatusButtonWidth = 68;
        private readonly GameViewResolutionSettings _resolutions = new();
        private GraphicsRect _lastFittedViewport;
        private SceneRenderStatistics _renderStatistics;
        private bool _hasRenderStatistics;
        private bool _statusExpanded;

        public ImGuiGameWindow() : this(null!) { }

        internal GameViewResolution selectedResolution => _resolutions.selected;
        internal GraphicsRect FitRenderViewport(GraphicsRect available)
        {
            _lastFittedViewport = _resolutions.FitViewport(available);
            return _lastFittedViewport;
        }
        internal (int Width, int Height) ApplyTargetSize(GraphicsRect fittedViewport)
        {
            _resolutions.ApplyScreenSize(fittedViewport);
            return _resolutions.ResolveTargetSize(fittedViewport);
        }
        internal void UpdateRenderStatistics(SceneRenderStatistics statistics)
        {
            _renderStatistics = statistics;
            _hasRenderStatistics = true;
        }

        protected override void OnGUI()
        {
            DrawWindowToolbarBackground();
            var toolbarWidth = GUI.visibleViewWidth;
            var showResolution = toolbarWidth >= StatusButtonWidth + 56;
            var statusWidth = (Fix64)StatusButtonWidth;
            var resolutionWidth = showResolution
                ? Fix64.Max(44, Fix64.Min(132,
                    toolbarWidth - statusWidth - 12))
                : Fix64.Zero;
            GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbar.fixedHeight));
            if (showResolution && GUILayout.Button(new GUIContent(_resolutions.selected.SizeLabel,
                        _resolutions.selected.MenuLabel), EditorStyles.toolbarButton,
                        GUILayout.Width(resolutionWidth)))
            {
                var anchor = GUILayout.LastRect;
                var topLeft = GUI.GUIToRootPoint(anchor.position);
                var bottomRight = GUI.GUIToRootPoint(new Vector2(anchor.xMax, anchor.yMax));
                ShowResolutionMenu(anchor, new Rect(topLeft.x, topLeft.y,
                    bottomRight.x - topLeft.x, bottomRight.y - topLeft.y));
            }
            var usedBeforeStatus = showResolution
                ? 8 + resolutionWidth
                : (Fix64)4;
            GUILayout.Space(Fix64.Max(0,
                toolbarWidth - statusWidth - 4 - usedBeforeStatus));
            if (GUILayout.Button(new GUIContent("Status", "Show Game rendering statistics"),
                    _statusExpanded ? EditorStyles.toolbarIconButtonSelected : EditorStyles.toolbarButton,
                    GUILayout.Width(statusWidth)))
                _statusExpanded = !_statusExpanded;
            GUILayout.EndHorizontal();
            if (_statusExpanded) DrawStatusOverlay();
        }

        private void DrawStatusOverlay()
        {
            var availableWidth = Fix64.Max(0, GUIUtility.currentViewWidth - 12);
            var availableHeight = Fix64.Max(0,
                GUIUtility.currentViewHeight - EditorStyles.toolbar.fixedHeight - 12);
            if (availableWidth < 112 || availableHeight < 44) return;

            var panelWidth = Fix64.Min(310, availableWidth);
            var compact = panelWidth < 250;
            var lines = StatusLines(compact);
            var lineHeight = Fix64.Max(16,
                GUITextMetrics.MeasureLineHeight(EditorStyles.miniLabel.fontSize, GUIUtility.fontFamily) + 2);
            var maximumLineCount = Math.Max(1,
                (int)((availableHeight - 12) / lineHeight) - 1);
            if (lines.Count > maximumLineCount) lines.RemoveRange(maximumLineCount,
                lines.Count - maximumLineCount);
            var panelHeight = Fix64.Min(availableHeight, 12 + lineHeight * (lines.Count + 1));
            var panel = new Rect(
                Fix64.Max(6, GUIUtility.currentViewWidth - panelWidth - 6),
                EditorStyles.toolbar.fixedHeight + 6,
                panelWidth,
                panelHeight);
            GUI.Box(panel, GUIContent.none, EditorStyles.helpBox);

            GUI.Label(new Rect(panel.x + 6, panel.y + 4, panel.width - 12, lineHeight),
                "Statistics", EditorStyles.centeredBoldLabel);
            var y = panel.y + 4 + lineHeight;
            foreach (var (text, heading) in lines)
            {
                GUI.Label(new Rect(panel.x + 10, y, panel.width - 20, lineHeight), text,
                    heading ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                y += lineHeight;
            }
        }

        private List<(string Text, bool Heading)> StatusLines(bool compact)
        {
            var timing = app?._mainWindow?.frameTiming ?? default;
            var backend = app?._mainWindow?.backend.ToString() ?? "Unavailable";
            var lines = new List<(string, bool)>
            {
                ("Frame", true),
                (timing.HasValue
                    ? $"{timing.FramesPerSecond:F1} FPS ({timing.FrameTimeMilliseconds:F1} ms)"
                    : "FPS: warming up", false)
            };
            if (compact)
            {
                lines.Add(("Graphics", true));
                lines.Add(($"Backend: {backend}", false));
            }
            else
                lines.Add(($"Graphics | {backend}", true));
            if (!_hasRenderStatistics)
            {
                lines.Add(("Waiting for Game rendering", false));
                return lines;
            }

            if (compact)
            {
                lines.Add(($"Cameras: {CompactNumber(_renderStatistics.CameraCount)}", false));
                lines.Add(($"Visible: {CompactNumber(_renderStatistics.VisibleSubmissionCount)}", false));
                lines.Add(($"Batches: {CompactNumber(_renderStatistics.BatchCount)}", false));
                lines.Add(($"Saved by batching: {CompactNumber(SavedByBatching())}", false));
                lines.Add((DrawMetric("Draw", _renderStatistics.DrawCallCount, true), false));
                lines.Add((DrawMetric("Tris", _renderStatistics.TriangleCount, true), false));
                lines.Add((DrawMetric("Verts", _renderStatistics.VertexCount, true), false));
            }
            else
            {
                lines.Add(($"Cameras: {_renderStatistics.CameraCount:N0}   " +
                           $"Visible: {_renderStatistics.VisibleSubmissionCount:N0}", false));
                lines.Add(($"Batches: {_renderStatistics.BatchCount:N0}   " +
                           DrawMetric("Draw calls", _renderStatistics.DrawCallCount), false));
                lines.Add(($"Saved by batching: {SavedByBatching():N0}", false));
                lines.Add(($"{DrawMetric("Tris", _renderStatistics.TriangleCount)}   " +
                           DrawMetric("Verts", _renderStatistics.VertexCount), false));
            }
            lines.Add((compact
                ? $"Screen: {_renderStatistics.TargetWidth}x{_renderStatistics.TargetHeight}"
                : $"Screen: {_renderStatistics.TargetWidth:N0} x {_renderStatistics.TargetHeight:N0}", false));
            return lines;
        }

        private int SavedByBatching() => Math.Max(0,
            _renderStatistics.VisibleSubmissionCount - _renderStatistics.BatchCount);

        private string DrawMetric(string label, long value, bool compact = false) =>
            _renderStatistics.HasCompleteDrawStatistics
                ? $"{label}: {(compact ? CompactNumber(value) : $"{value:N0}")}"
                : $"{label}: unavailable";

        private static string CompactNumber(long value) => value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000d:F1}B",
            >= 1_000_000 => $"{value / 1_000_000d:F1}M",
            >= 10_000 => $"{value / 1_000d:F1}K",
            _ => value.ToString()
        };

        private void ShowResolutionMenu(Rect anchor, Rect rootAnchor)
        {
            var menu = new GenericMenu();
            foreach (var option in _resolutions.builtInOptions)
            {
                var path = option.IsFreeAspect
                    ? option.MenuLabel
                    : $"{option.Category}/{option.MenuLabel}";
                menu.AddItem(new GUIContent(path), option.Id == _resolutions.selected.Id,
                    () => SelectResolution(option));
            }

            if (_resolutions.customOptions.Count > 0)
            {
                menu.AddSeparator(string.Empty);
                foreach (var option in _resolutions.customOptions)
                {
                    menu.AddItem(new GUIContent($"Custom/{option.MenuLabel}"),
                        option.Id == _resolutions.selected.Id,
                        () => SelectResolution(option));
                }
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Add Custom Resolution..."), false,
                () => GameViewResolutionWindow.Open(rootAnchor, AddCustomResolution));
            foreach (var option in _resolutions.customOptions)
            {
                menu.AddItem(new GUIContent($"Delete Custom/{option.MenuLabel}"), false,
                    () => DeleteCustomResolution(option.Id));
            }
            menu.ShowAsAdvancedDropdown(anchor);
        }

        private void SelectResolution(GameViewResolution option)
        {
            _resolutions.Select(option);
            ApplyFreeAspectScreenSize();
            Repaint();
        }

        private void AddCustomResolution(string name, int width, int height)
        {
            _resolutions.AddCustom(name, width, height);
            Repaint();
        }

        private void DeleteCustomResolution(string id)
        {
            _resolutions.RemoveCustom(id);
            ApplyFreeAspectScreenSize();
            Repaint();
        }

        private void ApplyFreeAspectScreenSize()
        {
            if (_resolutions.selected.IsFreeAspect &&
                _lastFittedViewport.Width > 0 && _lastFittedViewport.Height > 0)
                _resolutions.ApplyScreenSize(_lastFittedViewport);
        }
    }

    private sealed class ImGuiInspectorWindow(GpuEditorApplication app) : EditorWindow
    {
        private BObject? _lastTarget;
        private Editor? _editor;
        private readonly Dictionary<Component, Editor> _componentEditors = [];
        private readonly HashSet<Component> _drawnComponents = [];
        private readonly HashSet<Component> _collapsedComponents = [];
        private readonly ImGuiScrollRegion _scroll = new();
        private int _tagVersion = -1;
        private int _layerVersion = -1;
        private string[] _tagLabels = [];
        private (string Name, ulong Value)[] _layerOptions = [];
        private string[] _layerLabels = [];
        private bool _previewExpanded = true;
        private bool _previewHeightInitialized;
        private Fix64 _previewHeight;
        private Fix64 _previewDragStartY;
        private Fix64 _previewDragStartHeight;
        public ImGuiInspectorWindow() : this(null!) { }
        internal BObject? LockedTarget => isLocked ? _lastTarget : null;
        internal void RebuildEditor(bool force = false)
        {
            if (!force && isLocked && _lastTarget is not null) return;
            var lockedTarget = force && isLocked ? _lastTarget : null;
            DisposeEditors();
            _lastTarget = lockedTarget;
            if (lockedTarget is not null && lockedTarget is not GameObject)
                _editor = Editor.CreateEditor(lockedTarget);
        }
        internal void RemapLockedTarget(Func<BObject?, BObject?> mapper)
        {
            ArgumentNullException.ThrowIfNull(mapper);
            if (!isLocked || _lastTarget is null)
            {
                RebuildEditor(force: true);
                return;
            }

            var mapped = mapper(_lastTarget);
            DisposeEditors();
            _lastTarget = mapped;
            if (mapped is not null && mapped is not GameObject)
                _editor = Editor.CreateEditor(mapped);
        }
        protected override void OnGUI()
        {
            var target = ResolveTarget();
            SynchronizeTarget(target);
            var hasPreview = target is not null && target is not GameObject &&
                             _editor?.HasPreviewGUIInternal() is true;
            var previewHeight = PreviewPaneHeight(hasPreview);
            if (hasPreview && _previewExpanded)
            {
                var splitter = new Rect(0,
                    Fix64.Max(0, GUIUtility.currentViewHeight - previewHeight - 3),
                    GUIUtility.currentViewWidth, 6);
                previewHeight = HandlePreviewSplitter(splitter, previewHeight);
            }
            var inspectorHeight = Fix64.Max(0, GUIUtility.currentViewHeight - previewHeight);

            using (GUILayout.Area(new Rect(0, 0, GUIUtility.currentViewWidth, inspectorHeight)))
            {
                _scroll.Begin();
                try { DrawInspector(target); }
                finally { _scroll.End(); }
            }

            if (!hasPreview || _editor is null) return;
            using (GUILayout.Area(new Rect(0, inspectorHeight, GUIUtility.currentViewWidth, previewHeight)))
                DrawPreviewPane(_editor, target!, previewHeight);
        }
        private BObject? ResolveTarget()
        {
            var selected = app.Selected is not null ? (BObject)app.Selected : app.SelectedAsset;
            return isLocked && _lastTarget is not null ? _lastTarget : selected;
        }
        private void SynchronizeTarget(BObject? target)
        {
            if (ReferenceEquals(_lastTarget, target) &&
                (target is GameObject || target is null || _editor is not null)) return;
            DisposeEditors();
            _lastTarget = target;
            if (target is not null && target is not GameObject) _editor = Editor.CreateEditor(target);
        }
        private void DrawInspector(BObject? target)
        {
            if (target is null)
            {
                GUILayout.Space(8);
                if (app.SelectedAssetPath is not null)
                {
                    GUILayout.Label(Path.GetFileName(app.SelectedAssetPath), EditorStyles.boldLabel);
                    GUILayout.Label(app.SelectedAssetPath, EditorStyles.miniLabel);
                }
                else GUILayout.Label("No object selected", EditorStyles.miniLabel);
                return;
            }
            var readOnly = (target.hideFlags & HideFlags.NotEditable) != 0;
            if (target is GameObject inspectedGameObject)
                DrawGameObjectHeader(inspectedGameObject, readOnly);
            else
            {
                string name;
                using (new EditorGUI.DisabledScope(readOnly))
                    name = EditorGUILayout.TextField("Name", target.name);
                if (!readOnly && name != target.name)
                {
                    Undo.RecordObject(target, "Rename");
                    target.name = name;
                    if (target is Component component && component.gameObject.scene is { } owner)
                        app.MarkDirty(owner);
                }
            }
            if (target is GameObject prefabObject && PrefabUtility.IsPartOfPrefabInstance(prefabObject) &&
                PrefabUtility.IsAnyPrefabInstanceRoot(prefabObject))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Prefab", EditorBuiltinIcons.Assets.Prefab,
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabObject)), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Open", GUILayout.Width(62)))
                    PrefabStageUtility.OpenPrefab(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabObject));
                using (new EditorGUI.DisabledScope(readOnly || app._playing))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(62)))
                        PrefabUtility.ApplyPrefabInstance(prefabObject);
                    if (GUILayout.Button("Revert", GUILayout.Width(62)))
                        PrefabUtility.RevertPrefabInstance(prefabObject);
                    if (GUILayout.Button(new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.More,
                            "Prefab options"), GUILayout.Width(28)))
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Unpack Prefab"), false, () =>
                            PrefabUtility.UnpackPrefabInstance(prefabObject, PrefabUnpackMode.OutermostRoot));
                        menu.ShowAsContext();
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (target is not GameObject)
            {
                using var disabledEditor = new EditorGUI.DisabledScope(readOnly);
                _editor!.OnInspectorGUIInternal();
            }
            if (target is not GameObject gameObject) return;
            _drawnComponents.Clear();
            using var componentDisabled = new EditorGUI.DisabledScope(readOnly);
            foreach (var component in gameObject.components)
            {
                _drawnComponents.Add(component);
                GUILayout.Space(4);
                var componentType = component.GetType();
                var componentName = ObjectNames.NicifyVariableName(componentType.Name) +
                                    (component is MonoBehaviour ? " (Script)" : string.Empty);
                var componentIcon = EditorIconRegistry.GetComponentIconPath(componentType);
                var header = GUILayoutUtility.GetControlRect(EditorStyles.inspectorTitlebar.fixedHeight,
                    GUILayout.ExpandWidth(true));
                GUI.Box(header, GUIContent.none, EditorStyles.inspectorTitlebar);
                var collapsed = _collapsedComponents.Contains(component);
                var foldoutRect = new Rect(header.x + 3, header.y + 1, 18, Fix64.Max(18, header.height - 2));
                var foldoutClicked = Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                                     foldoutRect.Contains(Event.current.mousePosition);
                if (foldoutClicked) Event.current.Use();
                if (GUI.Button(foldoutRect, new GUIContent(string.Empty,
                        collapsed ? EditorBuiltinIcons.Toolbar.FoldoutClosed : EditorBuiltinIcons.Toolbar.FoldoutOpen,
                        collapsed ? "Expand component" : "Collapse component"), EditorStyles.foldout) ||
                    foldoutClicked)
                {
                    if (!_collapsedComponents.Add(component)) _collapsedComponents.Remove(component);
                    collapsed = _collapsedComponents.Contains(component);
                }
                var title = new GUIContent(componentName, componentIcon,
                    componentType.FullName ?? componentType.Name);
                var titleStart = header.x + 22;
                var enabled = component.enabled;
                if (component is not Transform)
                {
                    var enabledRect = new Rect(titleStart, header.y + 2, 18,
                        Fix64.Max(18, header.height - 4));
                    enabled = GUI.Toggle(enabledRect, component.enabled, GUIContent.none);
                    titleStart += 22;
                }
                var titleRect = new Rect(titleStart, header.y,
                    Fix64.Max(0, header.xMax - 29 - titleStart), header.height);
                GUI.Label(titleRect, title, EditorStyles.boldLabel);
                if (enabled != component.enabled)
                {
                    Undo.RecordObject(component, enabled ? "Enable Component" : "Disable Component");
                    component.enabled = enabled;
                    EditorUtility.SetDirty(component);
                    if (component.gameObject.scene is { } componentScene) app.MarkDirty(componentScene);
                }
                var menuRect = new Rect(header.xMax - 27, header.y + 1, 24,
                    Fix64.Max(18, header.height - 2));
                if (GUI.Button(menuRect,
                        new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.More, "Component menu"),
                        EditorStyles.toolbarIconButton))
                    ShowComponentMenu(component, readOnly);
                if (Event.current.type == EventType.ContextClick && header.Contains(Event.current.mousePosition))
                {
                    ShowComponentMenu(component, readOnly);
                    Event.current.Use();
                }
                if (collapsed) continue;
                if (component is MonoBehaviour behaviour)
                {
                    var scriptRow = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight,
                        GUILayout.ExpandWidth(true));
                    Rect scriptRect;
                    if (scriptRow.width >= 150)
                    {
                        var labelWidth = Fix64.Clamp(scriptRow.width * Fix64.FromDecimal(0.36m), 52, 130);
                        GUI.Label(new Rect(scriptRow.x + 6, scriptRow.y, labelWidth - 8, scriptRow.height),
                            "Script", EditorStyles.label);
                        scriptRect = new Rect(scriptRow.x + labelWidth, scriptRow.y,
                            Fix64.Max(1, scriptRow.width - labelWidth), scriptRow.height);
                    }
                    else
                    {
                        GUI.Label(new Rect(scriptRow.x + 6, scriptRow.y,
                            Fix64.Max(1, scriptRow.width - 12), scriptRow.height), "Script", EditorStyles.label);
                        scriptRect = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight,
                            GUILayout.ExpandWidth(true));
                    }
                    if (GUI.Button(scriptRect, new GUIContent(componentType.Name, componentIcon,
                            "Click to locate the script. Double-click to open it."), EditorStyles.textField))
                        app.SelectScript(behaviour, Event.current.clickCount >= 2);
                    EditorGUIUtility.AddCursorRect(scriptRect, MouseCursor.Link);
                }
                if (component.GetType() == typeof(Transform))
                {
                    DrawTransformInspector((Transform)component);
                    continue;
                }
                if (!_componentEditors.TryGetValue(component, out var componentEditor))
                {
                    componentEditor = Editor.CreateEditor(component);
                    _componentEditors.Add(component, componentEditor);
                }
                componentEditor.OnInspectorGUIInternal();
            }
            GUILayout.Space(8);
            var addComponentRow = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight + 2,
                GUILayout.ExpandWidth(true));
            var addComponentWidth = Fix64.Min(220, Fix64.Max(100, addComponentRow.width - 24));
            var addComponentRect = new Rect(addComponentRow.x + (addComponentRow.width - addComponentWidth) / 2,
                addComponentRow.y, addComponentWidth, addComponentRow.height);
            if (GUI.Button(addComponentRect, "Add Component")) app.ShowAddComponentMenu();
            if (_componentEditors.Count == _drawnComponents.Count) return;
            foreach (var removed in _componentEditors.Keys.Where(component => !_drawnComponents.Contains(component))
                         .ToArray())
            {
                _componentEditors.Remove(removed, out var componentEditor);
                componentEditor?.Dispose();
            }
        }

        private Fix64 PreviewPaneHeight(bool hasPreview)
        {
            if (!hasPreview) return 0;
            var header = Fix64.Max(22, EditorStyles.inspectorTitlebar.fixedHeight);
            if (!_previewExpanded) return header + 2;
            var (minimum, maximum) = PreviewHeightRange(header);
            if (!_previewHeightInitialized)
            {
                _previewHeight = Fix64.Clamp(
                    GUIUtility.currentViewHeight * Fix64.FromDecimal(0.36m), minimum,
                    Fix64.Min(260, maximum));
                _previewHeightInitialized = true;
            }
            _previewHeight = Fix64.Clamp(_previewHeight, minimum, maximum);
            return _previewHeight;
        }

        private (Fix64 Minimum, Fix64 Maximum) PreviewHeightRange(Fix64 header)
        {
            var maximum = Fix64.Min(GUIUtility.currentViewHeight,
                Fix64.Max(header + 36, GUIUtility.currentViewHeight - 72));
            return (Fix64.Min(header + 96, maximum), maximum);
        }

        private Fix64 HandlePreviewSplitter(Rect splitter, Fix64 currentHeight)
        {
            var header = Fix64.Max(22, EditorStyles.inspectorTitlebar.fixedHeight);
            var (minimum, maximum) = PreviewHeightRange(header);
            currentHeight = Fix64.Clamp(currentHeight, minimum, maximum);
            var id = GUIUtility.GetControlID("InspectorPreviewSplitter".GetHashCode(StringComparison.Ordinal),
                FocusType.Passive, splitter);
            var evt = Event.current;
            EditorGUIUtility.AddCursorRect(GUIUtility.hotControl == id
                    ? new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight)
                    : splitter,
                MouseCursor.ResizeVertical);
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown when evt.button == 0 && splitter.Contains(evt.mousePosition):
                    GUIUtility.hotControl = id;
                    _previewDragStartY = evt.mousePosition.y;
                    _previewDragStartHeight = currentHeight;
                    evt.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    currentHeight = Fix64.Clamp(
                        _previewDragStartHeight - (evt.mousePosition.y - _previewDragStartY),
                        minimum, maximum);
                    _previewHeight = currentHeight;
                    _previewHeightInitialized = true;
                    evt.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    break;
            }
            _previewHeight = currentHeight;
            return currentHeight;
        }

        private void DrawPreviewPane(Editor editor, BObject target, Fix64 paneHeight)
        {
            var width = GUIUtility.currentViewWidth;
            var headerHeight = Fix64.Max(22, EditorStyles.inspectorTitlebar.fixedHeight);
            var header = new Rect(0, 0, width, Fix64.Min(headerHeight, paneHeight));
            GUI.Box(header, GUIContent.none, EditorStyles.inspectorTitlebar);
            var foldout = new Rect(header.x + 3, header.y + 1, 20, Fix64.Max(18, header.height - 2));
            if (GUI.Button(foldout, new GUIContent(string.Empty,
                    _previewExpanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen :
                        EditorBuiltinIcons.Toolbar.FoldoutClosed,
                    _previewExpanded ? "Collapse preview" : "Expand preview"), EditorStyles.foldout))
                _previewExpanded = !_previewExpanded;
            var thumbnail = AssetPreview.GetMiniThumbnail(target);
            var iconRect = new Rect(foldout.xMax + 2, header.y + 2, Fix64.Max(16, header.height - 4),
                Fix64.Max(16, header.height - 4));
            GUI.DrawTexture(iconRect, thumbnail);
            GUI.Label(new Rect(iconRect.xMax + 5, header.y,
                    Fix64.Max(0, header.xMax - iconRect.xMax - 9), header.height),
                "Preview", EditorStyles.boldLabel);
            if (!_previewExpanded || paneHeight <= headerHeight + 8) return;

            var information = editor.GetInfoStringInternal();
            var informationHeight = string.IsNullOrWhiteSpace(information) ? Fix64.Zero : (Fix64)22;
            var body = new Rect(4, header.yMax + 4, Fix64.Max(0, width - 8),
                Fix64.Max(0, paneHeight - header.height - informationHeight - 8));
            GUI.Box(body, GUIContent.none, EditorStyles.viewBackground);
            GUI.BeginClip(body);
            try
            {
                editor.OnPreviewGUIInternal(new Rect(body.x + 2, body.y + 2,
                    Fix64.Max(0, body.width - 4), Fix64.Max(0, body.height - 4)));
            }
            finally { GUI.EndClip(); }
            if (informationHeight > 0)
                GUI.Label(new Rect(7, body.yMax + 1, Fix64.Max(0, width - 14), informationHeight),
                    information, EditorStyles.miniLabel);
            if (editor.RequiresConstantRepaintInternal()) Repaint();
        }

        private void DrawTransformInspector(Transform transform)
        {
            var position = TransformVectorField("Position", transform.localPosition, Vector2.zero);
            var rotation = TransformRotationField("Rotation", transform.localRotation);
            var scale = TransformVectorField("Scale", transform.localScale, Vector2.one);
            if (position == transform.localPosition && rotation == transform.localRotation &&
                scale == transform.localScale) return;
            Undo.RecordObject(transform, "Edit Transform");
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
            EditorUtility.SetDirty(transform);
            if (transform.gameObject.scene is { } scene) app.MarkDirty(scene);
        }

        private static Vector2 TransformVectorField(string label, Vector2 value, Vector2 resetValue)
        {
            var row = GUILayoutUtility.GetControlRect(EditorGUI.GetVectorFieldHeight(2),
                GUILayout.ExpandWidth(true));
            var resetWidth = Fix64.Min(22, Fix64.Max(0, row.width));
            var resetRect = new Rect(row.x, row.y, resetWidth, EditorGUIUtility.singleLineHeight);
            var fieldRect = new Rect(resetRect.xMax + 2, row.y,
                Fix64.Max(0, row.width - resetWidth - 2), row.height);
            if (GUI.Button(resetRect, new GUIContent("R", tooltip: $"Reset {label}"), EditorStyles.miniButton))
                value = resetValue;
            return EditorGUI.Vector2Field(fieldRect, label, value);
        }

        private static Fix64 TransformRotationField(string label, Fix64 value)
        {
            var row = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight,
                GUILayout.ExpandWidth(true));
            var resetWidth = Fix64.Min(22, Fix64.Max(0, row.width));
            var resetRect = new Rect(row.x, row.y, resetWidth, row.height);
            var fieldRect = new Rect(resetRect.xMax + 2, row.y,
                Fix64.Max(0, row.width - resetWidth - 2), row.height);
            if (GUI.Button(resetRect, new GUIContent("R", tooltip: $"Reset {label}"), EditorStyles.miniButton))
                value = Fix64.Zero;
            return (Fix64)EditorGUI.FloatField(fieldRect, label, (float)value);
        }

        private void DrawGameObjectHeader(GameObject gameObject, bool readOnly)
        {
            using var disabled = new EditorGUI.DisabledScope(readOnly);
            var header = GUILayoutUtility.GetControlRect(Fix64.Max(30, EditorGUIUtility.singleLineHeight + 8),
                GUILayout.ExpandWidth(true));
            var activeRect = new Rect(header.x + 5, header.y + 4, 18, header.height - 8);
            var iconRect = new Rect(activeRect.xMax + 4, header.y + 4, 22, header.height - 8);
            var compactHeader = header.width < 260;
            var staticWidth = compactHeader ? Fix64.Zero : (Fix64)72;
            var staticRect = compactHeader ? default : new Rect(header.xMax - staticWidth,
                header.y + 4, staticWidth - 4, header.height - 8);
            var nameRect = new Rect(iconRect.xMax + 5, header.y + 4,
                Fix64.Max(1, (compactHeader ? header.xMax - 5 : staticRect.x - 4) - iconRect.xMax - 5),
                header.height - 8);
            var active = GUI.Toggle(activeRect, gameObject.activeSelf, GUIContent.none);
            GUI.Label(iconRect, new GUIContent(string.Empty, EditorBuiltinIcons.Components.GameObject,
                "GameObject"));
            var name = GUI.TextField(nameRect, gameObject.name, style: EditorStyles.textField);
            bool isStatic;
            if (compactHeader)
            {
                var compactStaticRect = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight,
                    GUILayout.ExpandWidth(true));
                isStatic = GUI.Toggle(new Rect(compactStaticRect.x + 5, compactStaticRect.y,
                        Fix64.Max(1, compactStaticRect.width - 10), compactStaticRect.height),
                    gameObject.isStatic, new GUIContent("Static", tooltip: "Mark this GameObject as static"));
            }
            else
                isStatic = GUI.Toggle(staticRect, gameObject.isStatic,
                    new GUIContent("Static", tooltip: "Mark this GameObject as static"));

            RefreshTagLayerOptions();
            var tagValues = _tagLabels;
            var tagLabels = _tagLabels;
            var tagIndex = Array.FindIndex(tagValues,
                tag => tag.Equals(gameObject.tag, StringComparison.Ordinal));
            if (tagIndex < 0)
            {
                tagValues = [gameObject.tag, .. tagValues];
                tagLabels = [$"Missing: {gameObject.tag}", .. tagLabels];
                tagIndex = 0;
            }
            var layers = _layerOptions;
            var layerLabels = _layerLabels;
            var layerIndex = Array.FindIndex(layers, item => item.Value == gameObject.layer);
            if (layerIndex < 0)
            {
                layers = [.. layers, (LayerMask.LayerToName(gameObject.layer), gameObject.layer)];
                layerLabels = [.. layerLabels, $"2^{SortingLayer.IndexOf(gameObject.layer)}"];
                layerIndex = layers.Length - 1;
            }
            tagLabels = [.. tagLabels, "Add Tag..."];
            layerLabels = [.. layerLabels, "Edit Layers..."];
            var tagSettingsIndex = tagLabels.Length - 1;
            var layerSettingsIndex = layerLabels.Length - 1;
            var oldLabelWidth = EditorGUI.labelWidth;
            EditorGUI.labelWidth = 44;
            int nextTag;
            int nextLayer;
            if (GUI.visibleViewWidth >= 360)
            {
                var row = GUILayoutUtility.GetControlRect(EditorGUIUtility.singleLineHeight,
                    GUILayout.ExpandWidth(true));
                var half = Fix64.Max(1, row.width / 2 - 3);
                nextTag = EditorGUI.AdvancedPopup(new Rect(row.x, row.y, half, row.height), "Tag", tagIndex,
                    tagLabels);
                nextLayer = EditorGUI.AdvancedPopup(new Rect(row.x + half + 6, row.y, half, row.height), "Layer",
                    layerIndex, layerLabels);
            }
            else
            {
                nextTag = EditorGUILayout.AdvancedPopup("Tag", tagIndex, tagLabels);
                nextLayer = EditorGUILayout.AdvancedPopup("Layer", layerIndex, layerLabels);
            }
            EditorGUI.labelWidth = oldLabelWidth;

            if (nextTag == tagSettingsIndex)
            {
                TagLayerSettingsProvider.OpenTags();
                nextTag = tagIndex;
            }
            if (nextLayer == layerSettingsIndex)
            {
                TagLayerSettingsProvider.OpenLayers();
                nextLayer = layerIndex;
            }

            var changed = name != gameObject.name || active != gameObject.activeSelf ||
                          isStatic != gameObject.isStatic || nextTag != tagIndex || nextLayer != layerIndex;
            if (!changed || readOnly) return;
            Undo.RecordObject(gameObject, "Edit GameObject");
            if (name != gameObject.name) gameObject.name = name;
            if (active != gameObject.activeSelf) gameObject.SetActive(active);
            if (isStatic != gameObject.isStatic) gameObject.isStatic = isStatic;
            if (nextTag != tagIndex)
                gameObject.tag = tagValues[Math.Clamp(nextTag, 0, tagValues.Length - 1)];
            if (nextLayer != layerIndex)
                gameObject.layer = layers[Math.Clamp(nextLayer, 0, layers.Length - 1)].Value;
            EditorUtility.SetDirty(gameObject);
            if (gameObject.scene is { } scene) app.MarkDirty(scene);
        }

        private void RefreshTagLayerOptions()
        {
            if (_tagVersion != TagManager.version)
            {
                _tagLabels = TagManager.tags.ToArray();
                _tagVersion = TagManager.version;
            }
            if (_layerVersion == LayerMask.version) return;
            _layerOptions = LayerMask.layers
                .Select(static layer => (layer.Name, layer.Value))
                .ToArray();
            if (_layerOptions.Length == 0) _layerOptions = [("World", SortingLayer.Default)];
            _layerLabels = _layerOptions.Select(static item =>
                $"2^{SortingLayer.IndexOf(item.Value)}  {item.Name}").ToArray();
            _layerVersion = LayerMask.version;
        }

        private void ShowComponentMenu(Component component, bool readOnly = false)
        {
            var menu = new GenericMenu();
            if (readOnly || component is MissingComponent) menu.AddDisabledItem(new GUIContent("Reset"));
            else menu.AddItem(new GUIContent("Reset"), false, () =>
            {
                if (!ComponentClipboard.Reset(component)) return;
                if (component.gameObject.scene is { } scene) app.MarkDirty(scene);
                RebuildEditor();
            });
            menu.AddItem(new GUIContent("Copy Component"), false, () => ComponentClipboard.Copy(component));
            if (!readOnly && ComponentClipboard.CanPaste(component))
                menu.AddItem(new GUIContent("Paste Component Values"), false, () =>
                {
                    if (!ComponentClipboard.Paste(component)) return;
                    if (component.gameObject.scene is { } scene) app.MarkDirty(scene);
                    RebuildEditor();
                });
            else menu.AddDisabledItem(new GUIContent("Paste Component Values"));
            if (component is MonoBehaviour behaviour)
            {
                menu.AddSeparator(string.Empty);
                if (app.FindScriptSource(behaviour) is not null)
                    menu.AddItem(new GUIContent("Edit Script"), false,
                        () => app.SelectScript(behaviour, open: true));
                else menu.AddDisabledItem(new GUIContent("Edit Script"));
            }
            if (!readOnly)
            {
                var beforeContextCommands = menu.GetItemCount();
                app._menuItems.PopulateContext(menu, component);
                var componentCommands = ComponentContextMenuRegistry.GetCommands(component.GetType());
                if (componentCommands.Length > 0 && menu.GetItemCount() == beforeContextCommands)
                    menu.AddSeparator(string.Empty);
                foreach (var command in componentCommands)
                {
                    var captured = command;
                    menu.AddItem(new GUIContent(command.Name), false,
                        () => InvokeComponentContextMenu(component, captured));
                }
            }
            if (component is not Transform)
            {
                menu.AddSeparator(string.Empty);
                if (readOnly || !component.gameObject.CanRemoveComponent(component))
                    menu.AddDisabledItem(new GUIContent("Remove Component"));
                else menu.AddItem(new GUIContent("Remove Component"), false,
                    () => app.RemoveComponent(component));
            }
            menu.ShowAsContext();
        }

        private void InvokeComponentContextMenu(Component component, ComponentContextMenuCommand command)
        {
            var scene = component.gameObject.scene;
            Undo.RecordObject(component, command.Name);
            if (!EditorFeatureGuard.Invoke(component, command.MethodName, () => command.Callback(component))) return;
            EditorUtility.SetDirty(component);
            if (scene is not null) app.MarkDirty(scene);
            RebuildEditor();
        }
        protected override void OnDisable()
        {
            DisposeEditors();
        }
        private void DisposeEditors()
        {
            _editor?.Dispose(); _editor = null;
            foreach (var componentEditor in _componentEditors.Values) componentEditor.Dispose();
            _componentEditors.Clear(); _drawnComponents.Clear(); _collapsedComponents.Clear();
        }
    }

    private sealed class ImGuiProjectWindow(GpuEditorApplication app) : EditorWindow
    {
        private GpuEditorApplication Application => app;
        private string _search = string.Empty;
        private string _parsedSearch = string.Empty;
        private ProjectSearchFilter _searchFilter = ProjectSearchFilter.Parse(string.Empty);
        private string _projectBrowserMode = "OneColumn";
        private Fix64 _foldersWidth = 240;
        private Fix64 _packagesHeight;
        private bool _packagesHeightInitialized;
        private Fix64 _packagesDragStartY;
        private Fix64 _packagesDragStartHeight;
        private Fix64 _thumbnailSize = 64;
        private Vector2 _assetScroll;
        private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase) { "Assets", "Packages" };
        private ProjectBrowserItem[]? _cache;
        private ProjectBrowserItem[]? _indexedCache;
        private readonly HashSet<string> _parentPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _folderParentPaths = new(StringComparer.OrdinalIgnoreCase);
        private string? _pingedAssetPath;
        private string? _selectedPath;
        private string? _renamingPath;
        private string _renameValue = string.Empty;
        private string? _dragCandidatePath;
        private string? _draggedPath;
        private string? _dropTargetPath;
        private Vector2 _dragStart;
        private readonly ImGuiScrollRegion _assetsScroll = new();
        private readonly ImGuiScrollRegion _packagesScroll = new();
        private readonly TreeViewState<ulong> _assetsTreeState = new();
        private readonly TreeViewState<ulong> _packagesTreeState = new();
        private ProjectTreeView? _assetsTreeView;
        private ProjectTreeView? _packagesTreeView;
        private readonly Dictionary<string, ulong> _projectTreeIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, string> _projectTreePaths = [];
        public ImGuiProjectWindow() : this(null!) { }
        internal void Invalidate()
        {
            _cache = null;
            _indexedCache = null;
            _assetsTreeView?.Invalidate();
            _packagesTreeView?.Invalidate();
            Repaint();
        }
        internal void Toggle(string path)
        {
            path = ProjectBrowserPath.Normalize(path);
            if (!_expanded.Add(path)) _expanded.Remove(path);
        }
        internal void Ping(string path)
        {
            path = ProjectBrowserPath.Normalize(path);
            _pingedAssetPath = path;
            _selectedPath = path;
            for (var parent = ProjectBrowserPath.Parent(path); !string.IsNullOrWhiteSpace(parent);
                 parent = ProjectBrowserPath.Parent(parent))
                _expanded.Add(parent);
            Repaint();
        }
        protected override void OnGUI()
        {
            if (_draggedPath is not null && !DragAndDrop.isDragging) ClearDrag();
            DrawWindowToolbarBackground();
            GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbar.fixedHeight));
            var createClicked = EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Add, "Create asset",
                GUILayout.Width(24));
            var createRect = GUILayoutUtility.GetLastRect();
            if (createClicked) ShowCreateMenu(createRect);
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Refresh assets", GUILayout.Width(24)))
                app.RefreshAssets();
            _search = EditorToolbar.SearchField(_search, GUILayout.Width(Fix64.Max(60,
                GUIUtility.currentViewWidth - 68)));
            GUILayout.EndHorizontal();
            var items = _cache ??= BuildItems();
            EnsurePathIndexes(items);
            HandleKeyboard(items);
            var contentY = GUILayoutUtility.GetLastRect().yMax + 2;
            const int footerHeight = 24;
            var listHeight = Fix64.Max(1, GUIUtility.currentViewHeight - contentY - footerHeight);
            var contentRect = new Rect(0, contentY, GUIUtility.currentViewWidth, listHeight);
            var footerRect = new Rect(0, contentY + listHeight, GUIUtility.currentViewWidth, footerHeight);
            if (IsFooterPointerEvent(footerRect))
            {
                if ((Event.current.type is EventType.MouseUp or EventType.DragPerform or EventType.DragExited) &&
                    _draggedPath is not null) ClearDrag();
                DrawFooter(footerRect, items);
                if (Event.current.type != EventType.Used) Event.current.Use();
                return;
            }
            if (IsTwoColumn) DrawTwoColumn(items, contentRect);
            else DrawOneColumn(items, contentRect);
            if ((Event.current.type is EventType.MouseUp or EventType.DragPerform or EventType.DragExited) &&
                _draggedPath is not null) ClearDrag();
            DrawFooter(footerRect, items);
        }

        private static bool IsFooterPointerEvent(Rect rect)
        {
            var current = Event.current;
            return (current.type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag or
                EventType.ContextClick or EventType.ScrollWheel or EventType.DragUpdated or EventType.DragPerform or
                EventType.DragExited) && rect.Contains(current.mousePosition);
        }

        private bool IsTwoColumn => _projectBrowserMode.Equals("TwoColumn", StringComparison.OrdinalIgnoreCase);

        public override void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("One Column Layout"), !IsTwoColumn, () => SetBrowserMode("OneColumn"));
            menu.AddItem(new GUIContent("Two Column Layout"), IsTwoColumn, () => SetBrowserMode("TwoColumn"));
        }

        private void SetBrowserMode(string mode)
        {
            _projectBrowserMode = mode;
            Repaint();
        }

        internal void CaptureLayout(EditorLayoutDocument document)
        {
            document.ProjectBrowserMode = IsTwoColumn ? "TwoColumn" : "OneColumn";
            document.ProjectFoldersWidth = (float)_foldersWidth;
            document.ProjectPackagesHeight = _packagesHeightInitialized ? (float)_packagesHeight : 0;
            document.ProjectThumbnailSize = (float)_thumbnailSize;
        }

        internal void ApplyLayout(EditorLayoutDocument document)
        {
            _projectBrowserMode = document.ProjectBrowserMode.Equals("TwoColumn", StringComparison.OrdinalIgnoreCase)
                ? "TwoColumn" : "OneColumn";
            _foldersWidth = Fix64.Clamp((Fix64)document.ProjectFoldersWidth, 120, 600);
            _packagesHeight = Fix64.Max(0, (Fix64)document.ProjectPackagesHeight);
            _packagesHeightInitialized = _packagesHeight > 0;
            _thumbnailSize = Fix64.Clamp((Fix64)document.ProjectThumbnailSize, 32, 144);
        }

        private void DrawOneColumn(IReadOnlyList<ProjectBrowserItem> items, Rect contentRect)
        {
            DrawProjectTreePanes(items, contentRect, foldersOnly: false);
        }

        private void DrawTwoColumn(IReadOnlyList<ProjectBrowserItem> items, Rect contentRect)
        {
            _foldersWidth = Fix64.Clamp(_foldersWidth, 120, Fix64.Max(120, contentRect.width - 180));
            var splitter = new Rect(contentRect.x + _foldersWidth - 2, contentRect.y, 5, contentRect.height);
            HandleFoldersSplitter(splitter, contentRect);
            GUI.Box(new Rect(contentRect.x + _foldersWidth, contentRect.y, 1, contentRect.height),
                GUIContent.none, EditorStyles.separator);

            DrawProjectTreePanes(items,
                new Rect(contentRect.x, contentRect.y, _foldersWidth, contentRect.height), foldersOnly: true);

            var right = new Rect(contentRect.x + _foldersWidth + 1, contentRect.y,
                Fix64.Max(1, contentRect.width - _foldersWidth - 1), contentRect.height);
            DrawAssetGrid(items, right);
        }

        private void DrawProjectTreePanes(
            IReadOnlyList<ProjectBrowserItem> items,
            Rect treeRect,
            bool foldersOnly)
        {
            var visible = (foldersOnly ? VisibleFolders(items) : VisibleItems(items)).ToArray();
            var visibleAssets = visible.Where(item => !item.IsPackage).ToArray();
            var visiblePackages = visible.Where(item => item.IsPackage).ToArray();
            if (visibleAssets.Length == 0)
            {
                DrawProjectTreePane(treeRect, _packagesScroll, visiblePackages, foldersOnly,
                    packages: true);
                return;
            }
            if (visiblePackages.Length == 0)
            {
                DrawProjectTreePane(treeRect, _assetsScroll, visibleAssets, foldersOnly,
                    packages: false);
                return;
            }
            if (!_packagesHeightInitialized)
            {
                var visibleAssetRows = visibleAssets.Length;
                _packagesHeight = ProjectBrowserSplitLayoutUtility.FromAssetContentHeight(treeRect,
                    visibleAssetRows * EditorTreeViewGUI.rowHeight + 4);
                _packagesHeightInitialized = true;
            }

            var layout = ProjectBrowserSplitLayoutUtility.Calculate(treeRect, _packagesHeight);
            HandlePackagesSplitter(layout, treeRect);
            DrawProjectTreePane(layout.Assets, _assetsScroll, visibleAssets, foldersOnly, packages: false);
            DrawProjectTreePane(layout.Packages, _packagesScroll, visiblePackages, foldersOnly, packages: true);
            GUI.Box(layout.Separator, GUIContent.none, EditorStyles.separator);
        }

        private void DrawProjectTreePane(
            Rect rect,
            ImGuiScrollRegion scroll,
            IEnumerable<ProjectBrowserItem> items,
            bool foldersOnly,
            bool packages)
        {
            var materialized = items.ToArray();
            var tree = packages
                ? _packagesTreeView ??= new ProjectTreeView(this, _packagesTreeState, true)
                : _assetsTreeView ??= new ProjectTreeView(this, _assetsTreeState, false);
            tree.Configure(_cache ?? materialized, foldersOnly, _search);
            tree.OnGUI(rect);
            if (packages) _packagesScroll.position = _packagesTreeState.scrollPos;
            else _assetsScroll.position = _assetsTreeState.scrollPos;
        }

        private void HandlePackagesSplitter(ProjectBrowserSplitLayout layout, Rect treeRect)
        {
            var id = GUIUtility.GetControlID("ProjectPackagesSplitter".GetHashCode(StringComparison.Ordinal),
                FocusType.Passive, layout.SeparatorHitArea);
            var current = Event.current;
            EditorGUIUtility.AddCursorRect(GUIUtility.hotControl == id
                    ? new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight)
                    : layout.SeparatorHitArea,
                MouseCursor.ResizeVertical);
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 &&
                                                  layout.SeparatorHitArea.Contains(current.mousePosition):
                    GUIUtility.hotControl = id;
                    _packagesDragStartY = current.mousePosition.y;
                    _packagesDragStartHeight = layout.Packages.height;
                    current.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    _packagesHeight = ProjectBrowserSplitLayoutUtility.ResizePackagesHeight(
                        _packagesDragStartHeight, current.mousePosition.y - _packagesDragStartY, treeRect.height);
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
            }
        }

        private void HandleFoldersSplitter(Rect splitter, Rect contentRect)
        {
            var id = GUIUtility.GetControlID("ProjectFoldersSplitter".GetHashCode(StringComparison.Ordinal),
                FocusType.Passive, splitter);
            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeHorizontal);
            var current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                splitter.Contains(current.mousePosition))
            {
                GUIUtility.hotControl = id;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                _foldersWidth = Fix64.Clamp(current.mousePosition.x - contentRect.x, 120,
                    Fix64.Max(120, contentRect.width - 180));
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                current.Use();
            }
        }

        private IEnumerable<ProjectBrowserItem> VisibleFolders(IReadOnlyList<ProjectBrowserItem> items)
        {
            foreach (var item in items.Where(item => item.IsDirectory &&
                                                      !string.IsNullOrWhiteSpace(item.EffectiveDisplayName)))
            {
                var visible = true;
                for (var parent = item.ParentPath; parent is not null;
                     parent = ProjectBrowserPath.Parent(parent))
                {
                    if (_expanded.Contains(parent)) continue;
                    visible = false;
                    break;
                }
                if (visible) yield return item;
            }
        }

        private void DrawAssetGrid(IReadOnlyList<ProjectBrowserItem> items, Rect viewport)
        {
            var folder = SelectedFolder(items);
            var visible = (string.IsNullOrWhiteSpace(_search)
                    ? DirectChildren(items, folder)
                    : ProjectBrowserItemOrdering.Sort(items.Where(item =>
                        !item.VirtualPath.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
                        !item.VirtualPath.Equals("Packages", StringComparison.OrdinalIgnoreCase) &&
                        Matches(item))))
                .Where(item => !string.IsNullOrWhiteSpace(item.EffectiveDisplayName))
                .ToArray();
            var labelHeight = Fix64.Max(EditorGUIUtility.singleLineHeight + 2,
                GUITextMetrics.MeasureLineHeight(EditorStyles.miniLabel.fontSize, GUIUtility.fontFamily));
            var cellWidth = Fix64.Max(88, _thumbnailSize + 22);
            var cellHeight = _thumbnailSize + labelHeight + 12;
            var columns = Math.Max(1, (int)((viewport.width - 10) / cellWidth));
            var rows = Math.Max(1, (visible.Length + columns - 1) / columns);
            var contentHeight = Fix64.Max(viewport.height, rows * cellHeight + 10);
            _assetScroll = GUI.BeginScrollView(viewport, _assetScroll,
                new Rect(0, 0, Fix64.Max(1, viewport.width - 10), contentHeight));
            try
            {
                for (var index = 0; index < visible.Length; index++)
                    DrawGridItem(visible[index], index % columns, index / columns, cellWidth, cellHeight);
            }
            finally { GUI.EndScrollView(); }
        }

        private void DrawGridItem(ProjectBrowserItem item, int column, int row, Fix64 cellWidth, Fix64 cellHeight)
        {
            var cell = new Rect(5 + column * cellWidth, 5 + row * cellHeight,
                cellWidth - 4, cellHeight - 4);
            HandleDrag(item, cell);
            var selected = item.NormalizedPath.Equals(_selectedPath, StringComparison.OrdinalIgnoreCase) ||
                           item.NormalizedPath.Equals(_pingedAssetPath, StringComparison.OrdinalIgnoreCase);
            var clicked = GUI.Button(cell, new GUIContent(string.Empty, tooltip: ItemTooltip(item)),
                TreeRowStyle(selected));
            var preview = new Rect(cell.x + (cell.width - _thumbnailSize) / 2, cell.y + 4,
                _thumbnailSize, _thumbnailSize);
            GUI.DrawTexture(preview, PreviewPath(item));
            var labelRect = new Rect(cell.x + 2, preview.yMax + 2, cell.width - 4,
                EditorGUIUtility.singleLineHeight + 2);
            GUI.Label(labelRect, new GUIContent(item.EffectiveDisplayName,
                tooltip: ItemTooltip(item)), EditorStyles.centeredMiniLabel);
            if (clicked)
            {
                _selectedPath = item.NormalizedPath;
                _pingedAssetPath = null;
                if (app is not null) app.Select(item);
                if (Event.current.clickCount >= 2)
                {
                    if (item.IsDirectory) _expanded.Add(item.NormalizedPath);
                    else if (item.Asset is { } asset && app is not null) app.OpenAsset(asset);
                    else OpenExternal(item.SourcePath);
                }
            }
            if (Event.current.type == EventType.ContextClick && cell.Contains(Event.current.mousePosition))
            {
                _selectedPath = item.NormalizedPath;
                if (app is not null) app.Select(item);
                ShowItemContextMenu(item);
                Event.current.Use();
            }
        }

        private string SelectedFolder(IReadOnlyList<ProjectBrowserItem> items)
        {
            if (_selectedPath is null) return "Assets";
            var selected = items.FirstOrDefault(item => item.VirtualPath.Equals(
                _selectedPath, StringComparison.OrdinalIgnoreCase));
            return selected is null ? "Assets" : selected.IsDirectory
                ? selected.NormalizedPath : selected.ParentPath ?? "Assets";
        }

        private static IEnumerable<ProjectBrowserItem> DirectChildren(
            IEnumerable<ProjectBrowserItem> items, string folder)
        {
            var normalizedFolder = ProjectBrowserPath.Normalize(folder);
            return ProjectBrowserItemOrdering.Sort(items.Where(item => string.Equals(
                item.ParentPath, normalizedFolder, StringComparison.OrdinalIgnoreCase)));
        }

        private static string PreviewPath(ProjectBrowserItem item)
        {
            if (!item.IsDirectory && item.AssetType is "Texture" or "Image" && File.Exists(item.SourcePath))
                return item.SourcePath;
            if (item.AssetType == "Missing Package") return EditorBuiltinIcons.Toolbar.Warning;
            if (item.AssetType == "Package" || item.VirtualPath == "Packages")
                return "Icons/Windows/PackageManager.png";
            if (item.IsDirectory) return EditorAssetIcons.ClosedFolder;
            return EditorAssetIcons.GetIconPath(item.SourcePath);
        }

        private ProjectBrowserItem[] BuildItems()
        {
            var items = ProjectBrowserTreeBuilder.Build(app._workspace.AssetsPath, app.Assets, app.Packages);
            foreach (var package in items.Where(item => item.IsPackage && item.ParentPath == "Packages"))
                _expanded.Add(package.NormalizedPath);
            return items;
        }

        private void EnsurePathIndexes(ProjectBrowserItem[] items)
        {
            if (ReferenceEquals(_indexedCache, items)) return;
            _indexedCache = items;
            _parentPaths.Clear();
            _folderParentPaths.Clear();
            foreach (var parent in items.Select(item => item.ParentPath).OfType<string>())
                _parentPaths.Add(parent);
            foreach (var parent in items.Where(item => item.IsDirectory)
                         .Select(item => item.ParentPath).OfType<string>())
                _folderParentPaths.Add(parent);
        }

        private void DrawItem(ProjectBrowserItem item)
            => DrawTreeItem(item, _parentPaths);

        private void DrawFolderItem(ProjectBrowserItem item)
            => DrawTreeItem(item, _folderParentPaths);

        private void DrawTreeItem(ProjectBrowserItem item, IReadOnlySet<string> expandablePaths)
        {
            var rowHeight = EditorTreeViewGUI.BeginRow();
            var rowRect = GUILayoutUtility.GetLastRect();
            HandleDrag(item, rowRect);
            GUILayout.Space(item.Depth * 14);
            var canExpand = item.IsDirectory && expandablePaths.Contains(item.NormalizedPath);
            var hasEntries = item.IsDirectory && _parentPaths.Contains(item.NormalizedPath);
            var expanded = !string.IsNullOrWhiteSpace(_search) || _expanded.Contains(item.NormalizedPath);
            var icon = item.AssetType == "Missing Package"
                ? EditorBuiltinIcons.Toolbar.Warning
                : item.AssetType == "Package" || item.VirtualPath == "Packages"
                    ? "Icons/Windows/PackageManager.png"
                : item.IsDirectory
                    ? hasEntries
                        ? canExpand && expanded ? EditorAssetIcons.OpenFolder : EditorAssetIcons.ClosedFolder
                        : EditorAssetIcons.EmptyFolder
                    : EditorAssetIcons.GetIconPath(item.SourcePath);
            var selected = item.NormalizedPath.Equals(_selectedPath, StringComparison.OrdinalIgnoreCase) ||
                           item.NormalizedPath.Equals(_pingedAssetPath, StringComparison.OrdinalIgnoreCase) ||
                           item.NormalizedPath.Equals(_dropTargetPath, StringComparison.OrdinalIgnoreCase);
            if (canExpand)
            {
                var foldoutIcon = expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen :
                    EditorBuiltinIcons.Toolbar.FoldoutClosed;
                if (GUILayout.Button(new GUIContent(string.Empty, foldoutIcon, expanded ? "Collapse" : "Expand"),
                        EditorStyles.foldout, GUILayout.Width(18), GUILayout.Height(rowHeight)))
                    Toggle(item.VirtualPath);
            }
            else GUILayout.Space(18);
            var clicked = false;
            if (_renamingPath?.Equals(item.VirtualPath, StringComparison.OrdinalIgnoreCase) == true)
            {
                var commit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                var cancel = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
                GUI.SetNextControlName("ProjectRename");
                _renameValue = GUILayout.TextField(_renameValue, GUILayout.ExpandWidth(true),
                    GUILayout.Height(rowHeight));
                if (commit) CommitRename(item);
                else if (cancel) CancelRename();
            }
            else
                clicked = GUILayout.Button(new GUIContent(item.EffectiveDisplayName, icon, ItemTooltip(item)),
                    TreeRowStyle(selected), GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
            if (clicked)
            {
                _selectedPath = item.NormalizedPath;
                _pingedAssetPath = null;
                app.Select(item);
                if (item.Asset is { } asset)
                {
                    if (Event.current.clickCount >= 2) app.OpenAsset(asset);
                }
                else if (Event.current.clickCount >= 2)
                {
                    if (item.IsDirectory) Toggle(item.VirtualPath);
                    else OpenExternal(item.SourcePath);
                }
            }
            EditorTreeViewGUI.EndRow();
            if (Event.current.type == EventType.ContextClick && rowRect.Contains(Event.current.mousePosition))
            {
                _selectedPath = item.NormalizedPath;
                app.Select(item);
                ShowItemContextMenu(item);
                Event.current.Use();
            }
        }

        private ProjectTreeItem BuildProjectTreeRoot(bool packages, bool foldersOnly,
            IReadOnlyList<ProjectBrowserItem> items)
        {
            var root = new ProjectTreeItem(packages ? ulong.MaxValue - 1 : ulong.MaxValue, -1,
                packages ? "Packages Root" : "Assets Root", null);
            var section = packages ? "Packages" : "Assets";
            var byParent = items.Where(item => item.IsPackage == packages && (!foldersOnly || item.IsDirectory))
                .GroupBy(item => item.ParentPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            var sectionItem = items.FirstOrDefault(item => item.IsPackage == packages &&
                item.NormalizedPath.Equals(section, StringComparison.OrdinalIgnoreCase));
            if (sectionItem is not null) root.AddChild(BuildProjectTreeItem(sectionItem, byParent));
            return root;
        }

        private ProjectTreeItem BuildProjectTreeItem(ProjectBrowserItem item,
            IReadOnlyDictionary<string, ProjectBrowserItem[]> byParent)
        {
            var node = new ProjectTreeItem(ProjectTreeId(item.NormalizedPath), 0,
                item.EffectiveDisplayName, item);
            if (item.IsDirectory && byParent.TryGetValue(item.NormalizedPath, out var children))
                foreach (var child in ProjectBrowserItemOrdering.Sort(children))
                    node.AddChild(BuildProjectTreeItem(child, byParent));
            return node;
        }

        private ulong ProjectTreeId(string path)
        {
            path = ProjectBrowserPath.Normalize(path);
            if (_projectTreeIds.TryGetValue(path, out var existing)) return existing;
            var hash = 14695981039346656037UL;
            foreach (var character in path)
            {
                var normalized = char.ToUpperInvariant(character);
                hash ^= normalized;
                hash *= 1099511628211UL;
            }
            if (hash is 0 or ulong.MaxValue or ulong.MaxValue - 1) hash ^= 0x9E3779B97F4A7C15UL;
            while (_projectTreePaths.TryGetValue(hash, out var collision) &&
                   !collision.Equals(path, StringComparison.OrdinalIgnoreCase))
                hash = unchecked(hash * 1099511628211UL + 0x9E3779B97F4A7C15UL);
            _projectTreeIds[path] = hash;
            _projectTreePaths[hash] = path;
            return hash;
        }

        private void SynchronizeProjectExpanded(ProjectTreeView tree)
        {
            var sectionPaths = tree.GetExpanded().Select(id => _projectTreePaths.GetValueOrDefault(id))
                .Where(path => path is not null).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _expanded.Where(path => tree.OwnsPath(path) && !sectionPaths.Contains(path))
                         .ToArray())
                _expanded.Remove(path);
            foreach (var path in sectionPaths) _expanded.Add(path);
        }

        private void DrawProjectTreeRow(TreeView<ulong>.RowGUIArgs args, ProjectTreeView tree)
        {
            if (args.item is not ProjectTreeItem { Item: { } item } node) return;
            var rowRect = args.rowRect;
            GUI.Box(rowRect, GUIContent.none,
                args.selected ? EditorStyles.treeViewRowSelected : EditorStyles.treeViewRow);
            if (_dropTargetPath?.Equals(item.NormalizedPath, StringComparison.OrdinalIgnoreCase) == true)
                GUI.Box(rowRect, GUIContent.none, EditorStyles.treeViewRowSelected);
            HandleDrag(item, rowRect);

            var hasEntries = item.IsDirectory && _parentPaths.Contains(item.NormalizedPath);
            var expanded = tree.Searching || tree.IsExpanded(node.id);
            var icon = item.AssetType == "Missing Package"
                ? EditorBuiltinIcons.Toolbar.Warning
                : item.AssetType == "Package" || item.VirtualPath == "Packages"
                    ? "Icons/Windows/PackageManager.png"
                    : item.IsDirectory
                        ? hasEntries
                            ? node.hasChildren && expanded ? EditorAssetIcons.OpenFolder : EditorAssetIcons.ClosedFolder
                            : EditorAssetIcons.EmptyFolder
                        : EditorAssetIcons.GetIconPath(item.SourcePath);
            var foldoutX = rowRect.x + Math.Max(0, node.depth) * 14;
            var foldoutRect = new Rect(foldoutX, rowRect.y, 18, rowRect.height);
            if (!tree.Searching && node.hasChildren)
            {
                var foldoutIcon = expanded ? EditorBuiltinIcons.Toolbar.FoldoutOpen :
                    EditorBuiltinIcons.Toolbar.FoldoutClosed;
                if (GUI.Button(foldoutRect, new GUIContent(string.Empty, foldoutIcon,
                            expanded ? "Collapse" : "Expand"), EditorStyles.foldout))
                    tree.SetItemExpanded(node.id, !expanded);
            }
            var labelRect = new Rect(foldoutRect.xMax + 3, rowRect.y,
                Fix64.Max(1, rowRect.xMax - foldoutRect.xMax - 3), rowRect.height);
            if (_renamingPath?.Equals(item.VirtualPath, StringComparison.OrdinalIgnoreCase) == true)
            {
                var commit = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                var cancel = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape;
                GUI.SetNextControlName("ProjectRename");
                _renameValue = GUI.TextField(labelRect, _renameValue);
                if (commit) CommitRename(item);
                else if (cancel) CancelRename();
            }
            else
                GUI.Label(labelRect, new GUIContent(item.EffectiveDisplayName, icon, ItemTooltip(item)),
                    args.selected ? EditorStyles.treeViewRowSelected : EditorStyles.treeViewRow);
        }

        private sealed class ProjectTreeView(
            ImGuiProjectWindow owner,
            TreeViewState<ulong> state,
            bool packages) : TreeView<ulong>(state)
        {
            private IReadOnlyList<ProjectBrowserItem> _items = [];
            private bool _foldersOnly;
            private bool _invalid = true;

            internal void Invalidate() => _invalid = true;

            internal void Configure(IReadOnlyList<ProjectBrowserItem> items, bool foldersOnly, string search)
            {
                var sourceChanged = !ReferenceEquals(_items, items) || _foldersOnly != foldersOnly;
                var searchChanged = !state.searchString.Equals(search ?? string.Empty, StringComparison.Ordinal);
                _items = items;
                _foldersOnly = foldersOnly;
                state.searchString = search ?? string.Empty;
                var expanded = owner._expanded.Where(OwnsPath).Select(owner.ProjectTreeId).ToList();
                if (_invalid || sourceChanged || searchChanged || !isInitialized)
                {
                    state.expandedIDs = expanded;
                    Reload();
                    _invalid = false;
                }
                else
                    SetExpanded(expanded);

                var selection = owner._selectedPath is { } path && OwnsPath(path)
                    ? new[] { owner.ProjectTreeId(path) }
                    : [];
                SetSelection(selection);
            }

            protected override TreeViewItem<ulong> BuildRoot() =>
                owner.BuildProjectTreeRoot(packages, _foldersOnly, _items);

            protected override IList<TreeViewItem<ulong>> BuildRows(TreeViewItem<ulong> root)
            {
                var rows = new List<TreeViewItem<ulong>>();
                if (root.children is null) return rows;
                foreach (var child in root.children) AddVisible(child, rows);
                return rows;
            }

            private void AddVisible(TreeViewItem<ulong> item, ICollection<TreeViewItem<ulong>> rows)
            {
                if (hasSearch && !BranchMatches(item)) return;
                rows.Add(item);
                if (item.children is null || (!hasSearch && !IsExpanded(item.id))) return;
                foreach (var child in item.children) AddVisible(child, rows);
            }

            private bool BranchMatches(TreeViewItem<ulong> item) =>
                item is ProjectTreeItem { Item: { } projectItem } && owner.Matches(projectItem) ||
                item.children?.Any(BranchMatches) == true;

            protected override bool CanChangeExpandedState(TreeViewItem<ulong> item) => false;
            protected override bool CanRename(TreeViewItem<ulong> item) => false;
            protected override bool CanMultiSelect(TreeViewItem<ulong> item) => false;
            protected override void RowGUI(RowGUIArgs args) => owner.DrawProjectTreeRow(args, this);

            protected override void SelectionChanged(IList<ulong> selectedIds)
            {
                var id = selectedIds.LastOrDefault();
                if (id == 0 || FindItem(id, rootItem) is not ProjectTreeItem { Item: { } item }) return;
                owner._selectedPath = item.NormalizedPath;
                owner._pingedAssetPath = null;
                if (owner.Application is not null) owner.Application.Select(item);
            }

            protected override void DoubleClickedItem(ulong id)
            {
                if (FindItem(id, rootItem) is not ProjectTreeItem { Item: { } item }) return;
                if (item.IsDirectory) SetExpanded(id, true);
                else if (item.Asset is { } asset && owner.Application is not null) owner.Application.OpenAsset(asset);
                else OpenExternal(item.SourcePath);
            }

            protected override void ContextClickedItem(ulong id)
            {
                if (FindItem(id, rootItem) is ProjectTreeItem { Item: { } item })
                {
                    owner._selectedPath = item.NormalizedPath;
                    if (owner.Application is not null) owner.Application.Select(item);
                    owner.ShowItemContextMenu(item);
                }
            }

            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args) =>
                DragAndDrop.visualMode;
            protected override void ExpandedStateChanged() => owner.SynchronizeProjectExpanded(this);
            internal void SetItemExpanded(ulong id, bool expanded) => SetExpanded(id, expanded);
            internal bool Searching => hasSearch;
            internal bool OwnsPath(string path) => ProjectBrowserPath.Normalize(path).Equals(
                packages ? "Packages" : "Assets", StringComparison.OrdinalIgnoreCase) ||
                ProjectBrowserPath.Normalize(path).StartsWith(packages ? "Packages/" : "Assets/",
                    StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ProjectTreeItem(ulong id, int depth, string displayName, ProjectBrowserItem? item)
            : TreeViewItem<ulong>(id, depth, displayName)
        {
            internal ProjectBrowserItem? Item { get; } = item;
        }

        private void ShowItemContextMenu(ProjectBrowserItem item)
        {
            _selectedPath = item.NormalizedPath;
            var menu = new GenericMenu();
            (app._menuItems ?? MenuItemRegistry.Discover()).PopulateRoot(menu, "Assets", app.SelectedAsset);
            menu.ShowAsContext();
        }

        private void HandleKeyboard(IReadOnlyList<ProjectBrowserItem> items)
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (_renamingPath is not null && Event.current.keyCode == KeyCode.Return)
            {
                var renamed = items.FirstOrDefault(item => item.VirtualPath.Equals(
                    _renamingPath, StringComparison.OrdinalIgnoreCase));
                if (renamed is not null) CommitRename(renamed);
                else CancelRename();
                Event.current.Use();
                return;
            }
            if (_renamingPath is not null && Event.current.keyCode == KeyCode.Escape)
            {
                CancelRename();
                Event.current.Use();
                return;
            }
            if (Event.current.keyCode == KeyCode.F2 && !EditorGUIUtility.editingTextField &&
                _selectedPath is { } selectedPath &&
                items.FirstOrDefault(item => item.VirtualPath.Equals(selectedPath,
                    StringComparison.OrdinalIgnoreCase)) is { } selected && CanEdit(selected))
            {
                BeginRename(selected);
                Event.current.Use();
            }
        }

        private void BeginRename(ProjectBrowserItem item)
        {
            if (!CanEdit(item)) return;
            _renamingPath = item.VirtualPath;
            _renameValue = AssetPathUtility.EditableName(item.VirtualPath, item.IsDirectory);
            _selectedPath = item.VirtualPath;
            for (var parent = item.ParentPath; !string.IsNullOrWhiteSpace(parent);
                 parent = ProjectBrowserPath.Parent(parent))
                _expanded.Add(parent);
            GUI.FocusControl("ProjectRename");
            Repaint();
        }

        private void CommitRename(ProjectBrowserItem item)
        {
            var value = _renameValue.Trim();
            if (value.Length > 0 && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                value != AssetPathUtility.EditableName(item.VirtualPath, item.IsDirectory))
            {
                var error = AssetDatabase.RenameAsset(item.VirtualPath, value);
                if (!string.IsNullOrWhiteSpace(error)) Debug.LogError(error);
                else _selectedPath = null;
            }
            CancelRename();
        }

        private void CancelRename()
        {
            _renamingPath = null;
            _renameValue = string.Empty;
            GUIUtility.ReleaseInputFocus();
            Repaint();
        }

        private void ShowCreateMenu(Rect anchor)
        {
            var folder = SelectedAssetsFolder();
            var menu = new GenericMenu();
            PopulateCreateMenu(menu, folder);
            menu.ShowAsAdvancedDropdown(anchor);
        }

        private void PopulateCreateMenu(GenericMenu menu, string? folder, string prefix = "")
        {
            ArgumentNullException.ThrowIfNull(menu);
            var registry = app?._menuItems ?? MenuItemRegistry.Discover();
            var create = registry.GetRoot("Assets").FirstOrDefault(node =>
                node.Name.Equals("Create", StringComparison.Ordinal));
            if (create is null) return;
            PopulateCreateNodes(menu, create.Children, prefix, folder is not null);
        }

        private static void PopulateCreateNodes(
            GenericMenu menu,
            IEnumerable<MenuItemRegistry.MenuNode> nodes,
            string prefix,
            bool targetAvailable)
        {
            foreach (var node in nodes)
            {
                var path = prefix + node.Name;
                if (node.Children.Count > 0)
                {
                    PopulateCreateNodes(menu, node.Children, path + "/", targetAvailable);
                    continue;
                }
                if (targetAvailable && node.Enabled && node.Execute is not null)
                {
                    var execute = node.Execute;
                    menu.AddItem(new GUIContent(path), node.Checked, () => execute());
                }
                else
                    menu.AddDisabledItem(new GUIContent(path), node.Checked);
            }
        }

        internal string? SelectedAssetsFolder()
        {
            if (_selectedPath is null) return "Assets";
            var item = (_cache ??= BuildItems()).FirstOrDefault(candidate => candidate.VirtualPath.Equals(
                _selectedPath, StringComparison.OrdinalIgnoreCase));
            if (item is null) return "Assets";
            if (item.IsPackage) return null;
            return item.IsDirectory ? item.VirtualPath : item.ParentPath ?? "Assets";
        }

        internal string? SelectedAssetPath => _selectedPath;

        internal bool CanExecuteCommand(ProjectAssetCommand command)
        {
            if (!EditorAssetWritePolicy.CanWrite)
            {
                return command is ProjectAssetCommand.Open or ProjectAssetCommand.ShowInExplorer or
                    ProjectAssetCommand.CopyPath or ProjectAssetCommand.CopyFullPath;
            }
            if (command == ProjectAssetCommand.Refresh) return true;
            if (command == ProjectAssetCommand.ImportNewAsset) return SelectedAssetsFolder() is not null;
            var item = SelectedItem();
            if (item is null) return false;
            return command switch
            {
                ProjectAssetCommand.Open or ProjectAssetCommand.ShowInExplorer or
                    ProjectAssetCommand.CopyPath or ProjectAssetCommand.CopyFullPath => true,
                ProjectAssetCommand.Rename or ProjectAssetCommand.Duplicate or ProjectAssetCommand.Delete =>
                    CanEdit(item),
                ProjectAssetCommand.Reimport => !item.IsPackage && item.Asset is not null,
                _ => false
            };
        }

        internal bool ExecuteCommand(ProjectAssetCommand command)
        {
            if (!CanExecuteCommand(command)) return false;
            try
            {
                var item = SelectedItem();
                switch (command)
                {
                    case ProjectAssetCommand.Open:
                        OpenItem(item!);
                        break;
                    case ProjectAssetCommand.ShowInExplorer:
                        ShowInExplorer(item!.SourcePath);
                        break;
                    case ProjectAssetCommand.CopyPath:
                        GUIUtility.systemCopyBuffer = item!.VirtualPath;
                        break;
                    case ProjectAssetCommand.CopyFullPath:
                        GUIUtility.systemCopyBuffer = item!.SourcePath;
                        break;
                    case ProjectAssetCommand.Rename:
                        BeginRename(item!);
                        break;
                    case ProjectAssetCommand.Duplicate:
                        PasteAsset(item!.VirtualPath);
                        break;
                    case ProjectAssetCommand.Delete:
                        DeleteSelectedAsset();
                        break;
                    case ProjectAssetCommand.Reimport:
                        AssetDatabase.ImportAsset(item!.VirtualPath, ImportAssetOptions.ForceUpdate);
                        break;
                    case ProjectAssetCommand.ImportNewAsset:
                        ImportNewAsset();
                        break;
                    case ProjectAssetCommand.Refresh:
                        app.RefreshAssets();
                        break;
                    default:
                        return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private ProjectBrowserItem? SelectedItem()
        {
            if (_selectedPath is null) return null;
            return (_cache ??= BuildItems()).FirstOrDefault(candidate => candidate.VirtualPath.Equals(
                _selectedPath, StringComparison.OrdinalIgnoreCase));
        }

        private void OpenItem(ProjectBrowserItem item)
        {
            if (item.IsDirectory)
            {
                _expanded.Add(item.NormalizedPath);
                _selectedPath = item.NormalizedPath;
                _assetScroll = Vector2.zero;
                Repaint();
            }
            else if (item.Asset is { } asset)
                app.OpenAsset(asset);
            else
                OpenExternal(item.SourcePath);
        }

        private void ImportNewAsset()
        {
            var folder = SelectedAssetsFolder();
            if (folder is null) return;
            EditorFileDialog.Open("Import New Asset", app._workspace.RootPath, "All files|*.*", source =>
            {
                var destination = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder.TrimEnd('/')}/{Path.GetFileName(source)}");
                var error = AssetFileOperations.Copy(source, app.ResolveAssetPath(destination));
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.LogError(error);
                    return;
                }
                app.RefreshAssets();
                SelectCreatedAsset(destination, beginRename: false);
            });
        }

        internal bool CanCopySelected() => CopySelectedPath() is not null;

        internal string? CopySelectedPath()
        {
            if (_selectedPath is null) return null;
            var item = (_cache ??= BuildItems()).FirstOrDefault(candidate => candidate.VirtualPath.Equals(
                _selectedPath, StringComparison.OrdinalIgnoreCase));
            return item is not null && CanEdit(item) ? item.VirtualPath : null;
        }

        internal void PasteAsset(string sourcePath)
        {
            var folder = SelectedAssetsFolder();
            if (folder is null) return;
            var source = app.ResolveAssetPath(sourcePath);
            if (!File.Exists(source) && !Directory.Exists(source)) return;
            var destination = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder.TrimEnd('/')}/{Path.GetFileName(sourcePath.TrimEnd('/'))}");
            var error = AssetFileOperations.Copy(source, app.ResolveAssetPath(destination));
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogError(error);
                return;
            }
            app.RefreshAssets();
            _cache = BuildItems();
            Ping(destination);
            if (_cache.FirstOrDefault(item => item.VirtualPath.Equals(destination,
                    StringComparison.OrdinalIgnoreCase)) is { } copied)
                app.Select(copied);
        }

        internal void DeleteSelectedAsset()
        {
            if (CopySelectedPath() is not { } path) return;
            if (!AssetDatabase.DeleteAsset(path)) return;
            _selectedPath = ProjectBrowserPath.Parent(path) ?? "Assets";
            _cache = null;
            app._selectedAsset = null;
            app._selectedAssetPath = null;
            app.RefreshAssets();
        }

        internal void SelectCreatedAsset(string path, bool beginRename = true)
        {
            _cache = BuildItems();
            Ping(path);
            var created = _cache.FirstOrDefault(item => item.VirtualPath.Equals(path,
                StringComparison.OrdinalIgnoreCase));
            if (created is null) return;
            app.Select(created);
            if (beginRename && CanEdit(created)) BeginRename(created);
        }

        private void HandleDrag(ProjectBrowserItem item, Rect rowRect)
        {
            var current = Event.current;
            var updating = current.type is EventType.MouseDrag or EventType.DragUpdated;
            var performing = current.type is EventType.MouseUp or EventType.DragPerform;
            if (current.type == EventType.MouseDown && current.button == 0 && rowRect.Contains(current.mousePosition) &&
                CanEdit(item))
            {
                _dragCandidatePath = item.VirtualPath;
                _dragStart = current.mousePosition;
                return;
            }
            if (current.type == EventType.MouseDrag && _dragCandidatePath is { } candidate &&
                _draggedPath is null && (current.mousePosition - _dragStart).sqrMagnitude >= 16)
            {
                _draggedPath = candidate;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.paths = [candidate];
                DragAndDrop.objectReferences = AssetDatabase.LoadMainAssetAtPath(candidate) is { } asset
                    ? [asset]
                    : [];
                DragAndDrop.SetGenericData("BEngine.Project.AssetPath", candidate);
                DragAndDrop.StartDrag(Path.GetFileName(candidate));
            }
            if (updating && _draggedPath is { } dragged &&
                CanDrop(dragged, item) && rowRect.Contains(current.mousePosition))
            {
                _dropTargetPath = item.VirtualPath;
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                Repaint();
                return;
            }
            if (!performing || _draggedPath is not { } source ||
                _dropTargetPath?.Equals(item.VirtualPath, StringComparison.OrdinalIgnoreCase) != true ||
                !rowRect.Contains(current.mousePosition)) return;
            var destination = $"{item.VirtualPath.TrimEnd('/')}/{Path.GetFileName(source)}";
            if (destination.Equals(source, StringComparison.OrdinalIgnoreCase))
            {
                DragAndDrop.AcceptDrag();
                ClearDrag();
                current.Use();
                return;
            }
            var error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrWhiteSpace(error)) Debug.LogError(error);
            else
            {
                _selectedPath = destination;
                DragAndDrop.AcceptDrag();
            }
            ClearDrag();
            current.Use();
        }

        private static bool CanEdit(ProjectBrowserItem item) => !item.IsPackage && item.Asset is not null &&
            !item.VirtualPath.Equals("Assets", StringComparison.OrdinalIgnoreCase);

        private static bool CanDrop(string source, ProjectBrowserItem target) => !target.IsPackage &&
            target.IsDirectory && !target.VirtualPath.Equals(source, StringComparison.OrdinalIgnoreCase) &&
            !target.VirtualPath.StartsWith(source.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase);

        private void ClearDrag()
        {
            _dragCandidatePath = null;
            _draggedPath = null;
            _dropTargetPath = null;
            Repaint();
        }

        private void DrawFooter(Rect rect, IReadOnlyList<ProjectBrowserItem> items)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.statusBar);
            var zoomWidth = IsTwoColumn ? Fix64.Min(142, Fix64.Max(92, rect.width * Fix64.FromDecimal(0.18m))) :
                Fix64.Zero;
            if (zoomWidth > 0) DrawZoomControl(new Rect(rect.xMax - zoomWidth, rect.y, zoomWidth, rect.height));
            var informationWidth = Fix64.Max(0, rect.width - zoomWidth);
            var selected = items.FirstOrDefault(item => item.VirtualPath.Equals(_selectedPath,
                StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                var packageCount = items.Count(item => item.IsPackage && item.ParentPath == "Packages");
                GUI.Label(new Rect(rect.x + 7, rect.y + 2, Fix64.Max(0, informationWidth - 14), rect.height - 3),
                    $"Assets  |  Packages ({packageCount} enabled)", EditorStyles.miniLabel);
                return;
            }
            var details = selected.IsPackage
                ? selected.PackageId is null
                    ? $"{items.Count(item => item.IsPackage && item.ParentPath == "Packages")} enabled packages"
                    : $"{selected.PackageId}  {selected.PackageVersion}"
                : selected.AssetType;
            var detailsWidth = Fix64.Min(220, Fix64.Max(100, informationWidth * Fix64.FromDecimal(0.28m)));
            var hierarchyWidth = Fix64.Max(0, informationWidth - detailsWidth - 18);
            var folder = selected.IsDirectory
                ? selected
                : items.FirstOrDefault(item => string.Equals(item.NormalizedPath, selected.ParentPath,
                    StringComparison.OrdinalIgnoreCase)) ?? selected;
            DrawBreadcrumb(new Rect(rect.x + 5, rect.y + 2, hierarchyWidth, rect.height - 3), folder, items);
            GUI.Label(new Rect(rect.x + informationWidth - detailsWidth - 7, rect.y + 2, detailsWidth,
                    rect.height - 3),
                new GUIContent(FitText(details, detailsWidth), tooltip: selected.SourcePath), EditorStyles.miniLabel);
        }

        private void DrawZoomControl(Rect rect)
        {
            GUI.Label(new Rect(rect.x + 2, rect.y + 3, 16, rect.height - 5),
                new GUIContent(string.Empty, EditorBuiltinIcons.Assets.Default, "Smaller previews"),
                EditorStyles.miniLabel);
            _thumbnailSize = GUI.HorizontalSlider(new Rect(rect.x + 20, rect.y + 7,
                Fix64.Max(20, rect.width - 42), Fix64.Max(8, rect.height - 13)), _thumbnailSize, 32, 144);
            GUI.Label(new Rect(rect.xMax - 18, rect.y + 2, 18, rect.height - 4),
                new GUIContent(string.Empty, EditorBuiltinIcons.Assets.Image, "Larger previews"),
                EditorStyles.miniLabel);
        }

        private IEnumerable<ProjectBrowserItem> VisibleItems(IReadOnlyList<ProjectBrowserItem> items)
        {
            if (string.IsNullOrWhiteSpace(_search))
            {
                foreach (var item in items)
                {
                    var visible = true;
                    for (var parent = item.ParentPath; parent is not null;
                         parent = ProjectBrowserPath.Parent(parent))
                    {
                        if (_expanded.Contains(parent)) continue;
                        visible = false;
                        break;
                    }
                    if (visible) yield return item;
                }
                yield break;
            }

            var visiblePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!Matches(item)) continue;
                visiblePaths.Add(item.NormalizedPath);
                for (var parent = item.ParentPath; parent is not null;
                     parent = ProjectBrowserPath.Parent(parent))
                    visiblePaths.Add(parent);
            }
            foreach (var item in items)
                if (visiblePaths.Contains(item.NormalizedPath)) yield return item;
        }

        private bool Matches(ProjectBrowserItem item)
        {
            if (!_parsedSearch.Equals(_search, StringComparison.Ordinal))
            {
                _parsedSearch = _search;
                _searchFilter = ProjectSearchFilter.Parse(_search);
            }
            return _searchFilter.Matches(item);
        }

        private void DrawBreadcrumb(Rect rect, ProjectBrowserItem item, IReadOnlyList<ProjectBrowserItem> items)
        {
            var paths = new List<string>();
            var current = item.NormalizedPath;
            while (current.Length > 0)
            {
                paths.Add(current);
                current = ProjectBrowserPath.Parent(current) ?? string.Empty;
            }
            paths.Reverse();

            var x = rect.x;
            for (var index = 0; index < paths.Count && x < rect.xMax; index++)
            {
                var path = paths[index];
                var target = items.FirstOrDefault(candidate => candidate.IsDirectory &&
                    string.Equals(candidate.NormalizedPath, path, StringComparison.OrdinalIgnoreCase));
                var label = target?.EffectiveDisplayName ??
                            ProjectBrowserPath.DisplayName(null, path, null);
                var width = Fix64.Min(Fix64.Max(28,
                    EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + 10), rect.xMax - x);
                if (GUI.Button(new Rect(x, rect.y, width, rect.height),
                        new GUIContent(label, tooltip: target?.SourcePath ?? path), EditorStyles.toolbarButton) &&
                    target is not null)
                    NavigateToFolder(target, items);
                x += width;
                if (index >= paths.Count - 1 || x >= rect.xMax) continue;
                var separatorWidth = Fix64.Min(16, rect.xMax - x);
                GUI.Label(new Rect(x, rect.y, separatorWidth, rect.height), ">", EditorStyles.miniLabel);
                x += separatorWidth;
            }
        }

        private void NavigateToFolder(ProjectBrowserItem folder, IReadOnlyList<ProjectBrowserItem> items)
        {
            if (!folder.IsDirectory) return;
            _selectedPath = folder.NormalizedPath;
            _pingedAssetPath = null;
            _assetScroll = Vector2.zero;
            _expanded.Add(folder.NormalizedPath);
            for (var parent = folder.ParentPath; parent is not null;
                 parent = ProjectBrowserPath.Parent(parent))
                _expanded.Add(parent);
            if (app is not null) app.Select(folder);
            Repaint();
        }

        private static string ItemTooltip(ProjectBrowserItem item) => item.IsPackage
            ? item.PackageId is null
                ? $"Enabled project packages\n{item.SourcePath}"
                : $"{item.PackageId} {item.PackageVersion}\n{item.SourcePath}"
            : $"{item.AssetType}\n{item.SourcePath}";

        private static string FitText(string text, Fix64 width)
        {
            var characters = Math.Max(4, (int)((double)width / 7));
            if (text.Length <= characters) return text;
            var side = Math.Max(1, (characters - 3) / 2);
            return $"{text[..side]}...{text[^side..]}";
        }

    }

    private sealed class ImGuiConsoleWindow : EditorWindow
    {
        private string _search = string.Empty;
        private bool _info = true, _warning = true, _error = true;
        private bool _collapse = ConsolePreferences.Collapse;
        private bool _errorPause = ConsolePreferences.ErrorPause;
        private LogEntry? _selected;
        private int _selectedRepeatCount = 1;
        private readonly ImGuiScrollRegion _listScroll = new();
        private readonly ImGuiScrollRegion _detailsScroll = new();
        private Fix64 _detailsHeight;
        private Fix64 _detailsDragStartY;
        private Fix64 _detailsDragStartHeight;
        private string? _cachedStackTrace;
        private Fix64 _cachedStackFontSize = -1;
        private (string Line, ConsoleStackFrame? Frame)[] _stackLines = [];
        private Fix64 _stackContentWidth;
        private long _observedLogVersion = -1;
        private long _observedClearVersion = -1;
        private string _cachedSearch = string.Empty;
        private bool _cachedInfo;
        private bool _cachedWarning;
        private bool _cachedError;
        private bool _cachedCollapse;
        private int _infoCount;
        private int _warningCount;
        private int _errorCount;
        private ConsoleLogViewEntry[] _visibleLogs = [];
        private bool _toolbarLayoutInitialized;
        private bool _showClearText = true;
        private bool _showModeButtons = true;
        private bool _showSearch = true;
        public ImGuiConsoleWindow() { }
        public ImGuiConsoleWindow(GpuEditorApplication app) => _ = app;
        protected override void OnGUI()
        {
            DrawWindowToolbarBackground();
            var persistedCollapse = ConsolePreferences.Collapse;
            var persistedErrorPause = ConsolePreferences.ErrorPause;
            if (persistedCollapse != _collapse)
            {
                _collapse = persistedCollapse;
                ClearSelection();
            }
            _errorPause = persistedErrorPause;
            RefreshLogCache();
            var infoText = FormatCount(_infoCount);
            var warningText = FormatCount(_warningCount);
            var errorText = FormatCount(_errorCount);
            var infoWidth = CountButtonWidth(infoText);
            var warningWidth = CountButtonWidth(warningText);
            var errorWidth = CountButtonWidth(errorText);
            var clearTextWidth = ToolbarTextButtonWidth("Clear", true);
            var collapseWidth = ToolbarTextButtonWidth("Collapse", false);
            var errorPauseWidth = ToolbarTextButtonWidth("Error Pause", false);
            var countWidth = infoWidth + warningWidth + errorWidth;
            var basicTextClearWidth = clearTextWidth + 24 + countWidth;
            var updateToolbarLayout = !_toolbarLayoutInitialized || Event.current.type == EventType.Layout;
            var toolbarLayoutWasInitialized = _toolbarLayoutInitialized;
            if (updateToolbarLayout)
            {
                _showClearText = StableToolbarVisibility(_showClearText,
                    GUIUtility.currentViewWidth, basicTextClearWidth + 24, toolbarLayoutWasInitialized);
            }
            var clearWidth = _showClearText ? clearTextWidth : (Fix64)24;
            var basicWidth = clearWidth + 24 + countWidth;
            var modeWidth = collapseWidth + errorPauseWidth;
            const int basicControlCount = 5;
            const int modeControlCount = 2;
            var widthWithModesAndSearch = basicWidth + modeWidth + 8 +
                                          (basicControlCount + modeControlCount) * 4 + 80;
            if (updateToolbarLayout)
                _showModeButtons = StableToolbarVisibility(_showModeButtons,
                    GUIUtility.currentViewWidth, widthWithModesAndSearch, toolbarLayoutWasInitialized);
            var showModeButtons = _showModeButtons;
            var fixedControlCount = basicControlCount + (showModeButtons ? modeControlCount : 0);
            var fixedWidth = basicWidth + (showModeButtons ? modeWidth : Fix64.Zero);
            var availableWidth = Fix64.Max(0, GUIUtility.currentViewWidth - fixedWidth - 8 -
                (fixedControlCount - 1) * 4);
            if (updateToolbarLayout)
            {
                _showSearch = StableToolbarVisibility(_showSearch, availableWidth, 84,
                    toolbarLayoutWasInitialized);
                _toolbarLayoutInitialized = true;
            }
            var showSearch = _showSearch;
            var searchSlotWidth = showSearch ? Fix64.Max(0, availableWidth - 4) : availableWidth;
            var searchWidth = Fix64.Min(220, searchSlotWidth);
            var spacerWidth = Fix64.Max(0, searchSlotWidth - (showSearch ? searchWidth : Fix64.Zero));
            GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbar.fixedHeight));
            if (clearWidth > 24)
            {
                if (EditorToolbar.Button(new GUIContent("Clear", EditorBuiltinIcons.Toolbar.Clear,
                        "Clear all Console entries"), GUILayout.Width(clearWidth))) Clear();
            }
            else if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Clear, "Clear all Console entries",
                         GUILayout.Width(clearWidth))) Clear();
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.More, "Clear options", GUILayout.Width(24)))
                ShowClearOptionsMenu();
            var collapse = showModeButtons
                ? EditorToolbar.Toggle(_collapse, new GUIContent("Collapse",
                    "Group identical logs and show their repeat count"), GUILayout.Width(collapseWidth))
                : _collapse;
            var errorPause = showModeButtons
                ? EditorToolbar.Toggle(_errorPause, new GUIContent("Error Pause",
                    "Pause Play Mode when an error is logged"), GUILayout.Width(errorPauseWidth))
                : _errorPause;
            var search = showSearch
                ? EditorToolbar.SearchField(_search, GUILayout.Width(searchWidth))
                : _search;
            if (spacerWidth > 0) GUILayout.Space(spacerWidth);
            var info = EditorToolbar.Toggle(_info, new GUIContent(infoText,
                EditorBuiltinIcons.Toolbar.Info, $"Show info logs ({_infoCount:N0})"), GUILayout.Width(infoWidth));
            var warning = EditorToolbar.Toggle(_warning, new GUIContent(warningText,
                EditorBuiltinIcons.Toolbar.Warning, $"Show warnings ({_warningCount:N0})"),
                GUILayout.Width(warningWidth));
            var error = EditorToolbar.Toggle(_error, new GUIContent(errorText,
                EditorBuiltinIcons.Toolbar.Error, $"Show errors ({_errorCount:N0})"), GUILayout.Width(errorWidth));
            GUILayout.EndHorizontal();

            if (collapse != _collapse)
            {
                _collapse = collapse;
                ConsolePreferences.Collapse = collapse;
                ClearSelection();
            }
            if (errorPause != _errorPause)
            {
                _errorPause = errorPause;
                ConsolePreferences.ErrorPause = errorPause;
            }
            if (search != _search || info != _info || warning != _warning || error != _error ||
                collapse != _cachedCollapse)
            {
                _search = search;
                _info = info;
                _warning = warning;
                _error = error;
                RefreshLogCache(true);
            }

            var contentY = GUILayoutUtility.GetLastRect().yMax + 3;
            var availableHeight = Fix64.Max(1, GUIUtility.currentViewHeight - contentY - 4);
            var separatorHeight = _selected is null ? Fix64.Zero : (Fix64)6;
            var maximumDetailsHeight = Fix64.Max(0, availableHeight - 72 - separatorHeight);
            var minimumDetailsHeight = Fix64.Min(96, maximumDetailsHeight);
            if (_selected is not null && _detailsHeight <= 0)
                _detailsHeight = Fix64.Clamp(availableHeight * Fix64.FromDecimal(0.38m),
                    minimumDetailsHeight, maximumDetailsHeight);
            var detailsHeight = _selected is null
                ? Fix64.Zero
                : Fix64.Clamp(_detailsHeight, minimumDetailsHeight, maximumDetailsHeight);
            var listHeight = Fix64.Max(1, availableHeight - detailsHeight - separatorHeight);
            var separatorRect = new Rect(0, contentY + listHeight, GUIUtility.currentViewWidth, separatorHeight);
            if (_selected is not null)
            {
                detailsHeight = HandleDetailsSplitter(separatorRect, availableHeight, detailsHeight);
                listHeight = Fix64.Max(1, availableHeight - detailsHeight - separatorHeight);
                separatorRect = new Rect(0, contentY + listHeight, GUIUtility.currentViewWidth, separatorHeight);
            }

            using (GUILayout.Area(new Rect(0, contentY, GUIUtility.currentViewWidth, listHeight)))
                DrawLogList();

            if (_selected is not { } selected || detailsHeight <= 0) return;
            GUI.Box(new Rect(separatorRect.x, separatorRect.y + 2, separatorRect.width, 2),
                GUIContent.none, EditorStyles.separator);
            using (GUILayout.Area(new Rect(0, contentY + listHeight + separatorHeight,
                       GUIUtility.currentViewWidth, detailsHeight)))
                DrawDetails(selected);
        }

        private Fix64 HandleDetailsSplitter(Rect separatorRect, Fix64 availableHeight, Fix64 currentDetailsHeight)
        {
            var maximum = Fix64.Max(0, availableHeight - 72 - separatorRect.height);
            var minimum = Fix64.Min(96, maximum);
            currentDetailsHeight = Fix64.Clamp(currentDetailsHeight, minimum, maximum);
            var id = GUIUtility.GetControlID("ConsoleDetailsSplitter".GetHashCode(StringComparison.Ordinal),
                FocusType.Passive, separatorRect);
            var evt = Event.current;
            EditorGUIUtility.AddCursorRect(GUIUtility.hotControl == id
                    ? new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight)
                    : separatorRect,
                MouseCursor.ResizeVertical);
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown when evt.button == 0 && separatorRect.Contains(evt.mousePosition):
                    GUIUtility.hotControl = id;
                    _detailsDragStartY = evt.mousePosition.y;
                    _detailsDragStartHeight = currentDetailsHeight;
                    evt.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    currentDetailsHeight = Fix64.Clamp(
                        _detailsDragStartHeight - (evt.mousePosition.y - _detailsDragStartY), minimum, maximum);
                    _detailsHeight = currentDetailsHeight;
                    evt.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    break;
            }
            _detailsHeight = currentDetailsHeight;
            return currentDetailsHeight;
        }

        private void DrawLogList()
        {
            RefreshLogCache();
            _listScroll.Begin();
            try
            {
                foreach (var view in _visibleLogs)
                {
                    var log = view.Entry;
                    var rowRect = GUILayoutUtility.GetControlRect(EditorStyles.treeViewRow.fixedHeight,
                        GUILayout.ExpandWidth(true));
                    var isSelected = _selected is { } current &&
                                     (_collapse ? SameLogGroup(current, log) : current.Equals(log));
                    if (GUI.Button(rowRect, new GUIContent(ViewRowText(view), IconFor(log.Type),
                                view.Count > 1 ? $"{view.Count:N0} identical {log.Type} logs" : log.Type.ToString()),
                            TreeRowStyle(isSelected)))
                    {
                        _selected = log;
                        _selectedRepeatCount = view.Count;
                    }

                    var evt = Event.current;
                    if (!rowRect.Contains(evt.mousePosition)) continue;
                    if (evt.type == EventType.ContextClick)
                    {
                        _selected = log;
                        _selectedRepeatCount = view.Count;
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Copy Message"), false,
                            () => GUIUtility.systemCopyBuffer = log.Message);
                        menu.AddItem(new GUIContent("Copy Full Log"), false,
                            () => GUIUtility.systemCopyBuffer = log.ToDetailedString());
                        menu.AddSeparator(string.Empty);
                        menu.AddItem(new GUIContent("Clear"), false, Clear);
                        menu.ShowAsContext();
                        evt.Use();
                    }
                }
            }
            finally { _listScroll.End(); }
        }

        private void DrawDetails(LogEntry log)
        {
            GUILayout.BeginHorizontal();
            var repeatText = _selectedRepeatCount > 1 ? $"  {_selectedRepeatCount:N0} occurrences" : string.Empty;
            GUILayout.Label(new GUIContent(
                $"{log.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}  {log.Type}{repeatText}",
                IconFor(log.Type), "Log details"), EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(58)))
                GUIUtility.systemCopyBuffer = log.ToDetailedString();
            GUILayout.EndHorizontal();

            EnsureStackTraceCache(log.StackTrace ?? string.Empty);
            var contentWidth = Fix64.Max(_stackContentWidth, MeasureLineWidth(log.Message ?? "null"));
            _detailsScroll.Begin(contentWidth + 12);
            try
            {
                DrawLines(log.Message ?? "null");
                GUILayout.Space(5);
                GUILayout.Label("Call Stack", EditorStyles.boldLabel);
                if (string.IsNullOrWhiteSpace(log.StackTrace))
                    GUILayout.Label("No call stack was recorded.", EditorStyles.miniLabel);
                else
                    DrawStackTrace(log.StackTrace ?? string.Empty);
            }
            finally { _detailsScroll.End(); }
        }

        private void Clear()
        {
            ClearSelection();
            EditorLogStore.Clear();
            RefreshLogCache(true);
        }

        private void ShowClearOptionsMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Collapse"), ConsolePreferences.Collapse,
                () => ConsolePreferences.Collapse = !ConsolePreferences.Collapse);
            menu.AddItem(new GUIContent("Error Pause"), ConsolePreferences.ErrorPause,
                () => ConsolePreferences.ErrorPause = !ConsolePreferences.ErrorPause);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Clear on Play"), ConsolePreferences.ClearOnPlay,
                () => ConsolePreferences.ClearOnPlay = !ConsolePreferences.ClearOnPlay);
            menu.AddItem(new GUIContent("Clear on Build"), ConsolePreferences.ClearOnBuild,
                () => ConsolePreferences.ClearOnBuild = !ConsolePreferences.ClearOnBuild);
            menu.AddItem(new GUIContent("Clear on Recompile"), ConsolePreferences.ClearOnRecompile,
                () => ConsolePreferences.ClearOnRecompile = !ConsolePreferences.ClearOnRecompile);
            menu.ShowAsContext();
        }

        private void ClearSelection()
        {
            _selected = null;
            _selectedRepeatCount = 1;
            _cachedStackTrace = null;
            _stackLines = [];
            _stackContentWidth = 0;
        }

        private void RefreshLogCache(bool force = false)
        {
            var version = EditorLogStore.version;
            var clearVersion = EditorLogStore.clearVersion;
            if (!force && version == _observedLogVersion && clearVersion == _observedClearVersion &&
                string.Equals(_cachedSearch, _search, StringComparison.Ordinal) &&
                _cachedInfo == _info && _cachedWarning == _warning && _cachedError == _error &&
                _cachedCollapse == _collapse) return;

            var logs = EditorLogStore.SnapshotWithVersion(out version);
            if (clearVersion != _observedClearVersion) ClearSelection();
            _observedLogVersion = version;
            _observedClearVersion = clearVersion;
            _cachedSearch = _search;
            _cachedInfo = _info;
            _cachedWarning = _warning;
            _cachedError = _error;
            _cachedCollapse = _collapse;
            _infoCount = 0;
            _warningCount = 0;
            _errorCount = 0;
            foreach (var log in logs)
            {
                switch (log.Type)
                {
                    case LogType.Warning: _warningCount++; break;
                    case LogType.Error: _errorCount++; break;
                    default: _infoCount++; break;
                }
            }

            var filtered = logs.Where(Show).ToArray();
            var visible = BuildVisibleLogs(filtered, _collapse);
            _visibleLogs = visible.Length <= 500 ? visible : visible[^500..];
        }

        private static ConsoleLogViewEntry[] BuildVisibleLogs(IReadOnlyList<LogEntry> logs, bool collapse)
        {
            if (!collapse)
            {
                var rows = new ConsoleLogViewEntry[logs.Count];
                for (var index = 0; index < logs.Count; index++) rows[index] = new(logs[index], 1);
                return rows;
            }

            var positions = new Dictionary<(LogType Type, string Message, string StackTrace), int>();
            var collapsed = new List<ConsoleLogViewEntry>(logs.Count);
            foreach (var log in logs)
            {
                var key = (log.Type, log.Message ?? string.Empty, log.StackTrace ?? string.Empty);
                if (positions.TryGetValue(key, out var position))
                {
                    var current = collapsed[position];
                    collapsed[position] = current with { Count = current.Count + 1 };
                    continue;
                }
                positions.Add(key, collapsed.Count);
                collapsed.Add(new ConsoleLogViewEntry(log, 1));
            }
            return [.. collapsed];
        }

        private static string FormatCount(int count) => count > 999
            ? "999+"
            : Math.Max(0, count).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static Fix64 CountButtonWidth(string text) =>
            Fix64.Max(58, GUITextMetrics.MeasureWidth(text, EditorStyles.toolbarButton.fontSize,
                GUIUtility.fontFamily) + 38);

        private static Fix64 ToolbarTextButtonWidth(string text, bool hasIcon) =>
            GUITextMetrics.MeasureWidth(text, EditorStyles.toolbarButton.fontSize, GUIUtility.fontFamily) +
            (hasIcon ? 28 : 12);

        private static bool StableToolbarVisibility(
            bool currentlyVisible, Fix64 width, Fix64 threshold, bool initialized)
        {
            if (!initialized) return width >= threshold;
            const int hysteresis = 12;
            return currentlyVisible
                ? width >= threshold - hysteresis
                : width >= threshold + hysteresis;
        }

        private static string ViewRowText(ConsoleLogViewEntry view) => view.Count > 1
            ? $"{RowText(view.Entry)}  [{view.Count:N0}]"
            : RowText(view.Entry);

        private static bool SameLogGroup(LogEntry left, LogEntry right) =>
            left.Type == right.Type &&
            string.Equals(left.Message, right.Message, StringComparison.Ordinal) &&
            string.Equals(left.StackTrace, right.StackTrace, StringComparison.Ordinal);

        private void DrawStackTrace(string stackTrace)
        {
            EnsureStackTraceCache(stackTrace);
            var rowHeight = Fix64.Max(22,
                GUITextMetrics.MeasureLineHeight(EditorStyles.label.fontSize, GUIUtility.fontFamily) + 1);
            foreach (var (line, frame) in _stackLines)
            {
                if (frame is not { } source)
                {
                    GUILayout.Label(string.IsNullOrEmpty(line) ? " " : line,
                        GUILayout.Height(rowHeight), GUILayout.Width(Fix64.Max(1, MeasureLineWidth(line) + 8)));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(source.Prefix))
                    GUILayout.Label(source.Prefix, GUILayout.Height(rowHeight),
                        GUILayout.Width(Fix64.Max(1, MeasureLineWidth(source.Prefix) + 8)));
                var linkWidth = Fix64.Max(40, MeasureLineWidth(source.SourceLocation) + 8);
                var content = new GUIContent(source.SourceLocation,
                    $"Open {source.FilePath} at line {source.LineNumber}");
                if (GUILayout.Button(content, EditorStyles.linkLabel,
                        GUILayout.Width(linkWidth), GUILayout.Height(rowHeight)))
                    AssetDatabase.OpenAsset(source.FilePath, source.LineNumber,
                        source.ColumnNumber > 0 ? source.ColumnNumber : -1);
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }
        }

        private void EnsureStackTraceCache(string stackTrace)
        {
            var fontSize = EditorStyles.label.fontSize;
            if (string.Equals(_cachedStackTrace, stackTrace, StringComparison.Ordinal) &&
                _cachedStackFontSize == fontSize) return;
            _cachedStackTrace = stackTrace;
            _cachedStackFontSize = fontSize;
            _stackLines = stackTrace.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Select(line => ConsoleStackTraceParser.TryParse(line, out var frame)
                    ? (line, (ConsoleStackFrame?)frame)
                    : (line, (ConsoleStackFrame?)null))
                .ToArray();
            _stackContentWidth = _stackLines.Aggregate(Fix64.Zero, (width, item) =>
            {
                var lineWidth = item.Frame is { } source
                    ? Fix64.Max(MeasureLineWidth(source.Prefix), MeasureLineWidth(source.SourceLocation))
                    : MeasureLineWidth(item.Line);
                return Fix64.Max(width, lineWidth + 8);
            });
        }

        private static Fix64 MeasureLineWidth(string text) =>
            GUITextMetrics.MeasureWidth(text, EditorStyles.label.fontSize, GUIUtility.fontFamily);

        private static void DrawLines(string text)
        {
            foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                GUILayout.Label(string.IsNullOrEmpty(line) ? " " : line);
        }

        private static string RowText(LogEntry log) =>
            $"[{log.Timestamp.ToLocalTime():HH:mm:ss.fff}] {(log.Message ?? "null").ReplaceLineEndings(" ")}";

        private static string IconFor(LogType type) => type switch
        {
            LogType.Warning => EditorBuiltinIcons.Toolbar.Warning,
            LogType.Error => EditorBuiltinIcons.Toolbar.Error,
            _ => EditorBuiltinIcons.Toolbar.Info
        };

        private bool Show(LogEntry item) => (_search.Length == 0 || item.Message?.Contains(_search,
            StringComparison.OrdinalIgnoreCase) == true ||
            item.StackTrace?.Contains(_search, StringComparison.OrdinalIgnoreCase) == true) &&
            item.Type switch { LogType.Warning => _warning, LogType.Error => _error, _ => _info };
    }

    private sealed class ImGuiPackageManagerWindow(GpuEditorApplication app) : EditorWindow
    {
        private enum PackageDetailTab
        {
            Description,
            Dependencies,
            Examples
        }

        private enum ExampleImportState
        {
            NotImported,
            Partial,
            Modified,
            UpdateAvailable,
            Imported
        }

        private string _search = string.Empty;
        private BPackageDefinition? _selected;
        private string? _selectedPackageId;
        private bool _coreSelected = true;
        private PackageDetailTab _selectedTab = PackageDetailTab.Description;
        private string? _examplesSource;
        private DateTime _examplesStampUtc;
        private string _examplesFingerprint = string.Empty;
        private PackageExampleInfo[] _examples = [];
        private readonly Dictionary<string, BPackageImportStatus> _exampleImportStatuses =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _exampleImportStatusErrors =
            new(StringComparer.Ordinal);
        private readonly ImGuiScrollRegion _packageScroll = new();
        private readonly ImGuiScrollRegion _detailsScroll = new();
        public ImGuiPackageManagerWindow() : this(null!) { titleContent = new GUIContent("Package Manager", "Icons/Windows/PackageManager.png"); }

        protected override void OnProjectChange()
        {
            _exampleImportStatuses.Clear();
            _exampleImportStatusErrors.Clear();
            Repaint();
        }

        protected override void OnGUI()
        {
            var width = GUIUtility.currentViewWidth;
            var height = GUIUtility.currentViewHeight;
            var leftWidth = Fix64.Clamp(width * Fix64.FromDecimal(0.35m), 220, 360);
            using (GUILayout.Area(new Rect(0, 0, leftWidth, height)))
            {
                DrawWindowToolbarBackground();
                _search = EditorToolbar.SearchField(_search);
                var showCore = MatchesCore(_search);
                var definitions = app.Packages.definitions.Where(item =>
                        item.Document.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                        item.Document.Id.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                        item.Document.Description.Contains(_search, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (!_coreSelected && _selectedPackageId is null && _selected is not null)
                    _selectedPackageId = _selected.Document.Id;
                if (_coreSelected && !showCore)
                {
                    _coreSelected = false;
                    _selected = definitions.FirstOrDefault();
                    _selectedPackageId = _selected?.Document.Id;
                }
                else if (!_coreSelected)
                {
                    _selected = definitions.FirstOrDefault(item => item.Document.Id.Equals(
                        _selectedPackageId, StringComparison.OrdinalIgnoreCase)) ?? definitions.FirstOrDefault();
                    _selectedPackageId = _selected?.Document.Id;
                    _coreSelected = _selected is null && showCore;
                }
                _packageScroll.Begin();
                try
                {
                    if (showCore && GUILayout.Button(new GUIContent("BEngine Core",
                            EditorBuiltinIcons.Assets.Assembly,
                            "Built-in engine APIs and complete examples"),
                            TreeRowStyle(_coreSelected),
                            GUILayout.Height(EditorStyles.treeViewRow.fixedHeight)))
                    {
                        _coreSelected = true;
                        _selected = null;
                        _selectedPackageId = null;
                        _selectedTab = PackageDetailTab.Description;
                        InvalidateExamples();
                    }
                    foreach (var definition in definitions)
                    {
                        var enabled = app.Packages.IsEnabled(definition.Document.Id);
                        if (GUILayout.Button(new GUIContent(definition.Document.DisplayName,
                                enabled ? EditorBuiltinIcons.Toolbar.Check : EditorBuiltinIcons.Components.Default,
                                $"{definition.Document.Id}\n{(enabled ? "Enabled" : "Disabled")}"),
                                TreeRowStyle(ReferenceEquals(_selected, definition)),
                                GUILayout.Height(EditorStyles.treeViewRow.fixedHeight)))
                        {
                            _coreSelected = false;
                            _selected = definition;
                            _selectedPackageId = definition.Document.Id;
                            _selectedTab = PackageDetailTab.Description;
                            InvalidateExamples();
                        }
                    }
                }
                finally { _packageScroll.End(); }
            }
            GUI.Box(new Rect(leftWidth, 0, 2, height), GUIContent.none, EditorStyles.separator);
            using (GUILayout.Area(new Rect(leftWidth + 2, 0, Fix64.Max(1, width - leftWidth - 2), height)))
            {
                _detailsScroll.Begin();
                try
                {
                    if (_coreSelected)
                    {
                        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbarButton.fixedHeight));
                        var titleWidth = Fix64.Max(24, GUILayout.CurrentGroupWidth - 240);
                        GUILayout.Label("BEngine Core", EditorStyles.largeLabel, GUILayout.Width(titleWidth));
                        DrawDocumentationButton(PackageDocumentationCatalog.FindCoreDocumentation());
                        var oldEnabled = GUI.enabled;
                        GUI.enabled = false;
                        GUILayout.Button(new GUIContent("Imported", EditorBuiltinIcons.Toolbar.Check,
                            "BEngine Core is built into the editor."), GUILayout.Width(96));
                        GUI.enabled = oldEnabled;
                        GUILayout.EndHorizontal();
                        GUILayout.Label("com.bengine.core | Built-in | Required", EditorStyles.miniLabel);
                        var sourceDirectory = PackageExampleCatalog.FindCoreExamplesDirectory();
                        var examples = GetExamples(sourceDirectory);
                        DrawDetailsTabs(0, examples.Length);
                        switch (_selectedTab)
                        {
                            case PackageDetailTab.Description:
                                DrawDescription("Built-in engine APIs for scenes, lifecycle, input, transforms, " +
                                                "rendering and editor extension development.");
                                break;
                            case PackageDetailTab.Dependencies:
                                DrawDependencies([]);
                                break;
                            case PackageDetailTab.Examples:
                                DrawExamples(sourceDirectory, true);
                                break;
                        }
                    }
                    else if (_selected is { } package)
                    {
                        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbarButton.fixedHeight));
                        var titleWidth = Fix64.Max(24, GUILayout.CurrentGroupWidth - 240);
                        GUILayout.Label(package.Document.DisplayName, EditorStyles.largeLabel,
                            GUILayout.Width(titleWidth));
                        DrawDocumentationButton(PackageDocumentationCatalog.FindForPackage(package));
                        var enabled = app.Packages.IsEnabled(package.Document.Id);
                        var old = GUI.enabled;
                        GUI.enabled = !(enabled && package.Document.Required);
                        var actionLabel = enabled
                            ? package.Document.Required ? "Imported" : "Remove"
                            : "Import";
                        if (GUILayout.Button(new GUIContent(actionLabel,
                                enabled ? EditorBuiltinIcons.Toolbar.Check : EditorBuiltinIcons.Toolbar.Browse,
                                enabled
                                    ? package.Document.Required
                                        ? "This required package is already imported."
                                        : "Remove this package and dependent packages from the project."
                                    : "Import this package and its dependencies into the project."),
                                GUILayout.Width(96)))
                        {
                            ChangePackageImportState(package, !enabled);
                        }
                        GUI.enabled = old;
                        GUILayout.EndHorizontal();
                        GUILayout.Label(package.Document.Id, EditorStyles.miniLabel);
                        GUILayout.Label($"Version {package.Document.PackageVersion}", EditorStyles.miniLabel);
                        var dependencies = app.Packages.GetDependencies(package.Document.Id).ToArray();
                        var sourceDirectory = Path.Combine(Path.GetDirectoryName(package.Path)!,
                            EditorResource.DirectoryName, "Examples");
                        var examples = GetExamples(sourceDirectory);
                        DrawDetailsTabs(dependencies.Length, examples.Length);
                        switch (_selectedTab)
                        {
                            case PackageDetailTab.Description:
                                DrawDescription(string.IsNullOrWhiteSpace(package.Document.Description)
                                    ? "No description available."
                                    : package.Document.Description);
                                break;
                            case PackageDetailTab.Dependencies:
                                DrawDependencies(dependencies);
                                break;
                            case PackageDetailTab.Examples:
                                DrawExamples(sourceDirectory, enabled);
                                break;
                        }
                    }
                    else
                    {
                        GUILayout.Space(12);
                        GUILayout.Label("No packages match the current search.", EditorStyles.miniLabel);
                    }
                }
                finally { _detailsScroll.End(); }
            }
        }

        private void DrawDetailsTabs(int dependencyCount, int exampleCount)
        {
            GUILayout.Space(8);
            var availableWidth = Fix64.Max(1, GUILayout.CurrentGroupWidth - 8);
            var tabWidth = Fix64.Max(72, (availableWidth - 8) / 3);
            GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.dockTab.fixedHeight));
            DrawDetailsTab(PackageDetailTab.Description, "Description", "Package description", tabWidth);
            DrawDetailsTab(PackageDetailTab.Dependencies, "Dependencies",
                $"Packages imported with this package ({dependencyCount})", tabWidth);
            DrawDetailsTab(PackageDetailTab.Examples, "Examples",
                $"Examples available to import into Assets/Examples ({exampleCount})", tabWidth);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private void DrawDetailsTab(PackageDetailTab tab, string label, string tooltip, Fix64 width)
        {
            var style = _selectedTab == tab ? EditorStyles.dockTabActive : EditorStyles.dockTab;
            if (GUILayout.Button(new GUIContent(label, tooltip: tooltip), style, GUILayout.Width(width)))
                _selectedTab = tab;
        }

        private static void DrawDescription(string description) =>
            GUILayout.Label(description, EditorStyles.helpBox);

        private static void DrawDocumentationButton(string? path)
        {
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && path is not null;
            var clicked = GUILayout.Button(new GUIContent("Documentation",
                    EditorBuiltinIcons.Toolbar.Browse,
                    path is null
                        ? "This package does not include offline documentation."
                        : "Open this package's offline documentation in the system browser."),
                GUILayout.Width(128));
            GUI.enabled = previousEnabled;
            if (clicked)
                EditorFeatureGuard.Invoke(typeof(ImGuiPackageManagerWindow), "OpenDocumentation",
                    () => Application.OpenURL(path!));
        }

        private void DrawDependencies(IReadOnlyList<BPackageDefinition> dependencies)
        {
            if (dependencies.Count == 0)
            {
                GUILayout.Label("None", EditorStyles.miniLabel);
                return;
            }

            foreach (var dependency in dependencies)
            {
                var imported = app.Packages.IsEnabled(dependency.Document.Id);
                GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.treeViewRow.fixedHeight));
                var dependencyNameWidth = Fix64.Max(24, GUILayout.CurrentGroupWidth - 84);
                GUILayout.Label(new GUIContent(dependency.Document.DisplayName,
                    imported ? EditorBuiltinIcons.Toolbar.Check : EditorBuiltinIcons.Components.Default,
                    dependency.Document.Id), GUILayout.Width(dependencyNameWidth));
                GUILayout.Label(imported ? "Imported" : "Required", EditorStyles.miniLabel,
                    GUILayout.Width(72));
                GUILayout.EndHorizontal();
            }
        }

        private void ChangePackageImportState(BPackageDefinition package, bool import)
        {
            try
            {
                app.Packages.SetEnabled(package.Document.Id, import);
                app._menuItems = DiscoverMenuItems(app._menuItems);
                InvalidateExamples();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void DrawExamples(string? sourceDirectory, bool packageEnabled)
        {
            var examples = GetExamples(sourceDirectory);
            if (examples.Length == 0)
            {
                GUILayout.Label("No examples are installed for this module.", EditorStyles.miniLabel);
                return;
            }

            foreach (var example in examples)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                var exampleNameWidth = Fix64.Max(24, GUILayout.CurrentGroupWidth - 104);
                GUILayout.Label(new GUIContent(example.DisplayName, EditorBuiltinIcons.Assets.Default,
                    example.ArchivePath), EditorStyles.boldLabel, GUILayout.Width(exampleNameWidth));
                var importState = GetImportState(example);
                var installationId = GetExampleInstallationId(example);
                _exampleImportStatusErrors.TryGetValue(installationId, out var statusError);
                var canImport = packageEnabled && EditorAssetWritePolicy.CanWrite &&
                                example.Manifest is not null &&
                                string.IsNullOrWhiteSpace(statusError);
                var oldEnabled = GUI.enabled;
                GUI.enabled = canImport;
                var reimport = importState != ExampleImportState.NotImported;
                var buttonLabel = reimport ? "Reimport" : "Import";
                var tooltip = example.Manifest is null
                    ? $"This example archive is invalid: {example.Error}"
                    : !packageEnabled
                        ? "Import the package before importing this example."
                        : !EditorAssetWritePolicy.CanWrite
                            ? "Examples cannot be imported while entering, running, or exiting Play Mode."
                        : !string.IsNullOrWhiteSpace(statusError)
                            ? $"The example import state could not be read: {statusError}"
                        : importState switch
                        {
                            ExampleImportState.Partial =>
                                $"Repair missing files in Assets/{example.ImportPath}; local changes are preserved.",
                            ExampleImportState.Modified =>
                                $"Reimport into Assets/{example.ImportPath}; local changes are preserved.",
                            ExampleImportState.UpdateAvailable =>
                                $"Update Assets/{example.ImportPath}; local changes are preserved and obsolete " +
                                "unmodified files are removed.",
                            ExampleImportState.Imported =>
                                $"Reimport Assets/{example.ImportPath}; unchanged files are reused.",
                            _ => $"Import into Assets/{example.ImportPath} " +
                                  $"({EditorUtility.FormatBytes(example.ArchiveSize)})."
                        };
                if (GUILayout.Button(new GUIContent(buttonLabel, EditorBuiltinIcons.Toolbar.Browse, tooltip),
                        GUILayout.Width(92)))
                {
                    ImportExample(example, reimport);
                }
                GUI.enabled = oldEnabled;
                GUILayout.EndHorizontal();

                if (example.Manifest is { } details)
                {
                    GUILayout.Label($"{ImportStateLabel(importState)} | Version {details.PackageVersion} | " +
                                    $"{details.Entries.Count} entries | " +
                                    EditorUtility.FormatBytes(example.ArchiveSize), EditorStyles.miniLabel);
                    if (!string.IsNullOrWhiteSpace(details.Description))
                        GUILayout.Label(details.Description, EditorStyles.helpBox);
                }
                else
                {
                    GUILayout.Label($"Invalid archive: {example.Error}", EditorStyles.helpBox);
                }
            }
        }
        private PackageExampleInfo[] GetExamples(string? sourceDirectory)
        {
            var fullPath = string.IsNullOrWhiteSpace(sourceDirectory) ? null : Path.GetFullPath(sourceDirectory);
            var stamp = fullPath is not null && Directory.Exists(fullPath)
                ? Directory.GetLastWriteTimeUtc(fullPath)
                : DateTime.MinValue;
            var fingerprint = fullPath is not null && Directory.Exists(fullPath)
                ? string.Join("|", Directory.EnumerateFiles(fullPath, "*.bpackage", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(path =>
                    {
                        var file = new FileInfo(path);
                        return $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
                    }))
                : string.Empty;
            if (string.Equals(_examplesSource, fullPath, StringComparison.OrdinalIgnoreCase) &&
                _examplesStampUtc == stamp && _examplesFingerprint == fingerprint) return _examples;
            _examplesSource = fullPath;
            _examplesStampUtc = stamp;
            _examplesFingerprint = fingerprint;
            _exampleImportStatuses.Clear();
            _exampleImportStatusErrors.Clear();
            _examples = PackageExampleCatalog.Discover(fullPath);
            return _examples;
        }

        private ExampleImportState GetImportState(PackageExampleInfo example)
        {
            if (example.Manifest is not { } manifest) return ExampleImportState.NotImported;
            var tracked = GetTrackedImportStatus(example);
            if (tracked is not null)
            {
                return tracked.State switch
                {
                    BPackageImportState.Partial => ExampleImportState.Partial,
                    BPackageImportState.Modified => ExampleImportState.Modified,
                    BPackageImportState.UpdateAvailable => ExampleImportState.UpdateAvailable,
                    BPackageImportState.Installed => ExampleImportState.Imported,
                    _ => ExampleImportState.NotImported
                };
            }

            // Older projects may contain examples imported before receipts were introduced.
            var importPath = example.ImportPath;
            var files = manifest.Entries.Where(entry => !entry.IsDirectory).ToArray();
            if (files.Length == 0) return ExampleImportState.NotImported;
            var existing = files.Count(entry => File.Exists(Path.Combine(app._workspace.AssetsPath,
                importPath.Replace('/', Path.DirectorySeparatorChar),
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            return existing switch
            {
                0 => ExampleImportState.NotImported,
                _ when existing == files.Length => ExampleImportState.Imported,
                _ => ExampleImportState.Partial
            };
        }

        private bool IsImported(PackageExampleInfo example) =>
            GetImportState(example) != ExampleImportState.NotImported;

        private BPackageImportStatus? GetTrackedImportStatus(PackageExampleInfo example)
        {
            var installationId = GetExampleInstallationId(example);
            if (_exampleImportStatuses.TryGetValue(installationId, out var cached)) return cached;
            if (_exampleImportStatusErrors.ContainsKey(installationId)) return null;
            try
            {
                var status = BPackageArchive.GetImportStatus(app._workspace, example.ArchivePath,
                    installationId, example.ImportPath);
                _exampleImportStatuses[installationId] = status;
                return status;
            }
            catch (Exception exception)
            {
                _exampleImportStatusErrors[installationId] = exception.Message;
                Debug.LogException(exception);
                return null;
            }
        }

        private string GetExampleInstallationId(PackageExampleInfo example)
        {
            var owner = _coreSelected ? "com.bengine.core" : _selectedPackageId ?? "com.bengine.unknown";
            return $"example:{owner}:{Path.GetFileNameWithoutExtension(example.ArchivePath)}";
        }

        private static string ImportStateLabel(ExampleImportState state) => state switch
        {
            ExampleImportState.Partial => "Partial",
            ExampleImportState.Modified => "Modified",
            ExampleImportState.UpdateAvailable => "Update available",
            ExampleImportState.Imported => "Imported",
            _ => "Not imported"
        };

        private void ImportExample(PackageExampleInfo example, bool overwrite)
        {
            try
            {
                EditorAssetWritePolicy.EnsureCanWrite("Importing package examples");
                if (example.Manifest is not { } manifest) return;
                PackageExampleLayout.Validate(manifest, Path.GetFileNameWithoutExtension(example.ArchivePath));
                var installationId = GetExampleInstallationId(example);
                var tracked = GetTrackedImportStatus(example);
                var reimport = overwrite || tracked?.Receipt is not null || IsImported(example);
                var result = BPackageArchive.ImportPackage(app._workspace, example.ArchivePath,
                    new BPackageImportOptions
                    {
                        ConflictPolicy = BPackageConflictPolicy.Fail,
                        DestinationDirectory = example.ImportPath,
                        InstallationId = installationId,
                        Mode = reimport ? BPackageImportMode.Reimport : BPackageImportMode.Import,
                        ModifiedFilePolicy = BPackageModifiedFilePolicy.Preserve
                    }, progress => EditorUtility.DisplayProgressBar("Import Example",
                        $"{progress.Phase}: {progress.Path}", progress.Fraction));
                app._project.Invalidate();
                InvalidateExamples();
                Debug.Log($"Imported example {result.Manifest.Name}: {result.ImportedPaths.Count} updated, " +
                          $"{result.UnchangedPaths.Count} unchanged, " +
                          $"{result.PreservedModifiedPaths.Count} local changes preserved, " +
                          $"{result.RemovedPaths.Count} obsolete entries removed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void InvalidateExamples()
        {
            _examplesSource = null;
            _examplesStampUtc = DateTime.MinValue;
            _examplesFingerprint = string.Empty;
            _examples = [];
            _exampleImportStatuses.Clear();
            _exampleImportStatusErrors.Clear();
        }

        private static bool MatchesCore(string search) => string.IsNullOrWhiteSpace(search) ||
            "BEngine Core".Contains(search, StringComparison.OrdinalIgnoreCase) ||
            "com.bengine.core".Contains(search, StringComparison.OrdinalIgnoreCase) ||
            "Built-in engine APIs".Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
