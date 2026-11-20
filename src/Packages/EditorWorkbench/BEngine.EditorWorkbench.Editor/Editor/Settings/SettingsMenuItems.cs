using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

internal static class SettingsMenuItems
{
    [MenuItem("Edit/Preferences...", false, 900)]
    private static void OpenPreferences() => SettingsService.OpenUserPreferences();

    [MenuItem("Edit/Project Settings...", false, 910)]
    private static void OpenProjectSettings() => SettingsService.OpenProjectSettings();
}
