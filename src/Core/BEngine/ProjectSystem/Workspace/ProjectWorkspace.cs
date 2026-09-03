using BEngine.Serialization;
using BEngine.Content;

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
    public ProjectData Project { get; }
    public RuntimeMetadataDocument? RuntimeMetadata { get; }
    public string RuntimeMetadataFilePath => Path.Combine(RootPath, RuntimeMetadataSerializer.FileName);

    internal ProjectWorkspace(string rootPath, ProjectData project)
        : this(rootPath, project, ensureDirectoryLayout: true, runtimeMetadata: null) { }

    private ProjectWorkspace(
        string rootPath,
        ProjectData project,
        bool ensureDirectoryLayout,
        RuntimeMetadataDocument? runtimeMetadata)
    {
        RootPath = Path.GetFullPath(rootPath);
        Project = project;
        RuntimeMetadata = runtimeMetadata;
        if (ensureDirectoryLayout) EnsureDirectoryLayout();
    }

    public static ProjectWorkspace Open(string path)
        => OpenCore(path, ensureDirectoryLayout: true);

    public static ProjectWorkspace OpenRuntime(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var archivePath = Path.Combine(
                fullPath,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                PlayerPackagedResourceAddresses.PlayerArchiveFileName);
            if (File.Exists(archivePath))
            {
                var archive = BuiltInResourceArchive.Open(archivePath);
                var metadata = RuntimeMetadataSerializer.Deserialize(
                    archive.ReadBytes(PlayerPackagedResourceAddresses.RuntimeMetadata));
                return new ProjectWorkspace(
                    fullPath, metadata.Project, ensureDirectoryLayout: false, runtimeMetadata: metadata);
            }
        }
        var metadataPath = Directory.Exists(fullPath)
            ? Path.Combine(fullPath, RuntimeMetadataSerializer.FileName)
            : fullPath;
        if (File.Exists(metadataPath) &&
            Path.GetFileName(metadataPath).Equals(
                RuntimeMetadataSerializer.FileName, StringComparison.OrdinalIgnoreCase))
        {
            var metadata = RuntimeMetadataSerializer.Load(metadataPath);
            var root = Path.GetDirectoryName(metadataPath) ??
                       throw new InvalidDataException("Runtime metadata root is invalid.");
            return new ProjectWorkspace(
                root, metadata.Project, ensureDirectoryLayout: false, runtimeMetadata: metadata);
        }
        return OpenCore(path, ensureDirectoryLayout: false);
    }

    private static ProjectWorkspace OpenCore(string path, bool ensureDirectoryLayout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var projectPath = Directory.Exists(fullPath) ? Path.Combine(fullPath, ProjectFileName) : fullPath;
        if (!File.Exists(projectPath) || !Path.GetFileName(projectPath).Equals(ProjectFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException($"The selected directory does not contain {ProjectFileName}.", projectPath);
        }

        var project = YamlUtility.Load<ProjectData>(projectPath);
        AssetDataValidation.ValidateProject(project);
        var root = Path.GetDirectoryName(projectPath) ?? throw new InvalidDataException("Project root is invalid.");
        return new ProjectWorkspace(root, project, ensureDirectoryLayout, runtimeMetadata: null);
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
