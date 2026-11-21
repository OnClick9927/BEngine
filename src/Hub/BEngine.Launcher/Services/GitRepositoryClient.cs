using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BEngine.Launcher;

internal sealed record GitRepositoryFile(string Path, string Sha, long Size);
internal sealed class GitRepositoryException(string message) : IOException(message);
internal sealed record GitCommandPolicy(TimeSpan MetadataTimeout, TimeSpan TransferTimeout, TimeSpan RetryDelay)
{
    internal static GitCommandPolicy Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(1));
}

/// <summary>Owns a shallow, sparse Git snapshot under the Hub's Temp directory.</summary>
internal sealed class GitRepositoryClient(Func<string, string>? remoteResolver = null, GitCommandPolicy? policy = null)
{
    internal async Task<GitRepositorySnapshot> OpenAsync(string repositoryUrl, string reference, string directory,
        IProgress<string> progress, CancellationToken token, string? installationRoot = null, string treePath = "", bool metadataOnly = false)
    {
        var parent = Path.Combine(installationRoot ?? LauncherInstallationPaths.ResolveRoot(), "Temp", "HubGit");
        LauncherUpdateService.EnsureNoReparsePoints(parent, "");
        var snapshot = new GitRepositorySnapshot(Path.Combine(parent, Guid.NewGuid().ToString("N")), progress, policy);
        Directory.CreateDirectory(snapshot.Root);
        try
        {
            var remote = remoteResolver?.Invoke(repositoryUrl) ?? repositoryUrl;
            snapshot.Remote = Uri.TryCreate(remote, UriKind.Absolute, out var remoteUri) ? remoteUri : null;
            await snapshot.RunAsync(["init", "--quiet", "--object-format=sha1"], token);
            var info = Path.Combine(snapshot.Root, ".git", "info");
            Directory.CreateDirectory(info);
            // Install committed bytes, independently of local EOL conversion and configured smudge filters.
            await File.WriteAllTextAsync(Path.Combine(info, "attributes"), "* -text -filter -ident -working-tree-encoding\n", token);
            progress.Report("Reading Git references");
            var refs = await snapshot.RunAsync(["ls-remote", "--symref", "--", remote, "HEAD", "refs/heads/*", "refs/tags/*"], token);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var defaultBranch = "";
            foreach (var line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var columns = line.Split('\t');
                if (columns.Length != 2) continue;
                if (columns[0].StartsWith("ref: refs/heads/", StringComparison.Ordinal) && columns[1] == "HEAD") defaultBranch = columns[0][16..];
                else if (Sha(columns[0])) names[columns[1]] = columns[0];
            }
            string? Resolve(string name) => names.GetValueOrDefault("refs/heads/" + name) ??
                names.GetValueOrDefault("refs/tags/" + name + "^{}") ?? names.GetValueOrDefault("refs/tags/" + name) ?? (Sha(name) ? name : null);
            if (treePath.Length > 0 && reference.Length == 0)
            {
                var parts = treePath.Split('/');
                for (var count = parts.Length; count > 0; count--)
                {
                    var candidate = string.Join('/', parts.Take(count));
                    if (Resolve(candidate) is null) continue;
                    reference = candidate;
                    if (directory.Length == 0) directory = string.Join('/', parts.Skip(count));
                    break;
                }
                if (reference.Length == 0) throw new GitRepositoryException("The branch in the GitHub URL was not found.");
            }
            else
            {
                if (reference.Length == 0) reference = defaultBranch;
                if (treePath.Length > 0 && directory.Length == 0)
                {
                    if (treePath == reference) directory = "";
                    else if (treePath.StartsWith(reference + "/", StringComparison.Ordinal)) directory = treePath[(reference.Length + 1)..];
                    else throw new GitRepositoryException("The Git revision does not match the GitHub tree URL.");
                }
            }
            var sha = Resolve(reference) ?? (reference.Length == 0 ? names.GetValueOrDefault("HEAD") : null)
                ?? throw new GitRepositoryException("The Git branch, tag or commit was not found.");
            if (directory.Length > 0) LauncherUpdateService.ValidateRelativePath(directory);
            await snapshot.RunAsync(["remote", "add", "origin", remote], token);
            progress.Report("Fetching Git repository");
            await snapshot.RunAsync(["fetch", "--depth=1", "--no-tags", "--filter=blob:none", "--progress", "origin", sha], token);
            snapshot.Commit = (await snapshot.RunAsync(["rev-parse", "FETCH_HEAD^{commit}"], token)).Trim();
            if (!Sha(snapshot.Commit)) throw new InvalidDataException("Invalid Git commit identifier.");
            snapshot.Reference = reference; snapshot.Directory = directory;
            snapshot.Tree = (await snapshot.RunAsync(["rev-parse", directory.Length == 0 ? snapshot.Commit + "^{tree}" : snapshot.Commit + ":" + directory], token)).Trim();
            if (!Sha(snapshot.Tree)) throw new InvalidDataException("Invalid Git directory identifier.");
            var listing = await snapshot.RunAsync(["ls-tree", "-rz", "--full-tree", snapshot.Commit, "--", directory.Length == 0 ? "." : directory + "/"], token);
            var entries = new List<(string Path, string Sha)>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = entry.IndexOf('\t');
                if (tab < 0) throw new InvalidDataException("Invalid Git file listing.");
                var metadata = entry[..tab].Split(' '); var path = entry[(tab + 1)..];
                LauncherUpdateService.ValidateRelativePath(path);
                if (metadata.Length != 3 || metadata[0] is not ("100644" or "100755") || metadata[1] != "blob" || !Sha(metadata[2]))
                    throw new InvalidDataException("Git sources cannot contain symbolic links or submodules: " + path);
                if (!paths.Add(path)) throw new InvalidDataException("Duplicate Windows path in Git source: " + path);
                entries.Add((path, metadata[2]));
            }
            if (entries.Count == 0) throw new GitRepositoryException("The selected Git directory is empty or was not found.");
            // A tree contains blob identities, but not their sizes. Asking Git for sizes
            // here would lazily download every blob, defeating a metadata-only preview.
            snapshot.Files = entries.Select(entry => new GitRepositoryFile(entry.Path, entry.Sha, -1)).ToArray();
            snapshot.Message = (await snapshot.RunAsync(["log", "-1", "--format=%B", snapshot.Commit], token)).Trim();
            if (!metadataOnly) await snapshot.FetchFilesAsync(snapshot.Files.Select(file => file.Path), token);
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private static bool Sha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);
}

