using System.Collections.Concurrent;
using System.Text;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

/// <summary>Reads package definitions and files from pinned Git snapshots.</summary>
internal sealed class GitHubPackageRepository(GitRepositoryClient? git = null) : IDisposable
{
    private readonly GitRepositoryClient _git = git ?? new();
    private readonly ConcurrentDictionary<(GitHubPackageSource Source, string Commit), Lazy<Task<GitRepositorySnapshot>>> _downloads = [];

    internal async Task<IReadOnlyList<GitHubPackageRelease>> ReadAsync(string url, IProgress<string> progress, CancellationToken token)
    {
        var address = GitHubPackageAddress.Parse(url); var source = address.Source;
        using var snapshot = await _git.OpenAsync(source.RepositoryUrl + ".git", source.Reference, source.Directory, progress, token, treePath: address.TreePath).ConfigureAwait(false);
        source = source with { Reference = snapshot.Reference, Directory = snapshot.Directory };
        var files = snapshot.Files.Select(file => new GitHubPackageFile(file.Path, file.Sha, file.Size)).ToArray();
        var prefix = source.Directory.Length == 0 ? "" : source.Directory + "/";
        var releases = new List<GitHubPackageRelease>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        progress.Report("Reading GitHub package list");
        foreach (var definition in files.Where(file => file.Path == "package.yaml" || file.Path.EndsWith("/package.yaml", StringComparison.Ordinal)))
        {
            token.ThrowIfCancellationRequested();
            if (!definition.Path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Package definition is outside the selected Git directory.");
            if (definition.Size > 2 * 1024 * 1024) throw new InvalidDataException("Package definition is too large.");
            var path = LauncherUpdateService.ResolvePath(snapshot.Root, definition.Path);
            if (!definition.Sha.Equals(await LauncherUpdateService.HashFileAsync(path, token).ConfigureAwait(false), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Git file verification failed: " + definition.Path);
            var document = PackageDefinitionLoader.Normalize(YamlUtility.Deserialize<PackageDefinitionDocument>(await File.ReadAllTextAsync(path, Encoding.UTF8, token).ConfigureAwait(false)), definition.Path);
            var folder = definition.Path == "package.yaml" ? "" : definition.Path[..^"/package.yaml".Length];
            var ownedPrefix = folder.Length == 0 ? "" : folder + "/";
            var owned = files.Where(file => file.Path.StartsWith(ownedPrefix, StringComparison.Ordinal)).ToArray();
            if (owned.Any(file => file != definition && file.Path.EndsWith("/package.yaml", StringComparison.Ordinal)))
                throw new InvalidDataException("Nested package definitions are not supported: " + definition.Path);
            if (owned.Any(file => file.Path[ownedPrefix.Length..].Equals(GitHubPackageManager.ReceiptName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Package contains reserved installation metadata: " + folder);
            if (owned.Any(file => file.Size > 512L * 1024 * 1024)) throw new InvalidDataException("Unsupported package file size.");
            if (!identities.Add(document.Id + "\n" + document.PackageVersion)) throw new InvalidDataException("Duplicate package ID and version in Git source.");
            releases.Add(new(source, snapshot.Commit, folder, document, owned));
        }
        if (releases.Count == 0) throw new InvalidDataException("No package.yaml was found in the selected GitHub directory.");
        return releases.OrderBy(item => item.Document.DisplayName).ThenByDescending(item => item.Document.PackageVersion, BPackageVersion.Comparer).ToArray();
    }

    internal async Task DownloadAsync(GitHubPackageRelease release, GitHubPackageFile file, string destination,
        IProgress<string> progress, CancellationToken token, IProgress<long>? bytesProgress = null)
    {
        var pending = _downloads.GetOrAdd((release.Source, release.Commit), key => new(() =>
            _git.OpenAsync(key.Source.RepositoryUrl + ".git", key.Commit, key.Source.Directory, progress, token)));
        var snapshot = await pending.Value.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (snapshot.Commit != release.Commit || !snapshot.Files.Any(value => value.Path == file.Path && value.Sha == file.Sha && value.Size == file.Size))
            throw new InvalidDataException("Git package snapshot changed after the preview.");
        LauncherUpdateService.EnsureNoReparsePoints(snapshot.Root, file.Path);
        await using var input = File.OpenRead(LauncherUpdateService.ResolvePath(snapshot.Root, file.Path));
        await using var output = File.Create(destination);
        var buffer = new byte[81920]; long copied = 0; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        { await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); copied += count; bytesProgress?.Report(copied); }
        if (copied != file.Size) throw new InvalidDataException("Git package file size changed: " + file.Path);
    }

    internal IDisposable BeginInstall() => new InstallScope(this);
    private sealed class InstallScope(GitHubPackageRepository owner) : IDisposable
    { public void Dispose() => owner.Dispose(); }
    public void Dispose()
    {
        List<Exception> errors = [];
        foreach (var key in _downloads.Keys.ToArray())
            if (_downloads.TryRemove(key, out var pending) && pending.IsValueCreated && pending.Value.IsCompletedSuccessfully)
                try { pending.Value.Result.Dispose(); } catch (IOException error) { errors.Add(error); }
        if (errors.Count > 0) throw new IOException("Unable to clean Git temporary files.", new AggregateException(errors));
    }
}
