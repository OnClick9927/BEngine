using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Stores editor-generated sub-assets under the owning project's Library.</summary>
internal static class GeneratedAssetArtifacts
{
    private static readonly Lock TransactionGate = new();
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();

    internal static GeneratedAssetArtifact Write(
        string ownerSourcePath,
        long localIdentifier,
        string extension,
        byte[] contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(contents);

        var ownerPath = Paths.GetFullPath(ownerSourcePath);
        var ownerMeta = AssetDatabase.LoadOrCreateMeta(ownerPath);
        if (!Guid.TryParse(ownerMeta.Guid, out var ownerGuid))
            throw new InvalidDataException($"Asset '{ownerPath}' does not have a valid GUID.");
        extension = NormalizeExtension(extension);
        var projectRoot = FindProjectRoot(ownerPath);
        var artifactDirectory = Paths.Combine(projectRoot, "Library", "Artifacts",
            ownerGuid.ToString("N")[..2]);
        var artifactPath = Paths.Combine(artifactDirectory,
            $"{ownerGuid:N}.{localIdentifier}{extension}");
        lock (TransactionGate)
        {
            FileSystem.CreateDirectory(artifactDirectory);
            var snapshots = CaptureSnapshots(artifactPath, artifactPath + ".meta");
            var committed = false;
            try
            {
                WriteAtomically(artifactPath, contents);
                AssetDatabase.RegisterFileSubAsset(artifactPath, ownerPath, localIdentifier);
                committed = true;
            }
            catch (Exception exception)
            {
                var rollbackFailures = RollbackSnapshots(snapshots);
                if (rollbackFailures.Count > 0)
                    throw new AggregateException(
                        $"Writing generated sub-asset '{artifactPath}' failed and could not be fully rolled back.",
                        [exception, .. rollbackFailures]);
                throw;
            }
            finally
            {
                if (committed) DiscardSnapshots(snapshots);
            }
        }
        BAsset.Invalidate(ownerPath);
        return new GeneratedAssetArtifact(
            ownerGuid,
            localIdentifier,
            $"guid:{ownerGuid:N}#subasset={localIdentifier}",
            artifactPath);
    }

