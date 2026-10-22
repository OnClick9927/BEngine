using BEngine.Platform;

namespace BEngine.UIElements;

public sealed class FileUIDocumentSourceLoader : IUIDocumentSourceLoader
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;

    public static FileUIDocumentSourceLoader Shared { get; } = new(
        RuntimeServiceAccess.FileSystem,
        RuntimeServiceAccess.PathService,
        RuntimeServiceAccess.PlatformService);

    public FileUIDocumentSourceLoader(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platform = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public VisualTreeAsset Load(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var resolved = ResolvePath(source);
        return VisualTreeAsset.Load(_fileSystem.FileExists(resolved) ? resolved : source);
    }

    private string ResolvePath(string source)
    {
        if (_paths.IsPathRooted(source)) return _paths.GetFullPath(source);
        var normalized = source.Replace('/', _paths.DirectorySeparatorChar);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{_paths.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
        {
            var dataPath = _paths.GetFullPath(Application.dataPath);
            var projectRoot = _paths.GetFileName(dataPath).Equals("Assets",
                StringComparison.OrdinalIgnoreCase)
                ? _paths.GetDirectoryName(dataPath) ?? _platform.CurrentDirectory
                : _platform.CurrentDirectory;
            return _paths.GetFullPath(_paths.Combine(projectRoot, normalized));
        }
        return _paths.GetFullPath(_paths.Combine(Application.dataPath, normalized));
    }
}
