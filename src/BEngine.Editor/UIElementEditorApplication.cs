using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BEngine.Codex.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Editor;
using BEngine.Serialization;
using BEngine.Serialization.Documents;
using BEngine.Serialization.Editor;
using BEngine.Serialization.Editor.Documents;
using BEngine.UIElements;
using BEngine.UIElements.Editor;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using UiButton = BEngine.UIElements.Button;
using UiLabel = BEngine.UIElements.Label;
using UiListView = BEngine.UIElements.ListView;
using UiTreeView = BEngine.UIElements.TreeView;

namespace BEngine.Editor;

internal sealed class EditorHostApplication : IDisposable, IEditorHost
{
    [Flags]
    private enum RefreshTargets
    {
        None = 0,
        Hierarchy = 1,
        Inspector = 2,
        Project = 4,
        Console = 8
    }

    private const int HierarchyEditDebounceMilliseconds = 120;
    private const int SearchDebounceMilliseconds = 180;
    private const int ConsoleRefreshIntervalMilliseconds = 100;

    private readonly ProjectWorkspace _workspace;
    private readonly ProjectDocument _project;
    private readonly ProjectSettingsDocument _projectSettings;
    private readonly EditorPreferencesDocument _preferences;
    private readonly string _preferencesPath;
    private readonly string _editorSettingsPath;
    private readonly string _editorLayoutPath;
    private readonly string _startupLogPath;
    private readonly bool _openEditorStatusOnStart;
    private readonly bool _openUiBuilderOnStart;
    private readonly YamlSceneSerializer _sceneSerializer = new();
    private readonly YamlEditorSerializer _editorSerializer = new();
    private readonly BEngine.ProjectSystem.Editor.AssetDatabase _assetDatabase;
    private readonly BPackageManager _packageManager;
    private readonly MenuItemRegistry _menuItems;
    private readonly List<LogEntry> _logs = [];
    private readonly object _editorLogGate = new();
    private readonly List<SerializedObject> _inspectorSerializedObjects = [];
    private readonly Dictionary<EditorWindow, WinFormsVisualElementHost> _editorWindows = [];
    private readonly Dictionary<string, WinFormsVisualElementHost> _utilityWindows = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Form _form;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly EditorTrayIcon _trayIcon;
    private DockWorkspace _dock = null!;
    private WinFormsVisualElementHost _hierarchyHost = null!;
    private WinFormsVisualElementHost _inspectorHost = null!;
    private WinFormsVisualElementHost _projectHost = null!;
    private WinFormsVisualElementHost _consoleHost = null!;
    private SceneViewportControl _sceneViewport = null!;
    private SceneViewportControl _gameViewport = null!;
    private ToolStripButton _playButton = null!;
    private ToolStripButton _pauseButton = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private Scene _scene;
    private SceneRuntime? _runtime;
    private GameObject? _selected;
    private DefaultAsset? _selectedAssetObject;
    private string? _selectedAssetPath;
    private string _scenePath;
    private string _assetDirectory;
    private string? _playSnapshot;
    private Guid? _selectionBeforePlay;
    private Tool _activeTool = Tool.Move;
    private bool _playing;
    private bool _paused;
    private bool _dirty;
    private bool _disposed;
    private bool _projectTwoColumn = true;
    private string _projectSearch = string.Empty;
    private string _consoleSearch = string.Empty;
    private bool _consoleInfo = true;
    private bool _consoleWarnings = true;
    private bool _consoleErrors = true;
    private int _logVersion;
    private int _renderedLogVersion = -1;
    private int _pendingRefreshes;
    private int _debouncedRefreshes;
    private int _pendingLogStatus;
    private long _hierarchyRefreshDueMilliseconds;
    private long _projectRefreshDueMilliseconds;
    private long _consoleRefreshDueMilliseconds;
    private long _nextConsoleRefreshMilliseconds;
    private LogEntry? _pendingStatusLog;
    private EditorWindow[] _editorWindowUpdateSnapshot = [];
    private bool _editorWindowUpdateSnapshotDirty = true;
    private double _lastTick;
    private NVector3 _editorCameraPosition = new(5, 4, -7);
    private float _editorCameraYaw = -35f * MathF.PI / 180f;
    private float _editorCameraPitch = 20f * MathF.PI / 180f;
    private float _sceneCameraFieldOfView = 60f;
    private float _sceneCameraNear = 0.05f;
    private float _sceneCameraFar = 2000f;
    private bool _drawSkybox = true;
    private bool _drawGrid = true;
    private EditorLayoutDocument _layout;

    public EditorHostApplication(
        string projectPath,
        bool openEditorStatusOnStart = false,
        bool openUiBuilderOnStart = false)
    {
        _openEditorStatusOnStart = openEditorStatusOnStart;
        _openUiBuilderOnStart = openUiBuilderOnStart;
        _workspace = ProjectWorkspace.Open(projectPath);
        Directory.SetCurrentDirectory(_workspace.RootPath);
        _startupLogPath = Path.Combine(_workspace.LogsPath, "Editor.log");
        TraceStartup($"Opening {_workspace.ProjectFilePath}");
        _preferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BEngine", "Preferences.yaml");
        _preferences = LoadPreferences();
        _projectSettings = LoadProjectSettings();
        _editorSettingsPath = Path.Combine(_workspace.ProjectSettingsPath, "EditorSettings.yaml");
        _editorLayoutPath = _workspace.EditorLayoutPath;
        _layout = LoadEditorLayout();
        ApplyCameraLayout(_layout);

        _assetDatabase = new BEngine.ProjectSystem.Editor.AssetDatabase(_workspace);
        _assetDatabase.assetsChanged += OnAssetsChanged;
        _assetDatabase.Refresh();
        _packageManager = new BPackageManager(_workspace);
        CodexEditorWindow.PackageEnabled = _packageManager.IsEnabled("com.bengine.codex");

        BEngine.Application.isEditor = true;
        BEngine.Application.isPlaying = false;
        BEngine.Application.dataPath = _workspace.AssetsPath;
        BEngine.Application.productName = _projectSettings.ProductName;
        BEngine.Application.companyName = _projectSettings.CompanyName;
        Screen.SetResolution(_projectSettings.DefaultScreenWidth, _projectSettings.DefaultScreenHeight,
            _projectSettings.FullScreen);
        Time.fixedDeltaTime = Fix64.Parse(_workspace.Project.FixedDeltaTime);

        CompileProjectScripts();
        _project = _workspace.Project;
        _scenePath = ResolveInitialScene();
        _scene = _sceneSerializer.Load(_scenePath);
        _selected = _scene.gameObjects.FirstOrDefault();
        _assetDirectory = _workspace.AssetsPath;
        _menuItems = MenuItemRegistry.Discover();

        EditorBridge.Attach(this);
        _form = BuildMainForm();
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += OnTick;
        _trayIcon = new EditorTrayIcon(_form.Icon ?? SystemIcons.Application, _workspace.Project.Name,
            ShowMainWindow, OpenEditorStatus, () => _form.Close());
        Debug.MessageLogged += OnLog;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorInitialization.Run();
        TraceStartup("UIElements editor initialized.");
    }

    public void Run()
    {
        TraceStartup($"Entering editor message loop. Disposed={_form.IsDisposed}, " +
                     $"handleCreated={_form.IsHandleCreated}, state={_form.WindowState}.");
        _form.Shown += (_, _) => TraceStartup(
            $"Editor main window shown. Handle=0x{_form.Handle.ToInt64():X}, " +
            $"visible={_form.Visible}, state={_form.WindowState}.");
        if (_openEditorStatusOnStart)
            _form.Shown += (_, _) => _form.BeginInvoke(OpenEditorStatus);
        if (_openUiBuilderOnStart)
            _form.Shown += (_, _) => _form.BeginInvoke(UIBuilderWindow.Open);
        _timer.Start();
        System.Windows.Forms.Application.Run(_form);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Debug.MessageLogged -= OnLog;
        Undo.undoRedoPerformed -= OnUndoRedo;
        _assetDatabase.assetsChanged -= OnAssetsChanged;
        _trayIcon.Dispose();
        foreach (var window in _editorWindows.Keys.ToArray()) window.CloseInternal();
        _editorWindows.Clear();
        DisposeInspectorObjects();
        EditorBridge.Detach(this);
        _form.Dispose();
        GC.SuppressFinalize(this);
    }

    private Form BuildMainForm()
    {
        var form = new Form
        {
            Text = BuildTitle(),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(_layout.WindowX, _layout.WindowY),
            ClientSize = new Size(Math.Clamp(_layout.WindowWidth, 800, 7680),
                Math.Clamp(_layout.WindowHeight, 560, 4320)),
            MinimumSize = new Size(900, 620),
            BackColor = UIElementsTheme.Workspace,
            ForeColor = UIElementsTheme.Text,
            Font = UIElementsTheme.Font(_preferences.EditorFontSize <= 0
                ? 9f
                : Math.Clamp(_preferences.EditorFontSize * 0.56f, 8.5f, 18f)),
            KeyPreview = true,
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
        };
        UIElementsTheme.ApplyDarkTitleBar(form);
        if (_layout.WindowMaximized) form.WindowState = FormWindowState.Maximized;

        var retainedRoot = new VisualElement { name = "BEngineEditorRoot" };
        retainedRoot.style.flexGrow = 1;
        var editorChrome = new NativeVisualElement(BuildEditorChrome);
        editorChrome.style.flexGrow = 1;
        retainedRoot.Add(editorChrome);
        var rootHost = new WinFormsVisualElementHost { Dock = DockStyle.Fill, Root = retainedRoot };
        form.Controls.Add(rootHost);
        form.KeyDown += OnGlobalKeyDown;
        form.FormClosing += OnFormClosing;
        form.FormClosed += (_, _) => _timer.Stop();
        form.Resize += (_, _) =>
        {
            if (form.WindowState != FormWindowState.Minimized) return;
            form.BeginInvoke(() =>
            {
                form.ShowInTaskbar = false;
                form.Hide();
                _trayIcon.NotifyMinimized();
            });
        };
        form.Shown += (_, _) =>
        {
            _dock.LeftWidth = (int)Math.Max(140, _layout.HierarchyWidth);
            _dock.RightWidth = (int)Math.Max(180, _layout.InspectorWidth);
            _dock.BottomHeight = (int)Math.Max(130, _layout.BottomHeight);
        };
        return form;
    }

