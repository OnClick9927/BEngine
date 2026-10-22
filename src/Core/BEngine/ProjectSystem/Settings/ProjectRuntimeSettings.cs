using BEngine.Serialization;
using BEngine.Platform;
namespace BEngine.ProjectSystem;

public static class ProjectRuntimeSettings
{
    public static ProjectSettingsData LoadAndApply(ProjectWorkspace workspace) =>
        LoadAndApply(workspace, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static ProjectSettingsData LoadAndApply(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var settings = workspace.SettingsOverride ??
            (fileSystem.FileExists(workspace.ProjectSettingsFilePath)
                ? YamlUtility.Load<ProjectSettingsData>(
                    workspace.ProjectSettingsFilePath, fileSystem, pathService)
                : new ProjectSettingsData());
        AssetDataValidation.ValidateProjectSettings(settings);
        Application.runInBackground = settings.RunInBackground;
        TagManager.Configure(settings.Tags);
        SortingLayerRegistry.Configure(settings.SortingLayers.Select(item => item.ToDefinition()));
        return settings;
    }
}
