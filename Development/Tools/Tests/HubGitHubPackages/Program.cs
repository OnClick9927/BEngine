using System.Text;
using System.Text.Json;
using BEngine.Launcher;

internal static class Probe
{
    private static int _checks;
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; Console.WriteLine("PASS|" + message); }
    private static async Task Reject(Func<Task> action, string message)
    { try { await action(); } catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException) { Check(true, message); return; } throw new Exception("Expected rejection: " + message); }
    internal static void Clean(string root)
    {
        var parent = Path.Combine(root, "Temp", "HubGit");
        Check(!Directory.Exists(parent) || !Directory.EnumerateDirectories(parent).Any(), "Git checkouts are removed from the Hub Temp directory");
    }
    [STAThread]
    private static void Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task RunAsync(string[] args)
    {
        if (args[0] == "--cleanup") { new GitRepositorySnapshot(args[1], new GitHubProgressSink<string>(_ => { })).Dispose(); return; }
        if (args[0] == "--incremental-engine")
        {
            await EngineIncrementalTests.RunAsync(Path.GetFullPath(args[1]), Check);
            Console.WriteLine("HUB_INCREMENTAL_ENGINE_OK|checks=" + _checks); return;
        }
        if (args[0] == "--git-network")
        {
            await GitNetworkTests.RunAsync(Path.GetFullPath(args[1]), Check);
            Console.WriteLine("HUB_GIT_NETWORK_OK|checks=" + _checks); return;
        }
        if (args[0] == "--window-ui")
        {
            var windowRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(windowRoot);
            Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", windowRoot);
            GitTestRepository.RemotesRoot = Path.Combine(windowRoot, "Remotes"); Directory.CreateDirectory(GitTestRepository.RemotesRoot);
            EngineUpdatesUiTests.Run(windowRoot, Check);
            Console.WriteLine("HUB_ENGINE_WINDOW_OK|checks=" + _checks); return;
        }
        if (args[0] == "--live")
        {
            Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.GetFullPath(args[2]));
            using var live = new GitHubPackageRepository();
            var liveReleases = await live.ReadAsync(args[1], new GitHubProgressSink<string>(Console.WriteLine), default);
            Console.WriteLine("GIT_LIVE_OK|releases=" + liveReleases.Count + "|commit=" + liveReleases[0].Commit); Clean(args[2]); return;
        }
        if (args[0] == "--live-engine")
        {
            var installation = Path.GetFullPath(args[1]); Directory.CreateDirectory(installation);
            Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", installation);
            using (var engine = new LauncherUpdateService())
            {
                var plan = await engine.CheckAsync(installation, new GitHubProgressSink<LauncherUpdateProgress>(value => Console.WriteLine(value.Message)), default);
                Check(plan.Files.Count > 0, "The public engine distribution is read through Git");
            }
            Clean(installation); return;
        }
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", root);
        GitTestRepository.RemotesRoot = Path.Combine(root, "Remotes"); Directory.CreateDirectory(GitTestRepository.RemotesRoot);
        var remote = new GitTestRepository(Path.Combine(GitTestRepository.RemotesRoot, "packages"));
        remote.Publish(GitTestRepository.Packages()); remote.Run("tag", "-a", "v1.0.0", "-m", "version one");
        var git = new GitRepositoryClient(_ => remote.Url);
        using var repository = new GitHubPackageRepository(git);
        using var catalog = new LauncherPackageCatalog(root, Path.Combine(root, "Packages"));
        var manager = new GitHubPackageManager(catalog, repository);
        var progress = new GitHubProgressSink<string>(_ => { });
        var releases = await repository.ReadAsync("https://github.com/vendor/packages.git", progress, default);
        Check(releases.Count == 4 && releases.All(value => value.Source.Reference == "main"), "Default branch discovers all package versions through Git"); Clean(root);
        var branch = await repository.ReadAsync("https://github.com/vendor/packages/tree/feature/packages/Published/Animation", progress, default);
        Check(branch.Count == 3 && branch.All(value => value.Source.Reference == "feature/packages" && value.Source.Directory == "Published/Animation"), "Slash-containing branches and GitHub tree URLs resolve without the REST API");
        var tagged = await repository.ReadAsync("https://github.com/vendor/packages.git?path=/Published#v1.0.0", progress, default);
        Check(tagged.Count == 4 && tagged[0].Commit == releases[0].Commit, "Annotated tags and directory query parameters resolve to pinned commits");
        var lockedSnapshot = await git.OpenAsync("https://github.com/vendor/packages.git", "main", "Published", progress, default);
        var temporaryLock = new FileStream(Path.Combine(lockedSnapshot.Root, lockedSnapshot.Files[0].Path), FileMode.Open, FileAccess.Read, FileShare.Read);
        var releaseLock = Task.Run(async () => { await Task.Delay(250); temporaryLock.Dispose(); });
        lockedSnapshot.Dispose(); await releaseLock;
        Check(!Directory.Exists(lockedSnapshot.Root), "Git cleanup tolerates a briefly locked Windows file and removes the checkout"); Clean(root);
        var v1 = releases.Single(value => value.Document.Id == "com.vendor.animation" && value.Document.PackageVersion == "1.0.0");
        var v2 = releases.Single(value => value.Document.Id == "com.vendor.animation" && value.Document.PackageVersion == "1.1.0");
        var helper = releases.Single(value => value.Document.Id == "com.vendor.helper");
        await Reject(() => manager.InstallAsync([v2], "0.1.0", progress, default), "Unsatisfied dependencies cannot publish partial installations"); Clean(root);
        await manager.InstallAsync(releases, "0.1.0", progress, default, allVersions: true);
        Check(manager.Installed().Count == 4 && manager.Installed().Any(value => value.Receipt.PackageVersion == "9.0.0"), "Git installation stores every source version"); Clean(root);
        remote.Publish(GitTestRepository.Packages(2, true));
        await manager.InstallAsync([v1], "0.1.0", progress, default);
        var installedV1 = manager.Installed().Single(value => value.Receipt.Id == v1.Document.Id && value.Receipt.PackageVersion == "1.0.0");
        Check(File.ReadAllText(Path.Combine(installedV1.Directory, "Runtime.cs")).Contains("revision 1", StringComparison.Ordinal), "A branch advancing after preview cannot alter the pinned package installation"); Clean(root);
        var newer = await repository.ReadAsync("https://github.com/vendor/packages.git#main", progress, default);
        var changes = GitHubPackageChanges.Compare(manager.Installed().Where(value => value.Receipt.Id == v1.Document.Id).ToArray(), newer.Where(value => value.Document.Id == v1.Document.Id).ToArray());
        Check(changes.Added.Contains("1.2.0") && changes.Modified.Count == 3, "Content comparisons distinguish new releases and changed versions");
        Check(!GitHubPackageChanges.Compare(manager.Installed().Where(value => value.Receipt.Id == helper.Document.Id).ToArray(), newer.Where(value => value.Document.Id == helper.Document.Id).ToArray()).HasUpdates,
            "Unchanged package content is unaffected by commits elsewhere in the repository");
        var changedV1 = newer.Single(value => value.Document.Id == v1.Document.Id && value.Document.PackageVersion == "1.0.0");
        var bad = changedV1 with { Files = changedV1.Files.Select(file => file.Path.EndsWith("Runtime.cs", StringComparison.Ordinal) ? file with { Sha = new string('0', 40) } : file).ToArray() };
        await Reject(() => manager.InstallAsync([bad], "0.1.0", progress, default), "Mismatching Git blob metadata cannot replace installed files"); Clean(root);
        Check(File.ReadAllText(Path.Combine(installedV1.Directory, "Runtime.cs")).Contains("revision 1", StringComparison.Ordinal), "A failed update preserves original package content");
        var helperFolder = manager.Installed().Single(value => value.Receipt.Id == helper.Document.Id).Directory;
        using (var locked = new FileStream(Path.Combine(helperFolder, GitHubPackageManager.ReceiptName), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Reject(() => manager.InstallAsync([changedV1, newer.Single(value => value.Document.Id == helper.Document.Id)], "0.1.0", progress, default),
                "A locked second destination rolls back an earlier package replacement");
        Check(File.ReadAllText(Path.Combine(installedV1.Directory, "Runtime.cs")).Contains("revision 1", StringComparison.Ordinal), "Publication rollback restores exact package bytes"); Clean(root);
        using (var cancellation = new CancellationTokenSource())
        {
            var cancel = new GitHubProgressSink<GitHubPackageProgress>(value => { if (value.CompletedBytes > 0) cancellation.Cancel(); });
            await Reject(() => manager.InstallAsync(newer, "0.1.0", progress, cancellation.Token, cancel, allVersions: true), "Cancellation during staging rolls back the package transaction");
        }
        Clean(root);
        await manager.InstallAsync(newer, "0.1.0", progress, default, allVersions: true);
        Check(manager.Installed().Count == 5, "Retry installs all versions after cancellation"); Clean(root);
        await Reject(() => repository.ReadAsync("https://github.com/vendor/packages.git?path=/../escape", progress, default), "Unsafe Git package paths are rejected");
        await Reject(() => repository.ReadAsync("https://github.com/vendor/packages.git#missing", progress, default), "Missing Git references report failure"); Clean(root);
        using (var cancelled = new CancellationTokenSource())
        {
            var cancel = new GitHubProgressSink<string>(message => { if (message == "Fetching Git repository") cancelled.Cancel(); });
            await Reject(async () => { using var snapshot = await git.OpenAsync("https://github.com/vendor/packages.git", "main", "Published", cancel, cancelled.Token); }, "Cancellation between Git init and fetch removes the partially created checkout");
        }
        Clean(root);
        await VerifyEngine(root, git, progress);
        await EngineIncrementalTests.RunAsync(Path.Combine(root, "IncrementalEngine"), Check);
        GitHubPackagesUiTests.Run(manager, repository, remote, root, Check);
        EngineUpdatesUiTests.Run(root, Check);
        manager.Remove(manager.Installed(), "0.1.0"); Clean(root);
        await GitHubDependencyTests.RunAsync(Path.Combine(root, "Dependencies"), Check, Reject); Clean(root);
        await GitNetworkTests.RunAsync(Path.Combine(root, "GitNetwork"), Check);
        var unsafeRemote = new GitTestRepository(Path.Combine(GitTestRepository.RemotesRoot, "symlink"));
        unsafeRemote.Publish(new() { ["package.yaml"] = GitTestRepository.Definition("com.vendor.unsafe", "1.0.0") });
        var linkSha = unsafeRemote.Command("../../outside", "hash-object", "-w", "--stdin");
        unsafeRemote.Run("update-index", "--add", "--cacheinfo", "120000," + linkSha + ",link"); unsafeRemote.Run("commit", "-m", "symlink");
        using var unsafeRepository = new GitHubPackageRepository(new GitRepositoryClient(_ => unsafeRemote.Url));
        await Reject(() => unsafeRepository.ReadAsync("https://github.com/vendor/unsafe.git#main", progress, default), "Git symlinks are rejected before checkout"); Clean(root);
        File.WriteAllText(Path.Combine(root, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = _checks }));
        Console.WriteLine("HUB_GIT_PACKAGES_OK|checks=" + _checks);
    }

    private static async Task VerifyEngine(string root, GitRepositoryClient unused, IProgress<string> progress)
    {
        var remote = new GitTestRepository(Path.Combine(GitTestRepository.RemotesRoot, "engine"));
        var files = new Dictionary<string, byte[]> { ["Engine/BEngine.Hub.exe"] = Encoding.UTF8.GetBytes("new hub"), ["Engine/BEgine/current/data.bin"] = Encoding.UTF8.GetBytes("new data") };
        remote.Publish(files);
        var installation = Path.Combine(root, "EngineInstall"); Directory.CreateDirectory(Path.Combine(installation, "BEgine", "current"));
        File.WriteAllText(Path.Combine(installation, "BEngine.Hub.exe"), "old hub"); File.WriteAllText(Path.Combine(installation, "BEgine", "current", "obsolete.bin"), "old");
        using var engine = new LauncherUpdateService("https://github.com/vendor/engine.git", new GitRepositoryClient(_ => remote.Url));
        var report = new GitHubProgressSink<LauncherUpdateProgress>(_ => { });
        var plan = await engine.CheckAsync(installation, report, default);
        Check(plan.Changes.Any(value => value.Kind == LauncherUpdateChangeKind.Deleted) && plan.Files.Count == 2, "Git engine preview retains additions, modifications and managed-file deletions");
        Check(Directory.EnumerateDirectories(Path.Combine(installation, "Temp", "HubGit")).Count() == 1, "Engine checkout lives exclusively in the installation's Temp directory");
        remote.Publish(new() { ["Engine/BEngine.Hub.exe"] = Encoding.UTF8.GetBytes("branch advanced") });
        await engine.DownloadAsync(plan, report, default);
        Check(File.ReadAllText(Path.Combine(plan.StagingRoot, "BEngine.Hub.exe")) == "new hub" && File.Exists(plan.PlanPath), "Engine staging uses the preview's commit when the branch advances"); Clean(installation);
        Check(File.ReadAllText(Path.Combine(installation, "BEngine.Hub.exe")) == "old hub", "Staging does not overwrite the running engine");
        File.WriteAllText(Path.Combine(plan.StagingRoot, "BEngine.Hub.exe"), "corrupt staging");
        await engine.DownloadAsync(plan, report, default);
        Check(File.ReadAllText(Path.Combine(plan.StagingRoot, "BEngine.Hub.exe")) == "new hub", "A retry verifies retained staging and replaces corrupt files from pinned Git content"); Clean(installation);
        var changedPlan = await engine.CheckAsync(installation, report, default);
        File.WriteAllText(Path.Combine(installation, "BEngine.Hub.exe"), "edited since preview");
        await Reject(() => engine.DownloadAsync(changedPlan, report, default), "A changed local baseline aborts engine staging"); Clean(installation);
        var cancelPlan = await engine.CheckAsync(installation, report, default);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Reject(() => engine.DownloadAsync(cancelPlan, report, cancellation.Token), "A cancelled engine update disposes the retained preview checkout"); Clean(installation);
        var copyingPlan = await engine.CheckAsync(installation, report, default);
        using var duringCopy = new CancellationTokenSource();
        var cancelCopy = new GitHubProgressSink<LauncherUpdateProgress>(value => { if (value.Completed > 0) duringCopy.Cancel(); });
        await Reject(() => engine.DownloadAsync(copyingPlan, cancelCopy, duringCopy.Token), "Cancellation during engine file copying removes the Git checkout"); Clean(installation);
        var currentRoot = Path.Combine(root, "CurrentEngine"); Directory.CreateDirectory(currentRoot);
        File.WriteAllText(Path.Combine(currentRoot, "BEngine.Hub.exe"), "branch advanced");
        var current = await engine.CheckAsync(currentRoot, report, default);
        Check(!current.HasUpdates, "An unchanged engine is recognized without an API call"); Clean(currentRoot);
        using var closing = new LauncherUpdateService("https://github.com/vendor/engine.git", new GitRepositoryClient(_ => remote.Url));
        var closeDuringFetch = new GitHubProgressSink<LauncherUpdateProgress>(value => { if (value.Message == "Fetching Git repository") closing.Dispose(); });
        try { await closing.CheckAsync(installation, closeDuringFetch, default); throw new Exception("Disposed check accepted"); }
        catch (ObjectDisposedException) { Check(true, "Closing Hub during a check cannot retain a late preview checkout"); }
        Clean(installation);
        try { LauncherUpdateInstaller.SafePath(installation, "Packages/GitHub/external/file.cs"); throw new Exception("Protected package accepted"); }
        catch (InvalidDataException) { Check(true, "The installer still protects external Git packages"); }
    }
}
