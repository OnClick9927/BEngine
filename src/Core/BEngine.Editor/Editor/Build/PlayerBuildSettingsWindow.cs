using BEngine.Build;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/ProjectSettings.png")]
public sealed class PlayerBuildSettingsWindow : EditorWindow
{
    private PlayerBuildSettings _settings = new();
    private Vector2 _sceneScroll;
    private string _status = string.Empty;
    private bool _targetAvailable;
    private string _targetUnavailableReason = string.Empty;

    public PlayerBuildSettingsWindow() => saveToLayout = false;

    public static PlayerBuildSettingsWindow Open()
    {
        var window = GetWindow<PlayerBuildSettingsWindow>("Build Settings", focus: true);
        window.position = window.position.width <= 1 || window.position.height <= 1
            ? new Rect(180, 100, 720, 360)
            : window.position;
        window.Focus();
        return window;
    }

    protected override void OnEnable()
    {
        titleContent = new GUIContent("Build Settings", "Icons/Windows/ProjectSettings.png",
            "Scenes included in the Player");
        minSize = new Vector2(520, 260);
        if (position.width <= 1 || position.height <= 1)
            position = new Rect(180, 100, 720, 360);
        PlayerBuildMenuCommands.stateChanged += OnBuildStateChanged;
        PlayerBuildSettingsStore.settingsChanged += OnSettingsChanged;
        LoadSettings();
    }

    protected override void OnDisable()
    {
        PlayerBuildMenuCommands.stateChanged -= OnBuildStateChanged;
        PlayerBuildSettingsStore.settingsChanged -= OnSettingsChanged;
    }

    protected override void OnProjectChange() => LoadSettings();

    protected override void OnGUI()
    {
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var footerHeight = CalculateFooterHeight(width, height);

        GUI.Box(new Rect(0, 0, width, height), GUIContent.none, GUI.skin.window);
        GUI.Label(new Rect(16, 10, Fix64.Max(0, width - 32), 26),
            "Scenes In Build", EditorStyles.boldLabel);
        DrawScenes(new Rect(12, 40, Fix64.Max(0, width - 24),
            Fix64.Max(0, height - footerHeight - 50)));
        if (footerHeight > 0)
            DrawFooter(new Rect(0, height - footerHeight, width, footerHeight));
    }

    private void DrawScenes(Rect rect)
    {
        if (rect.width <= 1 || rect.height <= 1) return;
        GUI.Box(rect, GUIContent.none, EditorStyles.viewBackground);
        var viewport = new Rect(rect.x + 1, rect.y + 1,
            Fix64.Max(1, rect.width - 2), Fix64.Max(1, rect.height - 2));
        const int rowHeight = 32;
        var contentHeight = Fix64.Max(viewport.height, _settings.Scenes.Count * rowHeight + 8);
        _sceneScroll = GUI.BeginScrollView(viewport, _sceneScroll,
            new Rect(0, 0, Fix64.Max(1, viewport.width - 12), contentHeight));
        try
        {
            for (var index = 0; index < _settings.Scenes.Count; index++)
            {
                var scene = _settings.Scenes[index];
                var row = new Rect(5, 4 + index * rowHeight,
                    Fix64.Max(1, viewport.width - 22), rowHeight - 4);
                if ((index & 1) != 0)
                    GUI.DrawRect(row, EditorStyles.treeViewRow.normal.backgroundColor);
                using (new EditorGUI.DisabledScope(true))
                    _ = GUI.Toggle(new Rect(row.x + 5, row.y + 4, 20, 20),
                        true, GUIContent.none);
                GUI.Label(new Rect(row.x + 29, row.y + 2,
                        Fix64.Max(1, row.width - 34), row.height - 4),
                    new GUIContent(scene.Path, EditorBuiltinIcons.Assets.Scene,
                        $"{scene.Path} is the required AOT startup scene"),
                    EditorStyles.label);
            }

            if (!string.IsNullOrWhiteSpace(_status))
                GUI.Label(new Rect(10, 12, Fix64.Max(1, viewport.width - 24),
                        Fix64.Max(40, viewport.height - 24)),
                    _status, EditorStyles.wordWrappedMiniLabel);
        }
        finally
        {
            GUI.EndScrollView();
        }
    }

