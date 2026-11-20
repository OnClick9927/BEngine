using BEngine.Platform;
using BEngine.Hosting;

namespace BEngine.Editor.Build;

/// <summary>Validates the shared Player publication layout without owning content or writing files.</summary>
public static class PlayerOutputValidation
{
    public static void ValidateLowercaseTree(
        string rootDirectory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var root = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(rootDirectory));
        if (!fileSystem.DirectoryExists(root))
            throw new DirectoryNotFoundException($"Build output was not found: '{root}'.");
        ValidateDirectory(root, root, fileSystem, pathService);
    }

    private static void ValidateDirectory(
        string root,
        string directory,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        foreach (var path in fileSystem.EnumerateFileSystemEntries(directory))
        {
            var name = pathService.GetFileName(path);
            // StreamingAssets is a public raw-file boundary: its canonical folder and
            // project-owned payload retain their original case on every platform.
            if (name.Equals(PlayerBuildLayout.StreamingAssetsDirectoryName, StringComparison.Ordinal) &&
                (directory.Equals(root, StringComparison.Ordinal) ||
                 pathService.GetDirectoryName(directory)?.Equals(root, StringComparison.Ordinal) == true) &&
                pathService.GetFileName(directory).EndsWith(
                    HostPackagedResourceAddresses.DataDirectorySuffix, StringComparison.Ordinal) &&
                fileSystem.DirectoryExists(path) &&
                (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                continue;
            if (!name.Equals(name.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Packaged file and directory names must be lowercase: " +
                    $"'{pathService.GetRelativePath(root, path)}'.");
            if (fileSystem.DirectoryExists(path))
                ValidateDirectory(root, path, fileSystem, pathService);
        }
    }
}
