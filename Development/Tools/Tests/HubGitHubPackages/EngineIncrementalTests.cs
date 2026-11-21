using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using BEngine.Launcher;

internal static class EngineIncrementalTests
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var remote = new GitTestRepository(Path.Combine(root, "Remote"));
        var stable = new byte[1024 * 1024]; new Random(1234).NextBytes(stable);
        var content = Encoding.UTF8.GetBytes("new shared content\n");
        var files = new Dictionary<string, byte[]>
        {
            ["Engine/BEngine.Hub.exe"] = Encoding.UTF8.GetBytes("new hub"),
            ["Engine/BEgine/current/stable.bin"] = stable,
            ["Engine/BEgine/current/copied.bin"] = stable,
            ["Engine/BEgine/current/a.bin"] = content,
            ["Engine/BEgine/current/b.bin"] = content,
            ["Engine/BEgine/current/empty.bin"] = [],
            ["Engine/Packages/GitHub/.operation.lock"] = Encoding.UTF8.GetBytes("remote lock"),
            ["Engine/Packages/GitHub/foreign/1.0.0/thirdparty.dll"] = Encoding.UTF8.GetBytes("remote external package"),
            ["outside.bin"] = Encoding.UTF8.GetBytes("do not fetch or check out a root file")
        };
        remote.Publish(files);
        var stableSha = remote.Run("rev-parse", "HEAD:Engine/BEgine/current/stable.bin");
        var hubSha = remote.Run("rev-parse", "HEAD:Engine/BEngine.Hub.exe");
        var contentSha = remote.Run("rev-parse", "HEAD:Engine/BEgine/current/a.bin");
        var outsideSha = remote.Run("rev-parse", "HEAD:outside.bin");
        var installation = Path.Combine(root, "Installation");
        Directory.CreateDirectory(Path.Combine(installation, "BEgine/current"));
        File.WriteAllText(Path.Combine(installation, "BEngine.Hub.exe"), "old hub");
        File.WriteAllBytes(Path.Combine(installation, "BEgine/current/stable.bin"), stable);
        File.WriteAllText(Path.Combine(installation, "BEgine/current/obsolete.bin"), "obsolete");
        var external = Path.Combine(installation, "Packages/GitHub/foreign/1.0.0/thirdparty.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!); File.WriteAllText(external, "local external package");
        var events = new ConcurrentQueue<string>();
        var inspectDownload = false; var observedFetch = false;
        HashSet<string> Missing(string checkout, string commit) => GitTestRepository.CommandAt(checkout, null,
            "rev-list", "--objects", "--missing=print", commit).Split('\n')
            .Where(line => line.StartsWith('?')).Select(line => line[1..].Trim()).ToHashSet();
        var progress = new GitHubProgressSink<LauncherUpdateProgress>(value =>
        {
            events.Enqueue(value.Message);
            if (!inspectDownload || value.Message != "Checking out Git files") return;
            var checkout = Directory.EnumerateDirectories(Path.Combine(installation, "Temp/HubGit")).Single();
            var missing = Missing(checkout, remote.Run("rev-parse", "HEAD"));
            check(missing.Contains(stableSha) && missing.Contains(outsideSha),
                "An incremental download leaves unchanged and out-of-directory blobs absent from the Git object database");
            check(!missing.Contains(hubSha) && (observedFetch || !missing.Contains(contentSha)),
                "An incremental download fetches the requested changed blob contents");
            check(!File.Exists(Path.Combine(checkout, "outside.bin")),
                "Selective checkout never materializes unrelated repository root files");
            observedFetch = true;
        });
        using var engine = new LauncherUpdateService("https://github.com/vendor/engine.git", new GitRepositoryClient(_ => remote.Url));
        var preview = await engine.CheckAsync(installation, progress, default);
        var previewRoot = Directory.EnumerateDirectories(Path.Combine(installation, "Temp/HubGit")).Single();
        var missingBefore = Missing(previewRoot, preview.CommitSha);
        File.WriteAllText(Path.Combine(root, "MetadataObjects.txt"), string.Join('\n', missingBefore) + "\nExpected: " +
            string.Join(',', hubSha, stableSha, contentSha, outsideSha));
        check(missingBefore.Contains(hubSha) && missingBefore.Contains(stableSha) && missingBefore.Contains(contentSha) && missingBefore.Contains(outsideSha),
            "Checking for engine updates downloads commit/tree metadata without downloading any file blobs");
        check(!events.Contains("Fetching Git file contents") && !File.Exists(Path.Combine(previewRoot, "Engine/BEngine.Hub.exe")),
            "An update preview performs no file-content fetch or checkout");
        check(preview.HasUnknownDownloadSize && preview.DownloadBytes == 0 && preview.Files.Single(file => file.Path.EndsWith("stable.bin")).Size == stable.Length,
            "A metadata preview marks changed sizes as pending and derives unchanged sizes from verified local files");
        check(!preview.Changes.Any(change => change.Path.EndsWith("stable.bin")) && preview.Changes.Any(change => change.Kind == LauncherUpdateChangeKind.Deleted),
            "Hash-only comparison preserves unchanged files and managed-file deletions");
        check(!preview.Files.Any(file => file.Path.StartsWith("Packages/GitHub/", StringComparison.OrdinalIgnoreCase)) &&
            !preview.Changes.Any(change => change.Path.StartsWith("Packages/GitHub/", StringComparison.OrdinalIgnoreCase)),
            "Protected Git package content and operation locks in the remote checkout do not invalidate an engine preview");
        inspectDownload = true;
        var resolved = await engine.DownloadAsync(preview, progress, default);
        check(observedFetch, "Engine staging explicitly fetches changed contents after preview");
        Probe.Clean(installation);
        check(!resolved.HasUnknownDownloadSize && resolved.Files.All(file => file.Size >= 0) &&
            resolved.Files.Single(file => file.Path.EndsWith("empty.bin")).Size == 0,
            "A verified installation plan resolves pending sizes, including empty files");
        var saved = JsonSerializer.Deserialize<LauncherUpdatePlan>(File.ReadAllText(resolved.PlanPath))!;
        check(saved.Files.All(file => file.Size >= 0) && saved.Changes.Where(change => change.Kind != LauncherUpdateChangeKind.Deleted).All(change => change.Size >= 0),
            "Only fully resolved sizes are written to the installer plan");
        check(File.ReadAllBytes(Path.Combine(resolved.StagingRoot, "BEgine/current/copied.bin")).SequenceEqual(stable) &&
            File.ReadAllBytes(Path.Combine(resolved.StagingRoot, "BEgine/current/b.bin")).SequenceEqual(content),
            "Locally available and shared changed blobs are copied to every destination");
        check(resolved.DownloadBytes == stable.Length + content.Length + files["Engine/BEngine.Hub.exe"].Length,
            "Staging byte totals deduplicate identical changed file contents");
        check(File.ReadAllText(external) == "local external package", "Engine staging preserves locally installed external packages");

        inspectDownload = false; events.Clear();
        using (var offline = new LauncherUpdateService("https://github.com/vendor/engine.git",
            new GitRepositoryClient(_ => throw new IOException("A verified staging retry must not use the network"))))
        {
            var retained = await offline.DownloadAsync(preview, progress, default);
            check(!events.Contains("Fetching Git repository") && !retained.HasUnknownDownloadSize,
                "A retry reuses verified staging and resolves preview sizes without accessing Git");
        }
        File.WriteAllText(Path.Combine(resolved.StagingRoot, "BEngine.Hub.exe"), "corrupt");
        inspectDownload = true;
        var repaired = await engine.DownloadAsync(preview, progress, default);
        check(File.ReadAllText(Path.Combine(repaired.StagingRoot, "BEngine.Hub.exe")) == "new hub",
            "A retry fetches and repairs corrupt staging while leaving verified blobs retained");
        Probe.Clean(installation);
        inspectDownload = false;
        foreach (var change in repaired.Changes)
        {
            var target = Path.Combine(installation, change.Path);
            if (change.Kind == LauncherUpdateChangeKind.Deleted) File.Delete(target);
            else File.Copy(Path.Combine(repaired.StagingRoot, change.Path), target, true);
        }
        events.Clear();
        var current = await engine.CheckAsync(installation, progress, default);
        check(!current.HasUpdates && !current.HasUnknownDownloadSize && !events.Contains("Fetching Git file contents"),
            "An up-to-date engine completes its check without downloading file contents");
        Probe.Clean(installation);
    }
}
