using BEngine.Platform;

namespace BEngine.Rendering;

/// <summary>Reads imported artifacts, physical image sources, or resource bytes without creating GPU objects.</summary>
public sealed class TextureSourceReader
{
    private readonly FileUtility _files;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;

    public TextureSourceReader(FileUtility files, PathUtility paths, PlatformUtility platform)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    public bool TryRead(string source, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = TextureImportResolver.Resolve?.Invoke(source)?.ArtifactPath ?? ResolveFilePath(source);
            if (path is not null)
            {
                bytes = _files.ReadAllBytes(path);
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

    // Source lookup is shared with streaming/prepared textures, whose callers
    // retain their own resource-reference, metadata and scheduling policies.
    internal string? ResolveFilePath(string source)
    {
        if (_paths.IsPathRooted(source))
        {
            var rooted = _paths.GetFullPath(source);
            return _files.FileExists(rooted) ? rooted : null;
        }
        var normalized = source.Replace('/', _paths.DirectorySeparatorChar);
        var current = _paths.GetFullPath(normalized);
        if (_files.FileExists(current)) return current;
        var dataPath = _paths.GetFullPath(Application.dataPath);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{_paths.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var root = _paths.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? _paths.GetDirectoryName(dataPath) ?? _platform.CurrentDirectory
                : _platform.CurrentDirectory;
            var assetPath = _paths.GetFullPath(_paths.Combine(root, normalized));
            return _files.FileExists(assetPath) ? assetPath : null;
        }
        var dataFile = _paths.GetFullPath(_paths.Combine(dataPath, normalized));
        return _files.FileExists(dataFile) ? dataFile : null;
    }
}