    internal static void Delete(string ownerSourcePath, long localIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localIdentifier);
        var ownerPath = Paths.GetFullPath(ownerSourcePath);
        var ownerMetaPath = ownerPath + ".meta";
        if (!FileSystem.FileExists(ownerMetaPath)) return;
        var ownerMeta = YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            ownerMetaPath, FileSystem, Paths);
        if (!Guid.TryParse(ownerMeta.Guid, out var ownerGuid))
            throw new InvalidDataException($"Asset '{ownerPath}' does not have a valid GUID.");
        var artifactsRoot = Paths.Combine(FindProjectRoot(ownerPath), "Library", "Artifacts");
        if (!FileSystem.DirectoryExists(artifactsRoot)) return;
        lock (TransactionGate)
        {
            var filesToDelete = new List<string>();
            foreach (var metaPath in FileSystem.EnumerateFiles(
                         artifactsRoot, "*.meta", SearchOption.AllDirectories))
            {
                BEngine.ProjectSystem.Editor.AssetMetaDocument child;
                try
                {
                    child = YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
                        metaPath, FileSystem, Paths);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                   InvalidDataException or FormatException or
                                                   YamlDotNet.Core.YamlException)
                {
                    continue;
                }
                if (child.LocalIdentifier != localIdentifier ||
                    !Guid.TryParse(child.ParentGuid, out var parentGuid) || parentGuid != ownerGuid) continue;
                filesToDelete.Add(metaPath);
                filesToDelete.Add(metaPath[..^".meta".Length]);
            }

            DeleteTransactionally(filesToDelete);
        }
        BAsset.Invalidate(ownerPath);
    }

    internal static bool IsGeneratedPath(string path, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(projectRoot)) return false;
        var candidate = Paths.GetFullPath(path);
        var artifacts = Paths.GetFullPath(Paths.Combine(projectRoot, "Library", "Artifacts"));
        return candidate.StartsWith(artifacts + Paths.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindProjectRoot(string ownerPath)
    {
        for (var directory = Paths.GetDirectoryName(ownerPath); directory is not null;
             directory = Paths.GetDirectoryName(directory))
        {
            var name = Paths.GetFileName(Paths.TrimEndingDirectorySeparator(directory));
            if (name.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Packages", StringComparison.OrdinalIgnoreCase))
                return Paths.GetDirectoryName(directory) ?? directory;
        }
        return EditorBridge.Host?.ProjectRootPath ??
               throw new InvalidDataException(
                   $"Generated asset '{ownerPath}' is not inside an Assets or Packages directory.");
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (!extension.StartsWith('.')) extension = "." + extension;
        if (extension.Length < 2 || extension.Skip(1).Any(character => !char.IsLetterOrDigit(character)))
            throw new ArgumentException($"Invalid generated artifact extension '{extension}'.", nameof(extension));
        return extension.ToLowerInvariant();
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporary = path + $".{Guid.NewGuid():N}.importing";
        try
        {
            FileSystem.WriteAllBytes(temporary, contents);
            FileSystem.MoveFile(temporary, path, overwrite: true);
        }
        finally
        {
            if (FileSystem.FileExists(temporary)) FileSystem.DeleteFile(temporary);
        }
    }

    private static List<FileSnapshot> CaptureSnapshots(params string[] paths)
    {
        var snapshots = new List<FileSnapshot>(paths.Length);
        try
        {
            foreach (var path in paths) snapshots.Add(FileSnapshot.Capture(path));
            return snapshots;
        }
        catch
        {
            DiscardSnapshots(snapshots);
            throw;
        }
    }

    private static List<Exception> RollbackSnapshots(IReadOnlyList<FileSnapshot> snapshots)
    {
        var failures = new List<Exception>();
        for (var index = snapshots.Count - 1; index >= 0; index--)
            try { snapshots[index].Restore(); }
            catch (Exception exception) { failures.Add(exception); }
        return failures;
    }

    private static void DiscardSnapshots(IEnumerable<FileSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots) snapshot.Discard();
    }

    private static void DeleteTransactionally(IEnumerable<string> paths)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var staged = new List<StagedFile>();
        try
        {
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!FileSystem.FileExists(path)) continue;
                var stagedPath = $"{path}.{transactionId}.deleting";
                FileSystem.MoveFile(path, stagedPath);
                staged.Add(new StagedFile(path, stagedPath));
            }
        }
        catch (Exception exception)
        {
            var rollbackFailures = new List<Exception>();
            for (var index = staged.Count - 1; index >= 0; index--)
                try { FileSystem.MoveFile(staged[index].StagedPath, staged[index].OriginalPath); }
                catch (Exception rollbackException) { rollbackFailures.Add(rollbackException); }
            if (rollbackFailures.Count > 0)
                throw new AggregateException(
                    "Deleting generated sub-assets failed and could not be fully rolled back.",
                    [exception, .. rollbackFailures]);
            throw;
        }

        // Once every source has been staged, the active asset and metadata are gone together.
        // Cleanup failures only leave unindexed transaction files and must not report a false rollback.
        foreach (var file in staged)
            try { FileSystem.DeleteFile(file.StagedPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed class FileSnapshot
    {
        private readonly bool _existed;
        private string? _backupPath;

        private FileSnapshot(string path, bool existed, string? backupPath)
        {
            Path = path;
            _existed = existed;
            _backupPath = backupPath;
        }

        private string Path { get; }

        internal static FileSnapshot Capture(string path)
        {
            if (!FileSystem.FileExists(path)) return new FileSnapshot(path, false, null);
            var backupPath = $"{path}.{Guid.NewGuid():N}.rollback";
            FileSystem.CopyFile(path, backupPath);
            return new FileSnapshot(path, true, backupPath);
        }

        internal void Restore()
        {
            if (!_existed)
            {
                if (FileSystem.FileExists(Path)) FileSystem.DeleteFile(Path);
                return;
            }
            if (_backupPath is null || !FileSystem.FileExists(_backupPath))
                throw new IOException($"Rollback data for '{Path}' is unavailable.");
            FileSystem.MoveFile(_backupPath, Path, overwrite: true);
            _backupPath = null;
        }

        internal void Discard()
        {
            if (_backupPath is null) return;
            try { FileSystem.DeleteFile(_backupPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            _backupPath = null;
        }
    }

    private readonly record struct StagedFile(string OriginalPath, string StagedPath);
}
