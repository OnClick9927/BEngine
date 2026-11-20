namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IPathService through dependency injection.</summary>
public sealed class PathUtility
{
    private readonly IPathService _implementation;

    public PathUtility(IPathService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static PathUtility Current => RuntimeServices.GetRequiredService<PathUtility>();

    public char DirectorySeparatorChar => _implementation.DirectorySeparatorChar;

    public char AltDirectorySeparatorChar => _implementation.AltDirectorySeparatorChar;

    public char PathSeparator => _implementation.PathSeparator;

    public string Combine(params string[] paths) =>
        _implementation.Combine(paths);

    public string GetFullPath(string path) =>
        _implementation.GetFullPath(path);

    public string GetFullPath(string path, string basePath) =>
        _implementation.GetFullPath(path, basePath);

    /// <summary>Resolves a strict relative child path, rejecting empty, dot, parent and colon segments.</summary>
    /// <remarks>This is a lexical check. Callers that disallow symbolic links must check the physical entries too.</remarks>
    public string ResolveChildPath(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (string.IsNullOrWhiteSpace(relative) || IsPathRooted(relative) ||
            relative.AsSpan().HasInvalidPathSegment(colon: true))
            throw new InvalidDataException($"Child path must remain relative: {relative}");
        root = TrimEndingDirectorySeparator(GetFullPath(root));
        var result = GetFullPath(Combine(root, relative.Replace('\\', DirectorySeparatorChar).Replace('/', DirectorySeparatorChar)));
        var prefix = root.EndsWith(DirectorySeparatorChar) ? root : root + DirectorySeparatorChar;
        if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Child path escapes its root: {relative}");
        return result;
    }

    public string? GetDirectoryName(string path) =>
        _implementation.GetDirectoryName(path);

    public string GetFileName(string path) =>
        _implementation.GetFileName(path);

    public string GetFileNameWithoutExtension(string path) =>
        _implementation.GetFileNameWithoutExtension(path);

    public string GetExtension(string path) =>
        _implementation.GetExtension(path);

    public bool HasExtension(string path) => GetExtension(path).Length > 0;

    public string ChangeExtension(string path, string extension)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(extension);
        if (extension.Length > 0 && extension[0] != '.') extension = "." + extension;
        var currentExtension = GetExtension(path);
        return path[..(path.Length - currentExtension.Length)] + extension;
    }

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
