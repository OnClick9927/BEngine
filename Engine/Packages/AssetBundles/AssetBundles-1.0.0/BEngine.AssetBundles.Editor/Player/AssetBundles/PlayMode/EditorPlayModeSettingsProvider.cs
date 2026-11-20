using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor.Build;

internal static class EditorPlayModeSettingsProvider
{
    private static string _project = string.Empty;
    private static EditorPlayModeSettings? _settings;
    private static string _error = string.Empty;

    [SettingsProvider]
    private static SettingsProvider PlayModeSettings() => new("Project/Editor Play Mode", SettingsScope.Project,
        ["AssetBundles", "virtual", "real", "Play Mode", "虚拟", "真实", "资源包"])
    {
        label = "Editor Play Mode",
        guiHandler = _ => Draw()
    };

    private static void Draw()
    {
        var project = EditorApplication.projectPath;
        if (string.IsNullOrWhiteSpace(project)) return;
        if (!_project.Equals(project, StringComparison.OrdinalIgnoreCase))
        {
            _project = project; _settings = null; _error = string.Empty;
        }
        if (_settings is null)
        {
            try { _settings = EditorPlayModeSettingsStore.Load(project); }
            catch (Exception error) { _error = error.Message; }
        }
        GUILayout.Label("Editor Play Mode", EditorStyles.boldLabel);
        GUILayout.Label("Select the content used when entering Play Mode. Player build settings are separate.", EditorStyles.wordWrappedLabel);
        if (_settings is null) { GUILayout.Label(_error, EditorStyles.wordWrappedLabel); return; }
        var playing = EditorApplication.isPlayingOrWillChangePlaymode;
        EditorGUI.BeginDisabledGroup(playing);
        try
        {
            var mode = (EditorPlayModeContentMode)EditorGUILayout.EnumPopup("Content Mode", _settings.Mode);
            var directory = _settings.ReleaseDirectory;
            var package = _settings.PackageName;
            if (mode == EditorPlayModeContentMode.Real)
            {
                directory = EditorGUILayout.TextField("AB Release Directory", directory);
                if (GUILayout.Button("Browse AB Release Directory..."))
                {
                    var paths = EditorCapabilities.GetRequiredService<BEngine.Platform.PathUtility>();
                    var initial = string.IsNullOrWhiteSpace(directory) ? project
                        : paths.IsPathRooted(directory) ? directory : paths.Combine(project, directory);
                    var selected = EditorUtility.OpenFolderPanel("Select built AssetBundle release", initial, string.Empty);
                    if (!string.IsNullOrWhiteSpace(selected)) directory = selected;
                }
                package = EditorGUILayout.TextField("Release Package Name", package);
                GUILayout.Label("Choose a built AssetBundles package directory containing latest.yaml, or a version directory containing version.yaml and bundles/*.bassetbundle. Paths may be project-relative or absolute. Release Package Name is the publishing identity (usually main), not a fonts/images package group. Missing or invalid content stops Play Mode; rebuild after asset or code changes.", EditorStyles.wordWrappedLabel);
            }
            else GUILayout.Label("Virtual uses the current imported project content without building physical AssetBundles.", EditorStyles.wordWrappedLabel);
            if (mode != _settings.Mode || directory != _settings.ReleaseDirectory || package != _settings.PackageName)
            {
                var next = new EditorPlayModeSettings { Mode = mode, ReleaseDirectory = directory, PackageName = package };
                try { EditorPlayModeSettingsStore.Save(project, next); _settings = next; _error = string.Empty; }
                catch (Exception error) { _error = error.Message; }
            }
        }
        finally { EditorGUI.EndDisabledGroup(); }
        if (playing) GUILayout.Label("Stop Play Mode before changing content settings.", EditorStyles.wordWrappedLabel);
        if (!string.IsNullOrWhiteSpace(_error)) GUILayout.Label(_error, EditorStyles.wordWrappedLabel);
    }
}
