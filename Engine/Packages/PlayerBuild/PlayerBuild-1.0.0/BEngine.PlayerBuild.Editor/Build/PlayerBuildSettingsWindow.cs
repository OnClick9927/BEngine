using BEngine.Build;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/ProjectSettings.png")]
public sealed class PlayerBuildSettingsWindow :
    EditorWindow<PlayerBuildSettingsWindowModel, PlayerBuildSettingsWindowController>
{
    public PlayerBuildSettingsWindow() => saveToLayout = false;

    protected override PlayerBuildSettingsWindowModel CreateWindowModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override PlayerBuildSettingsWindowController CreateWindowController(
        EditorWindowMvcContext context,
        PlayerBuildSettingsWindowModel windowModel) => new(context, windowModel);

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
        controller.Activate();
    }

    protected override void OnDisable()
    {
        controller.Deactivate();
    }

    protected override void OnProjectChange() => controller.Refresh();

    protected override void OnGUI()
    {
        var width = Fix64.Max(1, GUIUtility.currentViewWidth);
        var height = Fix64.Max(1, GUIUtility.currentViewHeight);
        var footerHeight = CalculateFooterHeight(width, height);

        GUI.Box(new Rect(0, 0, width, height), GUIContent.none, GUI.skin.window);
        DrawProfileToolbar(new Rect(0, 0, width, 30));
        GUI.Label(new Rect(16, 36, Fix64.Max(0, width - 32), 22),
            $"Startup: {model.Settings.HotUpdateStartupScene}", EditorStyles.miniLabel);
        GUI.Label(new Rect(16, 58, Fix64.Max(0, width - 32), 22),
            model.Settings.ScriptingDefineSymbols.Count == 0
                ? "Defines: none"
                : "Defines: " + string.Join(";", model.Settings.ScriptingDefineSymbols),
            EditorStyles.miniLabel);
        GUI.Label(new Rect(16, 84, Fix64.Max(0, width - 32), 24),
            "AOT Bootstrap Scene", EditorStyles.boldLabel);
        DrawScenes(new Rect(12, 110, Fix64.Max(0, width - 24),
            Fix64.Max(0, height - footerHeight - 120)));
        if (footerHeight > 0)
            DrawFooter(new Rect(0, height - footerHeight, width, footerHeight));
    }

    private void DrawProfileToolbar(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
        GUI.Label(new Rect(8, rect.y + 4, 82, 22), "Build Profile", EditorStyles.toolbarLabel);
        var profiles = model.Profiles.Profiles;
        var selected = Math.Max(0, profiles.FindIndex(profile =>
            profile.Id.Equals(model.Profiles.ActiveProfileId, StringComparison.OrdinalIgnoreCase)));
        var right = rect.xMax - 6;
        var delete = new Rect(right - 24, rect.y + 3, 24, 24); right -= 27;
        var rename = new Rect(right - 24, rect.y + 3, 24, 24); right -= 27;
        var duplicate = new Rect(right - 24, rect.y + 3, 24, 24); right -= 27;
        var create = new Rect(right - 24, rect.y + 3, 24, 24); right -= 29;
        var popup = new Rect(92, rect.y + 3, Fix64.Max(80, right - 92), 24);
        var next = EditorGUI.Popup(popup, string.Empty, selected,
            profiles.Select(profile => profile.Name).ToArray(), EditorStyles.toolbarDropDown);
        if (next != selected && next >= 0 && next < profiles.Count)
            controller.SelectProfile(profiles[next].Id);
        if (EditorToolbar.Button(create,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Add, "Create build profile")))
            controller.CreateProfile(create);
        if (EditorToolbar.Button(duplicate,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Duplicate, "Duplicate active build profile")))
            controller.DuplicateProfile(duplicate);
        if (EditorToolbar.Button(rename,
                new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Field, "Rename active build profile")))
            controller.RenameProfile(rename);
        using (new EditorGUI.DisabledScope(profiles.Count <= 1))
            if (EditorToolbar.Button(delete,
                    new GUIContent(string.Empty, EditorBuiltinIcons.Toolbar.Delete, "Delete active build profile")))
                controller.DeleteProfile();
    }

    private void DrawScenes(Rect rect)
    {
        if (rect.width <= 1 || rect.height <= 1) return;
        GUI.Box(rect, GUIContent.none, EditorStyles.viewBackground);
        var viewport = new Rect(rect.x + 1, rect.y + 1,
            Fix64.Max(1, rect.width - 2), Fix64.Max(1, rect.height - 2));
        const int rowHeight = 32;
        var contentHeight = Fix64.Max(viewport.height, model.Settings.Scenes.Count * rowHeight + 8);
        model.SceneScroll = GUI.BeginScrollView(viewport, model.SceneScroll,
            new Rect(0, 0, Fix64.Max(1, viewport.width - 12), contentHeight));
        try
        {
            for (var index = 0; index < model.Settings.Scenes.Count; index++)
            {
                var scene = model.Settings.Scenes[index];
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

            if (!string.IsNullOrWhiteSpace(model.Status))
                GUI.Label(new Rect(10, 12, Fix64.Max(1, viewport.width - 24),
                        Fix64.Max(40, viewport.height - 24)),
                    model.Status, EditorStyles.wordWrappedMiniLabel);
        }
        finally
        {
            GUI.EndScrollView();
        }
    }

    private void DrawFooter(Rect rect)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
        var canBuild = model.CanBuild;
        var compact = UsesCompactFooter(rect.width);
        var stacked = UsesStackedFooter(rect.width);
        var firstRowY = rect.y + (compact ? (Fix64)8 : Fix64.Max(4, (rect.height - 30) / 2));
        var commandY = stacked ? rect.y + 86 : compact ? rect.y + 49 : firstRowY;
        if (GUI.Button(new Rect(14, firstRowY, 124, 30), "Player Settings..."))
            controller.OpenPlayerSettings();

        const int gap = 8;
        const int buildWidth = 94;
        const int runWidth = 126;
        const int contentWidth = 196;
        var right = rect.xMax - 14;
        using (new EditorGUI.DisabledScope(!canBuild))
            if (GUI.Button(new Rect(right - buildWidth, commandY, buildWidth, 30), "Build"))
                controller.Build();
        right -= buildWidth + gap;
        using (new EditorGUI.DisabledScope(!canBuild ||
                   !controller.CanRunLocally))
            if (GUI.Button(new Rect(right - runWidth, commandY, runWidth, 30), "Build And Run"))
                controller.BuildAndRun();
        right -= runWidth + gap;
        using (new EditorGUI.DisabledScope(!canBuild))
            if (GUI.Button(new Rect(
                    stacked ? (Fix64)14 : right - contentWidth,
                    stacked ? rect.y + 47 : commandY,
                    contentWidth, 30),
                    "Build Hot Resources"))
                controller.BuildHotResources();

        if (PlayerBuildMenuCommands.IsBuilding)
            GUI.Label(new Rect(146, firstRowY + 4,
                    Fix64.Max(0, (compact ? rect.xMax - 14 : right) - 152), 22),
                "Building...", EditorStyles.miniLabel);
        else if (!model.TargetAvailable)
            GUI.Label(new Rect(146, firstRowY + 3,
                    Fix64.Max(0, (compact ? rect.xMax - 14 : right) - 152), 24),
                new GUIContent("Unavailable", tooltip: model.TargetUnavailableReason),
                EditorStyles.miniLabel);
    }

    internal static bool UsesCompactFooter(Fix64 width) => width < 680;

    private static bool UsesStackedFooter(Fix64 width) => width < 460;

    internal static Fix64 CalculateFooterHeight(Fix64 width, Fix64 height)
    {
        var desired = UsesStackedFooter(width) ? (Fix64)126 :
            UsesCompactFooter(width) ? (Fix64)88 : 56;
        return Fix64.Min(desired, Fix64.Max(0, height - 84));
    }
}

