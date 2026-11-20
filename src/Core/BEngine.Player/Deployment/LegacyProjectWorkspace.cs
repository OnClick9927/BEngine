using BEngine.ProjectSystem;

namespace BEngine.Platform;

/// <summary>Compatibility for unpackaged projects without adding authoring fields to the runtime workspace.</summary>
public static class LegacyProjectWorkspace
{
    public static string SettingsDirectory(RuntimeWorkspace workspace) =>
        PathUtility.Current.Combine(workspace.RootPath, "ProjectSettings");

    public static RuntimeWorkspace Open(string path, FileUtility files, PathUtility paths)
    {
        var fullPath = paths.GetFullPath(path);
        var projectPath = files.DirectoryExists(fullPath) ? paths.Combine(fullPath, "Project.yaml") : fullPath;
        if (!files.FileExists(projectPath) || !paths.GetFileName(projectPath).Equals("Project.yaml", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The selected directory does not contain Project.yaml.", projectPath);
        var project = YamlUtility.Deserialize<ProjectDocument>(files.ReadAllText(projectPath));
        if (project.Format != "BEngine.Project" || project.Version != 1)
            throw new InvalidDataException("Unsupported legacy project document.");
        var root = paths.GetDirectoryName(projectPath) ?? throw new InvalidDataException("Project root is invalid.");
        var settingsPath = paths.Combine(root, "ProjectSettings", "ProjectSettings.yaml");
        var settings = files.FileExists(settingsPath)
            ? YamlUtility.Deserialize<SettingsDocument>(files.ReadAllText(settingsPath)) : new();
        if (settings.Format != "BEngine.ProjectSettings" || settings.Version != 3)
            throw new InvalidDataException("Unsupported legacy project settings.");
        return RuntimeWorkspace.CreateRuntime(root, project, settings, files, paths);
    }

    // Extra authoring fields in the old YAML format are intentionally ignored.
    private sealed class ProjectDocument : RuntimeProjectData
    {
        public string Format { get; set; } = "BEngine.Project";
        public int Version { get; set; } = 1;
    }
    private sealed class SettingsDocument : RuntimeSettingsData
    {
        public string Format { get; set; } = "BEngine.ProjectSettings";
        public int Version { get; set; } = 3;
    }
}
