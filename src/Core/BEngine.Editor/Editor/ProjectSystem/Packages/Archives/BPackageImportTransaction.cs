using System.Security.Cryptography;

namespace BEngine.Editor;

internal sealed class BPackageImportTransaction : IDisposable
{
    private readonly string _workspaceRoot;
    private readonly string _assetsRoot;
    private readonly string _transactionRoot;
    private readonly string _backupRoot;
    private readonly List<string> _writtenFiles = [];
    private readonly List<(string Backup, string Target)> _backups = [];
    private readonly List<string> _createdDirectories = [];
    private readonly List<string> _deletedDirectories = [];
    private readonly HashSet<string> _possibleGeneratedMetadata = new(StringComparer.OrdinalIgnoreCase);
    private bool _active = true;

    internal BPackageImportTransaction(string workspaceRoot, string assetsRoot, string temporaryRoot)
    {
        _workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _assetsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetsRoot));
        _transactionRoot = Path.Combine(Path.GetFullPath(temporaryRoot), $"bpackage-{Guid.NewGuid():N}");
        StagingPath = Path.Combine(_transactionRoot, "staging");
        ScratchPath = Path.Combine(_transactionRoot, "scratch");
        _backupRoot = Path.Combine(_transactionRoot, "backup");
        EnsureInsideWorkspace(_assetsRoot);
        BPackagePathUtility.EnsureNoReparsePoint(_workspaceRoot, _assetsRoot);
        Directory.CreateDirectory(StagingPath);
        Directory.CreateDirectory(ScratchPath);
        Directory.CreateDirectory(_backupRoot);
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
            var target = BPackagePathUtility.ResolveInside(_assetsRoot, entry.RelativePath);
            BPackagePathUtility.EnsureNoReparsePoint(_assetsRoot, target);
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
            if (!File.Exists(target.Target)) continue;
            BackupExistingFile(target.Target);
            removed.Add(ToAssetPath(target.Target));
        }

        foreach (var target in targets.Where(item => item.Entry.Action == BPackageImportAction.WriteFile))
        {
            var sourceRelative = target.Entry.SourceRelativePath ??
                                 throw new InvalidOperationException("A package write has no staged source path.");
            var staged = BPackagePathUtility.ResolveInside(StagingPath, sourceRelative);
            if (File.Exists(target.Target)) BackupExistingFile(target.Target);
            File.Move(staged, target.Target);
            _writtenFiles.Add(target.Target);
            imported.Add(ToAssetPath(target.Target));
        }

        foreach (var target in targets.Where(item => item.Entry.Action == BPackageImportAction.DeleteDirectory)
                     .OrderByDescending(item => item.Target.Length))
        {
            if (!Directory.Exists(target.Target)) continue;
            if (Directory.EnumerateFileSystemEntries(target.Target).Any())
            {
                preserved.Add(ToAssetPath(target.Target));
                continue;
            }
            Directory.Delete(target.Target);
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
        var source = Path.GetFullPath(stagedPath);
        var target = Path.GetFullPath(targetPath);
        EnsureInsideWorkspace(target);
        if (!File.Exists(source)) throw new FileNotFoundException("The staged transaction file is missing.", source);
        if (Directory.Exists(target)) throw new IOException($"A directory blocks '{target}'.");
        EnsureDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) BackupExistingFile(target);
        File.Move(source, target);
        _writtenFiles.Add(target);
    }

    internal void Complete() => _active = false;

    internal void Rollback()
    {
        if (!_active) return;
        _active = false;
        var failures = new List<Exception>();

        foreach (var path in _writtenFiles.AsEnumerable().Reverse())
            Try(() => { if (File.Exists(path)) File.Delete(path); }, failures);

        foreach (var directory in _deletedDirectories.OrderBy(path => path.Length))
            Try(() => Directory.CreateDirectory(directory), failures);

        foreach (var (backup, target) in _backups.AsEnumerable().Reverse())
        {
            Try(() =>
            {
                if (!File.Exists(backup)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(backup, target, overwrite: true);
            }, failures);
        }

        foreach (var metadataPath in _possibleGeneratedMetadata)
            Try(() => { if (File.Exists(metadataPath)) File.Delete(metadataPath); }, failures);

        foreach (var directory in _createdDirectories.OrderByDescending(path => path.Length))
        {
            Try(() =>
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
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
            if (Directory.Exists(_transactionRoot)) Directory.Delete(_transactionRoot, recursive: true);
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
                    if (File.Exists(target.Target))
                        throw new IOException($"A file blocks the package directory '{ToAssetPath(target.Target)}'.");
                    break;
                case BPackageImportAction.WriteFile:
                    if (Directory.Exists(target.Target))
                        throw new IOException($"A directory blocks the package file '{ToAssetPath(target.Target)}'.");
                    VerifyExpectedFile(target.Entry, target.Target);
                    var sourceRelative = target.Entry.SourceRelativePath ??
                                         throw new InvalidOperationException("A package write has no source path.");
                    var staged = BPackagePathUtility.ResolveInside(StagingPath, sourceRelative);
                    if (!File.Exists(staged)) throw new FileNotFoundException("A staged package file is missing.", staged);
                    break;
                case BPackageImportAction.DeleteFile:
                    if (Directory.Exists(target.Target))
                        throw new IOException($"A directory replaced '{ToAssetPath(target.Target)}' during import.");
                    VerifyExpectedFile(target.Entry, target.Target);
                    break;
                case BPackageImportAction.DeleteDirectory:
                    if (File.Exists(target.Target))
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
            var parent = Path.GetDirectoryName(target.Target);
            while (!string.IsNullOrEmpty(parent) &&
                   !parent.Equals(_assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                needed.Add(parent);
                parent = Path.GetDirectoryName(parent);
            }
        }

        foreach (var directory in needed.OrderBy(path => path.Length)) EnsureDirectory(directory);
    }

    private void EnsureDirectory(string directory)
    {
        EnsureInsideWorkspace(directory);
        if (File.Exists(directory)) throw new IOException($"A file blocks directory '{directory}'.");
        if (Directory.Exists(directory)) return;
        Directory.CreateDirectory(directory);
        _createdDirectories.Add(directory);
        if (IsInside(_assetsRoot, directory) && !File.Exists(directory + ".meta"))
            _possibleGeneratedMetadata.Add(directory + ".meta");
    }

    private void BackupExistingFile(string target)
    {
        var backup = Path.Combine(_backupRoot, $"{_backups.Count:D8}.bak");
        File.Move(target, backup);
        _backups.Add((backup, target));
    }

    private static void VerifyExpectedFile(BPackageImportPlanEntry entry, string target)
    {
        if (entry.ExpectedSha256 is null)
        {
            if (File.Exists(target)) throw new IOException($"Package target changed during import: '{target}'.");
            return;
        }
        if (!File.Exists(target) || !ComputeHash(target).Equals(entry.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Package target changed during import: '{target}'.");
    }

    private void EnsureInsideWorkspace(string path)
    {
        if (!IsInside(_workspaceRoot, path))
            throw new InvalidDataException($"Package transaction path escapes the workspace: '{path}'.");
    }

    private static bool IsInside(string root, string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Try(Action action, ICollection<Exception> failures)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(exception); }
    }

    private string ToAssetPath(string path) =>
        $"Assets/{Path.GetRelativePath(_assetsRoot, path).Replace('\\', '/')}";
}
