using BEngine.Documents;
namespace BEngine.ProjectSystem;

public static class ProjectRuntimeSettings
{
    public static ProjectSettingsDocument LoadAndApply(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var exists = File.Exists(workspace.ProjectSettingsFilePath);
        var settings = exists
            ? YamlUtility.Load<ProjectSettingsDocument>(workspace.ProjectSettingsFilePath)
            : new ProjectSettingsDocument();
        var migrated = ProjectSettingsMigration.Normalize(settings);
        DocumentValidationRegistry.Validate(settings);
        if (exists && migrated)
        {
            try { settings.Save(workspace.ProjectSettingsFilePath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Project settings were migrated in memory but could not be saved: {exception.Message}");
            }
        }
        TagManager.Configure(settings.Tags);
        SortingLayerRegistry.Configure(settings.SortingLayers.Select(item => item.ToDefinition()));
        return settings;
    }
}