internal sealed class GitRepositorySnapshot(string root, IProgress<string> progress, GitCommandPolicy? policy = null) : IDisposable
{
    internal string Root { get; } = Path.GetFullPath(root);
    internal string Commit { get; set; } = "";
    internal string Tree { get; set; } = "";
    internal string Reference { get; set; } = "";
    internal string Directory { get; set; } = "";
    internal string Message { get; set; } = "";
    internal IReadOnlyList<GitRepositoryFile> Files { get; set; } = [];
    internal Uri? Remote { get; set; }
    private string? _resolvedHost;
    private string? _sslBackend = OperatingSystem.IsWindows() ? "schannel" : null;

    internal async Task FetchFilesAsync(IEnumerable<string> paths, CancellationToken token)
    {
        var requested = paths.ToHashSet(StringComparer.Ordinal);
        var selected = Files.Where(file => requested.Contains(file.Path)).ToArray();
        if (selected.Length != requested.Count) throw new InvalidDataException("A requested file is outside the Git snapshot.");
        if (selected.Length == 0) return;
        foreach (var file in selected) LauncherUpdateService.EnsureNoReparsePoints(Root, file.Path);
        progress.Report("Fetching Git file contents");
        await RunAsync(["fetch", "--no-tags", "--no-write-fetch-head", "--recurse-submodules=no", "--filter=blob:none", "--progress", "origin", "--stdin"],
            token, string.Join('\n', selected.Select(file => file.Sha).Distinct()) + "\n");
        // Populate an index with only the requested paths. A commit checkout (even a
        // sparse cone checkout) can implicitly fetch unchanged files and root files.
        await RunAsync(["read-tree", "--empty"], token);
        await RunAsync(["update-index", "-z", "--index-info"], token,
            string.Concat(selected.Select(file => "100644 " + file.Sha + "\t" + file.Path + "\0")));
        progress.Report("Checking out Git files");
        await RunAsync(["checkout-index", "--all", "--force"], token);
        Files = Files.Select(file => requested.Contains(file.Path)
            ? file with { Size = new FileInfo(LauncherUpdateService.ResolvePath(Root, file.Path)).Length } : file).ToArray();
    }

