using BEngine.Platform;

namespace BEngine.ProjectSystem;

/// <summary>Authoring layout and directory creation; never shipped in the runtime assembly.</summary>
public sealed class ProjectWorkspace : RuntimeWorkspace
{
    public const string ProjectFileName = "Project.yaml";
    public new ProjectData Project { get; }
    public string ProjectFilePath => _paths.Combine(RootPath, ProjectFileName);
    public string ProjectSettingsPath => _paths.Combine(RootPath, "ProjectSettings");
    public string ProjectSettingsFilePath => _paths.Combine(ProjectSettingsPath, "ProjectSettings.yaml");
    public string ScenesPath => ResolveInside(Project.ScenesDirectory);
    public string ScriptsPath => ResolveInside(Project.ScriptsDirectory);
    public string EditorScriptsPath => ResolveInside(Project.EditorScriptsDirectory);
    public string PackagesPath => _paths.Combine(RootPath, "Packages");
    public string ThirdPartyPath => _paths.Combine(AssetsPath, "3rd");
    public string PackageManifestPath => _paths.Combine(PackagesPath, "manifest.yaml");
    public string LibraryPath => _paths.Combine(RootPath, "Library");
    public string PackageCachePath => _paths.Combine(LibraryPath, "Packages");
    public string ScriptAssembliesPath => _paths.Combine(LibraryPath, "ScriptAssemblies");
    public string AssetArtifactsPath => _paths.Combine(LibraryPath, "Artifacts");
    public string ShaderArtifactsPath => _paths.Combine(LibraryPath, "ShaderCache");
    public string AssetDatabasePath => _paths.Combine(LibraryPath, "AssetDatabase.yaml");
    public string LogsPath => _paths.Combine(RootPath, "Logs");
    public string TempPath => _paths.Combine(RootPath, "Temp");
    public IEnumerable<string> AssetRoots => new[] { AssetsPath }
        .Where(_fileSystem.DirectoryExists);
    internal ProjectWorkspace(string rootPath, ProjectData project)
        : this(rootPath, project, RuntimeServices.FileSystem, RuntimeServices.PathService) { }
    internal ProjectWorkspace(string rootPath, ProjectData project, FileUtility files, PathUtility paths)
        : base(rootPath, project, new RuntimeSettingsData(), files, paths) { Project = project; }
    public static ProjectWorkspace Open(string path) => Open(path, RuntimeServices.FileSystem, RuntimeServices.PathService);
    public static ProjectWorkspace Open(string path, FileUtility files, PathUtility paths)
    {
        var fullPath = paths.GetFullPath(path);
        var projectPath = files.DirectoryExists(fullPath) ? paths.Combine(fullPath, ProjectFileName) : fullPath;
        if (!files.FileExists(projectPath) || !paths.GetFileName(projectPath).Equals(ProjectFileName, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException($"The selected directory does not contain {ProjectFileName}.", projectPath);
        var project = YamlUtility.Deserialize<ProjectData>(files.ReadAllText(projectPath));
        BEngine.Serialization.ProjectDocumentValidation.ValidateProject(project);
        if (string.IsNullOrWhiteSpace(project.EngineVersion))
        {
            project.EngineVersion = EngineVersion.Current;
            files.WriteAllText(projectPath, YamlUtility.Serialize(project));
        }
        return new(paths.GetDirectoryName(projectPath) ?? throw new InvalidDataException("Project root is invalid."), project, files, paths);
    }
}
