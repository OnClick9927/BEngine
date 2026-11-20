using System.Text.Json;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

/// <summary>Owns only Packages/GitHub releases; commits complete directories after validating the new catalog.</summary>
internal sealed class GitHubPackageManager(LauncherPackageCatalog catalog, GitHubPackageRepository repository)
{
    internal const string ReceiptName = ".bengine-github.json";
    internal const string ManagedDirectory = "GitHub";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string PackagesRoot => catalog.ResolvePackagesRoot();
    private string ManagedRoot => Path.Combine(PackagesRoot, ManagedDirectory);
    internal static bool IsManagedPath(string path) => path.Equals("Packages/GitHub", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Packages/GitHub/", StringComparison.OrdinalIgnoreCase);

    internal IReadOnlyList<InstalledGitHubPackage> Installed()
    {
        if (!Directory.Exists(ManagedRoot)) return [];
        var result = new List<InstalledGitHubPackage>();
        foreach (var owner in Directory.EnumerateDirectories(ManagedRoot).Where(path => !Path.GetFileName(path).StartsWith('.')))
        {
            LauncherUpdateService.EnsureNoReparsePoints(owner, "");
            foreach (var folder in Directory.EnumerateDirectories(owner).Where(path => !Path.GetFileName(path).StartsWith('.')))
            {
                LauncherUpdateService.EnsureNoReparsePoints(folder, ReceiptName);
                if (!File.Exists(Path.Combine(folder, ReceiptName))) continue;
                var receipt = JsonSerializer.Deserialize<GitHubPackageReceipt>(File.ReadAllText(Path.Combine(folder, ReceiptName)))
                    ?? throw new InvalidDataException("Invalid GitHub package installation record.");
                if (receipt.Format != "BEngine.GitHubPackage" || receipt.Version != 1 ||
                    !folder.Equals(Target(receipt.Id, receipt.PackageVersion), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GitHub package installation record does not match its directory: " + folder);
                result.Add(new(folder, receipt));
            }
        }
        return result.OrderBy(item => item.Receipt.Id).ThenByDescending(item => item.Receipt.PackageVersion, BPackageVersion.Comparer).ToArray();
    }

    internal async Task InstallAsync(IReadOnlyList<GitHubPackageRelease> releases, string engineVersion,
        IProgress<string> progress, CancellationToken token, IProgress<GitHubPackageProgress>? operation = null, bool allVersions = false)
    {
        using var gitFiles = repository.BeginInstall();
        if (releases.Count == 0) throw new InvalidDataException("Select packages to install.");
        using var ownership = Acquire();
        var installed = Installed();
        foreach (var release in releases.Where(_ => !allVersions))
            if (!BPackageCompatibility.SupportsEngine(release.Document, engineVersion))
                throw new InvalidDataException($"Package {release.Document.Id} {release.Document.PackageVersion} requires engine {release.Document.MinimumEngineVersion} or newer.");
        operation?.Report(new("Resolving package dependencies"));
        var definitions = catalog.GetDefinitions(PackagesRoot);
        var requestedTargets = releases.Select(item => Target(item.Document.Id, item.Document.PackageVersion)).ToArray();
        if (requestedTargets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedTargets.Length)
            throw new InvalidDataException("The same package release was selected more than once.");
        releases = await ResolveDependenciesAsync(releases, definitions, engineVersion, progress, token, allVersions).ConfigureAwait(false);
        var targets = releases.Select(item => Target(item.Document.Id, item.Document.PackageVersion)).ToArray();
        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
            throw new InvalidDataException("The same package release was selected more than once.");
        foreach (var release in releases)
        {
            var target = Target(release.Document.Id, release.Document.PackageVersion);
            var previous = installed.SingleOrDefault(item => item.Directory.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (Directory.Exists(target) && previous is null) throw new IOException("An untracked directory occupies the package destination: " + target);
            if (previous is not null && previous.Receipt.Source != release.Source)
                throw new IOException("This package release is already owned by another GitHub source. Remove that release first.");
            if (definitions.Any(item => item.Document.Id.Equals(release.Document.Id, StringComparison.OrdinalIgnoreCase) &&
                item.Document.PackageVersion == release.Document.PackageVersion &&
                !Path.GetDirectoryName(item.Path)!.Equals(target, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("This package ID and version already exists outside GitHub packages: " + release.Document.Id);
        }
        ValidateForEngines(definitions.Where(item => !targets.Contains(Path.GetDirectoryName(item.Path)!, StringComparer.OrdinalIgnoreCase))
            .Concat(releases.Select((release, index) => new BPackageDefinition(Path.Combine(targets[index], "package.yaml"), release.Document))), engineVersion);
        var totalFiles = releases.Sum(release => release.Files.Count);
        var totalBytes = releases.Sum(release => release.Files.Sum(file => file.Size));
        var received = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long receivedBytes = 0;
        var progressGate = new object();
        var completedFiles = 0;
        void Report(string stage, string detail, double? percent = null)
        {
            lock (progressGate)
            {
                var bytes = receivedBytes;
                operation?.Report(new(stage, detail, bytes, totalBytes, completedFiles, totalFiles,
                    percent ?? (totalBytes == 0 ? 0 : 90d * bytes / totalBytes)));
            }
        }
        Report("Downloading packages", "");
        var transaction = Path.Combine(ManagedRoot, ".transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        var stages = new List<string>();
        try
        {
            for (var index = 0; index < releases.Count; index++)
            {
                var release = releases[index];
                var stage = Path.Combine(transaction, index.ToString());
                Directory.CreateDirectory(stage); stages.Add(stage);
                var prefix = release.Directory.Length == 0 ? "" : release.Directory + "/";
                if (release.Files.Sum(file => file.Size) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("The selected package exceeds 2 GiB.");
                await Parallel.ForEachAsync(release.Files, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token },
                    async (file, cancellation) =>
                    {
                        if (!file.Path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("File is outside the selected package.");
                        var relative = file.Path[prefix.Length..];
                        if (relative.Equals(ReceiptName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package contains reserved installation metadata.");
                        var destination = LauncherUpdateService.ResolvePath(stage, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        progress.Report($"{release.Document.DisplayName} · {relative}");
                        await repository.DownloadAsync(release, file, destination, progress, cancellation,
                            new GitHubProgressSink<long>(bytes =>
                            {
                                lock (progressGate)
                                {
                                    receivedBytes += bytes - received.GetValueOrDefault(destination);
                                    received[destination] = bytes;
                                    Report("Downloading packages", relative);
                                }
                            })).ConfigureAwait(false);
                        if (!file.Sha.Equals(await LauncherUpdateService.HashFileAsync(destination, cancellation).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("GitHub file verification failed: " + file.Path);
                        lock (progressGate) { completedFiles++; Report("Downloading packages", relative); }
                    }).ConfigureAwait(false);
                var document = PackageDefinitionLoader.Normalize(YamlUtility.Deserialize<BEngine.Editor.Documents.PackageDefinitionDocument>(
                    File.ReadAllText(Path.Combine(stage, "package.yaml"))), stage);
                if (YamlUtility.Serialize(document) != YamlUtility.Serialize(release.Document))
                    throw new InvalidDataException("Package definition changed after the preview. Read the GitHub package list again.");
                var receipt = new GitHubPackageReceipt
                {
                    Id = document.Id, PackageVersion = document.PackageVersion, DisplayName = document.DisplayName, Source = release.Source,
                    Commit = release.Commit, PackageDirectory = release.Directory, Files = release.Files.ToList()
                };
                File.WriteAllText(Path.Combine(stage, ReceiptName), JsonSerializer.Serialize(receipt, JsonOptions));
            }
            Report("Verifying packages", "", 94);
            ValidateForEngines(definitions.Where(item => !targets.Contains(Path.GetDirectoryName(item.Path)!, StringComparer.OrdinalIgnoreCase))
                .Concat(releases.Select((release, index) => new BPackageDefinition(Path.Combine(stages[index], "package.yaml"), release.Document))), engineVersion);
            token.ThrowIfCancellationRequested();
            Commit(transaction, targets, stages, count => { token.ThrowIfCancellationRequested(); Report("Installing packages", "", 95 + 4d * count / targets.Length); });
            Report("Package operation completed", "", 100);
        }
        finally { Cleanup(transaction); catalog.Refresh(PackagesRoot); }
    }

    internal void Remove(IReadOnlyList<InstalledGitHubPackage> selected, string engineVersion,
        IProgress<GitHubPackageProgress>? progress = null, CancellationToken token = default)
    {
        if (selected.Count == 0) throw new InvalidDataException("Select installed GitHub packages to remove.");
        using var ownership = Acquire();
        token.ThrowIfCancellationRequested();
        progress?.Report(new("Checking package dependencies"));
        var current = Installed();
        var targets = selected.Select(item => item.Directory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var item in selected)
            if (!current.Any(value => value.Directory == item.Directory && value.Receipt.Commit == item.Receipt.Commit))
                throw new IOException("The installed package changed. Reload the list before removing it.");
        ValidateForEngines(catalog.GetDefinitions(PackagesRoot)
            .Where(item => !targets.Contains(Path.GetDirectoryName(item.Path)!, StringComparer.OrdinalIgnoreCase)), engineVersion);
        var transaction = Path.Combine(ManagedRoot, ".transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        try
        {
            token.ThrowIfCancellationRequested();
            Commit(transaction, targets, [], count =>
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new("Removing packages", CompletedFiles: count, TotalFiles: targets.Length, Percent: 95d * count / targets.Length));
            });
            progress?.Report(new("Package operation completed", CompletedFiles: targets.Length, TotalFiles: targets.Length, Percent: 100));
        }
        finally { Cleanup(transaction); catalog.Refresh(PackagesRoot); }
    }

    private static void ValidateCatalog(IEnumerable<BPackageDefinition> releases)
    {
        var all = releases.ToArray();
        LauncherPackageValidation.ValidateCompatibleReleases(all);
        _ = LauncherPackageCatalog.BuildPackageInfos(all);
        var selected = BPackageReleaseResolver.Resolve(all).ToDictionary(item => item.Document.Id, StringComparer.OrdinalIgnoreCase);
        // Resolution intentionally omits broken releases. Validate the latest candidate too,
        // otherwise an incomplete new package could be published and disappear from the Hub.
        foreach (var package in all.GroupBy(item => item.Document.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Document.PackageVersion, BPackageVersion.Comparer).First()))
        {
            foreach (var dependency in (package.Document.Runtime?.Dependencies ?? []).Concat(package.Document.Editor?.Dependencies ?? []))
                if (!dependency.Optional && (!selected.TryGetValue(dependency.PackageId, out var target) ||
                    (dependency.Target == "runtime" ? target.Document.Runtime : target.Document.Editor) is null))
                    throw new InvalidDataException($"{package.Document.Id} requires {dependency.PackageId} ({dependency.Target}). Install its dependencies together, or remove dependent packages together.");
            if (!selected.ContainsKey(package.Document.Id)) throw new InvalidDataException("Package dependencies cannot be resolved: " + package.Document.Id);
        }
    }

    private async Task<IReadOnlyList<GitHubPackageRelease>> ResolveDependenciesAsync(IReadOnlyList<GitHubPackageRelease> requested,
        IEnumerable<BPackageDefinition> installed, string engineVersion, IProgress<string> progress, CancellationToken token, bool allVersions)
    {
        if (!allVersions) return await GitHubPackageDependencies.ResolveAsync(requested, installed, repository, engineVersion, progress, token).ConfigureAwait(false);
        var snapshots = new Dictionary<string, IReadOnlyList<GitHubPackageRelease>>(StringComparer.Ordinal);
        var result = new Dictionary<string, GitHubPackageRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in requested.Select(release => release.Document.MinimumEngineVersion).Append(engineVersion)
            .Where(version => BPackageVersion.Comparer.Compare(version, engineVersion) >= 0).Distinct().Order(BPackageVersion.Comparer))
        {
            var roots = requested.Where(release => BPackageCompatibility.SupportsEngine(release.Document, version)).ToArray();
            if (roots.Length == 0) continue;
            foreach (var release in await GitHubPackageDependencies.ResolveAsync(roots, installed, repository, version, progress, token, snapshots).ConfigureAwait(false))
            {
                var identity = release.Document.Id + "\n" + release.Document.PackageVersion;
                if (result.TryGetValue(identity, out var previous) && (previous.Source != release.Source || previous.Commit != release.Commit))
                    throw new InvalidDataException("Conflicting Git sources for package release: " + release.Document.Id);
                result[identity] = release;
            }
        }
        return result.Values.ToArray();
    }

    private static void ValidateForEngines(IEnumerable<BPackageDefinition> definitions, string engineVersion)
    {
        var all = definitions.ToArray();
        foreach (var version in all.Select(item => item.Document.MinimumEngineVersion).Append(engineVersion)
            .Where(version => BPackageVersion.Comparer.Compare(version, engineVersion) >= 0).Distinct())
            ValidateCatalog(all.Where(item => BPackageCompatibility.SupportsEngine(item.Document, version)));
    }

    private string Target(string id, string version)
    {
        BPackageVersion.Validate(version);
        LauncherUpdateService.ValidateRelativePath(id);
        if (id.Contains('/')) throw new InvalidDataException("Package ID must be a single directory name.");
        return LauncherUpdateService.ResolvePath(ManagedRoot, id.ToLowerInvariant() + "/" + version);
    }
    private FileStream Acquire()
    {
        LauncherUpdateService.EnsureNoReparsePoints(PackagesRoot, ManagedDirectory);
        Directory.CreateDirectory(ManagedRoot);
        LauncherUpdateService.EnsureNoReparsePoints(ManagedRoot, ".operation.lock");
        return new FileStream(Path.Combine(ManagedRoot, ".operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void Commit(string transaction, string[] targets, IReadOnlyList<string> stages, Action<int>? progress = null)
    {
        var moved = new List<(string Target, string Backup, bool Published)>();
        try
        {
            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index]; var backup = Path.Combine(transaction, "backup-" + index);
                CheckTree(target);
                if (Directory.Exists(target)) Directory.Move(target, backup);
                moved.Add((target, backup, false));
                if (stages.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Directory.Move(stages[index], target);
                    moved[^1] = (target, backup, true);
                }
                progress?.Invoke(index + 1);
            }
        }
        catch (Exception failure)
        {
            try
            {
                foreach (var item in moved.AsEnumerable().Reverse())
                {
                    if (item.Published) Directory.Delete(item.Target, true);
                    if (Directory.Exists(item.Backup)) Directory.Move(item.Backup, item.Target);
                }
            }
            catch (Exception recovery)
            {
                File.WriteAllText(Path.Combine(transaction, "RECOVERY.txt"),
                    "Package rollback could not finish. Preserve this directory and its backups.\n" +
                    JsonSerializer.Serialize(moved.Select(item => new { item.Target, item.Backup })));
                throw new IOException("Package rollback could not finish. Backups were retained in " + transaction,
                    new AggregateException(failure, recovery));
            }
            throw;
        }
    }
    private static void CheckTree(string directory)
    {
        LauncherUpdateService.EnsureNoReparsePoints(directory, "");
        if (!Directory.Exists(directory)) return;
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var current))
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Package contains a symbolic link or junction: " + path);
                if (Directory.Exists(path)) pending.Push(path);
            }
    }
    private static void Cleanup(string transaction)
    {
        if (File.Exists(Path.Combine(transaction, "RECOVERY.txt"))) return;
        // A failed cleanup retains only hidden transaction files; installed releases stay usable.
        try { CheckTree(transaction); Directory.Delete(transaction, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(error.Message); }
    }
}
