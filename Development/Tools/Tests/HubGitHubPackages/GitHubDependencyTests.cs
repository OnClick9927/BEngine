using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine.Editor.Documents;
using BEngine.Launcher;
using BEngine.ProjectSystem;

internal static class GitHubDependencyTests
{
    private const string Shared = "https://github.com/vendor/shared.git?path=/Published#dev";
    private const string Leaf = "https://github.com/vendor/leaf.git#src";
    private static readonly IProgress<string> Progress = new Progress<string>();

    internal static async Task RunAsync(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject)
    {
        PackageDefinitionDocument Parse(byte[] bytes) => PackageDefinitionLoader.Normalize(
            YamlUtility.Deserialize<PackageDefinitionDocument>(Encoding.UTF8.GetString(bytes)));
        var legacy = Parse(Definition("app", dependencies: Dependency("shared")));
        check(legacy.Runtime!.Dependencies[0].GitUrl is null && !YamlUtility.Serialize(legacy).Contains("gitUrl", StringComparison.Ordinal),
            "Old YAML dependencies need no Git URL and serialize without the new field");
        var parsed = Parse(Definition("app", dependencies: Dependency("shared", "  " + Shared + "  "), editor: true,
            editorDependencies: Dependency("shared", Leaf, target: "editor")));
        check(parsed.Runtime!.Dependencies[0].GitUrl == Shared && parsed.Editor!.Dependencies[0].GitUrl == Leaf,
            "Runtime and editor Git URLs survive YAML loading and normalization");
        var roundTrip = Parse(Encoding.UTF8.GetBytes(YamlUtility.Serialize(parsed)));
        check(roundTrip.Runtime!.Dependencies[0].GitUrl == Shared && roundTrip.Editor!.Dependencies[0].GitUrl == Leaf,
            "Dependency Git URLs round-trip through YAML serialization");
        var snapshot = new PackageCatalogSnapshot { Packages = [new() { Path = "package.yaml", Document = parsed }] };
        check(JsonSerializer.Deserialize<PackageCatalogSnapshot>(JsonSerializer.Serialize(snapshot))!.Packages[0].Document.Runtime!.Dependencies[0].GitUrl == Shared,
            "The Hub-to-editor catalog preserves dependency Git URLs");

        using (var fixture = new Fixture(Path.Combine(root, "Recursive")))
        {
            fixture.Publish("main", Definition("app", dependencies: Dependency("shared", Shared) + Dependency("helper", Shared),
                editor: true, editorDependencies: Dependency("shared", Shared, target: "editor")));
            fixture.Publish("shared", ("Published/Shared-1.0.0", Definition("shared", editor: true, dependencies: Dependency("leaf", Leaf))),
                ("Published/Shared-2.0.0", Definition("shared", version: "2.0.0", editor: true, minimum: "9.0.0")),
                ("Published/Helper", Definition("helper")), ("Published/Unrelated", Definition("unrelated")));
            fixture.Publish("leaf", Definition("leaf"));
            await fixture.Install();
            var installed = fixture.Manager.Installed();
            check(installed.Select(item => item.Receipt.Id).Order().SequenceEqual(new[] { "app", "helper", "leaf", "shared" }),
                "Installing one package recursively obtains only its required Git packages");
            check(installed.Single(item => item.Receipt.Id == "shared").Receipt.PackageVersion == "1.0.0",
                "Git dependencies select the latest engine-compatible release");
            check(fixture.Network.CommitReads.GetValueOrDefault("shared") == 2,
                "Multiple IDs and runtime/editor declarations share one Git source read");
            check(installed.Single(item => item.Receipt.Id == "shared").Receipt.Source == new GitHubPackageSource("vendor", "shared", "dev", "Published") &&
                installed.Single(item => item.Receipt.Id == "leaf").Receipt.Source.Reference == "src",
                "Recursive Git dependencies preserve branch, path and source receipts");
            check(fixture.Catalog.GetSnapshot(fixture.Packages, "0.1.0").Packages.Single(item => item.Document.Id == "app")
                .Document.Runtime!.Dependencies[0].GitUrl == Shared, "Installed catalog retains the declared dependency Git URL");
            await fixture.Install();
            check(fixture.Network.CommitReads.GetValueOrDefault("shared") == 2 && fixture.Network.CommitReads.GetValueOrDefault("leaf") == 2,
                "Existing compatible dependencies are reused without reading their Git sources again");
            await reject(() => Task.Run(() => fixture.Manager.Remove([installed.Single(item => item.Receipt.Id == "leaf")], "0.1.0")),
                "An automatically installed required dependency cannot be removed while in use");
            fixture.Manager.Remove(fixture.Manager.Installed(), "0.1.0");
            check(fixture.Manager.Installed().Count == 0, "Main package and automatic Git dependencies can be removed together");
        }
        using (var fixture = new Fixture(Path.Combine(root, "Local")))
        {
            fixture.Publish("main", Definition("app", dependencies: Dependency("shared", Shared) + Dependency("absent", Leaf, optional: true)));
            fixture.AddLocal("shared", Definition("shared"));
            await fixture.Install();
            check(fixture.Manager.Installed().Count == 1 && fixture.Network.CommitReads.Keys.SequenceEqual(new[] { "main" }),
                "Local engine packages satisfy Git dependencies and optional packages are not downloaded");
        }
        using (var fixture = new Fixture(Path.Combine(root, "TreeUrl")))
        {
            fixture.Publish("main", Definition("app", dependencies: Dependency("shared", "https://github.com/vendor/shared/tree/dev/Published")));
            fixture.Publish("shared", ("Published/Shared", Definition("shared")));
            await fixture.Install();
            check(fixture.Manager.Installed().Single(item => item.Receipt.Id == "shared").Receipt.Source.Directory == "Published",
                "Dependency Git URLs also preserve GitHub tree branch and directory links");
        }
        using (var fixture = new Fixture(Path.Combine(root, "DeferredAddress")))
        {
            fixture.Publish("main", Definition("app", dependencies: Dependency("leaf") + Dependency("shared", Shared)));
            fixture.Publish("shared", ("Published/Shared", Definition("shared", dependencies: Dependency("leaf", Leaf))));
            fixture.Publish("leaf", Definition("leaf"));
            await fixture.Install();
            check(fixture.Manager.Installed().Count == 3, "A transitive declaration can supply the Git URL for another required dependency");
        }
        using (var fixture = new Fixture(Path.Combine(root, "CombinedTargets")))
        {
            fixture.Publish("main", Definition("app", dependencies: Dependency("shared", Shared) + Dependency("helper", Shared)));
            fixture.Publish("shared", ("Published/Shared-1.0.0", Definition("shared", editor: true)),
                ("Published/Shared-2.0.0", Definition("shared", version: "2.0.0")),
                ("Published/Helper", Definition("helper", editor: true, editorDependencies: Dependency("shared", Shared, target: "editor"))));
            await fixture.Install();
            check(fixture.Manager.Installed().Single(item => item.Receipt.Id == "shared").Receipt.PackageVersion == "1.0.0",
                "Transitive runtime and editor targets resolve to one release providing both assemblies");
        }
        foreach (var scenario in new[] { "WrongId", "WrongTarget", "Incompatible", "Conflict", "Cycle", "DownloadFailure", "MissingAddress", "InvalidAddress", "Cancelled" })
        {
            using var fixture = new Fixture(Path.Combine(root, scenario));
            var url = scenario == "InvalidAddress" ? "https://example.com/vendor/shared.git" : scenario == "MissingAddress" ? "" : Shared;
            fixture.Publish("main", Definition("app", dependencies: Dependency("shared", url), editor: scenario is "Conflict" or "WrongTarget",
                editorDependencies: scenario == "Conflict" ? Dependency("shared", Leaf) : scenario == "WrongTarget" ? Dependency("shared", url, target: "editor") : ""));
            fixture.Publish("shared", ("Published/Shared", Definition(scenario == "WrongId" ? "other" : "shared",
                minimum: scenario == "Incompatible" ? "9.0.0" : "0.1.0", dependencies: scenario == "Cycle" ? Dependency("app") : "")));
            fixture.Network.FailRepositoryDownloads = scenario == "DownloadFailure" ? "shared" : "";
            using var cancellation = new CancellationTokenSource();
            if (scenario == "Cancelled") cancellation.Cancel();
            await reject(() => fixture.Install(cancellation.Token), "Automatic Git dependency failure rejects the transaction: " + scenario);
            check(fixture.Manager.Installed().Count == 0 && (!Directory.Exists(fixture.Packages) ||
                !Directory.EnumerateFiles(fixture.Packages, "package.yaml", SearchOption.AllDirectories).Any()),
                "Automatic dependency failure publishes no partial package: " + scenario);
        }
        using (var fixture = new Fixture(Path.Combine(root, "UpdateFailure")))
        {
            fixture.Publish("main", Definition("app")); await fixture.Install();
            fixture.Publish("main", Definition("app", version: "2.0.0", dependencies: Dependency("shared", Shared)));
            fixture.Publish("shared", ("Published/Shared", Definition("shared")));
            fixture.Network.FailRepositoryDownloads = "shared";
            await reject(() => fixture.Install(), "A failed dependency download prevents publishing the main package update");
            check(fixture.Manager.Installed().Count == 1 && fixture.Manager.Installed()[0].Receipt.PackageVersion == "1.0.0",
                "A failed automatic dependency update preserves the existing installed version");
        }
    }

