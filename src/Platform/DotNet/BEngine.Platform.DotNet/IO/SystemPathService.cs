namespace BEngine.Platform;

public sealed class SystemPathService : IPathService
{
    public static SystemPathService Shared { get; } = new();

    public char DirectorySeparatorChar => Path.DirectorySeparatorChar;
    public char AltDirectorySeparatorChar => Path.AltDirectorySeparatorChar;
    public char PathSeparator => Path.PathSeparator;
    public string Combine(params string[] paths) => Path.Combine(paths);
    public string GetFullPath(string path) => Path.GetFullPath(path);
    public string GetFullPath(string path, string basePath) => Path.GetFullPath(path, basePath);
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetExtension(string path) => Path.GetExtension(path);
    public string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);
    public char[] GetInvalidFileNameChars() => Path.GetInvalidFileNameChars();
    public string TrimEndingDirectorySeparator(string path) => Path.TrimEndingDirectorySeparator(path);
    public bool IsPathRooted(string path) => Path.IsPathRooted(path);
    public bool IsPathFullyQualified(string path) => Path.IsPathFullyQualified(path);
}
