using System.Globalization;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BEngine.Launcher;

/// <summary>Reads a fixed GitHub snapshot without requiring a local Git installation.</summary>
internal sealed class LauncherUpdateService : IDisposable
{
    private const string DefaultRepositoryApi = "https://api.github.com/repos/OnClick9927/BEngine/";
    private const string DefaultRawBase = "https://raw.githubusercontent.com/OnClick9927/BEngine/";
    private const int MaximumNetworkAttempts = 3;
    private const int MaximumDownloadNetworkAttempts = 5;
    private const int MaximumConcurrentDownloads = 16;
    private const int MaximumConcurrentLargeDownloads = 8;
    private const int MaximumConcurrentFileRanges = 4;
    private const long ParallelRangeSizeLimit = 4 * 1024 * 1024;
    private const long RangeProbeSize = 64 * 1024;
    private const int MaximumBlobApiRequests = 16;
    private const int ReservedTailBlobApiRequests = 4;
    private const int ReservedApiRequests = 10;
    private const long SmallFileSizeLimit = 64 * 1024;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);
    // Recover promptly when an established transfer stops delivering any data.
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SlowRangeObservation = TimeSpan.FromSeconds(15);
    private const long SlowRangeBytesPerSecond = 64 * 1024;
    private static readonly TimeSpan SlowDownloadObservation = TimeSpan.FromSeconds(30);
    private const long MinimumDownloadBytesPerSecond = 8 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ProtectedRoots = new(StringComparer.OrdinalIgnoreCase)
        { "Preferences", "EditorData", "Temp", ".git", ".bengine-update-manifest.json" };
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _repositoryApi;
    private readonly Uri _rawBase;
    private readonly Uri? _blobApiBase;
    private readonly object _apiBudgetGate = new();
    private int _remainingApiRequests = 60;
    private int _blobApiRequests;
    private bool _blobApiUnavailable;
    private readonly ConcurrentDictionary<string, byte> _http11Authorities = new(StringComparer.OrdinalIgnoreCase);

    internal LauncherUpdateService(HttpClient? httpClient = null, string? repositoryApiBase = null, string? rawBase = null,
        string? blobApiBase = null)
    {
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = MaximumConcurrentDownloads,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _repositoryApi = CreateBaseUri(repositoryApiBase ?? DefaultRepositoryApi);
        _rawBase = CreateBaseUri(rawBase ?? DefaultRawBase);
        // Default downloads can recover through GitHub's official content-addressed API.
        // Custom transports opt in explicitly so injected/offline clients never contact GitHub.
        _blobApiBase = blobApiBase is not null ? CreateBaseUri(blobApiBase) :
            repositoryApiBase is null && rawBase is null ? new Uri(_repositoryApi, "git/blobs/") : null;
    }

    internal Task<LauncherUpdatePlan> CheckAsync(string installationRoot,
        IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => CheckCoreAsync(installationRoot, progress, cancellationToken), cancellationToken);

    private async Task<LauncherUpdatePlan> CheckCoreAsync(string installationRoot,
        IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The Hub installation directory was not found.");
        EnsureNoReparsePoints(root, string.Empty);

        // The branch response omits commit patches, which can make commits/main unexpectedly large.
        using var branch = await GetJsonAsync(new Uri(_repositoryApi, "branches/main"),
            "Connecting", "Reading main branch", progress, cancellationToken).ConfigureAwait(false);
        var commit = branch.RootElement.GetProperty("commit");
        var commitSha = ReadSha(commit.GetProperty("sha"));
        var commitInfo = commit.GetProperty("commit");
        var rootTree = ReadSha(commitInfo.GetProperty("tree").GetProperty("sha"));
        var commitMessage = "Snapshot commit: " + (commitInfo.GetProperty("message").GetString() ?? string.Empty);
        var commitUrl = commit.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty;

        using var rootListing = await GetJsonAsync(new Uri(_repositoryApi, $"git/trees/{rootTree}"),
            "Listing", "Reading Engine directory", progress, cancellationToken).ConfigureAwait(false);
        EnsureCompleteTree(rootListing.RootElement);
        var engineEntry = rootListing.RootElement.GetProperty("tree").EnumerateArray()
            .FirstOrDefault(entry => entry.GetProperty("path").GetString() == "Engine" &&
                                     entry.GetProperty("type").GetString() == "tree");
        if (engineEntry.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("The main branch does not contain an Engine distribution directory.");
        var engineTree = ReadSha(engineEntry.GetProperty("sha"));

        using var engineListing = await GetJsonAsync(new Uri(_repositoryApi, $"git/trees/{engineTree}?recursive=1"),
            "Listing", "Reading Engine file list", progress, cancellationToken).ConfigureAwait(false);
        EnsureCompleteTree(engineListing.RootElement);
        var files = new List<LauncherUpdateFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in engineListing.RootElement.GetProperty("tree").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = entry.GetProperty("path").GetString() ?? string.Empty;
            ValidateRelativePath(path);
            if (IsProtected(path))
                throw new InvalidDataException($"The remote distribution contains a protected user-data path: {path}");
            var type = entry.GetProperty("type").GetString();
            var mode = entry.GetProperty("mode").GetString();
            if (type == "tree") continue;
            if (type != "blob" || mode is not ("100644" or "100755"))
                throw new InvalidDataException($"Unsupported symbolic link or submodule in the distribution: {path}");
            if (!seen.Add(path)) throw new InvalidDataException($"The remote distribution contains duplicate Windows paths: {path}");
            var size = entry.GetProperty("size").GetInt64();
            if (size < 0) throw new InvalidDataException($"Invalid remote file size: {path}");
            files.Add(new(path, ReadSha(entry.GetProperty("sha")), size));
        }
        if (!files.Any(file => file.Path.Equals("BEngine.Hub.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The remote Engine distribution does not contain BEngine.Hub.exe.");
        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));

        try
        {
            using var history = await GetJsonAsync(new Uri(_repositoryApi,
                $"commits?path=Engine&sha={commitSha}&per_page=5"), "Listing", "Reading recent Engine changes",
                progress, cancellationToken, maximumAttempts: 1, timeout: TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            var messages = history.RootElement.EnumerateArray().Select(entry =>
                entry.GetProperty("commit").GetProperty("message").GetString()).Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
            if (messages.Length != 0)
                commitMessage = "Recent Engine changes (not a complete changelog):\n\n" + string.Join("\n\n", messages);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // The verified file list remains the authoritative preview when optional history is unavailable.
        }

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
                if (localLength != file.Size || !string.Equals(localSha, file.Sha, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new(file.Path, LauncherUpdateChangeKind.Modified, file.Size));
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

    internal Task DownloadAsync(LauncherUpdatePlan plan, IProgress<LauncherUpdateProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => DownloadCoreAsync(plan, progress, cancellationToken), cancellationToken);

    private async Task DownloadCoreAsync(LauncherUpdatePlan plan, IProgress<LauncherUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();
        if (!plan.HasUpdates) return;
        ValidatePlan(plan);
        LauncherUpdateWorkspace.ThrowIfInstallationBusy(plan.InstallationRoot);
        using var downloadLock = LauncherUpdateWorkspace.AcquireDownloadLock(plan.InstallationRoot, plan.WorkspaceRoot);
        LauncherUpdateWorkspace.ThrowIfInstallationBusy(plan.InstallationRoot);
        EnsureNoReparsePoints(plan.InstallationRoot, string.Empty);
        // Recheck the preview baseline, including files about to be removed or replaced.
        long lastVerifyTimestamp = 0;
        for (var index = 0; index < plan.OriginalFiles.Count; index++)
        {
            var file = plan.OriginalFiles[index];
            EnsureNoReparsePoints(plan.InstallationRoot, file.Path);
            var now = Stopwatch.GetTimestamp();
            if (index == 0 || index == plan.OriginalFiles.Count - 1 || Stopwatch.GetElapsedTime(lastVerifyTimestamp, now).TotalMilliseconds >= 50)
            {
                lastVerifyTimestamp = now;
                progress.Report(new("Verifying", file.Path, index, plan.OriginalFiles.Count, 100d * index / plan.OriginalFiles.Count,
                    FilePath: file.Path));
            }
            var localPath = ResolvePath(plan.InstallationRoot, file.Path);
            if ((!file.Exists && (File.Exists(localPath) || Directory.Exists(localPath))) ||
                (file.Exists && (!File.Exists(localPath) || new FileInfo(localPath).Length != file.Size ||
                 !string.Equals(await HashFileAsync(localPath, cancellationToken).ConfigureAwait(false), file.Sha, StringComparison.OrdinalIgnoreCase))))
                throw new IOException($"The installation changed after the update check. Check for updates again: {file.Path}");
        }
        EnsureNoReparsePoints(plan.WorkspaceRoot, "staging");
        EnsureNoReparsePoints(plan.WorkspaceRoot, "partial");
        EnsureNoReparsePoints(plan.WorkspaceRoot, "update-plan.json");
        EnsureNoReparsePoints(plan.WorkspaceRoot, "update-plan.json.part");
        Directory.CreateDirectory(plan.StagingRoot);
        Directory.CreateDirectory(Path.Combine(plan.WorkspaceRoot, "partial"));
        if (File.Exists(plan.PlanPath)) File.Delete(plan.PlanPath);
        // Credit the retained snapshot before opening network connections. A complete
        // length alone never makes a staging file trusted: its Git blob SHA must match.
        var downloads = plan.GetChangedBlobGroups().OrderByDescending(group => group[0].Size).ToArray();
        var retainedSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var retainedBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long downloaded = 0;
        for (var index = 0; index < downloads.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = downloads[index];
            var file = group[0];
            var key = BlobCacheKey(file);
            foreach (var destination in group)
            {
                EnsureNoReparsePoints(plan.WorkspaceRoot, $"staging/{destination.Path}");
                var candidate = ResolvePath(plan.StagingRoot, destination.Path);
                if (Directory.Exists(candidate))
                    throw new IOException($"A directory occupies an expected staging file path: {destination.Path}");
                if (File.Exists(candidate) && new FileInfo(candidate).Length == file.Size &&
                    string.Equals(await HashFileAsync(candidate, cancellationToken).ConfigureAwait(false), file.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    retainedSources[key] = candidate;
                    break;
                }
            }
            var retained = retainedSources.ContainsKey(key) ? file.Size : GetRetainedBlobBytes(plan.WorkspaceRoot, file);
            retainedBytes[key] = retained;
            downloaded += retained;
            if (index == 0 || index == downloads.Length - 1 || index % 32 == 0)
                progress.Report(new("Verifying", "Checking retained download files", index + 1, downloads.Length,
                    100d * (index + 1) / downloads.Length, FilePath: file.Path));
        }
        var totalBytes = plan.DownloadBytes;
        var progressGate = new object();
        long lastProgressTimestamp = 0;
        void ReportCopy(LauncherUpdateFile file)
        {
            lock (progressGate)
            {
                var completed = Interlocked.Read(ref downloaded);
                progress.Report(new("Downloading", $"Copying verified file · {file.Path}", completed, totalBytes,
                    totalBytes == 0 ? 100 : Math.Min(100, 100d * completed / totalBytes),
                    FilePath: file.Path, Status: "Copying verified file"));
            }
        }
        progress.Report(new("Downloading", downloaded > 0 ? "Resuming retained download files" : "Downloading changed files",
            downloaded, totalBytes, totalBytes == 0 ? 100 : Math.Min(100, 100d * downloaded / totalBytes)));
        // Transfer each Git blob only once, even when several runtime variants use the same
        // content. Start large blobs first and let the small-file tail use additional slots.
        using var largeDownloadSlots = new SemaphoreSlim(MaximumConcurrentLargeDownloads);
        using var networkSlots = new SemaphoreSlim(MaximumConcurrentDownloads);
        var downloadFailures = new ConcurrentQueue<(string Path, Exception Error)>();
        var remainingBlobs = downloads.Length;
        await Parallel.ForEachAsync(downloads, new ParallelOptions { MaxDegreeOfParallelism = MaximumConcurrentDownloads, CancellationToken = cancellationToken },
            async (group, token) =>
            {
                var file = group[0];
                var isLarge = file.Size > SmallFileSizeLimit;
                if (isLarge) await largeDownloadSlots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var target = ResolvePath(plan.StagingRoot, file.Path);
                    foreach (var destination in group)
                    {
                        EnsureNoReparsePoints(plan.WorkspaceRoot, $"staging/{destination.Path}");
                        if (Directory.Exists(ResolvePath(plan.StagingRoot, destination.Path)))
                            throw new IOException($"A directory occupies an expected staging file path: {destination.Path}");
                    }
                    // Cancellation or a transient failure may leave complete files from this exact
                    // snapshot at any destination in this group. Check length and blob identity
                    // before using that staged copy as the source for the other destinations.
                    if (retainedSources.TryGetValue(BlobCacheKey(file), out var verifiedSource))
                    {
                        lock (progressGate)
                        {
                            var completed = Interlocked.Read(ref downloaded);
                            progress.Report(new("Downloading", $"Reusing verified file · {file.Path}", completed, totalBytes,
                                totalBytes == 0 ? 100 : Math.Min(100, 100d * completed / totalBytes),
                                FilePath: file.Path, Status: "Reusing verified file"));
                        }
                        await StageBlobCopiesAsync(plan, group, verifiedSource, ReportCopy, token).ConfigureAwait(false);
                        return;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var relativeUrl = $"{plan.CommitSha}/Engine/{string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString))}";
                    var uri = new Uri(_rawBase, relativeUrl);
                    await DownloadResumableBlobAsync(file, uri, plan.WorkspaceRoot, target,
                        retainedBytes[BlobCacheKey(file)], networkSlots, progress,
                        () => Volatile.Read(ref remainingBlobs) <= MaximumConcurrentDownloads,
                        (delta, force, status) =>
                        {
                            lock (progressGate)
                            {
                                var completed = Interlocked.Add(ref downloaded, delta);
                                var now = Stopwatch.GetTimestamp();
                                if (force || completed == totalBytes || Stopwatch.GetElapsedTime(lastProgressTimestamp, now).TotalMilliseconds >= 100)
                                {
                                    lastProgressTimestamp = now;
                                    progress.Report(new("Downloading", $"{file.Path} · {status}", completed, totalBytes,
                                        totalBytes == 0 ? 100 : Math.Min(100, 100d * completed / totalBytes),
                                        FilePath: file.Path, Status: status));
                                }
                            }
                        }, token).ConfigureAwait(false);
                    await StageBlobCopiesAsync(plan, group, target, ReportCopy, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (!token.IsCancellationRequested &&
                    exception is NetworkRequestException or InvalidDataException)
                {
                    // A single inaccessible blob must not cancel other large transfers and
                    // discard their progress. Finish verified staging; a retry reuses it.
                    downloadFailures.Enqueue((file.Path, exception));
                }
                finally
                {
                    Interlocked.Decrement(ref remainingBlobs);
                    if (isLarge) largeDownloadSlots.Release();
                }
            }).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (!downloadFailures.IsEmpty)
        {
            var failures = downloadFailures.ToArray();
            static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..(maximum - 3)] + "...";
            var paths = string.Join(", ", failures.Take(3).Select(failure => Limit(failure.Path, 160)));
            throw new IOException($"Unable to download {failures.Length} update file(s): {paths}" +
                (failures.Length > 3 ? ", ..." : string.Empty) + ". " + Limit(failures[0].Error.Message, 400) +
                " Verified files are retained for retry. No installed files were replaced.",
                new AggregateException(failures.Select(failure => new IOException(failure.Path, failure.Error))));
        }
        progress.Report(new("Verifying", "Saving the verified update plan", totalBytes, totalBytes, 100));
        var planPart = plan.PlanPath + ".part";
        await File.WriteAllTextAsync(planPart, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(planPart, plan.PlanPath, true);
        progress.Report(new("Ready", "Download verified; ready to install", totalBytes, totalBytes, 100));
    }

    private static string BlobCacheKey(LauncherUpdateFile file) =>
        file.Sha.ToLowerInvariant() + "-" + file.Size.ToString(CultureInfo.InvariantCulture);

    private static (long Start, long End)[] BlobRanges(LauncherUpdateFile file)
    {
        if (file.Size < ParallelRangeSizeLimit) return [];
        var remaining = file.Size - RangeProbeSize;
        var length = (remaining + MaximumConcurrentFileRanges - 1) / MaximumConcurrentFileRanges;
        return [(0, RangeProbeSize - 1), .. Enumerable.Range(0, MaximumConcurrentFileRanges)
            .Select(index => (Start: RangeProbeSize + index * length,
                End: Math.Min(file.Size - 1, RangeProbeSize + (index + 1) * length - 1)))
            .Where(range => range.Start <= range.End)];
    }

    private static string RangePartName(long start, long end) =>
        start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + ".part";

    private static long ReadRetainedPartLength(string workspaceRoot, string blobRelative, string name, long maximum)
    {
        var relative = blobRelative + "/" + name;
        EnsureNoReparsePoints(workspaceRoot, relative);
        var path = ResolvePath(workspaceRoot, relative);
        if (Directory.Exists(path)) throw new IOException("A directory occupies an update download file: " + relative);
        if (!File.Exists(path)) return 0;
        var length = new FileInfo(path).Length;
        // Range files are never preallocated. Their physical length is the number of
        // contiguous bytes actually written; an oversized file is damaged, not complete.
        return length <= maximum ? length : 0;
    }

    private static long GetRetainedBlobBytes(string workspaceRoot, LauncherUpdateFile file)
    {
        var relative = "partial/" + BlobCacheKey(file);
        var whole = ReadRetainedPartLength(workspaceRoot, relative, "whole.part", file.Size);
        var chunks = BlobRanges(file).Sum(range => ReadRetainedPartLength(workspaceRoot, relative,
            RangePartName(range.Start, range.End), range.End - range.Start + 1));
        // A fallback whole response and retained ranges can cover the same bytes.
        return Math.Max(whole, chunks);
    }

    private async Task DownloadResumableBlobAsync(LauncherUpdateFile file, Uri uri, string workspaceRoot,
        string target, long initialCredit, SemaphoreSlim networkSlots, IProgress<LauncherUpdateProgress> progress,
        Func<bool> allowReservedFallback, Action<long, bool, string> report, CancellationToken cancellationToken)
    {
        var blobRelative = "partial/" + BlobCacheKey(file);
        EnsureNoReparsePoints(workspaceRoot, blobRelative);
        var blobRoot = ResolvePath(workspaceRoot, blobRelative);
        Directory.CreateDirectory(blobRoot);
        var ranges = BlobRanges(file);
        var wholePath = ResolvePath(blobRoot, "whole.part");
        var assembledPath = ResolvePath(blobRoot, "verified.part");
        var rangeBytes = new long[ranges.Length];
        long chunkBytes = 0, wholeBytes = 0, credited = initialCredit;
        var creditGate = new object();
        void ReportState(string status, bool force = false)
        {
            lock (creditGate)
            {
                var available = Math.Max(Interlocked.Read(ref chunkBytes), Interlocked.Read(ref wholeBytes));
                var delta = available - credited;
                credited = available;
                report(delta, force, status);
            }
        }
        long ReadAndRepair(string name, long maximum)
        {
            var retained = ReadRetainedPartLength(workspaceRoot, blobRelative, name, maximum);
            var path = ResolvePath(blobRoot, name);
            if (File.Exists(path) && new FileInfo(path).Length > maximum) File.Delete(path);
            return retained;
        }
        void ResetParts()
        {
            // Remove only the known files for this content identity, never unrelated
            // workspaces or files an interrupted installer might still need.
            foreach (var name in ranges.Select(range => RangePartName(range.Start, range.End))
                         .Concat(["whole.part", "verified.part", "seed.part"]))
            {
                EnsureNoReparsePoints(workspaceRoot, blobRelative + "/" + name);
                var path = ResolvePath(blobRoot, name);
                if (File.Exists(path)) File.Delete(path);
            }
            Array.Clear(rangeBytes);
            Interlocked.Exchange(ref chunkBytes, 0);
            Interlocked.Exchange(ref wholeBytes, 0);
            ReportState("Discarding damaged download data", true);
        }
        void RemoveCompletedParts()
        {
            try
            {
                foreach (var name in ranges.Select(range => RangePartName(range.Start, range.End))
                             .Concat(["whole.part", "verified.part", "seed.part"]))
                {
                    EnsureNoReparsePoints(workspaceRoot, blobRelative + "/" + name);
                    var path = ResolvePath(blobRoot, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                if (!Directory.EnumerateFileSystemEntries(blobRoot).Any()) Directory.Delete(blobRoot);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The verified staging file is already published. An antivirus holding
                // a cached part must not turn a completed transfer into a failed update.
            }
        }

        // A retained prefix can itself have been damaged by an interrupted disk write.
        // Verify the complete Git blob and allow one bounded, clean recovery download.
        for (var integrityAttempt = 0; integrityAttempt < 2; integrityAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            wholeBytes = ReadAndRepair("whole.part", file.Size);
            chunkBytes = 0;
            for (var index = 0; index < ranges.Length; index++)
            {
                var range = ranges[index];
                rangeBytes[index] = ReadAndRepair(RangePartName(range.Start, range.End), range.End - range.Start + 1);
                chunkBytes += rangeBytes[index];
            }
            ReportState(credited > 0 ? "Resuming retained download data" : "Waiting for response", true);
            string completedPath;
            if (wholeBytes == file.Size && File.Exists(wholePath)) completedPath = wholePath;
            else if (ranges.Length == 0 || wholeBytes > chunkBytes)
            {
                await TransferWholeAsync(preferAlternate: false).ConfigureAwait(false);
                completedPath = wholePath;
            }
            else
            {
                var rangeFailed = false;
                try
                {
                    // The probe keeps an origin that ignores Range from multiplying a
                    // whole-body response across four simultaneous connections.
                    await TransferRangeAsync(0, cancellationToken).ConfigureAwait(false);
                    var failures = new ConcurrentQueue<Exception>();
                    using var rangeStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    await Parallel.ForEachAsync(Enumerable.Range(1, ranges.Length - 1), new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaximumConcurrentFileRanges,
                        CancellationToken = cancellationToken
                    }, async (index, _) =>
                    {
                        try { await TransferRangeAsync(index, rangeStop.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (rangeStop.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
                        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
                            error is NetworkRequestException or InvalidDataException or RangeRecoveryException)
                        {
                            failures.Enqueue(error);
                            rangeStop.Cancel();
                        }
                    }).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    rangeFailed = !failures.IsEmpty;
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
                    error is NetworkRequestException or InvalidDataException or RangeRecoveryException)
                {
                    rangeFailed = true;
                }
                if (rangeFailed)
                {
                    // A late failed blob gets a fresh bounded recovery pass even when
                    // earlier blobs consumed the small alternate-API budget. Raw remains
                    // eligible on every retry; API exhaustion never reduces this to one try.
                    report(0, true, "Recovering interrupted download");
                    await SeedWholeFromRangesAsync().ConfigureAwait(false);
                    await TransferWholeAsync(preferAlternate: true).ConfigureAwait(false);
                    completedPath = wholePath;
                }
                else
                {
                    EnsureNoReparsePoints(workspaceRoot, blobRelative + "/verified.part");
                    await using (var output = new FileStream(assembledPath, FileMode.Create, FileAccess.Write,
                                     FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        foreach (var range in ranges)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var partPath = ResolvePath(blobRoot, RangePartName(range.Start, range.End));
                            EnsureNoReparsePoints(workspaceRoot, blobRelative + "/" + Path.GetFileName(partPath));
                            await using var source = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            if (source.Length != range.End - range.Start + 1)
                                throw new InvalidDataException("A retained download range changed while being assembled: " + file.Path);
                            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                        }
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    completedPath = assembledPath;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(completedPath).Length == file.Size &&
                string.Equals(await HashFileAsync(completedPath, cancellationToken).ConfigureAwait(false), file.Sha, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(completedPath, target, true);
                RemoveCompletedParts();
                report(0, true, "Download verified");
                return;
            }
            ResetParts();
        }
        throw new InvalidDataException($"File integrity verification failed after a clean retry: {file.Path}. No installed files were replaced.");

        async Task SeedWholeFromRangesAsync()
        {
            long prefix = 0;
            for (var index = 0; index < ranges.Length; index++)
            {
                var range = ranges[index];
                if (range.Start != prefix) break;
                prefix += rangeBytes[index];
                if (rangeBytes[index] != range.End - range.Start + 1) break;
            }
            if (prefix <= Interlocked.Read(ref wholeBytes)) return;
            // Preserve the longest contiguous prefix when only the tail failed. Write
            // this conversion atomically so cancellation also preserves an older prefix.
            EnsureNoReparsePoints(workspaceRoot, blobRelative + "/seed.part");
            var seedPath = ResolvePath(blobRoot, "seed.part");
            await using (var output = new FileStream(seedPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                for (var index = 0; index < ranges.Length && output.Position < prefix; index++)
                {
                    var range = ranges[index];
                    var name = RangePartName(range.Start, range.End);
                    EnsureNoReparsePoints(workspaceRoot, blobRelative + "/" + name);
                    await using var source = new FileStream(ResolvePath(blobRoot, name), FileMode.Open, FileAccess.Read,
                        FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (source.Length != rangeBytes[index])
                        throw new InvalidDataException("A retained download range changed while resuming: " + file.Path);
                    await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoints(workspaceRoot, blobRelative + "/whole.part");
            File.Move(seedPath, wholePath, true);
            Interlocked.Exchange(ref wholeBytes, prefix);
            ReportState("Resuming retained download data", true);
        }

        async Task TransferRangeAsync(int index, CancellationToken token)
        {
            var range = ranges[index];
            if (rangeBytes[index] == range.End - range.Start + 1) return;
            await WithNetworkRetriesAsync(async attempt =>
            {
                await networkSlots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var offset = range.Start + Interlocked.Read(ref rangeBytes[index]);
                    if (offset > range.End) return true;
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestTimeout.CancelAfter(attempt == 1 ? ResponseTimeout : ResponseTimeout + ResponseTimeout);
                    var elapsed = Stopwatch.StartNew();
                    var headersReceived = false;
                    string Status(string state) => NetworkStatus(state, uri, attempt, MaximumNetworkAttempts, elapsed.Elapsed);
                    void Heartbeat() => ReportState(Status(headersReceived ? "Receiving data" : "Waiting for response"), true);
                    Heartbeat();
                    var heartbeat = ReportHeartbeatAsync(Heartbeat, heartbeatStop.Token);
                    try
                    {
                        using var request = CreateRequest(uri);
                        request.Version = HttpVersion.Version11;
                        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
                        request.Headers.AcceptEncoding.ParseAdd("identity");
                        request.Headers.Range = new RangeHeaderValue(offset, range.End);
                        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                            requestTimeout.Token).ConfigureAwait(false);
                        EnsureNetworkSuccess(response, uri);
                        headersReceived = true;
                        if (response.StatusCode == HttpStatusCode.OK)
                            throw new RangeRecoveryException("The download origin does not support partial requests.");
                        ValidateContentRange(response, offset, range.End, file);
                        var name = RangePartName(range.Start, range.End);
                        EnsureNoReparsePoints(workspaceRoot, blobRelative + "/" + name);
                        await using var output = new FileStream(ResolvePath(blobRoot, name), FileMode.OpenOrCreate,
                            FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        output.Position = offset - range.Start;
                        var observation = Stopwatch.StartNew();
                        var responseStart = offset;
                        requestTimeout.CancelAfter(attempt == 1 ? DownloadIdleTimeout : DownloadIdleTimeout + DownloadIdleTimeout);
                        try
                        {
                            await using var source = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
                            var buffer = new byte[81920];
                            int count;
                            while ((count = await ReadNetworkAsync(source, buffer, requestTimeout.Token).ConfigureAwait(false)) != 0)
                            {
                                if (count > range.End - offset + 1)
                                    throw new InvalidDataException("The downloaded range exceeds its expected size: " + file.Path);
                                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                                offset += count;
                                Interlocked.Add(ref rangeBytes[index], count);
                                Interlocked.Add(ref chunkBytes, count);
                                ReportState(Status("Receiving data"));
                                requestTimeout.CancelAfter(attempt == 1 ? DownloadIdleTimeout : DownloadIdleTimeout + DownloadIdleTimeout);
                                if (observation.Elapsed >= SlowRangeObservation && range.End - offset + 1 >= 1024 * 1024 &&
                                    (offset - responseStart) / observation.Elapsed.TotalSeconds < SlowRangeBytesPerSecond &&
                                    CanUseBlobApi(file, allowReservedFallback()))
                                    throw new RangeRecoveryException("Recovering a slow download through the alternate route.");
                                if (observation.Elapsed >= SlowDownloadObservation && range.End - offset + 1 >= 64 * 1024 &&
                                    (offset - responseStart) / observation.Elapsed.TotalSeconds < MinimumDownloadBytesPerSecond)
                                    throw new RetryableNetworkException("The file range is making insufficient progress; resuming on a fresh connection.", isTransportFailure: true);
                            }
                            if (offset != range.End + 1)
                                throw new RetryableNetworkException("The file range transfer ended before all bytes arrived.", isTransportFailure: true);
                        }
                        finally { await output.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
                        Heartbeat();
                        return true;
                    }
                    finally
                    {
                        heartbeatStop.Cancel();
                        await heartbeat.ConfigureAwait(false);
                    }
                }
                finally { networkSlots.Release(); }
            }, uri, "Downloading", file.Path, progress, token, reportRetry: (_, nextAttempt) =>
                ReportState($"Retrying network request · {uri.Host} · {nextAttempt}/{MaximumNetworkAttempts}", true)).ConfigureAwait(false);
        }

        async Task TransferWholeAsync(bool preferAlternate)
        {
            // Empty blobs still go through final Git-SHA verification, without a request.
            if (file.Size == 0)
            {
                EnsureNoReparsePoints(workspaceRoot, blobRelative + "/whole.part");
                await File.WriteAllBytesAsync(wholePath, [], cancellationToken).ConfigureAwait(false);
                return;
            }
            await WithNetworkRetriesAsync(async attempt =>
            {
                var offset = Interlocked.Read(ref wholeBytes);
                if (offset == file.Size) return true;
                await networkSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var transferUri = (preferAlternate && attempt == 1 || attempt is 2 or 4)
                        ? TryCreateBlobApiUri(file, allowReservedFallback()) ?? uri : uri;
                    var useBlobApi = transferUri != uri;
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    requestTimeout.CancelAfter(attempt == 1 ? ResponseTimeout : ResponseTimeout + ResponseTimeout);
                    var elapsed = Stopwatch.StartNew();
                    var headersReceived = false;
                    string Status(string state) => NetworkStatus(state, transferUri, attempt, MaximumDownloadNetworkAttempts, elapsed.Elapsed);
                    void Heartbeat() => ReportState(Status(headersReceived ? "Receiving data" : "Waiting for response"), true);
                    Heartbeat();
                    var heartbeat = ReportHeartbeatAsync(Heartbeat, heartbeatStop.Token);
                    try
                    {
                        using var request = CreateRequest(transferUri);
                        request.Headers.AcceptEncoding.ParseAdd("identity");
                        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, file.Size - 1);
                        if (useBlobApi)
                        {
                            request.Headers.Accept.Clear();
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
                        }
                        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                            requestTimeout.Token).ConfigureAwait(false);
                        if (useBlobApi)
                        {
                            ObserveApiBudget(response);
                            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or
                                HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
                            {
                                DisableBlobApi();
                                throw new RetryableNetworkException($"The alternate GitHub route is unavailable (HTTP {(int)response.StatusCode}).");
                            }
                        }
                        EnsureNetworkSuccess(response, transferUri);
                        headersReceived = true;
                        if (response.StatusCode == HttpStatusCode.PartialContent)
                            ValidateContentRange(response, offset, file.Size - 1, file);
                        else if (response.StatusCode == HttpStatusCode.OK)
                        {
                            if (response.Content.Headers.ContentLength is { } contentLength && contentLength != file.Size)
                                throw new RetryableNetworkException("The downloaded file has an unexpected size: " + file.Path);
                            // A server that ignores Range supplies a whole response. Restart
                            // only this prefix, preserving independently retained range files.
                            offset = 0;
                            Interlocked.Exchange(ref wholeBytes, 0);
                            ReportState(Status("Restarting non-resumable response"), true);
                        }
                        else throw new RetryableNetworkException("The download returned an unexpected HTTP response.");
                        EnsureNoReparsePoints(workspaceRoot, blobRelative + "/whole.part");
                        await using var output = new FileStream(wholePath, FileMode.OpenOrCreate, FileAccess.Write,
                            FileShare.None, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        output.SetLength(offset);
                        output.Position = offset;
                        var observation = Stopwatch.StartNew();
                        var responseStart = offset;
                        requestTimeout.CancelAfter(attempt == 1 ? DownloadIdleTimeout : DownloadIdleTimeout + DownloadIdleTimeout);
                        try
                        {
                            await using var source = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
                            var buffer = new byte[81920];
                            int count;
                            while ((count = await ReadNetworkAsync(source, buffer, requestTimeout.Token).ConfigureAwait(false)) != 0)
                            {
                                if (count > file.Size - offset)
                                    throw new InvalidDataException("The downloaded file exceeds its expected size: " + file.Path);
                                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                                offset += count;
                                Interlocked.Exchange(ref wholeBytes, offset);
                                ReportState(Status("Receiving data"));
                                requestTimeout.CancelAfter(attempt == 1 ? DownloadIdleTimeout : DownloadIdleTimeout + DownloadIdleTimeout);
                                if (observation.Elapsed >= SlowDownloadObservation && file.Size - offset >= 64 * 1024 &&
                                    (offset - responseStart) / observation.Elapsed.TotalSeconds < MinimumDownloadBytesPerSecond)
                                    throw new RetryableNetworkException("The file download is making insufficient progress; resuming on a fresh connection.", isTransportFailure: true);
                            }
                            if (offset != file.Size)
                                throw new RetryableNetworkException("The file transfer ended before all bytes arrived.", isTransportFailure: true);
                        }
                        finally { await output.FlushAsync(CancellationToken.None).ConfigureAwait(false); }
                        Heartbeat();
                        return true;
                    }
                    finally
                    {
                        heartbeatStop.Cancel();
                        await heartbeat.ConfigureAwait(false);
                    }
                }
                finally { networkSlots.Release(); }
            }, uri, "Downloading", file.Path, progress, cancellationToken, maximumAttempts: MaximumDownloadNetworkAttempts,
                reportRetry: (_, nextAttempt) => ReportState(
                    $"Retrying network request · {uri.Host} · {nextAttempt}/{MaximumDownloadNetworkAttempts}", true)).ConfigureAwait(false);
        }
    }

    private static void ValidateContentRange(HttpResponseMessage response, long start, long end, LauncherUpdateFile file)
    {
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range is null ||
            !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) ||
            range.From != start || range.To != end || range.Length != file.Size ||
            response.Content.Headers.ContentLength is { } length && length != end - start + 1)
            throw new InvalidDataException("The downloaded file has an invalid Content-Range: " + file.Path);
    }

    private sealed class RangeRecoveryException(string message) : IOException(message);

    private bool CanUseBlobApi(LauncherUpdateFile file, bool allowReservedFallback)
    {
        if (_blobApiBase is null || file.Size > 100_000_000) return false;
        lock (_apiBudgetGate)
            return !_blobApiUnavailable && _blobApiRequests <
                (allowReservedFallback ? MaximumBlobApiRequests : MaximumBlobApiRequests - ReservedTailBlobApiRequests) &&
                _remainingApiRequests > ReservedApiRequests;
    }

    private Uri? TryCreateBlobApiUri(LauncherUpdateFile file, bool allowReservedFallback)
    {
        // GitHub's blob endpoint supports at most 100 MB. Leave API capacity for
        // metadata checks and use it only as a bounded recovery route.
        if (_blobApiBase is null || file.Size > 100_000_000) return null;
        lock (_apiBudgetGate)
        {
            // Reserve a few official API requests for the last files instead of
            // exhausting every recovery route on early transient failures.
            var limit = allowReservedFallback ? MaximumBlobApiRequests : MaximumBlobApiRequests - ReservedTailBlobApiRequests;
            if (_blobApiUnavailable || _blobApiRequests >= limit || _remainingApiRequests <= ReservedApiRequests)
                return null;
            _blobApiRequests++;
            _remainingApiRequests--;
            return new Uri(_blobApiBase, file.Sha.ToLowerInvariant());
        }
    }

    private void ObserveApiBudget(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) ||
            !int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var remaining) || remaining < 0)
            return;
        lock (_apiBudgetGate) _remainingApiRequests = Math.Min(_remainingApiRequests, remaining);
    }

    private void DisableBlobApi()
    {
        lock (_apiBudgetGate) _blobApiUnavailable = true;
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

    private Task<JsonDocument> GetJsonAsync(Uri uri, string stage, string label,
        IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken,
        int maximumAttempts = MaximumNetworkAttempts, TimeSpan? timeout = null) =>
        WithNetworkRetriesAsync(async attempt =>
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var idleTimeout = timeout ?? ResponseTimeout;
            requestTimeout.CancelAfter(idleTimeout);
            var elapsed = Stopwatch.StartNew();
            long received = 0, total = 0;
            var headersReceived = false;
            void Report() => progress.Report(new(stage,
                NetworkMessage(label, headersReceived ? "Receiving data" : "Waiting for response", uri, attempt, maximumAttempts, elapsed.Elapsed),
                Interlocked.Read(ref received), Interlocked.Read(ref total),
                Interlocked.Read(ref total) > 0 ? Math.Min(100, 100d * Interlocked.Read(ref received) / Interlocked.Read(ref total)) : null));
            Report();
            var heartbeat = ReportHeartbeatAsync(Report, heartbeatStop.Token);
            try
            {
                using var request = CreateRequest(uri);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
                ObserveApiBudget(response);
                EnsureNetworkSuccess(response, uri);
                headersReceived = true;
                total = response.Content.Headers.ContentLength ?? 0;
                const int maximumMetadataBytes = 32 * 1024 * 1024;
                if (total > maximumMetadataBytes) throw new InvalidDataException("The update metadata exceeds its maximum supported size.");
                requestTimeout.CancelAfter(idleTimeout);
                await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
                using var payload = new MemoryStream();
                var buffer = new byte[81920];
                long lastProgressTimestamp = 0;
                while (true)
                {
                    var count = await ReadNetworkAsync(stream, buffer, requestTimeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > maximumMetadataBytes) throw new InvalidDataException("The update metadata exceeds its maximum supported size.");
                    payload.Write(buffer.AsSpan(0, count));
                    requestTimeout.CancelAfter(idleTimeout);
                    var now = Stopwatch.GetTimestamp();
                    if (received == total || Stopwatch.GetElapsedTime(lastProgressTimestamp, now).TotalMilliseconds >= 100)
                    {
                        lastProgressTimestamp = now;
                        Report();
                    }
                }
                if (total > 0 && received != total) throw new RetryableNetworkException("The metadata transfer ended before all bytes arrived.", isTransportFailure: true);
                cancellationToken.ThrowIfCancellationRequested();
                Report();
                return JsonDocument.Parse(payload.GetBuffer().AsMemory(0, (int)payload.Length));
            }
            finally
            {
                heartbeatStop.Cancel();
                await heartbeat.ConfigureAwait(false);
            }
        }, uri, stage, label, progress, cancellationToken, maximumAttempts);

    private async Task<T> WithNetworkRetriesAsync<T>(Func<int, Task<T>> operation, Uri uri,
        string stage, string label, IProgress<LauncherUpdateProgress> progress, CancellationToken cancellationToken,
        int maximumAttempts = MaximumNetworkAttempts, Action<string, int>? reportRetry = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Exception failure;
            var timedOut = false;
            try { return await operation(attempt).ConfigureAwait(false); }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure = exception;
                timedOut = true;
            }
            catch (HttpRequestException exception) { failure = exception; }
            catch (RetryableNetworkException exception) { failure = exception; }
            cancellationToken.ThrowIfCancellationRequested();
            // A connection reset or stalled HTTP/2 connection can interrupt every multiplexed
            // file at once. Retry this authority over independent, bounded HTTP/1.1 connections
            // for the rest of this service session. HTTP responses and integrity failures do
            // not change transport policy, and unrelated authorities retain HTTP/2.
            if (timedOut || failure is HttpRequestException { StatusCode: null } or RetryableNetworkException { IsTransportFailure: true })
                _http11Authorities.TryAdd(uri.GetLeftPart(UriPartial.Authority), 0);
            if (attempt == maximumAttempts)
                throw new NetworkRequestException((timedOut ? "The network request timed out." : "The network request failed after retries.") +
                                      $" · {uri.Host} · {uri.AbsolutePath} · {attempt}/{maximumAttempts}" +
                                      (timedOut ? string.Empty : " · " + failure.GetBaseException().Message), failure);
            var retryMessage = $"{label} · Retrying network request · {uri.Host} · {attempt + 1}/{maximumAttempts}";
            if (reportRetry is null) progress.Report(new(stage, retryMessage));
            else reportRetry(retryMessage, attempt + 1);
            // The short, cancellable backoff also avoids hammering GitHub during transient failures.
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<int> ReadNetworkAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try { return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); }
        catch (IOException exception) { throw new RetryableNetworkException("The network connection was interrupted.", exception, isTransportFailure: true); }
    }

    private static async Task ReportHeartbeatAsync(Action report, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                report();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static string NetworkMessage(string label, string status, Uri uri, int attempt, int maximumAttempts, TimeSpan elapsed) =>
        $"{label} · {NetworkStatus(status, uri, attempt, maximumAttempts, elapsed)}";

    private static string NetworkStatus(string status, Uri uri, int attempt, int maximumAttempts, TimeSpan elapsed) =>
        $"{status} · {uri.Host} · {attempt}/{maximumAttempts} · {elapsed.TotalSeconds:F0} s";

    private static void EnsureNetworkSuccess(HttpResponseMessage response, Uri uri)
    {
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            throw new RetryableNetworkException($"GitHub returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        EnsureSuccess(response, uri);
    }

    private sealed class RetryableNetworkException(string message, Exception? inner = null, bool isTransportFailure = false) : IOException(message, inner)
    {
        internal bool IsTransportFailure => isTransportFailure;
    }

    private sealed class NetworkRequestException(string message, Exception? inner = null) : IOException(message, inner);

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        var useHttp11 = _http11Authorities.ContainsKey(uri.GetLeftPart(UriPartial.Authority));
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = useHttp11 ? HttpVersion.Version11 : HttpVersion.Version20,
            VersionPolicy = useHttp11 ? HttpVersionPolicy.RequestVersionExact : HttpVersionPolicy.RequestVersionOrLower
        };
        request.Headers.UserAgent.ParseAdd("BEngine-Hub/0.1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, Uri uri)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.ToString();
            if (retry is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) &&
                long.TryParse(resets.FirstOrDefault(), out var seconds))
                retry = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            throw new NetworkRequestException($"GitHub denied or rate-limited the request (HTTP {(int)response.StatusCode}) · {uri.Host} · {uri.AbsolutePath}." +
                                  (retry is { Length: > 0 } ? $" Retry after {retry}." : " Retry later."));
        }
        throw new NetworkRequestException($"GitHub returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) · {uri.Host} · {uri.AbsolutePath}. No installed files were replaced.");
    }

    private static void EnsureCompleteTree(JsonElement element)
    {
        if (element.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
            throw new InvalidDataException("GitHub returned an incomplete directory listing. The update was stopped to protect the installation.");
    }

    private static string ReadSha(JsonElement element)
    {
        var sha = element.GetString();
        if (sha is null || sha.Length != 40 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("GitHub returned an invalid snapshot identifier.");
        return sha.ToLowerInvariant();
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
        if (plan.CommitSha.Length != 40 || !plan.CommitSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update snapshot identifier is invalid.");
        var files = new Dictionary<string, LauncherUpdateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            ValidateRelativePath(file.Path);
            if (IsProtected(file.Path) || file.Size < 0 || file.Sha.Length != 40 || !file.Sha.All(Uri.IsHexDigit) || !files.TryAdd(file.Path, file))
                throw new InvalidDataException($"Invalid file in the update plan: {file.Path}");
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
        var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.OriginalFiles)
        {
            ValidateRelativePath(file.Path);
            if (IsProtected(file.Path) || !originals.Add(file.Path) || file.Size < 0 ||
                (file.Exists && (file.Sha.Length != 40 || !file.Sha.All(Uri.IsHexDigit))))
                throw new InvalidDataException($"Invalid original file in the update plan: {file.Path}");
        }
        if (files.Keys.Concat(changes).Any(path => !originals.Contains(path)))
            throw new InvalidDataException("The update plan is missing the local preview baseline. Check for updates again.");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {stream.Length.ToString(CultureInfo.InvariantCulture)}\0"));
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            hash.AppendData(buffer.AsSpan(0, count));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static Uri CreateBaseUri(string value)
    {
        var uri = new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new ArgumentException("The update endpoint must use HTTPS.", nameof(value));
        return uri;
    }

    private static bool IsProtected(string path)
    {
        var segments = path.Split('/');
        return segments[0].Equals("Cache", StringComparison.OrdinalIgnoreCase) ||
               segments[0].Equals("Projects", StringComparison.OrdinalIgnoreCase) ||
               segments.Any(ProtectedRoots.Contains);
    }

    private static void ValidateRelativePath(string path)
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

    private static string ResolvePath(string root, string relative)
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

    private static void EnsureNoReparsePoints(string root, string relative)
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
        if (_ownsHttp) _http.Dispose();
    }
}
