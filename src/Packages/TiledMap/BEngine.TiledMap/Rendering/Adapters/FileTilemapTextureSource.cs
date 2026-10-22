using BEngine.Platform;

namespace BEngine.TiledMap;

public sealed class FileTilemapTextureSource : ITilemapTextureSource
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;

    public static FileTilemapTextureSource Shared { get; } = new(
        RuntimeServiceAccess.FileSystem,
        RuntimeServiceAccess.PathService,
        RuntimeServiceAccess.PlatformService);

    public FileTilemapTextureSource(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platform = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public bool TryRead(string source, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = BEngine.Rendering.TextureImportResolver.Resolve?.Invoke(source)?.ArtifactPath ?? ResolvePath(source);
            if (path is not null)
            {
                bytes = _fileSystem.ReadAllBytes(path);
                return true;
            }
            bytes = Resources.Load<byte[]>(source) ?? [];
            return bytes.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public string GetName(string source) => _paths.GetFileName(source);

    private string? ResolvePath(string source)
    {
        if (_paths.IsPathRooted(source))
        {
            var rooted = _paths.GetFullPath(source);
            return _fileSystem.FileExists(rooted) ? rooted : null;
        }
        var normalized = source.Replace('/', _paths.DirectorySeparatorChar);
        var current = _paths.GetFullPath(normalized);
        if (_fileSystem.FileExists(current)) return current;
        var dataPath = _paths.GetFullPath(Application.dataPath);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{_paths.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
        {
            var root = _paths.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? _paths.GetDirectoryName(dataPath) ?? _platform.CurrentDirectory
                : _platform.CurrentDirectory;
            var assetPath = _paths.GetFullPath(_paths.Combine(root, normalized));
            return _fileSystem.FileExists(assetPath) ? assetPath : null;
        }
        var dataFile = _paths.GetFullPath(_paths.Combine(dataPath, normalized));
        return _fileSystem.FileExists(dataFile) ? dataFile : null;
    }
}
