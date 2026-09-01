using BEngine.Serialization;
namespace BEngine.ProjectSystem;

public static class ProjectRuntimeSettings
{
    public static ProjectSettingsData LoadAndApply(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var exists = File.Exists(workspace.ProjectSettingsFilePath);
        var settings = exists
            ? YamlUtility.Load<ProjectSettingsData>(workspace.ProjectSettingsFilePath)
            : new ProjectSettingsData();
        AssetDataValidation.ValidateProjectSettings(settings);
        TagManager.Configure(settings.Tags);
        SortingLayerRegistry.Configure(settings.SortingLayers.Select(item => item.ToDefinition()));
        return settings;
    }
}
