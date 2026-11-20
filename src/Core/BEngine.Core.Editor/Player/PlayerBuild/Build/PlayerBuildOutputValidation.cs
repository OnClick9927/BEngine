using BEngine.Hosting;
using BEngine.Platform;

namespace BEngine.Editor.Build;

/// <summary>Validates the engine data tree without interpreting the content provider's resource format.</summary>
public static class PlayerBuildOutputValidation
{
    public static void ValidateResourceDirectories(PlayerBuildLayout layout,
        IReadOnlyCollection<string> resourceFileNames, FileUtility files, PathUtility paths)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(resourceFileNames);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(paths);
        var expectedData = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HostPackagedResourceAddresses.NativeLibraryDirectoryName,
            HostPackagedResourceAddresses.ResourcesDirectoryName
        };
        if (files.DirectoryExists(layout.StreamingAssetsDirectory))
            expectedData.Add(PlayerBuildLayout.StreamingAssetsDirectoryName);
        ValidateEntries(layout.DataDirectory, expectedData, directories: true, files, paths);
        ValidateEntries(layout.ResourcesDirectory,
            new HashSet<string>(resourceFileNames, StringComparer.OrdinalIgnoreCase),
            directories: false, files, paths);
    }

    private static void ValidateEntries(string directory, HashSet<string> remaining,
        bool directories, FileUtility files, PathUtility paths)
    {
        foreach (var entry in files.EnumerateFileSystemEntries(directory))
        {
            if (!remaining.Remove(paths.GetFileName(entry)) ||
                !(directories ? files.DirectoryExists(entry) : files.FileExists(entry)))
                throw new InvalidDataException($"Unexpected Player output entry: '{entry}'.");
        }
        if (remaining.Count != 0)
            throw new InvalidDataException($"Missing Player output entries in '{directory}': {string.Join(", ", remaining.Order(StringComparer.Ordinal))}.");
    }
}
