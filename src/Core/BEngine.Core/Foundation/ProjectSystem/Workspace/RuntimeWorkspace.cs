using BEngine.Platform;

namespace BEngine.ProjectSystem;

public class RuntimeWorkspace
{
    protected readonly FileUtility _fileSystem;
    protected readonly PathUtility _paths;
    public string RootPath { get; }
    public string AssetsPath => ResolveInside(Project.AssetsDirectory);
    public string StartupScenePath => ResolveInside(Project.StartupScene);
    public RuntimeProjectData Project { get; }
    public RuntimeSettingsData Settings { get; private set; }

    protected internal RuntimeWorkspace(string rootPath, RuntimeProjectData project,
        RuntimeSettingsData settings, FileUtility files, PathUtility paths)
    {
        _fileSystem = files ?? throw new ArgumentNullException(nameof(files));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        RuntimeConfiguration.Validate(project); RuntimeConfiguration.Validate(settings);
        RootPath = paths.TrimEndingDirectorySeparator(paths.GetFullPath(rootPath));
        Project = project; Settings = settings;
    }
    public static RuntimeWorkspace CreateRuntime(string root, RuntimeProjectData project,
        RuntimeSettingsData settings, FileUtility files, PathUtility paths) => new(root, project, settings, files, paths);

    public void SetSettings(RuntimeSettingsData settings)
    {
        RuntimeConfiguration.Validate(settings);
        Settings = settings;
    }

    public string ResolveInside(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (_paths.IsPathRooted(relativePath)) throw new InvalidDataException($"Workspace path must be relative: {relativePath}");
        var resolved = _paths.GetFullPath(_paths.Combine(RootPath, relativePath.Replace('/', _paths.DirectorySeparatorChar)));
        var prefix = RootPath.EndsWith(_paths.DirectorySeparatorChar) ? RootPath : RootPath + _paths.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Workspace path escapes its root: {relativePath}");
        return resolved;
    }
}
