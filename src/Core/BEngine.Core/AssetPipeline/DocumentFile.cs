namespace BEngine;

/// <summary>Source-document persistence, preserving atomic replacement and failure cleanup.</summary>
internal static class DocumentFile
{
    internal static void Write(string path, Func<string> serialize)
    {
        var files = RuntimeServices.GetRequiredService<Platform.FileUtility>();
        var paths = RuntimeServices.GetRequiredService<Platform.PathUtility>();
        var fullPath = paths.GetFullPath(path);
        var directory = paths.GetDirectoryName(fullPath) ??
            RuntimeServices.GetRequiredService<Platform.PlatformUtility>().CurrentDirectory;
        files.CreateDirectory(directory);
        var temporaryPath = paths.Combine(directory, $".{paths.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            files.WriteAllText(temporaryPath, serialize());
            files.MoveFile(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (files.FileExists(temporaryPath)) files.DeleteFile(temporaryPath);
        }
    }
}
