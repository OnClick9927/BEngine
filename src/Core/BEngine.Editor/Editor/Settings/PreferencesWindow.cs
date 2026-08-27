using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Preferences.png")]
public sealed class PreferencesWindow : SettingsWindowBase
{
    protected override SettingsScope Scope => SettingsScope.User;
    protected override string WindowTitle => "Preferences";
    protected override string IconPath => "Icons/Windows/Preferences.png";

    public static PreferencesWindow Open(string settingsPath = "")
    {
        var window = GetWindow<PreferencesWindow>(EditorLocalization.Tr("Preferences"), focus: false);
        window.SelectPath(settingsPath);
        window.Focus();
        return window;
    }
}
