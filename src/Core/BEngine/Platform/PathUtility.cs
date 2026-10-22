namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IPathService through dependency injection.</summary>
public sealed class PathUtility
{
    private readonly IPathService _implementation;

    public PathUtility(IPathService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static PathUtility Current => RuntimeServiceAccess.GetRequiredService<PathUtility>();

    public char DirectorySeparatorChar => _implementation.DirectorySeparatorChar;

    public char AltDirectorySeparatorChar => _implementation.AltDirectorySeparatorChar;

    public char PathSeparator => _implementation.PathSeparator;

    public string Combine(params string[] paths) =>
        _implementation.Combine(paths);

    public string GetFullPath(string path) =>
        _implementation.GetFullPath(path);

    public string GetFullPath(string path, string basePath) =>
        _implementation.GetFullPath(path, basePath);

    public string? GetDirectoryName(string path) =>
        _implementation.GetDirectoryName(path);

    public string GetFileName(string path) =>
        _implementation.GetFileName(path);

    public string GetFileNameWithoutExtension(string path) =>
        _implementation.GetFileNameWithoutExtension(path);

    public string GetExtension(string path) =>
        _implementation.GetExtension(path);

    public string GetRelativePath(string relativeTo, string path) =>
        _implementation.GetRelativePath(relativeTo, path);

    public char[] GetInvalidFileNameChars() =>
        _implementation.GetInvalidFileNameChars();

    public string TrimEndingDirectorySeparator(string path) =>
        _implementation.TrimEndingDirectorySeparator(path);

    public bool IsPathRooted(string path) =>
        _implementation.IsPathRooted(path);

    public bool IsPathFullyQualified(string path) =>
        _implementation.IsPathFullyQualified(path);
}
