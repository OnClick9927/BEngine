namespace BEngine.ProjectSystem;

internal sealed class ProjectPackageCache
{
    private const string StagingPrefix = ".staging-";
    private const string BackupPrefix = ".backup-";
    private const string LockFileName = ".package-cache.lock";
    private readonly string _rootPath;
    private readonly string _lockPath;

    internal ProjectPackageCache(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _rootPath = GetCacheRoot(workspace);
        _lockPath = Path.Combine(workspace.LibraryPath, LockFileName);
        Directory.CreateDirectory(_rootPath);
        using var cacheLock = AcquireLock();
        CleanupInterruptedCopies();
    }

    internal static string GetCacheRoot(ProjectWorkspace workspace) =>
        workspace.PackagesPath;

    internal void Synchronize(
        BPackageCatalog catalog,
        IEnumerable<string> enabledPackageIds,
        Action<IReadOnlyDictionary<string, BPackageDefinition>> apply)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(enabledPackageIds);
        ArgumentNullException.ThrowIfNull(apply);
        var enabled = enabledPackageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var cacheLock = AcquireLock();
        var installed = InstallEnabled(catalog, enabled);
        apply(installed);
        RemoveDisabled(enabled);
    }

    internal void RemoveRetired(IEnumerable<string> packageIds)
    {
        ArgumentNullException.ThrowIfNull(packageIds);
        using var cacheLock = AcquireLock();
        var roots = new[]
        {
            _rootPath,
            Path.Combine(Path.GetDirectoryName(_rootPath)!, "Library", "Packages")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        foreach (var packageId in packageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(root, CacheFolderName(packageId));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private IReadOnlyDictionary<string, BPackageDefinition> InstallEnabled(
        BPackageCatalog catalog,
        IEnumerable<string> enabledPackageIds)
    {
        var installed = new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in enabledPackageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cachedDefinitionPath = catalog.TryGet(packageId, out var definition)
                ? Install(definition)
                : Path.Combine(_rootPath, CacheFolderName(packageId), "package.yaml");
            if (!File.Exists(cachedDefinitionPath)) continue;
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
        foreach (var directory in Directory.EnumerateDirectories(_rootPath, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith(".", StringComparison.Ordinal) || enabledFolders.Contains(name)) continue;
            Directory.Delete(directory, true);
        }
    }

    private string Install(BPackageDefinition definition)
    {
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(definition.Path))!;
        var targetRoot = Path.Combine(_rootPath, CacheFolderName(definition.Document.Id));
        if (PathsEqual(sourceRoot, targetRoot) || DirectoriesMatch(sourceRoot, targetRoot))
            return Path.Combine(targetRoot, "package.yaml");

        var staging = Path.Combine(_rootPath, $"{StagingPrefix}{Guid.NewGuid():N}");
        var backup = Path.Combine(_rootPath, $"{BackupPrefix}{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(sourceRoot, staging);
            var stagedDefinition = Path.Combine(staging, "package.yaml");
            if (!File.Exists(stagedDefinition))
                throw new FileNotFoundException("The package cache copy does not contain package.yaml.", stagedDefinition);

            if (Directory.Exists(targetRoot)) Directory.Move(targetRoot, backup);
            try
            {
                Directory.Move(staging, targetRoot);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            catch
            {
                if (!Directory.Exists(targetRoot) && Directory.Exists(backup)) Directory.Move(backup, targetRoot);
                throw;
            }
            return Path.Combine(targetRoot, "package.yaml");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }

    private void CleanupInterruptedCopies()
    {
        foreach (var directory in Directory.EnumerateDirectories(_rootPath, ".*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (!name.StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.Delete(directory, true);
        }
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
        var path = _lockPath;
        var deadline = Environment.TickCount64 + 15_000;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(25);
            }
            catch (IOException exception)
            {
                throw new IOException($"Timed out waiting for the project package cache lock '{path}'.", exception);
            }
        }
    }

    private static string CacheFolderName(string packageId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = string.Concat(packageId.Trim().Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character));
        if (value is "" or "." or "..") throw new InvalidDataException($"Invalid package id '{packageId}'.");
        return value;
    }

    private static bool DirectoriesMatch(string sourceRoot, string targetRoot)
    {
        if (!Directory.Exists(targetRoot)) return false;
        var sourceFiles = FilesByRelativePath(sourceRoot);
        var targetFiles = FilesByRelativePath(targetRoot);
        if (sourceFiles.Count != targetFiles.Count) return false;
        foreach (var (relativePath, source) in sourceFiles)
        {
            if (!targetFiles.TryGetValue(relativePath, out var target) || source.Length != target.Length ||
                source.LastWriteTimeUtc != target.LastWriteTimeUtc) return false;
        }
        return true;
    }

    private static Dictionary<string, FileInfo> FilesByRelativePath(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => new FileInfo(path),
                StringComparer.OrdinalIgnoreCase);

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)));
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        }
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);
}
