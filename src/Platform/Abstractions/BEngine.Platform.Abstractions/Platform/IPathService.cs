namespace BEngine.Platform;

public interface IPathService
{
    char DirectorySeparatorChar { get; }
    char AltDirectorySeparatorChar { get; }
    char PathSeparator { get; }
    string Combine(params string[] paths);
    string GetFullPath(string path);
    string GetFullPath(string path, string basePath);
    string? GetDirectoryName(string path);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string GetExtension(string path);
    string GetRelativePath(string relativeTo, string path);
    char[] GetInvalidFileNameChars();
    string TrimEndingDirectorySeparator(string path);
    bool IsPathRooted(string path);
    bool IsPathFullyQualified(string path);
}
