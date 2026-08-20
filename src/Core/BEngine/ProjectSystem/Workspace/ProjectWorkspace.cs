using BEngine.Documents;

namespace BEngine.ProjectSystem;

public sealed class ProjectWorkspace
{
    public const string ProjectFileName = "Project.yaml";

    public string RootPath { get; }
    public string ProjectFilePath => Path.Combine(RootPath, ProjectFileName);
    public string AssetsPath => ResolveInside(Project.AssetsDirectory);
    public string ScenesPath => Path.Combine(AssetsPath, "Scenes");
    public string ScriptsPath => ResolveInside(Project.ScriptsDirectory);
    public string EditorScriptsPath => ResolveInside(Project.EditorScriptsDirectory);
    public string ProjectSettingsPath => Path.Combine(RootPath, "ProjectSettings");
    public string ProjectSettingsFilePath => Path.Combine(ProjectSettingsPath, "ProjectSettings.yaml");
    public string EditorLayoutPath => Path.Combine(ProjectSettingsPath, "EditorLayout.yaml");
    public string PackagesPath => Path.Combine(RootPath, "Packages");
    public string PackageManifestPath => Path.Combine(PackagesPath, "manifest.yaml");
    public string LibraryPath => Path.Combine(RootPath, "Library");
    public string ScriptAssembliesPath => Path.Combine(LibraryPath, "ScriptAssemblies");
    public string AssetArtifactsPath => Path.Combine(LibraryPath, "Artifacts");
    public string ShaderArtifactsPath => Path.Combine(LibraryPath, "ShaderCache");
    public string AssetDatabasePath => Path.Combine(LibraryPath, "AssetDatabase.yaml");
    public string LogsPath => Path.Combine(RootPath, "Logs");
    public string TempPath => Path.Combine(RootPath, "Temp");
    public string StartupScenePath => ResolveInside(Project.StartupScene);
    public ProjectDocument Project { get; }

    internal ProjectWorkspace(string rootPath, ProjectDocument project)
    {
        RootPath = Path.GetFullPath(rootPath);
        Project = project;
        EnsureDirectoryLayout();
    }

    public static ProjectWorkspace Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var projectPath = Directory.Exists(fullPath) ? Path.Combine(fullPath, ProjectFileName) : fullPath;
        if (!File.Exists(projectPath) || !Path.GetFileName(projectPath).Equals(ProjectFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException($"The selected directory does not contain {ProjectFileName}.", projectPath);
        }

        var project = Document.Load<ProjectDocument>(projectPath);
        var root = Path.GetDirectoryName(projectPath) ?? throw new InvalidDataException("Project root is invalid.");
        return new ProjectWorkspace(root, project);
    }

    public string ResolveInside(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Project path must be relative: {relativePath}");
        }

        var resolved = Path.GetFullPath(Path.Combine(RootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Project path escapes the workspace: {relativePath}");
        }
        return resolved;
    }

    private void EnsureDirectoryLayout()
    {
        Directory.CreateDirectory(AssetsPath);
        Directory.CreateDirectory(ScenesPath);
        Directory.CreateDirectory(ScriptsPath);
        Directory.CreateDirectory(EditorScriptsPath);
        Directory.CreateDirectory(ProjectSettingsPath);
        Directory.CreateDirectory(PackagesPath);
        Directory.CreateDirectory(LibraryPath);
        Directory.CreateDirectory(ScriptAssembliesPath);
        Directory.CreateDirectory(AssetArtifactsPath);
        Directory.CreateDirectory(ShaderArtifactsPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(TempPath);
    }

}
