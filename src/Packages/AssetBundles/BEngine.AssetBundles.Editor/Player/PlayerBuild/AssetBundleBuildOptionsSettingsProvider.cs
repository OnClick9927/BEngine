namespace BEngine.Editor.Build;

internal static class AssetBundleBuildOptionsSettingsProvider
{
    private static string _project = string.Empty;
    private static PlayerBuildSettings? _settings;
    private static string _error = string.Empty;

    static AssetBundleBuildOptionsSettingsProvider()
    {
        PlayerBuildSettingsStore.settingsChanged += OnSettingsChanged;
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if (assembly != typeof(AssetBundleBuildOptionsSettingsProvider).Assembly) return;
            PlayerBuildSettingsStore.settingsChanged -= OnSettingsChanged;
            _settings = null;
            _project = string.Empty;
        });
    }

    private static void OnSettingsChanged(string project)
    {
        if (!string.Equals(project, _project, StringComparison.OrdinalIgnoreCase)) return;
        _settings = null;
        _error = string.Empty;
    }

    [SettingsProvider]
    private static SettingsProvider Create() => new("Project/Asset Bundle Build", SettingsScope.Project,
        ["AssetBundles", "compression", "build", "压缩", "打包"])
    {
        label = "Asset Bundle Build",
        activateHandler = _ => Reload(),
        guiHandler = _ => Draw()
    };

    private static void Reload()
    {
        _settings = null;
        _error = string.Empty;
        _project = EditorApplication.projectPath;
        if (_project.Length == 0) return;
        try { _settings = PlayerBuildSettingsStore.Load(_project); }
        catch (Exception error) { _error = error.Message; }
    }

    private static void Draw()
    {
        var project = EditorApplication.projectPath;
        if (project != _project || (_settings is null && _error.Length == 0)) Reload();
        if (_settings is not null)
        {
            var disabled = EditorApplication.isPlayingOrWillChangePlaymode;
            EditorGUI.BeginDisabledGroup(disabled);
            try
            {
                var value = AssetBundlePlayerBuildOptions.GetCompression(_settings.ContentOptions);
                var next = EditorGUILayout.Toggle("Compress Resources", value);
                if (next != value)
                {
                    // Reload before writing so edits from the build/profile window are retained.
                    var current = PlayerBuildSettingsStore.Load(project);
                    AssetBundlePlayerBuildOptions.SetCompression(current, next);
                    PlayerBuildSettingsStore.Save(project, current);
                    _settings = current;
                    _error = string.Empty;
                }
            }
            catch (Exception error) { _error = error.Message; }
            finally { EditorGUI.EndDisabledGroup(); }
        }
        if (_error.Length > 0) EditorGUILayout.HelpBox(_error, MessageType.Error);
    }
}
