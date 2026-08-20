using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/ProjectSettings.png")]
public sealed class ProjectSettingsWindow : SettingsWindowBase
{
    protected override SettingsScope Scope => SettingsScope.Project;
    protected override string WindowTitle => "Project Settings";
    protected override string IconPath => "Icons/Windows/ProjectSettings.png";

    public static ProjectSettingsWindow Open(string settingsPath = "")
    {
        var window = (ProjectSettingsWindow)GetWindow(typeof(ProjectSettingsWindow), utility: true,
            EditorLocalization.Tr("Project Settings"), focus: false);
        window.SelectPath(settingsPath);
        window.Focus();
        return window;
    }
}