    private void DrawFooter(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
        var canBuild = !PlayerBuildMenuCommands.IsBuilding && _targetAvailable &&
                       _settings.Scenes.Count == 1;
        var compact = UsesCompactFooter(rect.width);
        var firstRowY = rect.y + (compact ? (Fix64)8 : Fix64.Max(4, (rect.height - 30) / 2));
        var commandY = compact ? rect.y + 49 : firstRowY;
        if (GUI.Button(new Rect(14, firstRowY, 124, 30), "Player Settings..."))
            SettingsService.OpenProjectSettings("Project/Player");

        const int gap = 8;
        const int buildWidth = 94;
        const int runWidth = 116;
        const int contentWidth = 142;
        var right = rect.xMax - 14;
        using (new EditorGUI.DisabledScope(!canBuild))
            if (GUI.Button(new Rect(right - buildWidth, commandY, buildWidth, 30), "Build"))
                PlayerBuildMenuCommands.ChooseOutput(_settings, runAfterBuild: false);
        right -= buildWidth + gap;
        using (new EditorGUI.DisabledScope(!canBuild ||
                   !PlayerBuildMenuCommands.CanRunLocally(_settings.TargetId)))
            if (GUI.Button(new Rect(right - runWidth, commandY, runWidth, 30), "Build And Run"))
                PlayerBuildMenuCommands.ChooseOutput(_settings, runAfterBuild: true);
        right -= runWidth + gap;
        using (new EditorGUI.DisabledScope(!canBuild))
            if (GUI.Button(new Rect(right - contentWidth, commandY, contentWidth, 30),
                    "Build Hot Resources"))
                PlayerBuildMenuCommands.ChooseContentUpdateOutput(_settings);

        if (PlayerBuildMenuCommands.IsBuilding)
            GUI.Label(new Rect(146, firstRowY + 4,
                    Fix64.Max(0, (compact ? rect.xMax - 14 : right) - 152), 22),
                "Building...", EditorStyles.miniLabel);
        else if (!_targetAvailable)
            GUI.Label(new Rect(146, firstRowY + 3,
                    Fix64.Max(0, (compact ? rect.xMax - 14 : right) - 152), 24),
                new GUIContent(_targetUnavailableReason, tooltip: _targetUnavailableReason),
                EditorStyles.miniLabel);
    }

    private void LoadSettings()
    {
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath))
        {
            _settings = new PlayerBuildSettings
            {
                Scenes =
                [
                    new PlayerBuildScene
                    {
                        Path = BEngine.ProjectSystem.Editor.AotProjectLayout.SceneAssetPath,
                        Enabled = true
                    }
                ],
                TargetId = BuildTargetCatalog.InferCurrentDesktopPlatformId()
            };
            _targetAvailable = false;
            _targetUnavailableReason = "Open a project to build the Player.";
            return;
        }
        try
        {
            _settings = PlayerBuildSettingsStore.Load(EditorApplication.projectPath);
            _status = string.Empty;
            _targetAvailable = PlayerBuildPipeline.CanBuild(_settings.TargetId,
                out _targetUnavailableReason);
            Repaint();
        }
        catch (Exception exception)
        {
            _settings = new PlayerBuildSettings();
            _status = exception.Message;
            _targetAvailable = false;
            _targetUnavailableReason = exception.Message;
        }
    }

    private void OnBuildStateChanged()
    {
        LoadSettings();
        Repaint();
    }

    private void OnSettingsChanged(string projectPath)
    {
        if (projectPath.Equals(EditorApplication.projectPath,
                StringComparison.OrdinalIgnoreCase))
            LoadSettings();
    }

    internal static bool UsesCompactFooter(Fix64 width) => width < 680;

    internal static Fix64 CalculateFooterHeight(Fix64 width, Fix64 height)
    {
        var desired = UsesCompactFooter(width) ? (Fix64)88 : 56;
        return Fix64.Min(desired, Fix64.Max(0, height - 84));
    }
}
