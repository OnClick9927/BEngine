using BEngine.Editor;

var expected = Path.Combine(Path.GetTempPath(), $"BEngine-EditorDataPaths-{Guid.NewGuid():N}");
try
{
    Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", expected);
    Directory.CreateDirectory(Path.Combine(expected, "Preferences"));
    File.WriteAllText(Path.Combine(expected, "Preferences.yaml"), "legacy-preferences");
    File.WriteAllText(Path.Combine(expected, "EditorPrefs.yaml"), "legacy-editor-prefs");
    File.WriteAllText(Path.Combine(expected, "LauncherSettings.yaml"), "legacy-launcher-settings");
    File.WriteAllText(Path.Combine(expected, "Preferences", "Preferences.yaml"), "current-preferences");

    var actual = Path.GetFullPath(EditorDataPaths.rootPath);
    expected = Path.GetFullPath(expected);
    Require(actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
        $"Editor data path was '{actual}', expected the configured path '{expected}'.");
    Require(Directory.Exists(actual), "The configured editor data root was not created.");
    Require(EditorDataPaths.logsPath.Equals(Path.Combine(expected, "Logs"), StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(EditorDataPaths.logsPath),
        "Editor log path was not created inside the configured data root.");
    Require(EditorDataPaths.startupStatusDirectoryPath.Equals(Path.Combine(expected, "Startup"),
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(EditorDataPaths.startupStatusDirectoryPath),
        "Editor startup status directory was not created inside the configured data root.");
    var startupToken = Guid.NewGuid().ToString("N");
    Require(EditorDataPaths.GetStartupStatusPath(startupToken).Equals(
            Path.Combine(expected, "Startup", $"{startupToken}.json"),
            StringComparison.OrdinalIgnoreCase),
        "A valid startup token did not resolve to its isolated status file.");
    var invalidStartupTokenRejected = false;
    try
    {
        EditorDataPaths.GetStartupStatusPath("not-a-startup-token");
    }
    catch (ArgumentException)
    {
        invalidStartupTokenRejected = true;
    }
    Require(invalidStartupTokenRejected,
        "An invalid startup token can escape or alias the startup status directory.");
    Require(EditorDataPaths.preferencesDirectoryPath.Equals(Path.Combine(expected, "Preferences"),
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(EditorDataPaths.preferencesDirectoryPath),
        "Editor preferences directory was not created inside the configured data root.");
    Require(EditorDataPaths.themesPath.Equals(Path.Combine(expected, "Preferences", "Themes"),
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(EditorDataPaths.themesPath),
        "Editor GUI skin directory was not created inside Preferences.");
    Require(EditorDataPaths.preferencesPath.Equals(
            Path.Combine(expected, "Preferences", "Preferences.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "Preferences path escaped the configured data root.");
    Require(EditorDataPaths.editorPrefsPath.Equals(
            Path.Combine(expected, "Preferences", "EditorPrefs.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "EditorPrefs path escaped the configured data root.");
    Require(!File.Exists(Path.Combine(expected, "Preferences.yaml")) &&
            !File.Exists(Path.Combine(expected, "EditorPrefs.yaml")) &&
            File.ReadAllText(EditorDataPaths.preferencesPath) == "current-preferences" &&
            File.ReadAllText(EditorDataPaths.editorPrefsPath) == "legacy-editor-prefs",
        "Root editor preference files were not migrated without overwriting the current preferences.");
    var migratedPreferences = Directory.EnumerateFiles(
        Path.Combine(expected, "Preferences", "MigratedLegacy"), "Preferences-*.yaml").SingleOrDefault();
    Require(migratedPreferences is not null &&
            File.ReadAllText(migratedPreferences) == "legacy-preferences",
        "A conflicting legacy Preferences file was not preserved as a migration backup.");
    Require(EditorDataPaths.launcherSettingsPath.Equals(
            Path.Combine(expected, "Preferences", "LauncherSettings.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "Launcher settings path escaped the configured data root.");
    Require(EditorDataPaths.layoutsDirectoryPath.Equals(
            Path.Combine(expected, "Preferences", "Layouts"),
            StringComparison.OrdinalIgnoreCase) && Directory.Exists(EditorDataPaths.layoutsDirectoryPath),
        "Editor layouts escaped the global Preferences directory.");
    Require(EditorDataPaths.codexSettingsPath.Equals(
            Path.Combine(expected, "Preferences", "CodexSettings.yaml"),
            StringComparison.OrdinalIgnoreCase),
        "Codex settings path escaped the global Preferences directory.");
    Require(!File.Exists(Path.Combine(expected, "LauncherSettings.yaml")) &&
            File.ReadAllText(EditorDataPaths.launcherSettingsPath) == "legacy-launcher-settings",
        "Root LauncherSettings file was not migrated into the Preferences directory.");
    Require(EditorDataPaths.editorBootstrapLogPath.Equals(
            Path.Combine(expected, "Logs", "EditorBootstrap.log"), StringComparison.OrdinalIgnoreCase),
        "Editor bootstrap log path escaped the configured log directory.");

    Console.WriteLine("EDITOR_DATA_PATHS_OK|environment-override,root,logs,preferences-directory," +
                      "startup-status-path,invalid-startup-token,themes-directory," +
                      "layouts-directory,codex-settings,root-preferences-migration," +
                      "launcher-migration,conflict-backup");
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
