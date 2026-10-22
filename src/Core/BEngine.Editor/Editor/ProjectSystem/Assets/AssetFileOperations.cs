using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

internal static class AssetFileOperations
{
    internal static string Copy(string sourcePath, string targetPath)
        => Copy(sourcePath, targetPath,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string Copy(
        string sourcePath,
        string targetPath,
        FileUtility fileSystem,
        PathUtility paths)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            var source = paths.GetFullPath(sourcePath);
            var target = paths.GetFullPath(targetPath);
            var sourceIsFile = fileSystem.FileExists(source);
            if (!sourceIsFile && !fileSystem.DirectoryExists(source)) return $"Asset not found: {source}";
            if (fileSystem.FileExists(target) || fileSystem.DirectoryExists(target))
                return $"Destination already exists: {target}";
            if (fileSystem.DirectoryExists(source) && target.StartsWith(source + paths.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return "A folder cannot be copied into itself.";

            fileSystem.CreateDirectory(paths.GetDirectoryName(target)!);
            if (sourceIsFile)
            {
                fileSystem.CopyFile(source, target);
                return string.Empty;
            }

            fileSystem.CreateDirectory(target);
            foreach (var directory in fileSystem.EnumerateDirectories(
                         source, "*", SearchOption.AllDirectories))
                fileSystem.CreateDirectory(paths.Combine(target, paths.GetRelativePath(source, directory)));
            foreach (var file in fileSystem.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .Where(static file => !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var destination = paths.Combine(target, paths.GetRelativePath(source, file));
                fileSystem.CreateDirectory(paths.GetDirectoryName(destination)!);
                fileSystem.CopyFile(file, destination);
            }
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return exception.Message;
        }
    }

    internal static string Move(string sourcePath, string targetPath)
        => Move(sourcePath, targetPath,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string Move(
        string sourcePath,
        string targetPath,
        FileUtility fileSystem,
        PathUtility paths)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            var source = paths.GetFullPath(sourcePath);
            var target = paths.GetFullPath(targetPath);
            var sourceIsFile = fileSystem.FileExists(source);
            if (!sourceIsFile && !fileSystem.DirectoryExists(source)) return $"Asset not found: {source}";
            if (fileSystem.FileExists(target) || fileSystem.DirectoryExists(target))
                return $"Destination already exists: {target}";
            if (fileSystem.DirectoryExists(source) && target.StartsWith(source + paths.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return "A folder cannot be moved into itself.";

            var sourceMetadata = source + ".meta";
            var targetMetadata = target + ".meta";
            if (fileSystem.FileExists(sourceMetadata) && fileSystem.FileExists(targetMetadata))
                return $"Destination metadata already exists: {targetMetadata}";

            fileSystem.CreateDirectory(paths.GetDirectoryName(target)!);
            if (sourceIsFile) fileSystem.MoveFile(source, target);
            else fileSystem.MoveDirectory(source, target);
            try
            {
                if (fileSystem.FileExists(sourceMetadata))
                    fileSystem.MoveFile(sourceMetadata, targetMetadata);
            }
            catch
            {
                if (sourceIsFile) fileSystem.MoveFile(target, source);
                else fileSystem.MoveDirectory(target, source);
                throw;
            }

            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return exception.Message;
        }
    }
}
