using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>
/// Stores generated file sub-assets in the source owner's Library. Callers own
/// content formats and importer settings; this service owns paths, identity and rollback.
/// It can be used by import tools without an editor window or active project host.
/// </summary>
public static class GeneratedAssetArtifacts
{
    private static readonly Lock TransactionGate = new();
    private static FileUtility FileSystem => EditorCapabilities.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorCapabilities.GetRequiredService<PathUtility>();

    /// <summary>Publishes bytes and sub-asset metadata, restoring the previous pair on failure.</summary>
    public static GeneratedAssetArtifact Write(
        string ownerSourcePath,
        long localIdentifier,
        string extension,
        byte[] contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(contents);
        extension = NormalizeExtension(extension);
        var ownerPath = Paths.GetFullPath(ownerSourcePath);
        var projectRoot = FindProjectRoot(ownerPath);
        if (!FileSystem.FileExists(ownerPath))
            throw new FileNotFoundException("The generated asset owner source does not exist.", ownerPath);
        var ownerMeta = AssetDatabase.LoadOrCreateMeta(ownerPath);
        if (!Guid.TryParse(ownerMeta.Guid, out var ownerGuid))
            throw new InvalidDataException($"Asset '{ownerPath}' does not have a valid GUID.");
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

    /// <summary>Removes generated files for one owner/local identity, retaining the owner source and metadata.</summary>
    public static void Delete(string ownerSourcePath, long localIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSourcePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localIdentifier);
        var ownerPath = Paths.GetFullPath(ownerSourcePath);
        var projectRoot = FindProjectRoot(ownerPath);
        var ownerMetaPath = ownerPath + ".meta";
        if (!FileSystem.FileExists(ownerMetaPath)) return;
        var ownerMeta = YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
            ownerMetaPath, FileSystem, Paths);
        if (!Guid.TryParse(ownerMeta.Guid, out var ownerGuid))
            throw new InvalidDataException($"Asset '{ownerPath}' does not have a valid GUID.");
        var artifactsRoot = Paths.Combine(projectRoot, "Library", "Artifacts");
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
        throw new InvalidDataException(
                   $"Generated asset '{ownerPath}' is not inside an Assets or Packages directory.");
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (!extension.StartsWith('.')) extension = "." + extension;
        if (extension.Length < 2 || Matches6299())
            throw new ArgumentException($"Invalid generated artifact extension '{extension}'.", nameof(extension));
        [global::BEngine.SkipLoopGuard]
        bool Matches6299()
        {
            var matchSource6299 = extension.Skip(1);
            ArgumentNullException.ThrowIfNull(matchSource6299, "source");
            global::System.ReadOnlySpan<char> matchValues6299;
            if (matchSource6299.GetType() == typeof(char[]))
            {
                matchValues6299 = (char[])(object)matchSource6299;
            }
            else if (matchSource6299.GetType() == typeof(global::System.Collections.Generic.List<char>))
            {
                matchValues6299 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<char>)(object)matchSource6299);
            }
            else
            {
                foreach (var character in (global::System.Collections.Generic.IEnumerable<char>)matchSource6299)
                {
                    if (!char.IsLetterOrDigit(character))
                        return true;
                }
        
                return false;
            }
        
            foreach (var character in matchValues6299)
            {
                if (!char.IsLetterOrDigit(character))
                    return true;
            }
        
            return false;
        }
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
