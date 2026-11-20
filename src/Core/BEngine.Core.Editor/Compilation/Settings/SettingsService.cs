namespace BEngine.Editor;
public static class SettingsService
{
    public static event Action<SettingsScope,string>? OpenRequested;
    public static void OpenUserPreferences(string settingsPath = "") => OpenRequested?.Invoke(SettingsScope.User, settingsPath);
    public static void OpenProjectSettings(string settingsPath = "") => OpenRequested?.Invoke(SettingsScope.Project, settingsPath);
}