    private Control BuildEditorChrome()
    {
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = UIElementsTheme.Workspace };
        _dock = new DockWorkspace { Dock = DockStyle.Fill };
        _dock.layoutChanged += MarkLayoutDirty;
        shell.Controls.Add(_dock);

        var status = new StatusStrip { Dock = DockStyle.Bottom };
        UIElementsTheme.ApplyStatusStrip(status);
        _statusLabel = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        status.Items.Add(_statusLabel);
        shell.Controls.Add(status);

        var toolbar = BuildToolbar();
        shell.Controls.Add(toolbar);
        var menu = BuildMenuBar();
        shell.Controls.Add(menu);

        BuildBuiltInPanels();
        return shell;
    }

    private MenuStrip BuildMenuBar()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };
        UIElementsTheme.ApplyMenuStrip(menu);
        var file = AddMenu(menu, "文件");
        AddMenuItem(file, "保存场景", SaveScene, Keys.Control | Keys.S);
        AddMenuItem(file, "重新加载场景", ReloadScene);
        file.DropDownItems.Add(new ToolStripSeparator());
        AddMenuItem(file, "退出", () => _form.Close(), Keys.Alt | Keys.F4);

        var edit = AddMenu(menu, "编辑");
        AddMenuItem(edit, "撤销", Undo.PerformUndo, Keys.Control | Keys.Z);
        AddMenuItem(edit, "重做", Undo.PerformRedo, Keys.Control | Keys.Y);
        AddMenuItem(edit, "删除", DeleteSelected, Keys.Delete);

        var assets = AddMenu(menu, "资源");
        AddMenuItem(assets, "刷新", RefreshAssets, Keys.Control | Keys.R);
        AddMenuItem(assets, "在资源管理器显示", () => ShowInExplorer(_assetDirectory, false));

        var gameObject = AddMenu(menu, "游戏对象");
        AddMenuItem(gameObject, "创建空对象", () => CreateGameObject());
        var primitive = new ToolStripMenuItem("3D 对象");
        AddMenuItem(primitive, "立方体", () => CreatePrimitive("Cube", "Cube"));
        AddMenuItem(primitive, "平面", () => CreatePrimitive("Plane", "Plane"));
        gameObject.DropDownItems.Add(primitive);
        AddMenuItem(gameObject, "摄像机", CreateCamera);
        AddMenuItem(gameObject, "方向光", () => CreateLight(LightType.Directional));
        AddMenuItem(gameObject, "点光源", () => CreateLight(LightType.Point));
        AddMenuItem(gameObject, "天空盒", CreateSkybox);

        var window = AddMenu(menu, "窗口");
        foreach (var (id, title) in BuiltInWindowTitles)
            AddMenuItem(window, title, () => _dock.ShowPanel(id));
        window.DropDownItems.Add(new ToolStripSeparator());
        AddMenuItem(window, "包管理器", OpenPackageManager);
        AddMenuItem(window, "偏好设置", OpenPreferences);
        AddMenuItem(window, "项目设置", OpenProjectSettings);
        AddMenuItem(window, "快捷键总览", OpenShortcutWindow);
        if (CodexEditorWindow.PackageEnabled)
            AddMenuItem(window, "Codex", () => EditorWindow.GetWindow<CodexEditorWindow>("Codex"));

        var help = AddMenu(menu, "帮助");
        AddMenuItem(help, "关于 BEngine", () => MessageBox.Show(_form,
            "BEngine 0.1.0\n.NET 9 fixed-point 3D engine\nUIElements Editor",
            "关于 BEngine", MessageBoxButtons.OK, MessageBoxIcon.Information));

        foreach (var root in _menuItems.Roots.Where(root => root is not "CONTEXT"))
        {
            var target = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == root) ?? AddMenu(menu, root);
            AppendCustomMenuNodes(target.DropDownItems, _menuItems.GetRoot(root));
        }
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { Dock = DockStyle.Top };
        UIElementsTheme.ApplyMainToolbar(toolbar);
        foreach (var (text, tool, tooltip) in new[]
                 {
                     ("Q", Tool.View, "视图工具"), ("W", Tool.Move, "移动工具"),
                     ("E", Tool.Rotate, "旋转工具"), ("R", Tool.Scale, "缩放工具")
                 })
        {
            var button = new ToolStripButton(text)
            {
                ToolTipText = tooltip,
                CheckOnClick = true,
                AutoSize = false,
                Size = new Size(30, 24),
                Margin = Padding.Empty
            };
            button.Click += (_, _) => SetTool(tool, toolbar);
            toolbar.Items.Add(button);
        }
        toolbar.Items.Add(new ToolStripSeparator());
        var frame = new ToolStripButton("聚焦")
        {
            ToolTipText = "聚焦所选对象",
            AutoSize = false,
            Size = new Size(48, 24)
        };
        frame.Click += (_, _) => FrameSelected();
        toolbar.Items.Add(frame);
        toolbar.Items.Add(new ToolStripSeparator());
        _playButton = new ToolStripButton("播放") { CheckOnClick = true, AutoSize = false, Size = new Size(48, 24) };
        _playButton.Click += (_, _) => TogglePlayMode();
        toolbar.Items.Add(_playButton);
        _pauseButton = new ToolStripButton("暂停")
            { CheckOnClick = true, Enabled = false, AutoSize = false, Size = new Size(48, 24) };
        _pauseButton.Click += (_, _) => _paused = _pauseButton.Checked;
        toolbar.Items.Add(_pauseButton);
        var step = new ToolStripButton("单帧") { ToolTipText = "运行一帧", AutoSize = false, Size = new Size(48, 24) };
        step.Click += (_, _) => StepPlayMode();
        toolbar.Items.Add(step);
        toolbar.Items.Add(new ToolStripSeparator());
        var project = new ToolStripLabel($"{_workspace.Project.Name}  |  {Path.GetFileName(_scenePath)}")
        {
            ForeColor = UIElementsTheme.TextMuted,
            Alignment = ToolStripItemAlignment.Right
        };
        toolbar.Items.Add(project);
        SetTool(_activeTool, toolbar);
        return toolbar;
    }

    private void BuildBuiltInPanels()
    {
        _hierarchyHost = new WinFormsVisualElementHost();
        _inspectorHost = new WinFormsVisualElementHost();
        _projectHost = new WinFormsVisualElementHost();
        _consoleHost = new WinFormsVisualElementHost();
        _sceneViewport = new SceneViewportControl(() => _scene, () => GetEditorCamera(), () => _drawGrid,
            drawUi: false, OrbitSceneCamera, ZoomSceneCamera, drawGizmos: true);
        _gameViewport = new SceneViewportControl(() => _scene,
            () => EngineRenderer.TryResolveGameCamera(_scene, out var camera) ? camera : null,
            () => false, drawUi: true,
            emptyCameraMessage: "未找到启用的相机\n请在场景中添加 Camera 并设为主相机");

        _dock.AddPanel("Hierarchy", "Hierarchy", _hierarchyHost, DockZone.Left);
        _dock.AddPanel("Scene", "Scene", BuildScenePanel(), DockZone.Center);
        _dock.AddPanel("Game", "Game", _gameViewport, DockZone.Center);
        _dock.AddPanel("Inspector", "Inspector", _inspectorHost, DockZone.Right);
        _dock.AddPanel("Project", "Project", _projectHost, DockZone.Bottom);
        _dock.AddPanel("Console", "Console", _consoleHost, DockZone.Bottom);
        RefreshHierarchy();
        RefreshInspector();
        RefreshProject();
        RefreshConsole(force: true);
        if (!_layout.ShowHierarchy) _dock.ClosePanel("Hierarchy");
        if (!_layout.ShowSceneView) _dock.ClosePanel("Scene");
        if (!_layout.ShowGameView) _dock.ClosePanel("Game");
        if (!_layout.ShowInspector) _dock.ClosePanel("Inspector");
        if (!_layout.ShowBottomPanel || !_layout.ShowProject) _dock.ClosePanel("Project");
        if (!_layout.ShowBottomPanel || !_layout.ShowConsole) _dock.ClosePanel("Console");
    }

    private Control BuildScenePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UIElementsTheme.Panel };
        var toolbar = new ToolStrip { Dock = DockStyle.Top };
        UIElementsTheme.ApplyCompactToolbar(toolbar);
        var skybox = new ToolStripButton("天空盒") { Checked = _drawSkybox, CheckOnClick = true };
        skybox.CheckedChanged += (_, _) => { _drawSkybox = skybox.Checked; MarkLayoutDirty(); };
        var grid = new ToolStripButton("网格") { Checked = _drawGrid, CheckOnClick = true };
        grid.CheckedChanged += (_, _) => { _drawGrid = grid.Checked; MarkLayoutDirty(); };
        toolbar.Items.Add(skybox);
        toolbar.Items.Add(grid);
        toolbar.Items.Add(new ToolStripSeparator());
        var camera = new ToolStripButton("Scene Camera");
        camera.Click += (_, _) => OpenSceneCameraSettings();
        toolbar.Items.Add(camera);
        panel.Controls.Add(_sceneViewport);
        panel.Controls.Add(toolbar);
        return panel;
    }

    private void RefreshHierarchy()
    {
        CancelScheduledRefresh(RefreshTargets.Hierarchy);
        if (_hierarchyHost is null) return;
        var root = new VisualElement { name = "HierarchyRoot" };
        root.style.flexGrow = 1;
        root.style.backgroundColor = UIElementsTheme.UiColor(UIElementsTheme.Panel);
        var toolbar = new Toolbar();
        toolbar.Add(new UiButton(() => CreateGameObject(), "+"));
        toolbar.Add(new UiButton(RefreshHierarchy, "刷新"));
        root.Add(toolbar);
        var tree = new UiTreeView
        {
            items = _scene.rootGameObjects.Select(BuildHierarchyItem).ToArray(),
            selectedId = _selected?.GetInstanceID()
        };
        tree.style.flexGrow = 1;
        tree.selectionChanged += item =>
        {
            if (item?.Data is not GameObject gameObject) return;
            SelectGameObject(gameObject, updateHierarchySelection: false);
        };
        tree.contextMenuRequested += (item, menu) =>
        {
            if (item.Data is GameObject gameObject) SelectGameObject(gameObject);
            menu.AddAction("创建空子对象", () => CreateGameObject(_selected?.transform));
            menu.AddAction("复制", DuplicateSelected, _selected is not null);
            menu.AddSeparator();
            menu.AddAction("删除", DeleteSelected, _selected is not null);
        };
        root.Add(tree);
        _hierarchyHost.Root = root;
    }

    private TreeViewItem BuildHierarchyItem(GameObject gameObject) => new(
        gameObject.GetInstanceID(),
        gameObject.name,
        gameObject,
        gameObject.transform.children.Select(child => BuildHierarchyItem(child.gameObject)).ToArray());

    private void RefreshInspector()
    {
        CancelScheduledRefresh(RefreshTargets.Inspector);
        if (_inspectorHost is null) return;
        DisposeInspectorObjects();
        var root = new ScrollView { name = "InspectorRoot" };
        root.style.SetPadding(4);
        root.style.backgroundColor = UIElementsTheme.UiColor(UIElementsTheme.Panel);
        if (_selected is not null) BuildGameObjectInspector(root, _selected);
        else if (_selectedAssetPath is not null) BuildAssetInspector(root, _selectedAssetPath);
        else root.Add(new UiLabel("未选择对象"));
        _inspectorHost.Root = root;
    }

    private void BuildGameObjectInspector(VisualElement root, GameObject gameObject)
    {
        var active = new Toggle("启用") { value = gameObject.activeSelf };
        active.valueChanged += value =>
        {
            gameObject.SetActive(value);
            MarkDirty();
            RequestRefresh(RefreshTargets.Hierarchy);
        };
        root.Add(active);
        var name = new TextField("名称") { value = gameObject.name };
        name.valueChanged += value =>
        {
            gameObject.name = value;
            MarkDirty();
            RequestDebouncedRefresh(RefreshTargets.Hierarchy, HierarchyEditDebounceMilliseconds);
        };
        root.Add(name);
        var tag = new TextField("Tag") { value = gameObject.tag };
        tag.valueChanged += value => { gameObject.tag = value; MarkDirty(); };
        root.Add(tag);
        var layer = new IntegerField("Layer") { value = gameObject.layer };
        layer.valueChanged += value => { gameObject.layer = value; MarkDirty(); };
        root.Add(layer);
        foreach (var component in gameObject.components.ToArray()) BuildComponentInspector(root, component);
        var add = new UiButton(() => ShowAddComponentMenu(gameObject), "添加组件");
        add.style.marginTop = 8;
        root.Add(add);
    }

    private void BuildComponentInspector(VisualElement root, Component component)
    {
        var section = new VisualElement();
        section.style.backgroundColor = UIElementsTheme.UiColor(UIElementsTheme.PanelRaised);
        section.style.SetPadding(4);
        section.style.marginTop = 4;
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.height = 24;
        header.style.backgroundColor = UIElementsTheme.UiColor(UIElementsTheme.PanelHeader);
        header.style.SetPadding(2, 1, 2, 1);
        if (component is Behaviour behaviour)
        {
            var enabled = new Toggle { value = behaviour.enabled };
            enabled.valueChanged += value => { behaviour.enabled = value; MarkDirty(); };
            header.Add(enabled);
        }
        var title = new UiLabel(ObjectNames.NicifyVariableName(component.GetType().Name)) { tooltip = component.GetType().FullName ?? string.Empty };
        title.style.flexGrow = 1;
        title.style.color = UIElementsTheme.UiColor(UIElementsTheme.TextStrong);
        header.Add(title);
        if (component is not Transform)
        {
            var remove = new UiButton(() =>
            {
                component.gameObject.RemoveComponent(component);
                MarkDirty();
                RefreshInspector();
            }, "x") { tooltip = "移除组件" };
            remove.style.width = 24;
            remove.style.height = 20;
            header.Add(remove);
        }
        section.Add(header);

        var source = ProjectScriptSourceLocator.Find(_workspace,
            component.GetType().AssemblyQualifiedName ?? component.GetType().FullName ?? component.GetType().Name);
        var script = new TextField("Script") { isReadOnly = true, value = component.GetType().Name };
        script.doubleClicked += () =>
        {
            if (!string.IsNullOrWhiteSpace(source)) OpenScript(source);
        };
        script.tooltip = source ?? "内置组件";
        section.Add(script);

        var serializedObject = SerializedObject.Create(component);
        _inspectorSerializedObjects.Add(serializedObject);
        var editor = BEngine.Editor.Editor.CreateEditor(component);
        if (editor.CreateInspectorGUI() is { } customInspector)
        {
            section.Add(customInspector);
        }
        else
        {
            foreach (var property in serializedObject.GetVisibleProperties())
            {
                if (property.propertyPath is nameof(Component.enabled)) continue;
                if (PropertyDrawerRegistry.TryCreate(property, out var drawer) &&
                    drawer.CreatePropertyGUI(property) is { } customProperty)
                {
                    section.Add(customProperty);
                }
                else
                {
                    section.Add(CreateSerializedField(serializedObject, property));
                }
            }
        }
        root.Add(section);
    }

    private VisualElement CreateSerializedField(SerializedObject serializedObject, SerializedProperty property)
    {
        void Commit()
        {
            if (serializedObject.ApplyModifiedProperties()) MarkDirty();
        }

        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean:
            {
                var field = new Toggle(property.displayName) { value = property.boolValue, tooltip = property.tooltip };
                field.valueChanged += value => { property.boolValue = value; Commit(); };
                return field;
            }
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            {
                var field = new IntegerField(property.displayName) { value = property.intValue, tooltip = property.tooltip };
                field.valueChanged += value => { property.intValue = value; Commit(); };
                return field;
            }
            case SerializedPropertyType.Float:
            {
                var field = new FloatField(property.displayName) { value = property.floatValue, tooltip = property.tooltip };
                field.valueChanged += value => { property.floatValue = value; Commit(); };
                return field;
            }
            case SerializedPropertyType.String:
            {
                var field = new TextField(property.displayName) { value = property.stringValue, tooltip = property.tooltip };
                field.valueChanged += value => { property.stringValue = value; Commit(); };
                return field;
            }
            case SerializedPropertyType.Enum:
            {
                var names = property.enumDisplayNames;
                var selected = property.enumValueIndex >= 0 && property.enumValueIndex < names.Length
                    ? names[property.enumValueIndex]
                    : names.FirstOrDefault() ?? string.Empty;
                var field = new DropdownField(property.displayName, names) { value = selected, tooltip = property.tooltip };
                field.valueChanged += value =>
                {
                    property.enumValueIndex = Math.Max(0, Array.IndexOf(names, value));
                    Commit();
                };
                return field;
            }
            case SerializedPropertyType.Vector2:
            {
                var value = property.vector2Value;
                return CreateVectorFields(property.displayName, [("X", (float)value.x), ("Y", (float)value.y)], values =>
                {
                    property.vector2Value = new Vector2((Fix64)values[0], (Fix64)values[1]);
                    Commit();
                });
            }
            case SerializedPropertyType.Vector3:
            {
                var value = property.vector3Value;
                return CreateVectorFields(property.displayName,
                    [("X", (float)value.x), ("Y", (float)value.y), ("Z", (float)value.z)], values =>
                    {
                        property.vector3Value = new Vector3((Fix64)values[0], (Fix64)values[1], (Fix64)values[2]);
                        Commit();
                    });
            }
            case SerializedPropertyType.Vector4:
            case SerializedPropertyType.Quaternion:
            case SerializedPropertyType.Color:
            case SerializedPropertyType.Rect:
            case SerializedPropertyType.Bounds:
            case SerializedPropertyType.ObjectReference:
            case SerializedPropertyType.Generic:
            default:
                return new TextField(property.displayName)
                {
                    value = property.boxedValue?.ToString() ?? "None",
                    isReadOnly = true,
                    tooltip = property.tooltip
                };
        }
    }

    private static VisualElement CreateVectorFields(
        string label,
        IReadOnlyList<(string Name, float Value)> components,
        Action<float[]> changed)
    {
        var values = components.Select(component => component.Value).ToArray();
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        var caption = new UiLabel(label);
        caption.style.flexGrow = 2;
        container.Add(caption);
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexGrow = 3;
        for (var index = 0; index < components.Count; index++)
        {
            var componentIndex = index;
            var field = new FloatField(components[index].Name) { value = components[index].Value };
            field.style.flexGrow = 1;
            field.style.minWidth = 48;
            field.valueChanged += value =>
            {
                values[componentIndex] = value;
                changed(values);
            };
            row.Add(field);
        }
        container.Add(row);
        return container;
    }

    private void BuildAssetInspector(VisualElement root, string path)
    {
        var record = _assetDatabase.GetRecord(NormalizeAssetPath(path));
        root.Add(new UiLabel(Path.GetFileName(path)) { tooltip = path });
        root.Add(new TextField("资源路径") { value = NormalizeAssetPath(path), isReadOnly = true });
        root.Add(new TextField("类型") { value = record?.AssetType ?? "DefaultAsset", isReadOnly = true });
        if (File.Exists(path))
        {
            root.Add(new TextField("大小") { value = $"{new FileInfo(path).Length:N0} bytes", isReadOnly = true });
            if (IsImage(path))
            {
                var preview = new BEngine.UIElements.Image { sourcePath = path };
                preview.style.height = 220;
                root.Add(preview);
            }
            else if (IsTextAsset(path))
            {
                var preview = new TextField("预览")
                {
                    value = ReadPreviewText(path),
                    multiline = true,
                    isReadOnly = true
                };
                preview.style.height = 260;
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) preview.doubleClicked += () => OpenScript(path);
                root.Add(preview);
            }
        }
        var actions = new Toolbar();
        actions.Add(new UiButton(() => OpenAssetOrExternal(path), "打开"));
        actions.Add(new UiButton(() => ShowInExplorer(path, true), "在资源管理器显示"));
        root.Add(actions);
    }

    private void RefreshProject()
    {
        CancelScheduledRefresh(RefreshTargets.Project);
        if (_projectHost is null) return;
        var root = new VisualElement { name = "ProjectRoot" };
        root.style.flexGrow = 1;
        root.style.backgroundColor = UIElementsTheme.UiColor(UIElementsTheme.Panel);
        var toolbar = new Toolbar();
        var search = new SearchField { value = _projectSearch };
        search.style.width = 220;
        search.valueChanged += value =>
        {
            if (string.Equals(_projectSearch, value, StringComparison.Ordinal)) return;
            _projectSearch = value;
            RequestDebouncedRefresh(RefreshTargets.Project, SearchDebounceMilliseconds);
        };
        toolbar.Add(search);
        var layout = new DropdownField(string.Empty, ["双栏", "单栏"])
        {
            value = _projectTwoColumn ? "双栏" : "单栏"
        };
        layout.style.width = 92;
        layout.valueChanged += value => { _projectTwoColumn = value == "双栏"; RefreshProject(); MarkLayoutDirty(); };
        toolbar.Add(layout);
        toolbar.Add(new UiButton(RefreshAssets, "刷新"));
        root.Add(toolbar);
        var browser = new NativeVisualElement(BuildProjectBrowserControl);
        browser.style.flexGrow = 1;
        root.Add(browser);
        _projectHost.Root = root;
    }

    private Control BuildProjectBrowserControl()
    {
        if (!_projectTwoColumn)
        {
            var host = new WinFormsVisualElementHost { Root = BuildAssetList(_workspace.AssetsPath, recursive: true) };
            return host;
        }
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = Math.Clamp((int)_layout.ProjectFoldersWidth, 100, 600)
        };
        UIElementsTheme.ApplySplitContainer(split);
        var folders = new UiTreeView { items = [BuildAssetFolderItem(_workspace.AssetsPath)] };
        folders.selectionChanged += item =>
        {
            if (item?.Data is string directory)
            {
                _assetDirectory = directory;
                RequestRefresh(RefreshTargets.Project);
            }
        };
        var folderHost = new WinFormsVisualElementHost { Root = folders };
        var filesHost = new WinFormsVisualElementHost { Root = BuildAssetList(_assetDirectory, recursive: false) };
        split.Panel1.Controls.Add(folderHost);
        split.Panel2.Controls.Add(filesHost);
        split.SplitterMoved += (_, _) => { _layout.ProjectFoldersWidth = split.SplitterDistance; MarkLayoutDirty(); };
        return split;
    }

    private TreeViewItem BuildAssetFolderItem(string directory)
    {
        var id = StringComparer.OrdinalIgnoreCase.GetHashCode(Path.GetFullPath(directory));
        return new TreeViewItem(id, Path.GetFileName(directory), directory,
            Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName)
                .Select(BuildAssetFolderItem).ToArray());
    }

    private UiListView BuildAssetList(string directory, bool recursive)
    {
        var options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var paths = Directory.EnumerateFileSystemEntries(directory, "*", options)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(_projectSearch) ||
                           Path.GetFileName(path).Contains(_projectSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Directory.Exists(path) ? 0 : 1)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToArray();
        var list = new UiListView
        {
            itemsSource = paths,
            makeItemText = item => item is string path
                ? $"{(Directory.Exists(path) ? "[Folder]" : "[Asset]")}  {Path.GetFileName(path)}"
                : string.Empty
        };
        list.style.flexGrow = 1;
        list.selectionChanged += item =>
        {
            if (item is string path) SelectAsset(path);
        };
        list.itemChosen += item =>
        {
            if (item is not string path) return;
            if (Directory.Exists(path)) { _assetDirectory = path; RequestRefresh(RefreshTargets.Project); }
            else OpenAssetOrExternal(path);
        };
        list.contextMenuRequested += (item, menu) => BuildAssetContextMenu(item as string, menu);
        return list;
    }

    private void BuildAssetContextMenu(string? path, ContextMenuBuilder menu)
    {
        menu.AddAction("新建文件夹", CreateAssetFolder);
        menu.AddAction("新建 C# 脚本", CreateScriptAsset);
        menu.AddSeparator();
        menu.AddAction("打开", () => { if (path is not null) OpenAssetOrExternal(path); }, path is not null);
        menu.AddAction("在资源管理器显示", () => ShowInExplorer(path ?? _assetDirectory,
            path is not null && File.Exists(path)));
        menu.AddAction("复制路径", () => Clipboard.SetText(path ?? _assetDirectory));
        menu.AddSeparator();
        menu.AddAction("删除", () => DeleteAssetWithConfirmation(path),
            path is not null && !Path.GetFullPath(path).Equals(Path.GetFullPath(_workspace.AssetsPath),
                StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshConsole(bool force = false)
    {
        CancelScheduledRefresh(RefreshTargets.Console);
        if (_consoleHost is null) return;
        LogEntry[] snapshot;
        int version;
        lock (_logs)
        {
            version = _logVersion;
            if (!force && version == _renderedLogVersion) return;
            snapshot = _logs.ToArray();
        }
        _renderedLogVersion = version;
        var root = new VisualElement { name = "ConsoleRoot" };
        root.style.flexGrow = 1;
        root.style.backgroundColor = UIElementsTheme.UiColor(UIElementsTheme.Panel);
        var toolbar = new Toolbar();
        toolbar.Add(new UiButton(ClearConsole, "清除"));
        var search = new SearchField { value = _consoleSearch };
        search.style.width = 220;
        search.valueChanged += value =>
        {
            if (string.Equals(_consoleSearch, value, StringComparison.Ordinal)) return;
            _consoleSearch = value;
            RequestDebouncedRefresh(RefreshTargets.Console, SearchDebounceMilliseconds);
        };
        toolbar.Add(search);
        var info = new Toggle("日志") { value = _consoleInfo };
        info.valueChanged += value =>
        {
            if (_consoleInfo == value) return;
            _consoleInfo = value;
            RequestRefresh(RefreshTargets.Console);
        };
        toolbar.Add(info);
        var warnings = new Toggle("警告") { value = _consoleWarnings };
        warnings.valueChanged += value =>
        {
            if (_consoleWarnings == value) return;
            _consoleWarnings = value;
            RequestRefresh(RefreshTargets.Console);
        };
        toolbar.Add(warnings);
        var errors = new Toggle("错误") { value = _consoleErrors };
        errors.valueChanged += value =>
        {
            if (_consoleErrors == value) return;
            _consoleErrors = value;
            RequestRefresh(RefreshTargets.Console);
        };
        toolbar.Add(errors);
        root.Add(toolbar);
        var filtered = snapshot.Where(IsVisibleLog).Cast<object>().ToArray();
        var list = new UiListView
        {
            itemsSource = filtered,
            makeItemText = item => item is LogEntry entry
                ? $"[{entry.Timestamp:HH:mm:ss}] {entry.Type,-7} {entry.Message}"
                : string.Empty
        };
        list.AddToClassList("unity-console-list");
        list.style.flexGrow = 1;
        list.contextMenuRequested += (item, menu) =>
        {
            if (item is LogEntry entry) menu.AddAction("复制", () => Clipboard.SetText(entry.Message));
        };
        root.Add(list);
        _consoleHost.Root = root;
    }

    private bool IsVisibleLog(LogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(_consoleSearch) &&
            !entry.Message.Contains(_consoleSearch, StringComparison.OrdinalIgnoreCase)) return false;
        return entry.Type switch
        {
            LogType.Warning => _consoleWarnings,
            LogType.Error => _consoleErrors,
            _ => _consoleInfo
        };
    }

    private void ClearConsole()
    {
        lock (_logs)
        {
            _logs.Clear();
            _logVersion++;
        }
        RefreshConsole(force: true);
        _nextConsoleRefreshMilliseconds = Environment.TickCount64 + ConsoleRefreshIntervalMilliseconds;
    }

    private void OpenPackageManager()
    {
        var root = new ScrollView();
        root.Add(new UiLabel("包管理器") { tooltip = "Packages/manifest.yaml" });
        foreach (var package in _packageManager.packages.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var toggle = new Toggle(package.Id) { value = package.Enabled };
            toggle.style.width = 360;
            toggle.valueChanged += value =>
            {
                _packageManager.SetEnabled(package.Id, value);
                CodexEditorWindow.PackageEnabled = _packageManager.IsEnabled("com.bengine.codex");
                OpenPackageManager();
            };
            row.Add(toggle);
            row.Add(new UiLabel(package.Version));
            root.Add(row);
        }
        OpenUtilityPanel("PackageManager", "Package Manager", root);
    }

    private void OpenPreferences()
    {
        var root = new ScrollView();
        root.Add(new UiLabel("BEngine 偏好设置"));
        var locale = new DropdownField("语言", ["zh-CN", "en-US"]) { value = _preferences.Locale };
        locale.valueChanged += value => _preferences.Locale = value;
        root.Add(locale);
        var editor = new TextField("外部脚本编辑器") { value = _preferences.ExternalScriptEditor };
        editor.valueChanged += value => _preferences.ExternalScriptEditor = value;
        root.Add(editor);
        var font = new IntegerField("编辑器字号") { value = _preferences.EditorFontSize };
        font.valueChanged += value => _preferences.EditorFontSize = Math.Clamp(value, 10, 32);
        root.Add(font);
        var autoRefresh = new Toggle("自动刷新资源") { value = _preferences.AutoRefreshAssets };
        autoRefresh.valueChanged += value => _preferences.AutoRefreshAssets = value;
        root.Add(autoRefresh);
        root.Add(new UiButton(() =>
        {
            YamlUtility.Save(_preferences, _preferencesPath);
            SetStatus("偏好设置已保存");
        }, "保存"));
        OpenUtilityPanel("Preferences", "Preferences", root);
    }

    private void OpenProjectSettings()
    {
        var root = new ScrollView();
        root.Add(new UiLabel("项目设置"));
        var product = new TextField("产品名称") { value = _projectSettings.ProductName };
        product.valueChanged += value => _projectSettings.ProductName = value;
        root.Add(product);
        var company = new TextField("公司名称") { value = _projectSettings.CompanyName };
        company.valueChanged += value => _projectSettings.CompanyName = value;
        root.Add(company);
        var width = new IntegerField("默认宽度") { value = _projectSettings.DefaultScreenWidth };
        width.valueChanged += value => _projectSettings.DefaultScreenWidth = Math.Max(1, value);
        root.Add(width);
        var height = new IntegerField("默认高度") { value = _projectSettings.DefaultScreenHeight };
        height.valueChanged += value => _projectSettings.DefaultScreenHeight = Math.Max(1, value);
        root.Add(height);
        var fullscreen = new Toggle("全屏") { value = _projectSettings.FullScreen };
        fullscreen.valueChanged += value => _projectSettings.FullScreen = value;
        root.Add(fullscreen);
        root.Add(new UiButton(() =>
        {
            YamlUtility.Save(_projectSettings, _workspace.ProjectSettingsFilePath);
            SetStatus("项目设置已保存");
        }, "保存"));
        OpenUtilityPanel("ProjectSettings", "Project Settings", root);
    }

    private void OpenSceneCameraSettings()
    {
        var root = new ScrollView();
        var fov = new FloatField("视野") { value = _sceneCameraFieldOfView };
        fov.valueChanged += value => { _sceneCameraFieldOfView = Math.Clamp(value, 10, 170); MarkLayoutDirty(); };
        root.Add(fov);
        var near = new FloatField("近裁剪面") { value = _sceneCameraNear };
        near.valueChanged += value => { _sceneCameraNear = Math.Clamp(value, 0.001f, _sceneCameraFar - 0.001f); MarkLayoutDirty(); };
        root.Add(near);
        var far = new FloatField("远裁剪面") { value = _sceneCameraFar };
        far.valueChanged += value => { _sceneCameraFar = Math.Max(_sceneCameraNear + 0.001f, value); MarkLayoutDirty(); };
        root.Add(far);
        root.Add(new UiButton(FrameSelected, "聚焦所选对象"));
        OpenUtilityPanel("SceneCamera", "Scene Camera", root);
    }

    private void OpenShortcutWindow()
    {
        var root = new ScrollView();
        root.Add(new UiLabel("快捷键总览"));
        foreach (var line in new[]
                 {
                     "Ctrl+S    保存场景", "Ctrl+Z    撤销", "Ctrl+Y    重做", "Delete    删除所选对象",
                     "Q         视图工具", "W         移动工具", "E         旋转工具", "R         缩放工具",
                     "F         聚焦所选对象", "Ctrl+R    刷新资源"
                 }) root.Add(new UiLabel(line));
        OpenUtilityPanel("Shortcuts", "Shortcuts", root);
    }

    private void OpenUtilityPanel(string id, string title, VisualElement root)
    {
        if (_utilityWindows.TryGetValue(id, out var existing))
        {
            existing.Root = root;
            _dock.ShowPanel(id);
            return;
        }
        var host = new WinFormsVisualElementHost { Root = root };
        _utilityWindows.Add(id, host);
        _dock.AddPanel(id, title, host, DockZone.Center);
        _dock.ShowPanel(id);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - _lastTick, 0, 0.1);
        _lastTick = now;
        Time.deltaTime = (Fix64)delta;
        Time.time += Time.deltaTime;
        EditorApplication.RaiseUpdate();
        if (_playing && !_paused && _runtime is not null) _runtime.Tick(Time.deltaTime);
        UpdateEditorWindows();
        _sceneViewport?.Tick();
        _gameViewport?.Tick();
        FlushPendingLogStatus();
        FlushScheduledRefreshes();
        RefreshConsoleIfNeeded();
    }

    private void UpdateEditorWindows()
    {
        if (_editorWindowUpdateSnapshotDirty)
        {
            _editorWindowUpdateSnapshot = _editorWindows.Keys.Where(window => window.IsOpen).ToArray();
            _editorWindowUpdateSnapshotDirty = false;
        }

        foreach (var window in _editorWindowUpdateSnapshot) window.UpdateInternal();
    }

    private void RequestRefresh(RefreshTargets targets) =>
        Interlocked.Or(ref _pendingRefreshes, (int)targets);

    private void RequestDebouncedRefresh(RefreshTargets targets, int delayMilliseconds)
    {
        var due = Environment.TickCount64 + Math.Max(0, delayMilliseconds);
        if ((targets & RefreshTargets.Hierarchy) != 0)
            Volatile.Write(ref _hierarchyRefreshDueMilliseconds, due);
        if ((targets & RefreshTargets.Project) != 0)
            Volatile.Write(ref _projectRefreshDueMilliseconds, due);
        if ((targets & RefreshTargets.Console) != 0)
            Volatile.Write(ref _consoleRefreshDueMilliseconds, due);

        var debounced = targets & (RefreshTargets.Hierarchy | RefreshTargets.Project | RefreshTargets.Console);
        if (debounced != RefreshTargets.None) Interlocked.Or(ref _debouncedRefreshes, (int)debounced);
        var immediate = targets & RefreshTargets.Inspector;
        if (immediate != RefreshTargets.None) RequestRefresh(immediate);
    }

    private void FlushScheduledRefreshes()
    {
        var now = Environment.TickCount64;
        var ready = (RefreshTargets)Interlocked.Exchange(ref _pendingRefreshes, 0);
        var debounced = (RefreshTargets)Volatile.Read(ref _debouncedRefreshes);
        if ((debounced & RefreshTargets.Hierarchy) != 0 &&
            now >= Volatile.Read(ref _hierarchyRefreshDueMilliseconds))
            ready |= RefreshTargets.Hierarchy;
        if ((debounced & RefreshTargets.Project) != 0 &&
            now >= Volatile.Read(ref _projectRefreshDueMilliseconds))
            ready |= RefreshTargets.Project;
        if ((debounced & RefreshTargets.Console) != 0 &&
            now >= Volatile.Read(ref _consoleRefreshDueMilliseconds))
            ready |= RefreshTargets.Console;

        if (ready == RefreshTargets.None) return;
        Interlocked.And(ref _debouncedRefreshes, ~(int)ready);
        if ((ready & RefreshTargets.Hierarchy) != 0) RefreshHierarchy();
        if ((ready & RefreshTargets.Inspector) != 0) RefreshInspector();
        if ((ready & RefreshTargets.Project) != 0) RefreshProject();
        if ((ready & RefreshTargets.Console) != 0) RefreshConsole(force: true);
    }

    private void CancelScheduledRefresh(RefreshTargets targets)
    {
        Interlocked.And(ref _pendingRefreshes, ~(int)targets);
        Interlocked.And(ref _debouncedRefreshes, ~(int)targets);
    }

    private void FlushPendingLogStatus()
    {
        if (Interlocked.Exchange(ref _pendingLogStatus, 0) == 0) return;
        LogEntry? entry;
        lock (_logs)
        {
            entry = _pendingStatusLog;
            _pendingStatusLog = null;
        }
        if (entry is { } value) SetStatus(value.Message, value.Type);
    }

    private void RefreshConsoleIfNeeded()
    {
        if (_consoleHost is null || !_consoleHost.Visible) return;
        if (Volatile.Read(ref _logVersion) == _renderedLogVersion) return;
        if (((RefreshTargets)Volatile.Read(ref _debouncedRefreshes) & RefreshTargets.Console) != 0) return;
        var now = Environment.TickCount64;
        if (now < _nextConsoleRefreshMilliseconds) return;
        RefreshConsole();
        _nextConsoleRefreshMilliseconds = now + ConsoleRefreshIntervalMilliseconds;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.L) { OpenEditorStatus(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.S) { SaveScene(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.Z) { Undo.PerformUndo(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.Y) { Undo.PerformRedo(); e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.R) { RefreshAssets(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.F) { FrameSelected(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Q) SetTool(Tool.View);
        else if (e.KeyCode == Keys.W) SetTool(Tool.Move);
        else if (e.KeyCode == Keys.E) SetTool(Tool.Rotate);
        else if (e.KeyCode == Keys.R) SetTool(Tool.Scale);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_playing) StopPlayMode();
        if (_dirty)
        {
            var result = MessageBox.Show(_form, "场景有未保存的修改，是否保存？", "BEngine",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) { e.Cancel = true; return; }
            if (result == DialogResult.Yes) SaveScene();
        }
        SaveLayout();
        EditorApplication.RaiseQuitting();
    }

    private void OnLog(LogEntry entry)
    {
        lock (_logs)
        {
            _logs.Add(entry);
            _logVersion++;
            _pendingStatusLog = entry;
        }
        Interlocked.Exchange(ref _pendingLogStatus, 1);
        AppendEditorLog(entry.Timestamp, $"[{entry.Type}] {entry.Message}");
    }

    private void OnUndoRedo()
    {
        MarkDirty();
        RequestRefresh(RefreshTargets.Hierarchy | RefreshTargets.Inspector);
    }

    private void OnAssetsChanged(IReadOnlyList<AssetChange> changes)
    {
        EditorApplication.RaiseProjectChanged();
        AssetPostprocessorDispatcher.Notify(
            changes.Where(change => change.Kind is AssetChangeKind.Imported or AssetChangeKind.Updated)
                .Select(change => change.AssetPath).ToArray(),
            changes.Where(change => change.Kind == AssetChangeKind.Deleted).Select(change => change.AssetPath).ToArray(),
            changes.Where(change => change.Kind == AssetChangeKind.Moved).Select(change => change.AssetPath).ToArray(),
            changes.Where(change => change.Kind == AssetChangeKind.Moved)
                .Select(change => change.PreviousPath ?? string.Empty).ToArray());
        var targets = RefreshTargets.Project;
        if (_selectedAssetPath is not null) targets |= RefreshTargets.Inspector;
        RequestRefresh(targets);
    }

    private void SelectGameObject(GameObject gameObject, bool updateHierarchySelection = true)
    {
        var changed = !ReferenceEquals(_selected, gameObject) || _selectedAssetPath is not null;
        _selected = gameObject;
        _selectedAssetPath = null;
        _selectedAssetObject = null;
        if (!changed) return;
        Selection.NotifyHostSelectionChanged(gameObject);
        RefreshInspector();
        if (updateHierarchySelection) RequestRefresh(RefreshTargets.Hierarchy);
    }

    private void SelectAsset(string path)
    {
        _selected = null;
        _selectedAssetPath = path;
        _selectedAssetObject = null;
        Selection.NotifyHostSelectionChanged(((IEditorHost)this).ActiveObject);
        RefreshInspector();
    }

    private GameObject CreateGameObject(Transform? parent = null)
    {
        var gameObject = _scene.CreateGameObject("GameObject");
        if (parent is not null) gameObject.transform.SetParent(parent, false);
        SelectGameObject(gameObject);
        MarkDirty();
        EditorApplication.RaiseHierarchyChanged();
        return gameObject;
    }

    private void CreatePrimitive(string name, string mesh)
    {
        var gameObject = _scene.CreateGameObject(name);
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.mesh = mesh;
        if (mesh.Equals("Plane", StringComparison.OrdinalIgnoreCase)) gameObject.transform.localScale = new Vector3(5, 1, 5);
        SelectGameObject(gameObject);
        MarkDirty();
        EditorApplication.RaiseHierarchyChanged();
    }

    private void CreateCamera()
    {
        var gameObject = _scene.CreateGameObject("Camera");
        gameObject.AddComponent<Camera>();
        SelectGameObject(gameObject);
        MarkDirty();
    }

    private void CreateLight(LightType type)
    {
        var gameObject = _scene.CreateGameObject(type == LightType.Directional ? "Directional Light" : "Point Light");
        var light = type == LightType.Directional
            ? gameObject.AddComponent<DirectionalLight>()
            : gameObject.AddComponent<Light>();
        light.type = type;
        if (type == LightType.Directional) gameObject.transform.localEulerAngles = new Vector3(50, -30, 0);
        SelectGameObject(gameObject);
        MarkDirty();
    }

    private void CreateSkybox()
    {
        var gameObject = _scene.CreateGameObject("Skybox");
        gameObject.AddComponent<Skybox>();
        SelectGameObject(gameObject);
        MarkDirty();
    }

    private void DuplicateSelected()
    {
        if (_selected is null) return;
        var copy = _scene.CreateGameObject(_selected.name + " Copy");
        copy.activeSelf = _selected.activeSelf;
        copy.tag = _selected.tag;
        copy.layer = _selected.layer;
        copy.transform.localPosition = _selected.transform.localPosition;
        copy.transform.localEulerAngles = _selected.transform.localEulerAngles;
        copy.transform.localScale = _selected.transform.localScale;
        foreach (var component in _selected.components.Where(component => component is not Transform))
        {
            var target = copy.AddComponent(component.GetType());
            ComponentFieldSerializer.Deserialize(target, ComponentFieldSerializer.Serialize(component));
        }
        SelectGameObject(copy);
        MarkDirty();
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        var next = _scene.gameObjects.FirstOrDefault(item => !ReferenceEquals(item, _selected));
        _scene.Destroy(_selected);
        _selected = next;
        Selection.NotifyHostSelectionChanged(next);
        MarkDirty();
        RequestRefresh(RefreshTargets.Hierarchy);
        RefreshInspector();
        EditorApplication.RaiseHierarchyChanged();
    }

    private void ShowAddComponentMenu(GameObject target)
    {
        var menu = new ContextMenuStrip();
        UIElementsTheme.ApplyContextMenu(menu);
        foreach (var type in TypeCache.GetTypesDerivedFrom<Component>()
                     .Where(type => !type.IsAbstract && type != typeof(Transform))
                     .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ToolStripMenuItem(ObjectNames.NicifyVariableName(type.Name));
            item.Click += (_, _) =>
            {
                try { target.AddComponent(type); MarkDirty(); RefreshInspector(); }
                catch (Exception exception) { Debug.LogError(exception.Message); }
            };
            menu.Items.Add(item);
        }
        menu.Show(Cursor.Position);
    }

    private void StartPlayMode()
    {
        EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
        _playSnapshot = _sceneSerializer.Serialize(_scene);
        _selectionBeforePlay = _selected?.Id;
        _runtime = new SceneRuntime(_scene);
        _runtime.Start();
        _playing = true;
        _paused = false;
        BEngine.Application.isPlaying = true;
        _playButton.Checked = true;
        _playButton.Text = "停止";
        _pauseButton.Enabled = true;
        EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
    }

    private void StopPlayMode()
    {
        EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
        _runtime?.Stop();
        if (_playSnapshot is not null)
        {
            _scene = _sceneSerializer.Deserialize(_playSnapshot);
            _selected = _selectionBeforePlay is { } id ? _scene.Find(id) : null;
        }
        _runtime = null;
        _playSnapshot = null;
        _playing = false;
        _paused = false;
        BEngine.Application.isPlaying = false;
        _playButton.Checked = false;
        _playButton.Text = "播放";
        _pauseButton.Checked = false;
        _pauseButton.Enabled = false;
        RequestRefresh(RefreshTargets.Hierarchy | RefreshTargets.Inspector);
        EditorApplication.RaisePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
    }

    private void TogglePlayMode()
    {
        if (_playing) StopPlayMode();
        else StartPlayMode();
    }

    private void StepPlayMode()
    {
        if (!_playing || _runtime is null) return;
        _paused = true;
        _pauseButton.Checked = true;
        _runtime.Tick(Time.fixedDeltaTime);
    }

    private void SaveScene()
    {
        if (_playing) return;
        _sceneSerializer.Save(_scene, _scenePath);
        _dirty = false;
        _form.Text = BuildTitle();
        SetStatus($"已保存 {Path.GetFileName(_scenePath)}");
    }

    private void ReloadScene()
    {
        if (_playing) return;
        var selectedId = _selected?.Id;
        _scene = _sceneSerializer.Load(_scenePath);
        _selected = selectedId is { } id ? _scene.Find(id) : _scene.gameObjects.FirstOrDefault();
        _dirty = false;
        _form.Text = BuildTitle();
        RequestRefresh(RefreshTargets.Hierarchy | RefreshTargets.Inspector);
    }

    private void OpenAssetOrExternal(string path)
    {
        if (Directory.Exists(path)) { _assetDirectory = path; RequestRefresh(RefreshTargets.Project); return; }
        if (path.EndsWith(".ui.yaml", StringComparison.OrdinalIgnoreCase))
        {
            UIBuilderWindow.Open(path);
            return;
        }
        if (path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
        {
            if (_dirty)
            {
                MessageBox.Show(_form, "请先保存当前场景。", "BEngine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _scenePath = path;
            _scene = _sceneSerializer.Load(path);
            _selected = _scene.gameObjects.FirstOrDefault();
            _editorSerializer.SaveEditorSettings(new EditorSettingsDocument
            {
                LastScene = Path.GetRelativePath(_workspace.RootPath, path).Replace('\\', '/')
            }, _editorSettingsPath);
            RequestRefresh(RefreshTargets.Hierarchy | RefreshTargets.Inspector);
            _form.Text = BuildTitle();
            return;
        }
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) OpenScript(path);
        else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenScript(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_preferences.ExternalScriptEditor) &&
                File.Exists(_preferences.ExternalScriptEditor))
                Process.Start(new ProcessStartInfo(_preferences.ExternalScriptEditor, $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) { Debug.LogError($"无法打开脚本: {exception.Message}"); }
    }

    private void RefreshAssets()
    {
        _assetDirectory = Directory.Exists(_assetDirectory) ? _assetDirectory : _workspace.AssetsPath;
        var changes = _assetDatabase.Refresh();
        RequestRefresh(RefreshTargets.Project);
        SetStatus($"资源已刷新：{changes.Count} 项变更");
    }

    private void CreateAssetFolder()
    {
        var path = UniquePath(_assetDirectory, "New Folder", string.Empty, true);
        Directory.CreateDirectory(path);
        _assetDatabase.ImportAsset(path);
        _assetDirectory = path;
        RequestRefresh(RefreshTargets.Project);
    }

    private void CreateScriptAsset()
    {
        var path = UniquePath(_assetDirectory, "NewBehaviour", ".cs", false);
        var className = Path.GetFileNameWithoutExtension(path);
        File.WriteAllText(path, $"namespace Game;{Environment.NewLine}{Environment.NewLine}" +
                                $"public sealed class {className} : BEngine.MonoBehaviour{Environment.NewLine}" +
                                $"{{{Environment.NewLine}}}{Environment.NewLine}");
        _assetDatabase.ImportAsset(path);
        SelectAsset(path);
        RequestRefresh(RefreshTargets.Project);
    }

    private void DeleteAssetWithConfirmation(string? path)
    {
        if (path is null) return;
        if (MessageBox.Show(_form, $"删除资源？\n{path}", "BEngine", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        ((IEditorHost)this).DeleteAsset(NormalizeAssetPath(path));
        _selectedAssetPath = null;
        RequestRefresh(RefreshTargets.Project);
        RefreshInspector();
    }

    private void FrameSelected()
    {
        if (_selected is null) return;
        var target = ToNumerics(_selected.transform.position);
        var rotation = NQuaternion.CreateFromYawPitchRoll(_editorCameraYaw, _editorCameraPitch, 0);
        var forward = NVector3.Normalize(NVector3.Transform(NVector3.UnitZ, rotation));
        _editorCameraPosition = target - forward * 5f;
        _dock.ShowPanel("Scene");
        MarkLayoutDirty();
    }

    private RenderCamera GetEditorCamera() => new(
        _editorCameraPosition,
        NQuaternion.CreateFromYawPitchRoll(_editorCameraYaw, _editorCameraPitch, 0),
        _sceneCameraFieldOfView * MathF.PI / 180f,
        _sceneCameraNear,
        _sceneCameraFar,
        new NVector4(0.055f, 0.062f, 0.071f, 1),
        _drawSkybox ? CameraClearFlags.Skybox : CameraClearFlags.Color);

    private void OrbitSceneCamera(float deltaX, float deltaY)
    {
        _editorCameraYaw += deltaX * 0.005f;
        _editorCameraPitch = Math.Clamp(_editorCameraPitch - deltaY * 0.005f,
            -89f * MathF.PI / 180f, 89f * MathF.PI / 180f);
        MarkLayoutDirty();
    }

    private void ZoomSceneCamera(float wheel)
    {
        var rotation = NQuaternion.CreateFromYawPitchRoll(_editorCameraYaw, _editorCameraPitch, 0);
        var forward = NVector3.Normalize(NVector3.Transform(NVector3.UnitZ, rotation));
        _editorCameraPosition += forward * wheel * 0.8f;
        MarkLayoutDirty();
    }

    private void SetTool(Tool tool, ToolStrip? toolbar = null)
    {
        _activeTool = tool;
        if (toolbar is null) return;
        foreach (var button in toolbar.Items.OfType<ToolStripButton>().Take(4))
            button.Checked = button.Text == tool switch
            {
                Tool.View => "Q",
                Tool.Move => "W",
                Tool.Rotate => "E",
                Tool.Scale => "R",
                _ => string.Empty
            };
    }

    private void MarkDirty()
    {
        _dirty = true;
        if (_form is { IsDisposed: false }) _form.Text = BuildTitle();
    }

    private void MarkLayoutDirty() => _layout.Version = 1;

    private void SaveLayout()
    {
        var bounds = _form.WindowState == FormWindowState.Normal ? _form.Bounds : _form.RestoreBounds;
        _layout.WindowX = bounds.X;
        _layout.WindowY = bounds.Y;
        _layout.WindowWidth = bounds.Width;
        _layout.WindowHeight = bounds.Height;
        _layout.WindowMaximized = _form.WindowState == FormWindowState.Maximized;
        _layout.HierarchyWidth = _dock.LeftWidth;
        _layout.InspectorWidth = _dock.RightWidth;
        _layout.BottomHeight = _dock.BottomHeight;
        var visibility = _dock.Panels.ToDictionary(panel => panel.Id, panel => panel.Visible,
            StringComparer.Ordinal);
        _layout.ShowHierarchy = visibility.GetValueOrDefault("Hierarchy");
        _layout.ShowSceneView = visibility.GetValueOrDefault("Scene");
        _layout.ShowGameView = visibility.GetValueOrDefault("Game");
        _layout.ShowInspector = visibility.GetValueOrDefault("Inspector");
        _layout.ShowProject = visibility.GetValueOrDefault("Project");
        _layout.ShowConsole = visibility.GetValueOrDefault("Console");
        _layout.ShowBottomPanel = _layout.ShowProject || _layout.ShowConsole;
        _layout.ProjectBrowserMode = _projectTwoColumn ? "TwoColumn" : "SingleColumn";
        _layout.SceneCameraPositionX = _editorCameraPosition.X;
        _layout.SceneCameraPositionY = _editorCameraPosition.Y;
        _layout.SceneCameraPositionZ = _editorCameraPosition.Z;
        _layout.SceneCameraYaw = _editorCameraYaw * 180f / MathF.PI;
        _layout.SceneCameraPitch = _editorCameraPitch * 180f / MathF.PI;
        _layout.SceneCameraFieldOfView = _sceneCameraFieldOfView;
        _layout.SceneCameraNearClipPlane = _sceneCameraNear;
        _layout.SceneCameraFarClipPlane = _sceneCameraFar;
        _layout.SceneCameraDrawSkybox = _drawSkybox;
        _layout.SceneCameraDrawGrid = _drawGrid;
        _editorSerializer.SaveEditorLayout(_layout, _editorLayoutPath);
    }

    private EditorLayoutDocument LoadEditorLayout()
    {
        try
        {
            return File.Exists(_editorLayoutPath)
                ? _editorSerializer.LoadEditorLayout(_editorLayoutPath)
                : new EditorLayoutDocument();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"无法读取编辑器布局: {exception.Message}");
            return new EditorLayoutDocument();
        }
    }

    private void ApplyCameraLayout(EditorLayoutDocument layout)
    {
        _projectTwoColumn = !layout.ProjectBrowserMode.Equals("SingleColumn", StringComparison.OrdinalIgnoreCase);
        _editorCameraPosition = new NVector3(layout.SceneCameraPositionX, layout.SceneCameraPositionY,
            layout.SceneCameraPositionZ);
        _editorCameraYaw = layout.SceneCameraYaw * MathF.PI / 180f;
        _editorCameraPitch = Math.Clamp(layout.SceneCameraPitch, -89, 89) * MathF.PI / 180f;
        _sceneCameraFieldOfView = Math.Clamp(layout.SceneCameraFieldOfView, 10, 170);
        _sceneCameraNear = Math.Max(0.001f, layout.SceneCameraNearClipPlane);
        _sceneCameraFar = Math.Max(_sceneCameraNear + 0.001f, layout.SceneCameraFarClipPlane);
        _drawSkybox = layout.SceneCameraDrawSkybox;
        _drawGrid = layout.SceneCameraDrawGrid;
    }

    private EditorPreferencesDocument LoadPreferences()
    {
        if (!File.Exists(_preferencesPath))
        {
            var defaults = new EditorPreferencesDocument();
            _editorSerializer.SaveEditorPreferences(defaults, _preferencesPath);
            return defaults;
        }
        return _editorSerializer.LoadEditorPreferences(_preferencesPath);
    }

    private ProjectSettingsDocument LoadProjectSettings()
    {
        if (!File.Exists(_workspace.ProjectSettingsFilePath))
        {
            var defaults = new ProjectSettingsDocument { ProductName = _workspace.Project.Name };
            YamlUtility.Save(defaults, _workspace.ProjectSettingsFilePath);
            return defaults;
        }
        return YamlUtility.Load<ProjectSettingsDocument>(_workspace.ProjectSettingsFilePath);
    }

    private string ResolveInitialScene()
    {
        if (!File.Exists(_editorSettingsPath)) return _workspace.StartupScenePath;
        try
        {
            var settings = _editorSerializer.LoadEditorSettings(_editorSettingsPath);
            var path = _workspace.ResolveInside(settings.LastScene);
            return File.Exists(path) ? path : _workspace.StartupScenePath;
        }
        catch { return _workspace.StartupScenePath; }
    }

    private void CompileProjectScripts()
    {
        TraceStartup("Compiling project scripts.");
        EditorApplication.isCompiling = true;
        try
        {
            var gameScripts = ProjectScriptCompiler.CompileAndLoad(_workspace);
            EditorProjectScriptCompiler.CompileAndLoad(_workspace, gameScripts,
                typeof(MenuItemAttribute).Assembly.Location);
        }
        finally { EditorApplication.isCompiling = false; }
    }

    private static ToolStripMenuItem AddMenu(MenuStrip menu, string title)
    {
        var item = new ToolStripMenuItem(title);
        menu.Items.Add(item);
        return item;
    }

    private static void AddMenuItem(ToolStripMenuItem parent, string title, Action action, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(title) { ShortcutKeys = shortcut };
        item.Click += (_, _) => action();
        parent.DropDownItems.Add(item);
    }

    private static void AppendCustomMenuNodes(ToolStripItemCollection items,
        IReadOnlyList<MenuItemRegistry.MenuNode> nodes)
    {
        int? previousPriority = null;
        foreach (var node in nodes)
        {
            if (previousPriority is { } previous && node.Priority - previous >= 11)
                items.Add(new ToolStripSeparator());
            previousPriority = node.Priority;
            var item = new ToolStripMenuItem(node.Name) { Enabled = node.Enabled };
            if (node.Execute is not null) item.Click += (_, _) => node.Execute();
            AppendCustomMenuNodes(item.DropDownItems, node.Children);
            items.Add(item);
        }
    }

    private void DisposeInspectorObjects()
    {
        foreach (var serializedObject in _inspectorSerializedObjects) serializedObject.Dispose();
        _inspectorSerializedObjects.Clear();
    }

    private string ResolveAssetPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        var normalized = assetPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var path = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
              normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(Path.Combine(_workspace.RootPath, normalized))
                : Path.GetFullPath(Path.Combine(_workspace.AssetsPath, normalized));
        var root = Path.GetFullPath(_workspace.AssetsPath);
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset path must stay inside {root}.");
        return path;
    }

    private string NormalizeAssetPath(string path) =>
        Path.GetRelativePath(_workspace.RootPath, ResolveAssetPath(path)).Replace('\\', '/');

    private static EditorAssetRecord ToEditorAssetRecord(BEngine.ProjectSystem.Editor.AssetRecord record) => new(
        record.Guid, record.AssetPath, record.SourcePath, record.AssetType, record.IsDirectory);

    private static bool IsImage(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool IsTextAsset(string path) => new[] { ".cs", ".yaml", ".shader", ".glsl", ".txt", ".json" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ||
        path.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase);

    private static string ReadPreviewText(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[8192];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, count) + (reader.Peek() >= 0 ? Environment.NewLine + "..." : string.Empty);
    }

    private static string UniquePath(string directory, string name, string extension, bool isDirectory)
    {
        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? string.Empty : $" {index}";
            var path = Path.Combine(directory, name + suffix + extension);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    private static void ShowInExplorer(string path, bool select)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    private void SetStatus(string text, LogType level = LogType.Info)
    {
        if (_statusLabel is null) return;
        _statusLabel.Text = text;
        _statusLabel.ForeColor = level switch
        {
            LogType.Error => UIElementsTheme.Error,
            LogType.Warning => UIElementsTheme.Warning,
            _ => UIElementsTheme.TextDisabled
        };
    }

    private string BuildTitle() => $"BEngine - {_project?.Name ?? _workspace.Project.Name}{(_dirty ? " *" : string.Empty)}";
    private void TraceStartup(string message) => AppendEditorLog(DateTimeOffset.Now, message);

    private void AppendEditorLog(DateTimeOffset timestamp, string message)
    {
        lock (_editorLogGate)
        {
            File.AppendAllText(_startupLogPath,
                $"[{timestamp:O}] {message}{Environment.NewLine}", new System.Text.UTF8Encoding(false));
        }
    }

    private void ShowMainWindow()
    {
        if (_form.IsDisposed) return;
        _form.ShowInTaskbar = true;
        _form.Show();
        _form.WindowState = FormWindowState.Normal;
        _form.BringToFront();
        _form.Activate();
    }

    private void OpenEditorStatus()
    {
        ShowMainWindow();
        EditorStatusWindow.Open();
        TraceStartup("Editor status window opened.");
    }
    private static NVector3 ToNumerics(Vector3 value) => new((float)value.x, (float)value.y, (float)value.z);

    private static readonly (string Id, string Title)[] BuiltInWindowTitles =
    [
        ("Hierarchy", "Hierarchy"), ("Scene", "Scene"), ("Game", "Game"),
        ("Inspector", "Inspector"), ("Project", "Project"), ("Console", "Console")
    ];

    Scene IEditorHost.ActiveScene => _scene;

    BObject? IEditorHost.ActiveObject
    {
        get
        {
            if (_selected is not null) return _selected;
            if (_selectedAssetPath is null) return null;
            var assetPath = NormalizeAssetPath(_selectedAssetPath);
            if (_selectedAssetObject?.assetPath == assetPath) return _selectedAssetObject;
            return _selectedAssetObject = AssetDatabase.LoadMainAssetAtPath(assetPath) as DefaultAsset;
        }
        set
        {
            if (value is GameObject gameObject && ReferenceEquals(gameObject.scene, _scene)) SelectGameObject(gameObject);
            else if (value is DefaultAsset asset) SelectAsset(ResolveAssetPath(asset.assetPath));
            else if (value is null) { _selected = null; _selectedAssetPath = null; RefreshInspector(); }
        }
    }

    GameObject? IEditorHost.ActiveGameObject
    {
        get => _selected;
        set { if (value is null) { _selected = null; RefreshInspector(); } else SelectGameObject(value); }
    }

    void IEditorHost.MarkSceneDirty() => MarkDirty();
    void IEditorHost.FrameSelected() => FrameSelected();
    bool IEditorHost.SaveActiveScene() { if (_playing) return false; SaveScene(); return true; }
    bool IEditorHost.OpenScene(string scenePath)
    {
        var path = ResolveAssetPath(scenePath);
        OpenAssetOrExternal(path);
        return Path.GetFullPath(_scenePath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }
    bool IEditorHost.IsPlaying { get => _playing; set { if (value != _playing) TogglePlayMode(); } }
    bool IEditorHost.IsPaused { get => _paused; set { _paused = _playing && value; _pauseButton.Checked = _paused; } }

    void IEditorHost.ShowWindow(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_editorWindows.TryGetValue(window, out var existing))
        {
            window.OpenInternal();
            _editorWindowUpdateSnapshotDirty = true;
            _dock.ShowPanel(window.PersistentId);
            if (window.ConsumeFloatingRequest()) _dock.FloatPanel(window.PersistentId, new Size(
                Math.Max(360, (int)window.position.width), Math.Max(240, (int)window.position.height)));
            existing.RefreshTree();
            return;
        }
        window.PersistentId = string.IsNullOrWhiteSpace(window.PersistentId)
            ? $"EditorWindow:{window.GetType().Assembly.GetName().Name}:{window.GetType().FullName}"
            : window.PersistentId;
        window.OpenInternal();
        var host = new WinFormsVisualElementHost { Root = window.rootVisualElement };
        _editorWindows.Add(window, host);
        _editorWindowUpdateSnapshotDirty = true;
        _dock.AddPanel(window.PersistentId, window.titleContent.text, host, DockZone.Center,
            () =>
            {
                window.CloseInternal();
                _editorWindowUpdateSnapshotDirty = true;
            });
        _dock.ShowPanel(window.PersistentId);
        if (window.ConsumeFloatingRequest()) _dock.FloatPanel(window.PersistentId, new Size(
            Math.Max(360, (int)window.position.width), Math.Max(240, (int)window.position.height)));
        window.FocusInternal();
    }

    void IEditorHost.CloseWindow(EditorWindow window)
    {
        if (!_editorWindows.ContainsKey(window)) return;
        _dock.ClosePanel(window.PersistentId);
    }

    void IEditorHost.RepaintWindow(EditorWindow window)
    {
        if (_editorWindows.TryGetValue(window, out var host)) host.RefreshTree();
        else ((IEditorHost)this).ShowWindow(window);
    }
    void IEditorHost.RepaintAllWindows()
    {
        RefreshHierarchy(); RefreshInspector(); RefreshProject(); RefreshConsole(true);
        foreach (var host in _editorWindows.Values) host.RefreshTree();
    }
    Tool IEditorHost.CurrentTool { get => _activeTool; set => SetTool(value); }
    bool IEditorHost.ExecuteMenuItem(string itemName) => _menuItems.Execute(itemName);
    void IEditorHost.Exit(int exitCode) => _form.Close();
    string IEditorHost.ProjectRootPath => _workspace.RootPath;
    string IEditorHost.AssetsRootPath => _workspace.AssetsPath;
    EditorAssetRecord[] IEditorHost.FindAssets(string search) => _assetDatabase.FindAssets(search)
        .Select(ToEditorAssetRecord).ToArray();
    EditorAssetRecord? IEditorHost.GetAsset(string assetPath)
    {
        var record = _assetDatabase.GetRecord(NormalizeAssetPath(assetPath));
        return record is null ? null : ToEditorAssetRecord(record);
    }
    EditorAssetRecord? IEditorHost.GetAsset(Guid guid)
    {
        var record = _assetDatabase.GetRecord(guid);
        return record is null ? null : ToEditorAssetRecord(record);
    }
    void IEditorHost.RefreshAssets() => RefreshAssets();
    void IEditorHost.ImportAsset(string assetPath) => _assetDatabase.ImportAsset(ResolveAssetPath(assetPath));
    string IEditorHost.CreateAssetFolder(string parentFolder, string newFolderName)
    {
        var path = Path.Combine(ResolveAssetPath(parentFolder), newFolderName);
        Directory.CreateDirectory(path);
        return _assetDatabase.ImportAsset(path).Guid.ToString("N");
    }
    bool IEditorHost.DeleteAsset(string assetPath)
    {
        var path = ResolveAssetPath(assetPath);
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path) && !path.Equals(_workspace.AssetsPath, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(path, true);
        else return false;
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
        _assetDatabase.Refresh();
        return true;
    }
    string IEditorHost.MoveAsset(string oldPath, string newPath)
    {
        try
        {
            var source = ResolveAssetPath(oldPath);
            var destination = ResolveAssetPath(newPath);
            if (File.Exists(destination) || Directory.Exists(destination)) return "Destination already exists.";
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(source)) File.Move(source, destination);
            else if (Directory.Exists(source)) Directory.Move(source, destination);
            else return "Source asset does not exist.";
            if (File.Exists(source + ".meta")) File.Move(source + ".meta", destination + ".meta");
            _assetDatabase.Refresh();
            return string.Empty;
        }
        catch (Exception exception) { return exception.Message; }
    }
}
