using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.ProjectSystem;

/// <summary>Authoring file adapter; runtime configuration application stays in Core.</summary>
public static class ProjectRuntimeSettings
{
    public static ProjectSettingsData LoadAndApply(ProjectWorkspace workspace) =>
        LoadAndApply(workspace, RuntimeServices.FileSystem, RuntimeServices.PathService);
    public static ProjectSettingsData LoadAndApply(ProjectWorkspace workspace, FileUtility files, PathUtility paths)
    {
        var settings = files.FileExists(workspace.ProjectSettingsFilePath)
            ? YamlUtility.Load<ProjectSettingsData>(workspace.ProjectSettingsFilePath, files, paths) : new();
        ProjectDocumentValidation.ValidateProjectSettings(settings);
        workspace.SetSettings(settings.ToRuntime());
        RuntimeConfiguration.Apply(workspace);
        return settings;
    }
}
