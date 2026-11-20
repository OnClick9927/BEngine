using BEngine.Hosting;
using BEngine.ProjectSystem;
using System.Runtime.CompilerServices;
using BEngine.Content;
using BEngine.Player.Content;
using BEngine.Platform;

namespace BEngine.Player.ProjectSystem;

/// <summary>Opens Player deployment formats and keeps their metadata outside the base workspace.</summary>
public static class PlayerWorkspace
{
    private static readonly ConditionalWeakTable<RuntimeWorkspace, RuntimeMetadataDocument> Metadata = new();
    private static readonly ConditionalWeakTable<RuntimeWorkspace, ContentEncryptionKey> EncryptionKeys = new();
    private static readonly ConditionalWeakTable<RuntimeWorkspace, BuiltInResourceArchive> Archives = new();

    public static ContentEncryptionKey? GetContentEncryptionKey(this RuntimeWorkspace workspace) =>
        EncryptionKeys.TryGetValue(workspace, out var key) ? key : null;

    public static RuntimeMetadataDocument? GetRuntimeMetadata(this RuntimeWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Metadata.TryGetValue(workspace, out var metadata) ? metadata : null;
    }

    internal static BuiltInResourceArchive? GetPlayerResourceArchive(this RuntimeWorkspace workspace) =>
        Archives.TryGetValue(workspace, out var archive) ? archive : null;

    internal static RuntimeWorkspace ReuseOrOpenRuntime(
        RuntimeWorkspace? workspace, string path, FileUtility files, PathUtility paths)
    {
        var fullPath = paths.GetFullPath(path);
        if (workspace is not null && workspace.RootPath.Equals(fullPath, StringComparison.Ordinal) &&
            workspace.GetPlayerResourceArchive() is { } archive &&
            archive.IsCurrentSource(archive.SourcePath, files, paths))
            return workspace;
        return OpenRuntime(fullPath, files, paths);
    }

    public static RuntimeWorkspace OpenRuntime(string path) =>
        OpenRuntime(path, RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>(), RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>());

    public static RuntimeWorkspace OpenRuntime(string path, FileUtility files, PathUtility paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(paths);
        var fullPath = paths.GetFullPath(path);
        var isDirectory = files.DirectoryExists(fullPath);
        if (isDirectory)
        {
            var archivePath = paths.Combine(fullPath,
                HostPackagedResourceAddresses.ResourcesDirectoryName,
                HostPackagedResourceAddresses.PlayerArchiveFileName);
            if (files.FileExists(archivePath))
            {
                var archive = BuiltInResourceArchive.Open(archivePath, files, paths);
                var metadata = RuntimeMetadataSerializer.Deserialize(
                    archive.ReadBytes(HostPackagedResourceAddresses.RuntimeMetadata));
                return Create(fullPath, metadata, files, paths, archive.EncryptionKey, archive);
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
        return LegacyProjectWorkspace.Open(path, files, paths);
    }

    private static RuntimeWorkspace Create(string root, RuntimeMetadataDocument metadata,
        FileUtility files, PathUtility paths, ContentEncryptionKey? encryptionKey = null,
        BuiltInResourceArchive? archive = null)
    {
        var workspace = RuntimeWorkspace.CreateRuntime(root, metadata.Project, metadata.ProjectSettings, files, paths);
        Metadata.Add(workspace, metadata);
        if (encryptionKey is not null) EncryptionKeys.Add(workspace, encryptionKey);
        if (archive is not null) Archives.Add(workspace, archive);
        return workspace;
    }
}
