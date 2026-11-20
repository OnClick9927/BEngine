using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BEngine.Launcher;

internal sealed record GitRepositoryFile(string Path, string Sha, long Size);
internal sealed class GitRepositoryException(string message) : IOException(message);

/// <summary>Owns a shallow, sparse Git snapshot under the Hub's Temp directory.</summary>
internal sealed class GitRepositoryClient(Func<string, string>? remoteResolver = null)
{
    internal async Task<GitRepositorySnapshot> OpenAsync(string repositoryUrl, string reference, string directory,
        IProgress<string> progress, CancellationToken token, string? installationRoot = null, string treePath = "")
    {
        var parent = Path.Combine(installationRoot ?? LauncherInstallationPaths.ResolveRoot(), "Temp", "HubGit");
        LauncherUpdateService.EnsureNoReparsePoints(parent, "");
        var snapshot = new GitRepositorySnapshot(Path.Combine(parent, Guid.NewGuid().ToString("N")), progress);
        Directory.CreateDirectory(snapshot.Root);
        try
        {
            var remote = remoteResolver?.Invoke(repositoryUrl) ?? repositoryUrl;
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
            if (directory.Length > 0)
            {
                await snapshot.RunAsync(["sparse-checkout", "init", "--cone"], token);
                await snapshot.RunAsync(["sparse-checkout", "set", "--", directory], token);
            }
            progress.Report("Checking out Git files");
            await snapshot.RunAsync(["checkout", "--detach", "--force", snapshot.Commit], token);
            snapshot.Files = entries.Select(entry =>
            {
                LauncherUpdateService.EnsureNoReparsePoints(snapshot.Root, entry.Path);
                return new GitRepositoryFile(entry.Path, entry.Sha, new FileInfo(LauncherUpdateService.ResolvePath(snapshot.Root, entry.Path)).Length);
            }).ToArray();
            snapshot.Message = (await snapshot.RunAsync(["log", "-1", "--format=%B", snapshot.Commit], token)).Trim();
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private static bool Sha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);
}

internal sealed class GitRepositorySnapshot(string root, IProgress<string> progress) : IDisposable
{
    internal string Root { get; } = Path.GetFullPath(root);
    internal string Commit { get; set; } = "";
    internal string Tree { get; set; } = "";
    internal string Reference { get; set; } = "";
    internal string Directory { get; set; } = "";
    internal string Message { get; set; } = "";
    internal IReadOnlyList<GitRepositoryFile> Files { get; set; } = [];

    internal async Task<string> RunAsync(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo { FileName = GitExecutable(), WorkingDirectory = Root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var value in new[] { "-c", "core.hooksPath=" + Path.Combine(Root, "disabled-hooks"), "-c", "core.autocrlf=false", "-c", "core.symlinks=false", "-c", "core.longpaths=true", "-c", "protocol.file.allow=always" }) start.ArgumentList.Add(value);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GCM_INTERACTIVE"] = "never";
        start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception) { throw new GitRepositoryException("Git is not installed. Install Git for Windows and retry."); }
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(TimeSpan.FromMinutes(2));
        using var cancel = idle.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var errors = new StringBuilder();
        var stderr = Task.Run(async () =>
        {
            var buffer = new char[1024]; var pending = new StringBuilder(); int count;
            while ((count = await process.StandardError.ReadAsync(buffer)) != 0)
            {
                idle.CancelAfter(TimeSpan.FromMinutes(2));
                foreach (var character in buffer.AsSpan(0, count))
                {
                    if (character is '\r' or '\n')
                    {
                        if (pending.Length == 0) continue;
                        var line = pending.ToString(); pending.Clear(); progress.Report("Git · " + line);
                        errors.AppendLine(line); if (errors.Length > 8000) errors.Remove(0, errors.Length - 4000);
                    }
                    else if (pending.Length < 4000) pending.Append(character);
                }
            }
            errors.Append(pending);
        });
        var stdout = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await stderr.ConfigureAwait(false); var output = await stdout.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (idle.IsCancellationRequested) throw new GitRepositoryException("Git transfer timed out. Retry the operation.");
        if (process.ExitCode != 0) throw new GitRepositoryException("Git request failed: " + errors.ToString().Trim());
        return output;
    }

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
