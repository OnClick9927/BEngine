using System.Runtime.CompilerServices;
using BEngine.Content;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

/// <summary>Opens Player deployment formats and keeps their metadata outside the base workspace.</summary>
public static class PlayerWorkspace
{
    private static readonly ConditionalWeakTable<ProjectWorkspace, RuntimeMetadataDocument> Metadata = new();
    private static readonly ConditionalWeakTable<ProjectWorkspace, ContentEncryptionKey> EncryptionKeys = new();

    public static ContentEncryptionKey? GetContentEncryptionKey(this ProjectWorkspace workspace) =>
        EncryptionKeys.TryGetValue(workspace, out var key) ? key : null;

    public static RuntimeMetadataDocument? GetRuntimeMetadata(this ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Metadata.TryGetValue(workspace, out var metadata) ? metadata : null;
    }

    public static ProjectWorkspace OpenRuntime(string path) =>
        OpenRuntime(path, RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService);

    public static ProjectWorkspace OpenRuntime(string path, FileUtility files, PathUtility paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(paths);
        var fullPath = paths.GetFullPath(path);
        var isDirectory = files.DirectoryExists(fullPath);
        if (isDirectory)
        {
            var archivePath = paths.Combine(fullPath,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                PlayerPackagedResourceAddresses.PlayerArchiveFileName);
            if (files.FileExists(archivePath))
            {
                var archive = BuiltInResourceArchive.Open(archivePath, files, paths);
                var metadata = RuntimeMetadataSerializer.Deserialize(
                    archive.ReadBytes(PlayerPackagedResourceAddresses.RuntimeMetadata));
                return Create(fullPath, metadata, files, paths, archive.EncryptionKey);
            }
        }
        var metadataPath = isDirectory ? paths.Combine(fullPath, RuntimeMetadataSerializer.FileName) : fullPath;
        if (files.FileExists(metadataPath) &&
            paths.GetFileName(metadataPath).Equals(RuntimeMetadataSerializer.FileName, StringComparison.OrdinalIgnoreCase))
        {
            var metadata = RuntimeMetadataSerializer.Load(metadataPath, files, paths);
            var root = paths.GetDirectoryName(metadataPath) ??
                throw new InvalidDataException("Runtime metadata root is invalid.");
            return Create(root, metadata, files, paths);
        }
        return ProjectWorkspace.OpenRuntime(path, files, paths);
    }

    private static ProjectWorkspace Create(string root, RuntimeMetadataDocument metadata,
        FileUtility files, PathUtility paths, ContentEncryptionKey? encryptionKey = null)
    {
        var workspace = ProjectWorkspace.CreateRuntime(root, metadata.Project, metadata.ProjectSettings, files, paths);
        Metadata.Add(workspace, metadata);
        if (encryptionKey is not null) EncryptionKeys.Add(workspace, encryptionKey);
        return workspace;
    }
}
