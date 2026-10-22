using System.Globalization;
using System.Diagnostics;
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
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ProtectedRoots = new(StringComparer.OrdinalIgnoreCase)
        { "Preferences", "EditorData", "Temp", ".git", ".bengine-update-manifest.json" };
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _repositoryApi;
    private readonly Uri _rawBase;

    internal LauncherUpdateService(HttpClient? httpClient = null, string? repositoryApiBase = null, string? rawBase = null)
    {
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _repositoryApi = CreateBaseUri(repositoryApiBase ?? DefaultRepositoryApi);
        _rawBase = CreateBaseUri(rawBase ?? DefaultRawBase);
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

        progress.Report(new("Connecting", "Reading main branch"));
        using var commit = await GetJsonAsync(new Uri(_repositoryApi, "commits/main"), cancellationToken).ConfigureAwait(false);
        var commitSha = ReadSha(commit.RootElement.GetProperty("sha"));
        var commitInfo = commit.RootElement.GetProperty("commit");
        var rootTree = ReadSha(commitInfo.GetProperty("tree").GetProperty("sha"));
        var commitMessage = "Snapshot commit: " + (commitInfo.GetProperty("message").GetString() ?? string.Empty);
        var commitUrl = commit.RootElement.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty;

        progress.Report(new("Listing", "Reading Engine directory"));
        using var rootListing = await GetJsonAsync(new Uri(_repositoryApi, $"git/trees/{rootTree}"), cancellationToken).ConfigureAwait(false);
        EnsureCompleteTree(rootListing.RootElement);
        var engineEntry = rootListing.RootElement.GetProperty("tree").EnumerateArray()
            .FirstOrDefault(entry => entry.GetProperty("path").GetString() == "Engine" &&
                                     entry.GetProperty("type").GetString() == "tree");
        if (engineEntry.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("The main branch does not contain an Engine distribution directory.");
        var engineTree = ReadSha(engineEntry.GetProperty("sha"));

        using var engineListing = await GetJsonAsync(new Uri(_repositoryApi, $"git/trees/{engineTree}?recursive=1"), cancellationToken).ConfigureAwait(false);
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

        progress.Report(new("Listing", "Reading recent Engine changes"));
        try
        {
            using var history = await GetJsonAsync(new Uri(_repositoryApi,
                $"commits?path=Engine&sha={commitSha}&per_page=5"), cancellationToken, 15).ConfigureAwait(false);
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
                progress.Report(new("Comparing", file.Path, index, files.Count, 100d * index / files.Count));
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

        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BEngine", "HubUpdates", Guid.NewGuid().ToString("N")));
        if (IsInside(root, workspace)) throw new IOException("The update workspace must be outside the Engine installation directory.");
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
        EnsureNoReparsePoints(plan.InstallationRoot, string.Empty);
        var changedPaths = plan.Changes.Where(change => change.Kind != LauncherUpdateChangeKind.Deleted)
            .Select(change => change.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                progress.Report(new("Verifying", file.Path, index, plan.OriginalFiles.Count, 100d * index / plan.OriginalFiles.Count));
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
        long downloaded = 0;
        var totalBytes = plan.DownloadBytes;
        var progressGate = new object();
        long lastProgressTimestamp = 0;
        progress.Report(new("Downloading", "Downloading changed files", 0, totalBytes, totalBytes == 0 ? 100 : 0));
        var downloads = plan.Files.Where(file => changedPaths.Contains(file.Path)).ToArray();
        await Parallel.ForEachAsync(downloads, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (file, token) =>
            {
                var target = ResolvePath(plan.StagingRoot, file.Path);
                EnsureNoReparsePoints(plan.WorkspaceRoot, $"staging/{file.Path}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var partial = Path.Combine(plan.WorkspaceRoot, "partial", Guid.NewGuid().ToString("N") + ".part");
                var relativeUrl = $"{plan.CommitSha}/Engine/{string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString))}";
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromMinutes(10));
                try
                {
                    using var request = CreateRequest(new Uri(_rawBase, relativeUrl));
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
                    EnsureSuccess(response);
                    if (response.Content.Headers.ContentLength is { } contentLength && contentLength != file.Size)
                        throw new InvalidDataException($"The downloaded file has an unexpected size: {file.Path}");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                    hash.AppendData(Encoding.ASCII.GetBytes($"blob {file.Size.ToString(CultureInfo.InvariantCulture)}\0"));
                    await using var source = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
                    await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        int count;
                        while ((count = await source.ReadAsync(buffer, requestTimeout.Token).ConfigureAwait(false)) != 0)
                        {
                            received += count;
                            if (received > file.Size) throw new InvalidDataException($"The downloaded file exceeds its expected size: {file.Path}");
                            hash.AppendData(buffer.AsSpan(0, count));
                            await output.WriteAsync(buffer.AsMemory(0, count), requestTimeout.Token).ConfigureAwait(false);
                            Interlocked.Add(ref downloaded, count);
                            lock (progressGate)
                            {
                                var completed = Interlocked.Read(ref downloaded);
                                var now = Stopwatch.GetTimestamp();
                                if (completed == totalBytes || Stopwatch.GetElapsedTime(lastProgressTimestamp, now).TotalMilliseconds >= 100)
                                {
                                    lastProgressTimestamp = now;
                                    progress.Report(new("Downloading", file.Path, completed, totalBytes,
                                        totalBytes == 0 ? 100 : Math.Min(100, 100d * completed / totalBytes)));
                                }
                            }
                        }
                        if (received != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"File integrity verification failed: {file.Path}. No installed files were replaced.");
                        await output.FlushAsync(requestTimeout.Token).ConfigureAwait(false);
                    }
                    File.Move(partial, target, true);
                    lock (progressGate)
                    {
                        var completed = Interlocked.Read(ref downloaded);
                        lastProgressTimestamp = Stopwatch.GetTimestamp();
                        progress.Report(new("Downloading", file.Path, completed, totalBytes,
                            totalBytes == 0 ? 100 : Math.Min(100, 100d * completed / totalBytes)));
                    }
                }
                catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
                {
                    throw new IOException($"The download timed out: {file.Path}. Retry when the network is available.", exception);
                }
                finally
                {
                    if (File.Exists(partial)) File.Delete(partial);
                }
            }).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new("Verifying", "Saving the verified update plan", totalBytes, totalBytes, 100));
        var planPart = plan.PlanPath + ".part";
        await File.WriteAllTextAsync(planPart, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(planPart, plan.PlanPath, true);
        progress.Report(new("Ready", "Download verified; ready to install", totalBytes, totalBytes, 100));
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken, int timeoutSeconds = 90)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var request = CreateRequest(uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            EnsureSuccess(response);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("The update check timed out. Retry when the network is available.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new IOException("Could not contact GitHub. Check the network connection and retry.", exception);
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("BEngine-Hub/0.1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.ToString();
            if (retry is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) &&
                long.TryParse(resets.FirstOrDefault(), out var seconds))
                retry = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            throw new IOException($"GitHub denied or rate-limited the request (HTTP {(int)response.StatusCode})." +
                                  (retry is { Length: > 0 } ? $" Retry after {retry}." : " Retry later."));
        }
        throw new IOException($"GitHub returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). No installed files were replaced.");
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
        if (!Path.IsPathFullyQualified(plan.InstallationRoot) || !Path.IsPathFullyQualified(plan.WorkspaceRoot) ||
            IsInside(plan.InstallationRoot, plan.WorkspaceRoot) || IsInside(plan.WorkspaceRoot, plan.InstallationRoot))
            throw new InvalidDataException("The update workspace must be separate from the installation directory.");
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

    private static bool IsProtected(string path) => path.Split('/').Any(ProtectedRoots.Contains);

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
