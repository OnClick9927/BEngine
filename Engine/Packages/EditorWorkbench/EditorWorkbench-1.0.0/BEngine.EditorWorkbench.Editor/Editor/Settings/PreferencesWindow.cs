using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Preferences.png")]
public sealed class PreferencesWindow :
    SettingsWindowBase<PreferencesWindowModel, PreferencesWindowController>
{
    protected override SettingsScope Scope => SettingsScope.User;
    protected override string WindowTitle => "Preferences";
    protected override string IconPath => "Icons/Windows/Preferences.png";

    protected override PreferencesWindowModel CreateSettingsModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override PreferencesWindowController CreateSettingsController(
        EditorWindowMvcContext context,
        PreferencesWindowModel windowModel) => new(context, windowModel);

    public static PreferencesWindow Open(string settingsPath = "")
    {
        var window = GetWindow<PreferencesWindow>(EditorLocalization.Tr("Preferences"), focus: false);
        window.SelectPath(settingsPath);
        window.Focus();
        return window;
    }
}