    private static string Dependency(string id, string url = "", string target = "runtime", bool optional = false) =>
        $"  - packageId: {id}\n    target: {target}\n" + (url.Length == 0 ? "" : "    gitUrl: '" + url + "'\n") + (optional ? "    optional: true\n" : "");
    private static byte[] Definition(string id, string version = "1.0.0", string minimum = "0.1.0", string dependencies = "", bool editor = false, string editorDependencies = "") =>
        Encoding.UTF8.GetBytes($"format: BEngine.Package\nversion: 2\nid: {id}\npackageVersion: {version}\nminimumEngineVersion: {minimum}\nruntime:\n  assembly: {id}.Runtime\n  rootNamespace: Example\n" +
            (dependencies.Length == 0 ? "" : "  dependencies:\n" + dependencies) + (editor ? $"editor:\n  assembly: {id}.Editor\n  rootNamespace: Example\n" +
                (editorDependencies.Length == 0 ? "" : "  dependencies:\n" + editorDependencies) : ""));

    private sealed class Fixture : IDisposable
    {
        internal readonly DependencyNetwork Network = new();
        
        internal readonly LauncherPackageCatalog Catalog;
        private readonly GitHubPackageRepository _repository;
        internal readonly GitHubPackageManager Manager;
        internal readonly string Packages;
        internal Fixture(string root)
        {
            Directory.CreateDirectory(root); Packages = Path.Combine(root, "Packages");
            _repository = new(new GitRepositoryClient(Network.Resolve)); Catalog = new(root, Packages); Manager = new(Catalog, _repository);
        }
        internal void Publish(string repository, byte[] definition) => Publish(repository, ("", definition));
        internal void Publish(string repository, params (string Directory, byte[] Definition)[] packages)
        {
            var files = new Dictionary<string, byte[]>();
            foreach (var (directory, definition) in packages)
            {
                var prefix = directory.Length == 0 ? "" : directory + "/";
                files.Add(prefix + "package.yaml", definition);
                files.Add(prefix + "Runtime.cs", Encoding.UTF8.GetBytes("// fixture " + repository));
            }
            Network.Publish(repository, files);
        }
        internal void AddLocal(string id, byte[] definition)
        {
            var path = Path.Combine(Packages, "Bundled", id); Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "package.yaml"), definition); Catalog.Refresh(Packages);
        }
        internal async Task Install(CancellationToken token = default)
        {
            var releases = await _repository.ReadAsync("https://github.com/vendor/main.git#main", Progress, token);
            await Manager.InstallAsync(releases, "0.1.0", Progress, token);
        }
        public void Dispose() { Catalog.Dispose(); _repository.Dispose(); }
    }

    private sealed class DependencyNetwork
    {
        private readonly Dictionary<string, GitTestRepository> _repositories = [];
        internal readonly Dictionary<string, int> CommitReads = [];
        internal string FailRepositoryDownloads = "";
        internal void Publish(string name, Dictionary<string, byte[]> files)
        {
            if (!_repositories.TryGetValue(name, out var repository))
                _repositories.Add(name, repository = new GitTestRepository(Path.Combine(GitTestRepository.RemotesRoot, Guid.NewGuid().ToString("N"))));
            repository.Publish(files);
        }
        internal string Resolve(string url)
        {
            var name = Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
            CommitReads[name] = CommitReads.GetValueOrDefault(name) + 1;
            return name == FailRepositoryDownloads || !_repositories.TryGetValue(name, out var remote)
                ? new Uri(Path.Combine(GitTestRepository.RemotesRoot, "missing-" + name)).AbsoluteUri : remote.Url;
        }
    }
}
