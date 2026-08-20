using BEngine.Editor;

var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", "..", "EditorData"));
var actual = Path.GetFullPath(EditorDataPaths.rootPath);
Require(actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
    $"Editor data path was '{actual}', expected '{expected}'.");
Require(EditorDataPaths.logsPath.Equals(Path.Combine(expected, "Logs"), StringComparison.OrdinalIgnoreCase),
    "Editor log path is not inside Output/EditorData/Logs.");
Require(EditorDataPaths.preferencesPath.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
    "Preferences path escaped Output/EditorData.");
Require(EditorDataPaths.editorPrefsPath.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
    "EditorPrefs path escaped Output/EditorData.");
Require(EditorDataPaths.launcherSettingsPath.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
    "Launcher settings path escaped Output/EditorData.");

Console.WriteLine($"EDITOR_DATA_PATHS_OK|root={actual}|logs={EditorDataPaths.logsPath}");
return;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
