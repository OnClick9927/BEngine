using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

/// <summary>Copies project raw files without importing them into a resource archive.</summary>
internal static class PlayerStreamingAssetsBuilder
{
    internal static void Copy(
        ProjectWorkspace workspace,
        PlayerBuildLayout layout,
        FileUtility files,
        PathUtility paths,
        CancellationToken cancellationToken)
    {
        var source = paths.Combine(workspace.AssetsPath, PlayerBuildLayout.StreamingAssetsDirectoryName);
        if (!files.DirectoryExists(source)) return;
        CopyDirectory(source, layout.StreamingAssetsDirectory, files, paths, cancellationToken);
    }

    private static void CopyDirectory(
        string source,
        string destination,
        FileUtility files,
        PathUtility paths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePhysicalEntry(source, files);
        files.CreateDirectory(destination);
        foreach (var entry in files.EnumerateFileSystemEntries(source).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = paths.GetFileName(entry);
            if (ProjectAssetPathFilter.IsIgnored(entry)) continue;
            if (files.DirectoryExists(entry))
            {
                if (name.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
                    name is ".git" or ".svn" or ".hg" or "__MACOSX") continue;
                CopyDirectory(entry, paths.Combine(destination, name), files, paths, cancellationToken);
            }
            else
            {
                EnsurePhysicalEntry(entry, files);
                files.CopyFile(entry, paths.Combine(destination, name));
            }
        }
    }

    private static void EnsurePhysicalEntry(string path, FileUtility files)
    {
        if ((files.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"StreamingAssets cannot contain linked files or directories: '{path}'.");
    }
}