    internal async Task<string> RunAsync(string[] arguments, CancellationToken token, string? input = null)
    {
        var network = arguments[0] is "ls-remote" or "fetch" or "checkout";
        var phase = arguments[0] switch
        {
            "ls-remote" => "Reading Git references",
            "fetch" => input is null ? "Fetching Git repository" : "Fetching Git file contents",
            "checkout" or "checkout-index" => "Checking out Git files",
            _ => arguments[0]
        };
        var settings = policy ?? GitCommandPolicy.Default;
        var timeout = arguments[0] == "ls-remote" ? settings.MetadataTimeout : settings.TransferTimeout;
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var elapsed = Stopwatch.StartNew();
            var initialTimeout = network ? settings.MetadataTimeout : timeout;
            GitRepositoryDiagnostics.Write(Root, $"Starting {phase}; attempt {attempt}; response timeout {initialTimeout.TotalSeconds:F0} s; idle timeout {timeout.TotalSeconds:F0} s; TLS {_sslBackend ?? "default"}");
            var start = new ProcessStartInfo { FileName = GitExecutable(), WorkingDirectory = Root, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (var value in new[] { "-c", "core.hooksPath=" + Path.Combine(Root, "disabled-hooks"), "-c", "core.autocrlf=false", "-c", "core.symlinks=false", "-c", "core.longpaths=true", "-c", "protocol.file.allow=always",
                "-c", "http.version=HTTP/1.1", "-c", "http.lowSpeedLimit=1", "-c", "http.lowSpeedTime=45", "-c", "http.sslVerify=true", "-c", "credential.interactive=never" }) start.ArgumentList.Add(value);
            if (_sslBackend is not null) { start.ArgumentList.Add("-c"); start.ArgumentList.Add("http.sslBackend=" + _sslBackend); }
            if (_resolvedHost is not null) { start.ArgumentList.Add("-c"); start.ArgumentList.Add("http.curloptResolve=" + _resolvedHost); }
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GCM_INTERACTIVE"] = "never";
            start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1"; start.Environment["GIT_PROGRESS_DELAY"] = "0";
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (Win32Exception) { throw new GitRepositoryException("Git is not installed. Install Git for Windows and retry."); }
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var heartbeatStop = new CancellationTokenSource();
            var idleTicks = initialTimeout.Ticks;
            idle.CancelAfter(initialTimeout);
            void ReceivedData() { Interlocked.Exchange(ref idleTicks, timeout.Ticks); idle.CancelAfter(timeout); }
            using var cancel = idle.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            });
            var errors = new StringBuilder();
            var heartbeat = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(1000, heartbeatStop.Token).ConfigureAwait(false);
                        progress.Report("Git · " + phase + " · " + elapsed.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s");
                    }
                }
                catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
            });
            var stderr = Task.Run(async () =>
            {
                var buffer = new char[1024]; var pending = new StringBuilder(); int count;
                void Line()
                {
                    if (pending.Length == 0) return;
                    var line = GitRepositoryDiagnostics.Redact(pending.ToString()); pending.Clear();
                    progress.Report("Git · " + line); errors.AppendLine(line);
                    if (errors.Length > 8000) errors.Remove(0, errors.Length - 4000);
                }
                while ((count = await process.StandardError.ReadAsync(buffer)) != 0)
                {
                    ReceivedData();
                    foreach (var character in buffer.AsSpan(0, count))
                        if (character is '\r' or '\n') Line(); else if (pending.Length < 4000) pending.Append(character);
                }
                Line();
            });
            async Task<string> ReadOutput()
            {
                var output = new StringBuilder(); var buffer = new char[8192]; int count;
                while ((count = await process.StandardOutput.ReadAsync(buffer)) != 0)
                { ReceivedData(); output.Append(buffer, 0, count); }
                return output.ToString();
            }
            async Task WriteInput()
            {
                if (input is null) return;
                try { await process.StandardInput.WriteAsync(input.AsMemory(), idle.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
                finally { process.StandardInput.Close(); }
            }
            var stdout = ReadOutput(); var stdin = WriteInput();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await heartbeatStop.CancelAsync().ConfigureAwait(false); await heartbeat.ConfigureAwait(false);
            await Task.WhenAll(stderr, stdin).ConfigureAwait(false); var output = await stdout.ConfigureAwait(false);
            var detail = errors.ToString().Trim();
            GitRepositoryDiagnostics.Write(Root, $"Finished {phase}; attempt {attempt}; exit {process.ExitCode}; {elapsed.Elapsed.TotalSeconds:F1} s\n{detail}");
            token.ThrowIfCancellationRequested();
            if (!idle.IsCancellationRequested && process.ExitCode == 0) return output;
            var transient = idle.IsCancellationRequested || IsTransient(detail);
            if (network && transient && attempt < 2)
            {
                progress.Report("Retrying Git transfer");
                if (_sslBackend == "schannel" && detail.Contains("Unsupported SSL backend", StringComparison.OrdinalIgnoreCase)) _sslBackend = "openssl";
                else if (Remote is { Scheme: "https", Host: "github.com", Port: 443 } && _resolvedHost is null)
                {
                    _resolvedHost = await GitHostResolution.TryResolveAsync(token).ConfigureAwait(false);
                    GitRepositoryDiagnostics.Write(Root, _resolvedHost is null ? "Git DNS fallback unavailable" : "Retrying with a fresh GitHub DNS response: " + _resolvedHost);
                }
                await Task.Delay(settings.RetryDelay, token).ConfigureAwait(false); continue;
            }
            throw new GitCommandException(phase, idle.IsCancellationRequested, TimeSpan.FromTicks(Volatile.Read(ref idleTicks)), detail);
        }
    }

    private static bool IsTransient(string detail) => new[] { "timed out", "Timeout", "Connection was reset", "connection reset", "Could not resolve host", "Failed to connect", "SSL", "TLS", "early EOF", "RPC failed", "HTTP 408", "HTTP 429", "error: 500", "error: 502", "error: 503", "error: 504" }
        .Any(value => detail.Contains(value, StringComparison.OrdinalIgnoreCase));
    private static string GitExecutable()
    {
        foreach (var folder in new[] { Path.Combine(LauncherInstallationPaths.ResolveRoot(), "Tools", "Git"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git") })
            if (File.Exists(Path.Combine(folder, "cmd", "git.exe"))) return Path.Combine(folder, "cmd", "git.exe");
        return "git";
    }

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(Root)!;
        if (!Path.GetFileName(parent).Equals("HubGit", StringComparison.Ordinal) || !Guid.TryParseExact(Path.GetFileName(Root), "N", out _) ||
            !string.Equals(Path.GetFileName(Path.GetDirectoryName(parent)), "Temp", StringComparison.Ordinal))
            throw new IOException("Invalid Git temporary workspace.");
        LauncherUpdateService.EnsureNoReparsePoints(Root, "");
        for (var attempt = 0; ; attempt++)
        {
            if (!System.IO.Directory.Exists(Root)) return;
            LauncherUpdateService.EnsureNoReparsePoints(Root, "");
            try { Remove(Root); return; }
            catch (Exception error) when (attempt < 30 && error is IOException or UnauthorizedAccessException)
            { Thread.Sleep(100); }
        }
        static void Remove(string directory)
        {
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0) Remove(entry);
                else
                {
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                    if ((attributes & FileAttributes.Directory) != 0) System.IO.Directory.Delete(entry); else File.Delete(entry);
                }
            }
            File.SetAttributes(directory, File.GetAttributes(directory) & ~FileAttributes.ReadOnly);
            System.IO.Directory.Delete(directory);
        }
    }
}
