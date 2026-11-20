using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/ProjectSettings.png")]
public sealed class ProjectSettingsWindow :
    SettingsWindowBase<ProjectSettingsWindowModel, ProjectSettingsWindowController>
{
    protected override SettingsScope Scope => SettingsScope.Project;
    protected override string WindowTitle => "Project Settings";
    protected override string IconPath => "Icons/Windows/ProjectSettings.png";

    protected override ProjectSettingsWindowModel CreateSettingsModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override ProjectSettingsWindowController CreateSettingsController(
        EditorWindowMvcContext context,
        ProjectSettingsWindowModel windowModel) => new(context, windowModel);

    public static ProjectSettingsWindow Open(string settingsPath = "")
    {
        var window = GetWindow<ProjectSettingsWindow>(EditorLocalization.Tr("Project Settings"), focus: false);
        window.SelectPath(settingsPath);
        window.Focus();
        return window;
    }
}
