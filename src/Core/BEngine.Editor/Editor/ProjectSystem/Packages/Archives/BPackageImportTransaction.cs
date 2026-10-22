using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.Editor;

internal sealed class BPackageImportTransaction : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _assetsRoot;
    private readonly string _transactionRoot;
    private readonly string _backupRoot;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly List<string> _writtenFiles = [];
    private readonly List<(string Backup, string Target)> _backups = [];
    private readonly List<string> _createdDirectories = [];
    private readonly List<string> _deletedDirectories = [];
    private readonly HashSet<string> _possibleGeneratedMetadata = new(StringComparer.OrdinalIgnoreCase);
    private bool _active = true;

    internal BPackageImportTransaction(string workspaceRoot, string assetsRoot, string temporaryRoot)
        : this(
            workspaceRoot,
            assetsRoot,
            temporaryRoot,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>()) { }

    internal BPackageImportTransaction(
        string workspaceRoot,
        string assetsRoot,
        string temporaryRoot,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _workspaceRoot = _pathService.TrimEndingDirectorySeparator(
            _pathService.GetFullPath(workspaceRoot));
        _assetsRoot = _pathService.TrimEndingDirectorySeparator(
            _pathService.GetFullPath(assetsRoot));
        _transactionRoot = _pathService.Combine(
            _pathService.GetFullPath(temporaryRoot), $"bpackage-{Guid.NewGuid():N}");
        StagingPath = _pathService.Combine(_transactionRoot, "staging");
        ScratchPath = _pathService.Combine(_transactionRoot, "scratch");
        _backupRoot = _pathService.Combine(_transactionRoot, "backup");
        EnsureInsideWorkspace(_assetsRoot);
        BPackagePathUtility.EnsureNoReparsePoint(
            _workspaceRoot, _assetsRoot, _fileSystem, _pathService);
        _fileSystem.CreateDirectory(StagingPath);
        _fileSystem.CreateDirectory(ScratchPath);
        _fileSystem.CreateDirectory(_backupRoot);
    }

    internal string StagingPath { get; }
    internal string ScratchPath { get; }

    internal (
        IReadOnlyList<string> Imported,
        IReadOnlyList<string> Skipped,
        IReadOnlyList<string> Unchanged,
        IReadOnlyList<string> PreservedModified,
        IReadOnlyList<string> Removed) Apply(BPackageImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var targets = plan.Entries.Select(entry =>
        {
            var target = BPackagePathUtility.ResolveInside(
                _assetsRoot, entry.RelativePath, _pathService);
            BPackagePathUtility.EnsureNoReparsePoint(
                _assetsRoot, target, _fileSystem, _pathService);
            return (Entry: entry, Target: target);
        }).ToArray();

        Preflight(targets);
        CreateRequiredDirectories(targets);

        var imported = new List<string>();
        var skipped = new List<string>();
        var unchanged = new List<string>();
        var preserved = new List<string>();
        var removed = new List<string>();

        foreach (var target in targets.Where(item => item.Entry.Action == BPackageImportAction.DeleteFile))
        {
            if (!_fileSystem.FileExists(target.Target)) continue;
            BackupExistingFile(target.Target);
            removed.Add(ToAssetPath(target.Target));
        }

        foreach (var target in targets.Where(item => item.Entry.Action == BPackageImportAction.WriteFile))
        {
            var sourceRelative = target.Entry.SourceRelativePath ??
                                 throw new InvalidOperationException("A package write has no staged source path.");
            var staged = BPackagePathUtility.ResolveInside(
                StagingPath, sourceRelative, _pathService);
            if (_fileSystem.FileExists(target.Target)) BackupExistingFile(target.Target);
            _fileSystem.MoveFile(staged, target.Target);
            _writtenFiles.Add(target.Target);
            imported.Add(ToAssetPath(target.Target));
        }

        foreach (var target in targets.Where(item => item.Entry.Action == BPackageImportAction.DeleteDirectory)
                     .OrderByDescending(item => item.Target.Length))
        {
            if (!_fileSystem.DirectoryExists(target.Target)) continue;
            if (_fileSystem.EnumerateFileSystemEntries(target.Target).Any())
            {
                preserved.Add(ToAssetPath(target.Target));
                continue;
            }
            _fileSystem.DeleteDirectory(target.Target);
            _deletedDirectories.Add(target.Target);
            removed.Add(ToAssetPath(target.Target));
        }

        foreach (var target in targets)
        {
            var path = ToAssetPath(target.Target);
            switch (target.Entry.Action)
            {
                case BPackageImportAction.CreateDirectory:
                    imported.Add(path);
                    break;
                case BPackageImportAction.Unchanged:
                    unchanged.Add(path);
                    break;
                case BPackageImportAction.Skipped:
                    skipped.Add(path);
                    break;
                case BPackageImportAction.PreserveModified:
                    preserved.Add(path);
                    break;
            }
        }

        return (
            imported.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            skipped.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            unchanged.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            preserved.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            removed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal void ReplaceExternalFile(string stagedPath, string targetPath)
    {
        var source = _pathService.GetFullPath(stagedPath);
        var target = _pathService.GetFullPath(targetPath);
        EnsureInsideWorkspace(target);
        if (!_fileSystem.FileExists(source))
            throw new FileNotFoundException("The staged transaction file is missing.", source);
        if (_fileSystem.DirectoryExists(target)) throw new IOException($"A directory blocks '{target}'.");
        EnsureDirectory(_pathService.GetDirectoryName(target)!);
        if (_fileSystem.FileExists(target)) BackupExistingFile(target);
        _fileSystem.MoveFile(source, target);
        _writtenFiles.Add(target);
    }

    internal void Complete() => _active = false;

    internal void Rollback()
    {
        if (!_active) return;
        _active = false;
        var failures = new List<Exception>();

        foreach (var path in _writtenFiles.AsEnumerable().Reverse())
            Try(() => { if (_fileSystem.FileExists(path)) _fileSystem.DeleteFile(path); }, failures);

        foreach (var directory in _deletedDirectories.OrderBy(path => path.Length))
            Try(() => _fileSystem.CreateDirectory(directory), failures);

        foreach (var (backup, target) in _backups.AsEnumerable().Reverse())
        {
            Try(() =>
            {
                if (!_fileSystem.FileExists(backup)) return;
                _fileSystem.CreateDirectory(_pathService.GetDirectoryName(target)!);
                _fileSystem.MoveFile(backup, target, overwrite: true);
            }, failures);
        }

        foreach (var metadataPath in _possibleGeneratedMetadata)
            Try(() =>
            {
                if (_fileSystem.FileExists(metadataPath)) _fileSystem.DeleteFile(metadataPath);
            }, failures);

        foreach (var directory in _createdDirectories.OrderByDescending(path => path.Length))
        {
            Try(() =>
            {
                if (_fileSystem.DirectoryExists(directory) &&
                    !_fileSystem.EnumerateFileSystemEntries(directory).Any())
                    _fileSystem.DeleteDirectory(directory);
            }, failures);
        }

        if (failures.Count > 0) throw new AggregateException("The package import rollback was incomplete.", failures);
    }

    public void Dispose()
    {
        if (_active)
        {
            try { Rollback(); }
            catch { }
        }
        try
        {
            if (_fileSystem.DirectoryExists(_transactionRoot))
                _fileSystem.DeleteDirectory(_transactionRoot, recursive: true);
        }
        catch { }
    }

    private void Preflight(IReadOnlyList<(BPackageImportPlanEntry Entry, string Target)> targets)
    {
        foreach (var target in targets)
        {
            switch (target.Entry.Action)
            {
                case BPackageImportAction.CreateDirectory:
                    if (_fileSystem.FileExists(target.Target))
                        throw new IOException($"A file blocks the package directory '{ToAssetPath(target.Target)}'.");
                    break;
                case BPackageImportAction.WriteFile:
                    if (_fileSystem.DirectoryExists(target.Target))
                        throw new IOException($"A directory blocks the package file '{ToAssetPath(target.Target)}'.");
                    VerifyExpectedFile(target.Entry, target.Target);
                    var sourceRelative = target.Entry.SourceRelativePath ??
                                         throw new InvalidOperationException("A package write has no source path.");
                    var staged = BPackagePathUtility.ResolveInside(
                        StagingPath, sourceRelative, _pathService);
                    if (!_fileSystem.FileExists(staged))
                        throw new FileNotFoundException("A staged package file is missing.", staged);
                    break;
                case BPackageImportAction.DeleteFile:
                    if (_fileSystem.DirectoryExists(target.Target))
                        throw new IOException($"A directory replaced '{ToAssetPath(target.Target)}' during import.");
                    VerifyExpectedFile(target.Entry, target.Target);
                    break;
                case BPackageImportAction.DeleteDirectory:
                    if (_fileSystem.FileExists(target.Target))
                        throw new IOException($"A file replaced '{ToAssetPath(target.Target)}' during import.");
                    break;
            }
        }
    }

    private void CreateRequiredDirectories(IReadOnlyList<(BPackageImportPlanEntry Entry, string Target)> targets)
    {
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.Where(item => item.Entry.Action is
                     BPackageImportAction.CreateDirectory or BPackageImportAction.WriteFile))
        {
            if (target.Entry.Action == BPackageImportAction.CreateDirectory) needed.Add(target.Target);
            var parent = _pathService.GetDirectoryName(target.Target);
            while (!string.IsNullOrEmpty(parent) &&
                   !parent.Equals(_assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                needed.Add(parent);
                parent = _pathService.GetDirectoryName(parent);
            }
        }

        foreach (var directory in needed.OrderBy(path => path.Length)) EnsureDirectory(directory);
    }

    private void EnsureDirectory(string directory)
    {
        EnsureInsideWorkspace(directory);
        if (_fileSystem.FileExists(directory))
            throw new IOException($"A file blocks directory '{directory}'.");
        if (_fileSystem.DirectoryExists(directory)) return;
        _fileSystem.CreateDirectory(directory);
        _createdDirectories.Add(directory);
        if (IsInside(_assetsRoot, directory) && !_fileSystem.FileExists(directory + ".meta"))
            _possibleGeneratedMetadata.Add(directory + ".meta");
    }

    private void BackupExistingFile(string target)
    {
        var backup = _pathService.Combine(_backupRoot, $"{_backups.Count:D8}.bak");
        _fileSystem.MoveFile(target, backup);
        _backups.Add((backup, target));
    }

    private void VerifyExpectedFile(BPackageImportPlanEntry entry, string target)
    {
        if (entry.ExpectedSha256 is null)
        {
            if (_fileSystem.FileExists(target))
                throw new IOException($"Package target changed during import: '{target}'.");
            return;
        }
        if (!_fileSystem.FileExists(target) || !ComputeHash(target).Equals(entry.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Package target changed during import: '{target}'.");
    }

    private void EnsureInsideWorkspace(string path)
    {
        if (!IsInside(_workspaceRoot, path))
            throw new InvalidDataException($"Package transaction path escapes the workspace: '{path}'.");
    }

    private bool IsInside(string root, string path)
    {
        var fullPath = _pathService.GetFullPath(path);
        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(root + _pathService.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private string ComputeHash(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Try(Action action, ICollection<Exception> failures)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(exception); }
    }

    private string ToAssetPath(string path) =>
        $"Assets/{_pathService.GetRelativePath(_assetsRoot, path).Replace('\\', '/')}";
}
