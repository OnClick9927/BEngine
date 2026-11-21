using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BEngine.Launcher;

/// <summary>Previews and stages an immutable Engine directory fetched through Git.</summary>
internal sealed class LauncherUpdateService(string? repositoryUrl = null, GitRepositoryClient? git = null) : IDisposable
{
    private readonly string _repositoryUrl = repositoryUrl ?? "https://github.com/OnClick9927/BEngine.git";
    private readonly GitRepositoryClient _git = git ?? new();
    private GitRepositorySnapshot? _previewSnapshot;
    private readonly object _snapshotGate = new();
    private bool _disposed;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ProtectedRoots = new(StringComparer.OrdinalIgnoreCase)
        { "Preferences", "EditorData", "Temp", ".git", ".bengine-update-manifest.json" };

    internal Task<LauncherUpdatePlan> CheckAsync(string installationRoot, IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => CheckCoreAsync(installationRoot, progress, cancellationToken), CancellationToken.None);

    private async Task<LauncherUpdatePlan> CheckCoreAsync(string installationRoot, IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The Hub installation directory was not found.");
        EnsureNoReparsePoints(root, "");
        Interlocked.Exchange(ref _previewSnapshot, null)?.Dispose();
        var snapshot = await _git.OpenAsync(_repositoryUrl, "main", "Engine",
            new GitHubProgressSink<string>(message => progress.Report(new("Connecting", message))), cancellationToken, root, metadataOnly: true).ConfigureAwait(false);
        try
        {
            var plan = await CompareSnapshotAsync(root, snapshot, progress, cancellationToken).ConfigureAwait(false);
            if (plan.HasUpdates)
            {
                lock (_snapshotGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    cancellationToken.ThrowIfCancellationRequested();
                    _previewSnapshot = snapshot;
                }
            }
            else snapshot.Dispose();
            return plan;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private async Task<LauncherUpdatePlan> CompareSnapshotAsync(string root, GitRepositorySnapshot snapshot,
        IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken)
    {
        var commitSha = snapshot.Commit; var engineTree = snapshot.Tree;
        var commitMessage = snapshot.Message; var commitUrl = _repositoryUrl.Replace(".git", "", StringComparison.Ordinal) + "/commit/" + commitSha;
        var files = new List<LauncherUpdateFile>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.Path.StartsWith("Engine/", StringComparison.Ordinal)) throw new InvalidDataException("File is outside the Engine distribution.");
            var path = entry.Path[7..]; ValidateRelativePath(path);
            // A source checkout may contain locally installed Git packages or Hub
            // state. Such files never belong to the managed engine distribution.
            if (IsProtected(path)) continue;
            if (!seen.Add(path)) throw new InvalidDataException("Duplicate path in the Engine distribution: " + path);
            files.Add(new(path, entry.Sha, entry.Size));
        }
        if (!files.Any(file => file.Path.Equals("BEngine.Hub.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The remote Engine distribution does not contain BEngine.Hub.exe.");
        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
        var previous = await ReadPreviousManifestAsync(root, cancellationToken).ConfigureAwait(false);
        var managed = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { "BEgine", "Packages" })
            EnumerateManagedFiles(root, folder, managed, cancellationToken);
        if (File.Exists(Path.Combine(root, "BEngine.Hub.exe"))) managed.Add("BEngine.Hub.exe");

        var changes = new List<LauncherUpdateChange>();
        var originals = new List<LauncherUpdateLocalFile>();
        long lastCompareTimestamp = 0;
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var now = Stopwatch.GetTimestamp();
            if (index == 0 || index == files.Count - 1 || Stopwatch.GetElapsedTime(lastCompareTimestamp, now).TotalMilliseconds >= 50)
            {
                lastCompareTimestamp = now;
                progress.Report(new("Comparing", file.Path, index, files.Count, 100d * index / files.Count,
                    FilePath: file.Path));
            }
            EnsureNoReparsePoints(root, file.Path);
            var localPath = ResolvePath(root, file.Path);
            if (Directory.Exists(localPath)) throw new IOException($"A directory occupies an expected file path: {file.Path}");
            if (!File.Exists(localPath))
            {
                originals.Add(new(file.Path, false, string.Empty, 0));
                changes.Add(new(file.Path, LauncherUpdateChangeKind.Added, file.Size));
            }
            else
            {
                var localLength = new FileInfo(localPath).Length;
                var localSha = await HashFileAsync(localPath, cancellationToken).ConfigureAwait(false);
                originals.Add(new(file.Path, true, localSha, localLength));
                if (!string.Equals(localSha, file.Sha, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new(file.Path, LauncherUpdateChangeKind.Modified, file.Size));
                else files[index] = file with { Size = localLength };
            }
        }
        foreach (var oldPath in managed.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Contains(oldPath)) continue;
            EnsureNoReparsePoints(root, oldPath);
            var oldFile = ResolvePath(root, oldPath);
            if (File.Exists(oldFile))
            {
                var length = new FileInfo(oldFile).Length;
                originals.Add(new(oldPath, true, await HashFileAsync(oldFile, cancellationToken).ConfigureAwait(false), length));
                changes.Add(new(oldPath, LauncherUpdateChangeKind.Deleted, length));
            }
        }

        var workspace = LauncherUpdateWorkspace.GetOrCreate(root, commitSha);
        var plan = new LauncherUpdatePlan
        {
            InstallationRoot = root, WorkspaceRoot = workspace, CommitSha = commitSha, EngineTreeSha = engineTree,
            CommitMessage = commitMessage, CommitUrl = commitUrl, Files = files,
            Changes = changes, OriginalFiles = originals, PreviousManagedFiles = managed.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
        progress.Report(new("Ready", plan.HasUpdates ? "Update preview is ready" : "The installation is up to date", files.Count, files.Count, 100));
        return plan;
    }

    internal Task<LauncherUpdatePlan> DownloadAsync(LauncherUpdatePlan plan, IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => DownloadCoreAsync(plan, progress, cancellationToken), CancellationToken.None);

    private async Task<LauncherUpdatePlan> DownloadCoreAsync(LauncherUpdatePlan plan, IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(progress);
        var snapshot = Interlocked.Exchange(ref _previewSnapshot, null);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!plan.HasUpdates) return plan;
            ValidatePlan(plan);
            LauncherUpdateWorkspace.ThrowIfInstallationBusy(plan.InstallationRoot);
            using var downloadLock = LauncherUpdateWorkspace.AcquireDownloadLock(plan.InstallationRoot, plan.WorkspaceRoot);
            LauncherUpdateWorkspace.ThrowIfInstallationBusy(plan.InstallationRoot);
            foreach (var file in plan.OriginalFiles)
            {
                cancellationToken.ThrowIfCancellationRequested(); EnsureNoReparsePoints(plan.InstallationRoot, file.Path);
                var local = ResolvePath(plan.InstallationRoot, file.Path);
                if ((!file.Exists && (File.Exists(local) || Directory.Exists(local))) ||
                    (file.Exists && (!File.Exists(local) || new FileInfo(local).Length != file.Size ||
                     !string.Equals(await HashFileAsync(local, cancellationToken).ConfigureAwait(false), file.Sha, StringComparison.OrdinalIgnoreCase))))
                    throw new IOException("The installation changed after the update check. Check for updates again: " + file.Path);
            }
            EnsureNoReparsePoints(plan.WorkspaceRoot, "staging"); EnsureNoReparsePoints(plan.WorkspaceRoot, "update-plan.json");
            EnsureNoReparsePoints(plan.WorkspaceRoot, "update-plan.json.part");
            Directory.CreateDirectory(plan.StagingRoot);
            if (File.Exists(plan.PlanPath)) File.Delete(plan.PlanPath);
            if (snapshot is not null && snapshot.Commit != plan.CommitSha) { snapshot.Dispose(); snapshot = null; }
            var groups = plan.GetChangedBlobGroups();
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<LauncherUpdateFile>();
            var localBlobs = plan.OriginalFiles.Where(file => file.Exists).ToLookup(file => file.Sha, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = group[0]; string? retained = null;
                foreach (var destination in group)
                {
                    EnsureNoReparsePoints(plan.WorkspaceRoot, "staging/" + destination.Path);
                    var candidate = ResolvePath(plan.StagingRoot, destination.Path);
                    if (File.Exists(candidate) && (file.Size < 0 || new FileInfo(candidate).Length == file.Size) &&
                        string.Equals(await HashFileAsync(candidate, cancellationToken).ConfigureAwait(false), file.Sha, StringComparison.OrdinalIgnoreCase))
                    { retained = candidate; break; }
                }
                // A renamed file or a duplicate at a new destination can reuse an
                // already verified local blob without a network request.
                retained ??= localBlobs[file.Sha].Select(value => ResolvePath(plan.InstallationRoot, value.Path)).FirstOrDefault();
                if (retained is not null) sources.Add(file.Sha, retained);
                else missing.Add(file);
            }
            if (missing.Count > 0)
            {
                snapshot ??= await _git.OpenAsync(_repositoryUrl, plan.CommitSha, "Engine",
                    new GitHubProgressSink<string>(message => progress.Report(new("Downloading", message))), cancellationToken,
                    plan.InstallationRoot, metadataOnly: true).ConfigureAwait(false);
                if (snapshot.Tree != plan.EngineTreeSha) throw new InvalidDataException("The Git snapshot does not match the update preview.");
                var remoteFiles = snapshot.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
                foreach (var file in missing)
                    if (!remoteFiles.TryGetValue("Engine/" + file.Path, out var remote) || remote.Sha != file.Sha)
                        throw new InvalidDataException("The Git snapshot does not match the update preview: " + file.Path);
                await snapshot.FetchFilesAsync(missing.Select(file => "Engine/" + file.Path), cancellationToken).ConfigureAwait(false);
                foreach (var file in missing) sources.Add(file.Sha, ResolvePath(snapshot.Root, "Engine/" + file.Path));
            }
            var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = group[0]; var source = sources[file.Sha];
                var size = new FileInfo(source).Length;
                if (file.Size >= 0 && file.Size != size ||
                    !string.Equals(await HashFileAsync(source, cancellationToken).ConfigureAwait(false), file.Sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Git file verification failed: " + file.Path);
                sizes.Add(file.Sha, size);
            }
            // Only a complete, verified plan may reach the installer. Keep the
            // preview immutable so a cancelled download can retry against its baseline.
            var fileShas = plan.Files.ToDictionary(file => file.Path, file => file.Sha, StringComparer.OrdinalIgnoreCase);
            plan = plan with
            {
                Files = plan.Files.Select(file => sizes.TryGetValue(file.Sha, out var size) ? file with { Size = size } : file).ToArray(),
                Changes = plan.Changes.Select(change => change.Kind == LauncherUpdateChangeKind.Deleted ? change :
                    change with { Size = sizes[fileShas[change.Path]] }).ToArray()
            };
            if (plan.Files.Any(file => file.Size < 0)) throw new InvalidDataException("The installation plan has unresolved file sizes.");
            long completed = 0; var total = plan.DownloadBytes;
            foreach (var group in plan.GetChangedBlobGroups())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = group[0]; var source = sources[file.Sha];
                // Retained staging is already in place; other sources are copied via
                // a partial file so cancellation never publishes an incomplete blob.
                var destinations = group.Where(destination => !string.Equals(ResolvePath(plan.StagingRoot, destination.Path), source,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                await StageBlobCopiesAsync(plan, destinations, source, _ => { }, cancellationToken).ConfigureAwait(false);
                completed += file.Size;
                progress.Report(new("Downloading", "Copying Git files", completed, total,
                    total == 0 ? 100 : 100d * completed / total, FilePath: file.Path));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(plan.PlanPath + ".part", JsonSerializer.Serialize(plan, JsonOptions), cancellationToken).ConfigureAwait(false);
            File.Move(plan.PlanPath + ".part", plan.PlanPath, true);
            progress.Report(new("Ready", "Download verified; ready to install", total, total, 100));
            return plan;
        }
        finally { snapshot?.Dispose(); }
    }
    private static async Task StageBlobCopiesAsync(LauncherUpdatePlan plan, IReadOnlyList<LauncherUpdateFile> group,
        string verifiedSource, Action<LauncherUpdateFile> reportCopy, CancellationToken cancellationToken)
    {
        foreach (var file in group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolvePath(plan.StagingRoot, file.Path);
            EnsureNoReparsePoints(plan.WorkspaceRoot, $"staging/{file.Path}");
            if (Directory.Exists(target)) throw new IOException($"A directory occupies an expected staging file path: {file.Path}");
            if (target.Equals(verifiedSource, StringComparison.OrdinalIgnoreCase)) continue;
            reportCopy(file);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            EnsureNoReparsePoints(plan.WorkspaceRoot, "partial");
            Directory.CreateDirectory(Path.Combine(plan.WorkspaceRoot, "partial"));
            var partial = Path.Combine(plan.WorkspaceRoot, "partial", Guid.NewGuid().ToString("N") + ".part");
            try
            {
                await using (var source = new FileStream(verifiedSource, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                // Publish only complete copies so cancellation leaves reusable staged files.
                File.Move(partial, target, true);
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadPreviousManifestAsync(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, LauncherUpdateManifest.FileName);
        EnsureNoReparsePoints(root, LauncherUpdateManifest.FileName);
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<LauncherUpdateManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException("The installed update manifest is invalid.");
        var paths = new List<string>();
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path);
            if (IsProtected(file.Path)) throw new InvalidDataException($"The installed manifest contains a protected path: {file.Path}");
            paths.Add(file.Path);
        }
        return paths;
    }

    private static void EnumerateManagedFiles(string root, string folder, HashSet<string> managed, CancellationToken token)
    {
        var first = ResolvePath(root, folder);
        EnsureNoReparsePoints(root, folder);
        if (!Directory.Exists(first)) return;
        var pending = new Stack<string>();
        pending.Push(first);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (IsProtected(relativePath)) continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"A symbolic link or junction is present in the installation: {Path.GetRelativePath(root, entry)}");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else managed.Add(relativePath);
            }
        }
    }

    private static void ValidatePlan(LauncherUpdatePlan plan)
    {
        LauncherUpdateWorkspace.Validate(plan.InstallationRoot, plan.WorkspaceRoot);
        if (plan.CommitSha.Length != 40 || !Scan70031(plan.CommitSha, Uri.IsHexDigit))
            throw new InvalidDataException("The update snapshot identifier is invalid.");
        [global::BEngine.SkipLoopGuard]
        bool Scan70031(string scanSource70031, global::System.Func<char, bool> scanPredicate70031)
        {
            ArgumentNullException.ThrowIfNull(scanSource70031, "source");
            ArgumentNullException.ThrowIfNull(scanPredicate70031, "predicate");
            foreach (var scanItem70031 in scanSource70031)
            {
                if (!scanPredicate70031(scanItem70031))
                    return false;
            }

            return true;
        }
        var files = new Dictionary<string, LauncherUpdateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            ValidateRelativePath(file.Path);
            if (IsProtected(file.Path) || file.Size < -1 || file.Sha.Length != 40 || !Scan70442(file.Sha, Uri.IsHexDigit) || !files.TryAdd(file.Path, file))
                throw new InvalidDataException($"Invalid file in the update plan: {file.Path}");
            [global::BEngine.SkipLoopGuard]
            bool Scan70442(string scanSource70442, global::System.Func<char, bool> scanPredicate70442)
            {
                ArgumentNullException.ThrowIfNull(scanSource70442, "source");
                ArgumentNullException.ThrowIfNull(scanPredicate70442, "predicate");
                foreach (var scanItem70442 in scanSource70442)
                {
                    if (!scanPredicate70442(scanItem70442))
                        return false;
                }

                return true;
            }
        }
        var changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in plan.Changes)
        {
            ValidateRelativePath(change.Path);
            if (IsProtected(change.Path) || !changes.Add(change.Path) || !Enum.IsDefined(change.Kind))
                throw new InvalidDataException($"Invalid change in the update plan: {change.Path}");
            if (change.Kind != LauncherUpdateChangeKind.Deleted &&
                (!files.TryGetValue(change.Path, out var file) || file.Size != change.Size))
                throw new InvalidDataException($"Missing file in the update plan: {change.Path}");
        }
        if (files.Values.Any(file => file.Size < 0 && !changes.Contains(file.Path)))
            throw new InvalidDataException("An unchanged file has an unresolved size in the update plan.");
        var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.OriginalFiles)
        {
            ValidateRelativePath(file.Path);
            if (IsProtected(file.Path) || !originals.Add(file.Path) || file.Size < 0 || (file.Exists && (file.Sha.Length != 40 || !Scan71613(file.Sha, Uri.IsHexDigit))))
                throw new InvalidDataException($"Invalid original file in the update plan: {file.Path}");
            [global::BEngine.SkipLoopGuard]
            bool Scan71613(string scanSource71613, global::System.Func<char, bool> scanPredicate71613)
            {
                ArgumentNullException.ThrowIfNull(scanSource71613, "source");
                ArgumentNullException.ThrowIfNull(scanPredicate71613, "predicate");
                foreach (var scanItem71613 in scanSource71613)
                {
                    if (!scanPredicate71613(scanItem71613))
                        return false;
                }

                return true;
            }
        }
        bool Query71776()
        {
            var querySource71776 = files.Keys.Concat(changes);
            ArgumentNullException.ThrowIfNull(querySource71776, "source");
            if (querySource71776.GetType() == typeof(string[]) || querySource71776.GetType() == typeof(global::System.Collections.Generic.List<string>))
            {
                global::System.ReadOnlySpan<string> queryValues71776 = querySource71776.GetType() == typeof(string[]) ? (string[])(object)querySource71776 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)querySource71776);
                foreach (var path in queryValues71776)
                {
                    if (!originals.Contains(path))
                        return true;
                }
            }
            else
            {
                foreach (var path in querySource71776)
                {
                    if (!originals.Contains(path))
                        return true;
                }
            }

            return false;
        }

        if (Query71776())
            throw new InvalidDataException("The update plan is missing the local preview baseline. Check for updates again.");
    }

    internal static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {stream.Length.ToString(CultureInfo.InvariantCulture)}\0"));
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            hash.AppendData(buffer.AsSpan(0, count));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool IsProtected(string path)
    {
        var segments = path.Split('/');
        return segments[0].Equals("Cache", StringComparison.OrdinalIgnoreCase) ||
               GitHubPackageManager.IsManagedPath(path) ||
               segments[0].Equals("Projects", StringComparison.OrdinalIgnoreCase) ||
               segments.Any(ProtectedRoots.Contains);
    }

    internal static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
            throw new InvalidDataException($"Unsafe update path: {path}");
        foreach (var segment in path.Split('/'))
        {
            var device = segment.Split('.')[0];
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                device.Equals("CON", StringComparison.OrdinalIgnoreCase) || device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) || device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (device.Length == 4 && (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 device[3] is >= '0' and <= '9'))
                throw new InvalidDataException($"Unsafe update path: {path}");
        }
    }

    internal static string ResolvePath(string root, string relative)
    {
        ValidateRelativePath(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(root, path) || path.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The update path escapes the installation: {relative}");
        return path;
    }

    private static bool IsInside(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static void EnsureNoReparsePoints(string root, string relative)
    {
        var current = Path.GetFullPath(root);
        for (var ancestor = new DirectoryInfo(current); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"The update directory cannot contain a symbolic link or junction: {ancestor.FullName}");
        if (relative.Length == 0) return;
        ValidateRelativePath(relative);
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"An update path contains a symbolic link or junction: {relative}");
        }
    }

    public void Dispose()
    {
        lock (_snapshotGate) { _disposed = true; Interlocked.Exchange(ref _previewSnapshot, null)?.Dispose(); }
    }
    internal void ClearPreview()
    {
        lock (_snapshotGate) { _previewSnapshot?.Dispose(); _previewSnapshot = null; }
    }
}