public sealed class PlayerBuildSettingsWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public PlayerBuildSettings Settings { get; internal set; } = new();
    public PlayerBuildProfilesDocument Profiles { get; internal set; } = new()
    {
        ActiveProfileId = "00000000000000000000000000000000",
        Profiles =
        [
            new PlayerBuildProfile
            {
                Id = "00000000000000000000000000000000",
                Name = "Default"
            }
        ]
    };
    public Vector2 SceneScroll { get; set; }
    public string Status { get; internal set; } = string.Empty;
    public bool TargetAvailable { get; internal set; }
    public string TargetUnavailableReason { get; internal set; } = string.Empty;
    public bool CanBuild => !PlayerBuildMenuCommands.IsBuilding && !EditorApplication.isCompiling && TargetAvailable &&
                            Settings.Scenes.Count == 1;
}

public sealed class PlayerBuildSettingsWindowController :
    EditorWindowController<PlayerBuildSettingsWindow, PlayerBuildSettingsWindowModel>
{
    private bool _active;

    public PlayerBuildSettingsWindowController(
        EditorWindowMvcContext context,
        PlayerBuildSettingsWindowModel model) : base(context, model) { }

    public bool CanRunLocally => PlayerBuildMenuCommands.CanRunLocally(Model.Settings.TargetId);

    public void Activate()
    {
        if (!_active)
        {
            _active = true;
            PlayerBuildMenuCommands.stateChanged += OnBuildStateChanged;
            PlayerBuildSettingsStore.settingsChanged += OnSettingsChanged;
            PlayerBuildProfileStore.profilesChanged += OnProfilesChanged;
        }
        Refresh();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        PlayerBuildMenuCommands.stateChanged -= OnBuildStateChanged;
        PlayerBuildSettingsStore.settingsChanged -= OnSettingsChanged;
        PlayerBuildProfileStore.profilesChanged -= OnProfilesChanged;
    }

    public void Refresh()
    {
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath))
        {
            Model.Settings = new PlayerBuildSettings
            {
                Scenes =
                [
                    new PlayerBuildScene
                    {
                        Path = BEngine.ProjectSystem.Editor.AotProjectLayout.SceneAssetPath,
                        Enabled = true
                    }
                ],
                TargetId = BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktopPlatformId()
            };
            Model.Status = string.Empty;
            Model.TargetAvailable = false;
            Model.TargetUnavailableReason = "Open a project to build the Player.";
            Repaint();
            return;
        }

        try
        {
            Model.Profiles = PlayerBuildProfileStore.Ensure(EditorApplication.projectPath);
            Model.Settings = PlayerBuildSettingsStore.Load(EditorApplication.projectPath);
            Model.Status = string.Empty;
            Model.TargetAvailable = EditorServiceAccess.GetRequiredService<IPlayerBuildPipeline>()
                .CanBuild(Model.Settings.TargetId, out var unavailableReason);
            Model.TargetUnavailableReason = unavailableReason;
        }
        catch (Exception exception)
        {
            Model.Settings = new PlayerBuildSettings();
            Model.Status = exception.Message;
            Model.TargetAvailable = false;
            Model.TargetUnavailableReason = exception.Message;
        }
        Repaint();
    }

    public void OpenPlayerSettings() => SettingsService.OpenProjectSettings("Project/Player");

    public void SelectProfile(string profileId) => RunProfileOperation(() =>
    {
        _ = PlayerBuildProfileStore.SetActive(EditorApplication.projectPath, profileId);
        _ = PlayerBuildProfileStore.ApplyActiveScriptingDefines(EditorApplication.projectPath);
    });

    public void CreateProfile(Rect anchor) => PlayerBuildProfileNameWindow.Open(
        "Create Build Profile", "New Profile", anchor, name => RunProfileOperation(() =>
        {
            _ = PlayerBuildProfileStore.Create(EditorApplication.projectPath, name);
            _ = PlayerBuildProfileStore.ApplyActiveScriptingDefines(EditorApplication.projectPath);
        }));

    public void DuplicateProfile(Rect anchor)
    {
        var active = Model.Profiles.Profiles.First(profile =>
            profile.Id.Equals(Model.Profiles.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        PlayerBuildProfileNameWindow.Open(
            "Duplicate Build Profile", active.Name + " Copy", anchor, name => RunProfileOperation(() =>
            {
                _ = PlayerBuildProfileStore.Create(EditorApplication.projectPath, name);
                _ = PlayerBuildProfileStore.ApplyActiveScriptingDefines(EditorApplication.projectPath);
            }));
    }

    public void RenameProfile(Rect anchor)
    {
        var active = Model.Profiles.Profiles.First(profile =>
            profile.Id.Equals(Model.Profiles.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        PlayerBuildProfileNameWindow.Open(
            "Rename Build Profile", active.Name, anchor, name => RunProfileOperation(() =>
                PlayerBuildProfileStore.Rename(EditorApplication.projectPath, active.Id, name)));
    }

    public void DeleteProfile()
    {
        var active = Model.Profiles.Profiles.First(profile =>
            profile.Id.Equals(Model.Profiles.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        if (!EditorUtility.DisplayDialog("Delete Build Profile",
                $"Delete build profile '{active.Name}'?", "Delete", "Cancel")) return;
        _ = RunProfileOperation(() =>
        {
            _ = PlayerBuildProfileStore.Delete(EditorApplication.projectPath, active.Id);
            _ = PlayerBuildProfileStore.ApplyActiveScriptingDefines(EditorApplication.projectPath);
        });
    }

    public void Build() => PlayerBuildMenuCommands.ChooseOutput(Model.Settings, runAfterBuild: false);

    public void BuildAndRun() =>
        PlayerBuildMenuCommands.ChooseOutput(Model.Settings, runAfterBuild: true);

    public void BuildHotResources() =>
        PlayerBuildMenuCommands.ChooseContentUpdateOutput(Model.Settings);

    private void OnBuildStateChanged() => Refresh();

    private void OnSettingsChanged(string projectPath)
    {
        if (projectPath.Equals(EditorApplication.projectPath,
                StringComparison.OrdinalIgnoreCase)) Refresh();
    }

    private void OnProfilesChanged(string projectPath)
    {
        if (projectPath.Equals(EditorApplication.projectPath,
                StringComparison.OrdinalIgnoreCase)) Refresh();
    }

    private bool RunProfileOperation(Action operation)
    {
        try
        {
            operation();
            Model.Status = string.Empty;
            Refresh();
            return true;
        }
        catch (Exception exception)
        {
            Model.Status = exception.Message;
            Repaint();
            return false;
        }
    }
}
