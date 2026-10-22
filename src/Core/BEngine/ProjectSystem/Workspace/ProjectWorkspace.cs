using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

public sealed class ProjectWorkspace
{
    public const string ProjectFileName = "Project.yaml";

    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;

    public string RootPath { get; }
    public string ProjectFilePath => _paths.Combine(RootPath, ProjectFileName);
    public string AssetsPath => ResolveInside(Project.AssetsDirectory);
    public string ScenesPath => ResolveInside(Project.ScenesDirectory);
    public string ScriptsPath => ResolveInside(Project.ScriptsDirectory);
    public string EditorScriptsPath => ResolveInside(Project.EditorScriptsDirectory);
    public string ProjectSettingsPath => _paths.Combine(RootPath, "ProjectSettings");
    public string ProjectSettingsFilePath => _paths.Combine(ProjectSettingsPath, "ProjectSettings.yaml");
    public string PackagesPath => _paths.Combine(RootPath, "Packages");
    public string ThirdPartyPath => _paths.Combine(AssetsPath, "3rd");
    public IEnumerable<string> AssetRoots => new[] { AssetsPath }
        .Where(_fileSystem.DirectoryExists);
    public string PackageManifestPath => _paths.Combine(PackagesPath, "manifest.yaml");
    public string LibraryPath => _paths.Combine(RootPath, "Library");
    public string PackageCachePath => _paths.Combine(LibraryPath, "Packages");
    public string ScriptAssembliesPath => _paths.Combine(LibraryPath, "ScriptAssemblies");
    public string AssetArtifactsPath => _paths.Combine(LibraryPath, "Artifacts");
    public string ShaderArtifactsPath => _paths.Combine(LibraryPath, "ShaderCache");
    public string AssetDatabasePath => _paths.Combine(LibraryPath, "AssetDatabase.yaml");
    public string LogsPath => _paths.Combine(RootPath, "Logs");
    public string TempPath => _paths.Combine(RootPath, "Temp");
    public string StartupScenePath => ResolveInside(Project.StartupScene);
    public ProjectData Project { get; }
    internal ProjectSettingsData? SettingsOverride { get; }

    internal ProjectWorkspace(string rootPath, ProjectData project)
        : this(
            rootPath,
            project,
            ensureDirectoryLayout: true,
            settingsOverride: null,
            RuntimeServiceAccess.FileSystem,
            RuntimeServiceAccess.PathService) { }

    internal ProjectWorkspace(
        string rootPath,
        ProjectData project,
        FileUtility fileSystem,
        PathUtility pathService)
        : this(rootPath, project, ensureDirectoryLayout: true, settingsOverride: null, fileSystem, pathService) { }

    private ProjectWorkspace(
        string rootPath,
        ProjectData project,
        bool ensureDirectoryLayout,
        ProjectSettingsData? settingsOverride,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = pathService ?? throw new ArgumentNullException(nameof(pathService));
        RootPath = _paths.GetFullPath(rootPath);
        Project = project;
        SettingsOverride = settingsOverride;
        if (ensureDirectoryLayout) EnsureDirectoryLayout();
    }

    public static ProjectWorkspace Open(string path)
        => Open(path, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static ProjectWorkspace Open(
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
        => OpenCore(path, ensureDirectoryLayout: true, fileSystem, pathService);

    public static ProjectWorkspace OpenRuntime(string path) =>
        OpenRuntime(path, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static ProjectWorkspace OpenRuntime(
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        return OpenCore(path, ensureDirectoryLayout: false, fileSystem, pathService);
    }

    /// <summary>Creates a runtime workspace from validated data without creating directories.</summary>
    public static ProjectWorkspace CreateRuntime(string root, ProjectData project,
        ProjectSettingsData settings, FileUtility files, PathUtility paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);
        AssetDataValidation.ValidateProject(project);
        AssetDataValidation.ValidateProjectSettings(settings);
        return new(root, project, false, settings, files, paths);
    }

    private static ProjectWorkspace OpenCore(
        string path,
        bool ensureDirectoryLayout,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullPath = pathService.GetFullPath(path);
        var projectPath = fileSystem.DirectoryExists(fullPath)
            ? pathService.Combine(fullPath, ProjectFileName)
            : fullPath;
        if (!fileSystem.FileExists(projectPath) ||
            !pathService.GetFileName(projectPath).Equals(ProjectFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException($"The selected directory does not contain {ProjectFileName}.", projectPath);
        }

        var project = YamlUtility.Deserialize<ProjectData>(fileSystem.ReadAllText(projectPath));
        AssetDataValidation.ValidateProject(project);
        if (ensureDirectoryLayout && string.IsNullOrWhiteSpace(project.EngineVersion))
        {
            project.EngineVersion = BEngine.EngineVersion.Current;
            fileSystem.WriteAllText(projectPath, YamlUtility.Serialize(project));
        }
        var root = pathService.GetDirectoryName(projectPath) ??
                   throw new InvalidDataException("Project root is invalid.");
        return new ProjectWorkspace(
            root, project, ensureDirectoryLayout, settingsOverride: null, fileSystem, pathService);
    }

    public string ResolveInside(string relativePath)
    {
        if (_paths.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Project path must be relative: {relativePath}");
        }

        var resolved = _paths.GetFullPath(_paths.Combine(RootPath,
            relativePath.Replace('/', _paths.DirectorySeparatorChar)));
        if (!resolved.StartsWith(RootPath + _paths.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Project path escapes the workspace: {relativePath}");
        }
        return resolved;
    }

    private void EnsureDirectoryLayout()
    {
        _fileSystem.CreateDirectory(AssetsPath);
        _fileSystem.CreateDirectory(ScenesPath);
        _fileSystem.CreateDirectory(ScriptsPath);
        _fileSystem.CreateDirectory(EditorScriptsPath);
        _fileSystem.CreateDirectory(ProjectSettingsPath);
        _fileSystem.CreateDirectory(PackagesPath);
        _fileSystem.CreateDirectory(ThirdPartyPath);
        _fileSystem.CreateDirectory(LibraryPath);
        _fileSystem.CreateDirectory(PackageCachePath);
        _fileSystem.CreateDirectory(ScriptAssembliesPath);
        _fileSystem.CreateDirectory(AssetArtifactsPath);
        _fileSystem.CreateDirectory(ShaderArtifactsPath);
        _fileSystem.CreateDirectory(LogsPath);
        _fileSystem.CreateDirectory(TempPath);
    }

}
