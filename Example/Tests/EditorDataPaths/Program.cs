using BEngine.Editor;

var expected = Path.Combine(Path.GetTempPath(), $"BEngine-EditorDataPaths-{Guid.NewGuid():N}");
try
{
    Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", expected);
    var actual = Path.GetFullPath(EditorDataPaths.rootPath);
    expected = Path.GetFullPath(expected);
    Require(actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
        $"Editor data path was '{actual}', expected the configured path '{expected}'.");
    Require(Directory.Exists(actual), "The configured editor data root was not created.");
    Require(EditorDataPaths.logsPath.Equals(Path.Combine(expected, "Logs"), StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(EditorDataPaths.logsPath),
        "Editor log path was not created inside the configured data root.");
    Require(EditorDataPaths.preferencesPath.Equals(Path.Combine(expected, "Preferences.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "Preferences path escaped the configured data root.");
    Require(EditorDataPaths.editorPrefsPath.Equals(Path.Combine(expected, "EditorPrefs.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "EditorPrefs path escaped the configured data root.");
    Require(EditorDataPaths.launcherSettingsPath.Equals(Path.Combine(expected, "LauncherSettings.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "Launcher settings path escaped the configured data root.");
    Require(EditorDataPaths.editorBootstrapLogPath.Equals(
            Path.Combine(expected, "Logs", "EditorBootstrap.log"), StringComparison.OrdinalIgnoreCase),
        "Editor bootstrap log path escaped the configured log directory.");

    Console.WriteLine("EDITOR_DATA_PATHS_OK|environment-override,root,logs,preferences");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"EDITOR_DATA_PATHS_FAILED|{exception}");
    return 1;
}
finally
{
    Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", null);
    try { Directory.Delete(expected, recursive: true); } catch { }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
