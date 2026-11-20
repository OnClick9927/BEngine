using System.Globalization;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

internal sealed class ProjectPackageCache
{
    private const string StagingPrefix = ".staging-";
    private const string BackupPrefix = ".backup-";
    private const string InPlaceBackupMarker = ".package-cache-inplace-backup";
    private const string LockFileName = ".package-cache.lock";
    private readonly string _rootPath;
    private readonly string _legacyRootPath;
    private readonly string _lockPath;

    internal ProjectPackageCache(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _rootPath = GetCacheRoot(workspace);
        _legacyRootPath = workspace.PackagesPath;
        _lockPath = EditorHostServices.Paths.Combine(workspace.LibraryPath, LockFileName);
        EditorHostServices.FileSystem.CreateDirectory(_rootPath);
        using var cacheLock = AcquireLock();
        MigrateLegacyCache();
        CleanupInterruptedCopies();
    }

    internal static string GetCacheRoot(ProjectWorkspace workspace) =>
        workspace.PackageCachePath;

    internal string GetInstalledDefinitionPath(string packageId) =>
        EditorHostServices.Paths.Combine(_rootPath, CacheFolderName(packageId), "package.yaml");

    internal void Synchronize(
        BPackageCatalog catalog,
        IEnumerable<string> enabledPackageIds,
        Action<IReadOnlyDictionary<string, BPackageDefinition>> apply)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(enabledPackageIds);
        ArgumentNullException.ThrowIfNull(apply);
        var enabled = ((((global::System.Collections.Generic.IEnumerable<string>)(enabledPackageIds)), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems1571, distinctComparer1571) => distinctItems1571.ToHashSet<string>(distinctComparer1571).ToArray()});
        using var cacheLock = AcquireLock();
        var replacements = new List<CacheReplacement>();
        try
        {
            var installed = InstallEnabled(catalog, enabled, replacements);
            apply(installed);
        }
        catch (Exception failure)
        {
            List<Exception>? rollbackFailures = null;
            for (var index = replacements.Count - 1; index >= 0; index--)
            {
                var replacement = replacements[index];
                try
                {
                    if (replacement.InPlace)
                    {
                        ReplaceDirectoryContents(replacement.Backup, replacement.Target);
                        EditorHostServices.FileSystem.DeleteDirectory(replacement.Backup, true);
                    }
                    else
                    {
                        if (EditorHostServices.FileSystem.DirectoryExists(replacement.Target))
                            EditorHostServices.FileSystem.DeleteDirectory(replacement.Target, true);
                        if (EditorHostServices.FileSystem.DirectoryExists(replacement.Backup))
                            EditorHostServices.FileSystem.MoveDirectory(replacement.Backup, replacement.Target);
                    }
                }
                catch (Exception rollbackFailure) { (rollbackFailures ??= []).Add(rollbackFailure); }
            }
            if (rollbackFailures is not null)
                throw new AggregateException("Package synchronization and rollback failed; recovery backups were retained.",
                    new[] { failure }.Concat(rollbackFailures));
            throw;
        }
        foreach (var replacement in replacements)
        {
            // Removing the marker commits an in-place transaction. A cleanup failure must
            // never cause the next startup to restore an already committed old snapshot.
            if (replacement.InPlace)
                EditorHostServices.FileSystem.DeleteFile(EditorHostServices.Paths.Combine(replacement.Backup, InPlaceBackupMarker));
            if (EditorHostServices.FileSystem.DirectoryExists(replacement.Backup))
                EditorHostServices.FileSystem.DeleteDirectory(replacement.Backup, true);
        }
        RemoveDisabled(enabled);
    }

    internal void RemoveRetired(IEnumerable<string> packageIds)
    {
        ArgumentNullException.ThrowIfNull(packageIds);
        using var cacheLock = AcquireLock();
        var roots = new[]
        {
            _rootPath,
            _legacyRootPath
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        foreach (var packageId in packageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = EditorHostServices.Paths.Combine(root, CacheFolderName(packageId));
            if (EditorHostServices.FileSystem.DirectoryExists(directory)) EditorHostServices.FileSystem.DeleteDirectory(directory, true);
        }
    }

    private IReadOnlyDictionary<string, BPackageDefinition> InstallEnabled(
        BPackageCatalog catalog,
        IEnumerable<string> enabledPackageIds,
        List<CacheReplacement> replacements)
    {
        var installed = new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in enabledPackageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cachedDefinitionPath = catalog.TryGet(packageId, out var definition)
                ? Install(definition, replacements)
                : GetInstalledDefinitionPath(packageId);
            if (!EditorHostServices.FileSystem.FileExists(cachedDefinitionPath))
                throw new FileNotFoundException(
                    $"Enabled package '{packageId}' is missing from both the project cache and the engine catalog.",
                    cachedDefinitionPath);
            var cachedDocument = PackageDefinitionLoader.Load(cachedDefinitionPath);
            if (!cachedDocument.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Cached package '{cachedDefinitionPath}' changed id from '{packageId}' to '{cachedDocument.Id}'.");
            installed.Add(packageId, new BPackageDefinition(cachedDefinitionPath, cachedDocument));
        }
        return installed;
    }

    private void RemoveDisabled(IEnumerable<string> enabledPackageIds)
    {
        ArgumentNullException.ThrowIfNull(enabledPackageIds);
        var enabledFolders = enabledPackageIds.Select(CacheFolderName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in EditorHostServices.FileSystem.EnumerateDirectories(_rootPath, "*", SearchOption.TopDirectoryOnly))
        {
            var name = EditorHostServices.Paths.GetFileName(directory);
            if (name.StartsWith('.') || enabledFolders.Contains(name)) continue;
            EditorHostServices.FileSystem.DeleteDirectory(directory, true);
        }
    }

    private string Install(BPackageDefinition definition, List<CacheReplacement> replacements)
    {
        var sourceRoot = EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(definition.Path))!;
        var targetRoot = EditorHostServices.Paths.Combine(_rootPath, CacheFolderName(definition.Document.Id));
        if (PathsEqual(sourceRoot, targetRoot) || DirectoriesMatch(sourceRoot, targetRoot))
            return EditorHostServices.Paths.Combine(targetRoot, "package.yaml");

        var staging = EditorHostServices.Paths.Combine(_rootPath, $"{StagingPrefix}{Guid.NewGuid():N}");
        var backup = EditorHostServices.Paths.Combine(_rootPath,
            $"{BackupPrefix}{EditorHostServices.Platform.UtcNow.UtcTicks:D19}-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(sourceRoot, staging);
            var stagedDefinition = EditorHostServices.Paths.Combine(staging, "package.yaml");
            if (!EditorHostServices.FileSystem.FileExists(stagedDefinition))
                throw new FileNotFoundException("The package cache copy does not contain package.yaml.", stagedDefinition);
            var stagedDocument = PackageDefinitionLoader.Load(stagedDefinition);
            if (!stagedDocument.Id.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase) ||
                stagedDocument.Type != definition.Document.Type)
                throw new InvalidDataException("The staged package identity changed during copying.");

            if (EditorHostServices.FileSystem.DirectoryExists(targetRoot))
            {
                try { EditorHostServices.FileSystem.MoveDirectory(targetRoot, backup); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException &&
                    EditorHostServices.FileSystem.DirectoryExists(targetRoot) &&
                    !EditorHostServices.FileSystem.DirectoryExists(backup))
                {
                    // IDE design-time builds can keep an obj directory or its mapped
                    // assemblies open. Keep that directory stable and transact only the
                    // actual package payload; generated bin/obj files are not payload.
                    CopyDirectory(targetRoot, backup);
                    EditorHostServices.FileSystem.WriteAllText(
                        EditorHostServices.Paths.Combine(backup, InPlaceBackupMarker), "1");
                    replacements.Add(new CacheReplacement(targetRoot, backup, true));
                    ReplaceDirectoryContents(staging, targetRoot);
                    return EditorHostServices.Paths.Combine(targetRoot, "package.yaml");
                }
            }
            try
            {
                EditorHostServices.FileSystem.MoveDirectory(staging, targetRoot);
                replacements.Add(new CacheReplacement(targetRoot, backup, false));
            }
            catch
            {
                if (!EditorHostServices.FileSystem.DirectoryExists(targetRoot) && EditorHostServices.FileSystem.DirectoryExists(backup)) EditorHostServices.FileSystem.MoveDirectory(backup, targetRoot);
                throw;
            }
            return EditorHostServices.Paths.Combine(targetRoot, "package.yaml");
        }
        finally
        {
            if (EditorHostServices.FileSystem.DirectoryExists(staging)) EditorHostServices.FileSystem.DeleteDirectory(staging, true);
            // If rollback itself failed, the backup may be the only surviving complete package.
            // Preserve it for the next startup unless the expected destination is valid.
            if (!replacements.Exists(item => item.Backup == backup) &&
                !EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(backup, InPlaceBackupMarker)) &&
                EditorHostServices.FileSystem.DirectoryExists(backup) && ReadPackageIdentity(targetRoot)is { } current && current.Type == definition.Document.Type && current.Id.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase))
                EditorHostServices.FileSystem.DeleteDirectory(backup, true);
        }
    }

    private void CleanupInterruptedCopies()
    {
        var fs = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var backups = new List<(string Path, PackageDefinitionDocument Document, long Timestamp)>();
        foreach (var directory in fs.EnumerateDirectories(_rootPath, ".*", SearchOption.TopDirectoryOnly).ToArray())
        {
            var name = paths.GetFileName(directory);
            if (name.StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                fs.DeleteDirectory(directory, true);
                continue;
            }
            if (!name.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            // A damaged or inaccessible backup cannot be identified safely. Retain it for repair.
            if (ReadPackageIdentity(directory) is not { } document) continue;
            var suffix = name.AsSpan(BackupPrefix.Length);
            var timestamp = suffix.Length > 19 && suffix[19] == '-' &&
                            long.TryParse(suffix[..19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                ? ticks
                : fs.GetLastWriteTimeUtc(directory).Ticks;
            backups.Add((directory, document, timestamp));
        }
        // New backups carry the actual transaction time. Legacy backups only have filesystem
        // modification time; use it as the available ordering evidence, with a stable tie-breaker.
        foreach (var backup in backups.OrderByDescending(item => item.Timestamp)
                     .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var destination = paths.Combine(_rootPath, CacheFolderName(backup.Document.Id));
            var current = ReadPackageIdentity(destination);
            if (fs.FileExists(paths.Combine(backup.Path, InPlaceBackupMarker)))
            {
                if (current is not null && (current.Type != backup.Document.Type ||
                    !current.Id.Equals(backup.Document.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Package recovery destination '{destination}' has a different identity. " +
                                                   $"Backup '{backup.Path}' was preserved.");
                ReplaceDirectoryContents(backup.Path, destination);
                fs.DeleteDirectory(backup.Path, true);
                continue;
            }
            if (current is not null)
            {
                if (current.Type != backup.Document.Type ||
                    !current.Id.Equals(backup.Document.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package recovery destination '{destination}' belongs to " +
                                                   $"'{current.Id}'. Backup '{backup.Path}' was preserved.");
                fs.DeleteDirectory(backup.Path, true);
                continue;
            }

            if (fs.DirectoryExists(destination) || fs.FileExists(destination))
            {
                var quarantine = paths.Combine(_rootPath, $".interrupted-{Guid.NewGuid():N}");
                if (fs.DirectoryExists(destination)) fs.MoveDirectory(destination, quarantine);
                else fs.MoveFile(destination, quarantine);
            }
            fs.MoveDirectory(backup.Path, destination);
        }
    }

    private void MigrateLegacyCache()
    {
        var fs = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        if (!fs.DirectoryExists(_legacyRootPath) ||
            PathsEqual(_legacyRootPath, _rootPath)) return;

        foreach (var directory in fs.EnumerateDirectories(_legacyRootPath, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            var name = paths.GetFileName(directory);
            if (name.StartsWith('.'))
            {
                fs.DeleteDirectory(directory, true);
                continue;
            }
            var target = paths.Combine(_rootPath, name);
            if (!fs.DirectoryExists(target))
            {
                fs.MoveDirectory(directory, target);
                continue;
            }

            // The old project-local cache is generated content. If a new Library cache
            // already exists, keep the canonical copy and remove the stale duplicate.
            fs.DeleteDirectory(directory, true);
        }

        foreach (var file in fs.EnumerateFiles(_legacyRootPath, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            if (paths.GetFileName(file).Equals("manifest.yaml", StringComparison.OrdinalIgnoreCase)) continue;
            var target = paths.Combine(_rootPath, paths.GetFileName(file));
            if (fs.FileExists(target)) fs.DeleteFile(file);
            else fs.MoveFile(file, target);
        }
    }

    private static PackageDefinitionDocument? ReadPackageIdentity(string directory)
    {
        var path = EditorHostServices.Paths.Combine(directory, "package.yaml");
        if (!EditorHostServices.FileSystem.FileExists(path)) return null;
        try { return PackageDefinitionLoader.Load(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException) { return null; }
    }

    private Stream AcquireLock()
    {
        EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(_lockPath)!);
        var path = _lockPath;
        var deadline = EditorHostServices.Platform.TickCount64 + 15_000;
        while (true)
        {
            try
            {
                return EditorHostServices.FileSystem.OpenFile(path, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (EditorHostServices.Platform.TickCount64 < deadline)
            {
                EditorHostServices.Wait.Wait(TimeSpan.FromMilliseconds(25));
            }
            catch (IOException exception)
            {
                throw new IOException($"Timed out waiting for the project package cache lock '{path}'.", exception);
            }
        }
    }

    private static string CacheFolderName(string packageId)
    {
        var invalid = EditorHostServices.Paths.GetInvalidFileNameChars();
        var value = string.Concat(packageId.Trim().Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character));
        if (value is "" or "." or "..") throw new InvalidDataException($"Invalid package id '{packageId}'.");
        return value;
    }

    private static bool DirectoriesMatch(string sourceRoot, string targetRoot)
    {
        if (!EditorHostServices.FileSystem.DirectoryExists(targetRoot)) return false;
        // Compare lazily: a changed/missing first file must not stat both complete
        // trees before starting the replacement. Matching trees still verify every
        // file's size and timestamp; no stamp survives this synchronization.
        var targetFiles = BuildIndex16014(EnumeratePackageFiles(targetRoot), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex16014(global::System.Collections.Generic.IEnumerable<string> indexSource16014, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer16014)
        {
            ArgumentNullException.ThrowIfNull(indexSource16014, "source");
            var knownCount = indexSource16014.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer16014);
            global::System.ReadOnlySpan<string> values;
            if (knownCount && indexSource16014 is string[] array)
                values = array;
            else if (knownCount && indexSource16014 is global::System.Collections.Generic.List<string> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer16014);
                foreach (var item in indexSource16014)
                    result.Add(IndexKey16014(item), IndexValue16014(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer16014);
                foreach (var item in values)
                    result.Add(IndexKey16014(item), IndexValue16014(item));
                return result;
            }
        
            string IndexKey16014(string path) => EditorHostServices.Paths.GetRelativePath(targetRoot, path);
            string IndexValue16014(string path) => path;
        }
        foreach (var source in EnumeratePackageFiles(sourceRoot))
        {
            var relativePath = EditorHostServices.Paths.GetRelativePath(sourceRoot, source);
            if (!targetFiles.Remove(relativePath, out var target) ||
                EditorHostServices.FileSystem.GetFileLength(source) != EditorHostServices.FileSystem.GetFileLength(target) ||
                (EditorHostServices.FileSystem.GetLastWriteTimeUtc(source) != EditorHostServices.FileSystem.GetLastWriteTimeUtc(target) &&
                 !FileContentsMatch(source, target)))
                return false;
        }
        return targetFiles.Count == 0;
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        EditorHostServices.FileSystem.CreateDirectory(targetRoot);
        foreach (var source in EnumeratePackageFiles(sourceRoot))
        {
            var target = EditorHostServices.Paths.Combine(targetRoot, EditorHostServices.Paths.GetRelativePath(sourceRoot, source));
            EditorHostServices.FileSystem.CreateDirectory(EditorHostServices.Paths.GetDirectoryName(target)!);
            EditorHostServices.FileSystem.CopyFile(source, target, true);
            EditorHostServices.FileSystem.SetLastWriteTimeUtc(target, EditorHostServices.FileSystem.GetLastWriteTimeUtc(source));
        }
    }

    private static void ReplaceDirectoryContents(string sourceRoot, string targetRoot)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var sourceFiles = EnumeratePackageFiles(sourceRoot).ToDictionary(
            file => paths.GetRelativePath(sourceRoot, file), StringComparer.OrdinalIgnoreCase);
        files.CreateDirectory(targetRoot);
        foreach (var target in EnumeratePackageFiles(targetRoot).ToArray())
            if (!sourceFiles.ContainsKey(paths.GetRelativePath(targetRoot, target))) files.DeleteFile(target);
        foreach (var (relative, source) in sourceFiles)
        {
            var target = paths.Combine(targetRoot, relative);
            // Do not overwrite unchanged DLLs that an IDE may have mapped.
            if (files.FileExists(target) && FileContentsMatch(source, target)) continue;
            files.CreateDirectory(paths.GetDirectoryName(target)!);
            files.CopyFile(source, target, true);
            files.SetLastWriteTimeUtc(target, files.GetLastWriteTimeUtc(source));
        }
    }

    private static bool FileContentsMatch(string source, string target)
    {
        var files = EditorHostServices.FileSystem;
        if (files.GetFileLength(source) != files.GetFileLength(target)) return false;
        using var left = files.OpenRead(source);
        using var right = files.OpenRead(target);
        Span<byte> leftBytes = stackalloc byte[4096];
        Span<byte> rightBytes = stackalloc byte[4096];
        while (true)
        {
            var count = left.Read(leftBytes);
            if (right.ReadAtLeast(rightBytes[..count], count, throwOnEndOfStream: false) != count ||
                !leftBytes[..count].SequenceEqual(rightBytes[..count])) return false;
            if (count == 0) return true;
        }
    }

    private sealed record CacheReplacement(string Target, string Backup, bool InPlace);

    private static IEnumerable<string> EnumeratePackageFiles(string root)
    {
        var files = EditorHostServices.FileSystem;
        var paths = EditorHostServices.Paths;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in files.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                if (!paths.GetFileName(file).Equals(InPlaceBackupMarker, StringComparison.OrdinalIgnoreCase)) yield return file;
            foreach (var child in files.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var name = paths.GetFileName(child);
                // IDE evaluation can generate build outputs even inside a published package.
                // Match the export contract: these are neither payload nor cache invalidation inputs.
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                pending.Push(child);
            }
        }
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        EditorHostServices.Paths.TrimEndingDirectorySeparator(EditorHostServices.Paths.GetFullPath(left)),
        EditorHostServices.Paths.TrimEndingDirectorySeparator(EditorHostServices.Paths.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

}
