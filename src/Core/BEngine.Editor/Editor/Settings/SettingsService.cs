using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public static class SettingsService
{
    public static void OpenUserPreferences(string settingsPath = "") =>
        PreferencesWindow.Open(settingsPath);

    public static void OpenProjectSettings(string settingsPath = "") =>
        ProjectSettingsWindow.Open(TagLayerSettingsProvider.ResolveSettingsPath(settingsPath));
}
