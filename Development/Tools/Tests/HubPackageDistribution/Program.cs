using System.Text.Json;
using System.IO.Pipes;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Launcher;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Tooling;
using Microsoft.Extensions.DependencyInjection;

DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices()
    .AddBEngineEditorPlatformServices().BuildServiceProvider();
using var scope = EditorServiceScope.Attach(services);
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks.Add(name);
}
if (args[0] == "--installed")
{
    var installedPackages = Path.GetFullPath(args[1]);
    var engineVersion = args[2];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var pipe = new NamedPipeClientStream(".", PackageCatalogTransport.PipeName(installedPackages),
        PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(timeout.Token);
    await PackageCatalogTransport.WriteAsync(pipe,
        YamlUtility.Serialize(new PackageCatalogRequest { EngineVersion = engineVersion }), timeout.Token);
    var response = YamlUtility.Deserialize<PackageCatalogResponse>(await PackageCatalogTransport.ReadAsync(pipe, timeout.Token));
    Check(string.IsNullOrEmpty(response.Error) && response.Snapshot is not null, "Installed Hub serves its package catalog");
    var snapshot = response.Snapshot!;
    var installedDistribution = LauncherBundledPackages.Load(installedPackages, engineVersion);
    Check(snapshot.Packages.Count > 0 && snapshot.Packages.Any(item => item.Distribution == PackageDistributionKind.Bundled),
        "Installed Hub identifies bundled packages");
    Check(snapshot.Packages.All(item => item.Distribution == installedDistribution.Classify(new(item.Path, item.Document))),
        "Installed Hub IPC matches the exported release manifest");
    Console.WriteLine($"HUB_INSTALLED_DISTRIBUTION_OK|packages={snapshot.Packages.Count}|bundled={snapshot.Packages.Count(item => item.Distribution == PackageDistributionKind.Bundled)}");
    return;
}
var root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
var packages = Path.Combine(root, "Packages");
BPackageDefinition Package(string folder, string id, string version = "1.0.0", string type = "package")
{
    var path = Path.GetFullPath(Path.Combine(packages, folder, "package.yaml"));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var document = new PackageDefinitionDocument
    {
        Id = id, PackageVersion = version, MinimumEngineVersion = "0.1.0", DisplayName = id,
        Type = type, ProjectDirectory = type == "project" ? "Starter" : "",
        Runtime = type == "project" ? null : new() { Assembly = id, RootNamespace = id }
    };
    File.WriteAllText(path, YamlUtility.Serialize(document));
    return new(path, document);
}
var bundled = Package("Bundled/1.0.0", "com.vendor.bundled");
var project = Package("Starter/1.0.0", "com.bengine.starter", type: "project");
var external = Package("External/1.0.0", "com.bengine.external");
var updated = Package("Bundled/2.0.0", bundled.Document.Id, "2.0.0");
var clone = new BPackageDefinition(Path.Combine(root, "Copied/package.yaml"), bundled.Document);
string ManifestPath(string version) => Path.Combine(root, "BEgine", "BEgine-" + version, LauncherBundledPackages.FileName);
string Manifest(string version, params BPackageDefinition[] releases) => JsonSerializer.Serialize(new
{
    format = "BEngine.BundledPackages", version = 1, engineVersion = version,
    packages = releases.Select(release => new
    {
        id = release.Document.Id, version = release.Document.PackageVersion,
        path = Path.GetRelativePath(packages, release.Path).Replace('\\', '/')
    })
});
void Publish(string version, string text)
{
    var path = ManifestPath(version);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
}
Check(LauncherBundledPackages.Load(packages, "0.1.0").Classify(bundled) == PackageDistributionKind.Unknown,
    "Old engine without a manifest remains unknown");
Publish("0.1.0", Manifest("0.1.0", bundled, project));
var distribution = LauncherBundledPackages.Load(packages, "0.1.0");
Check(distribution.Classify(bundled) == PackageDistributionKind.Bundled, "Bundled package without the BEngine prefix is recognized");
Check(distribution.Classify(project) == PackageDistributionKind.Bundled, "Official project package is recognized");
Check(distribution.Classify(external) == PackageDistributionKind.External, "BEngine prefix does not grant bundled provenance");
Check(distribution.Classify(updated) == PackageDistributionKind.External, "Another version of the same package is external");
Check(distribution.Classify(clone) == PackageDistributionKind.External, "Copied package outside its recorded path is external");
var renamed = new BPackageDefinition(bundled.Path, new() { Id = external.Document.Id, PackageVersion = "1.0.0" });
Check(distribution.Classify(renamed) == PackageDistributionKind.External, "Another identity at a bundled path is external");
Publish("0.2.0", Manifest("0.2.0", updated));
Check(LauncherBundledPackages.Load(packages, "0.2.0").Classify(updated) == PackageDistributionKind.Bundled,
    "Each engine version owns its release list");
Check(LauncherBundledPackages.Load(packages, "0.2.0").Classify(bundled) == PackageDistributionKind.External,
    "Old package versions do not inherit another engine's provenance");

using (var index = new LauncherPackageIndex(packages))
{
    var snapshot = index.GetSnapshot("0.1.0");
    Check(snapshot.Packages.Single(item => item.Path == bundled.Path).Distribution == PackageDistributionKind.Bundled,
        "Hub snapshot contains bundled provenance");
    var restored = YamlUtility.Deserialize<PackageCatalogSnapshot>(YamlUtility.Serialize(snapshot));
    Check(restored.Packages.Single(item => item.Path == bundled.Path).Distribution == PackageDistributionKind.Bundled,
        "Catalog transport preserves provenance");
    var infos = index.GetPackages("0.1.0");
    Check(!infos.Single(item => item.Id == bundled.Document.Id).IsBundled,
        "Hub labels the selected newer external release correctly");
    var starter = infos.Single(item => item.Id == project.Document.Id);
    Check(starter.IsBundled && starter.DescribeContents(new("zh-CN")).Contains("引擎自带", StringComparison.Ordinal),
        "Hub details show the localized bundled label");
    Check(infos.Single(item => item.Id == external.Document.Id).DistributionLabel == "External package",
        "Hub view retains external provenance");
    var scans = index.ScanCount;
    for (var i = 0; i < 10; i++) Check(ReferenceEquals(snapshot, index.GetSnapshot("0.1.0")), "Cached snapshot reused " + i);
    Check(index.ScanCount == scans, "Provenance reads do not rescan the package repository");
    Publish("0.1.0", Manifest("0.1.0", bundled, project, external));
    Check(SpinWait.SpinUntil(() => index.GetSnapshot("0.1.0").Revision != snapshot.Revision, TimeSpan.FromSeconds(8)),
        "A distribution manifest change invalidates the catalog");
    Check(index.GetPackages("0.1.0").Single(item => item.Id == external.Document.Id).IsBundled,
        "A running Hub reloads the changed engine distribution");
    File.Delete(ManifestPath("0.1.0"));
    Check(SpinWait.SpinUntil(() => index.GetPackages("0.1.0").All(item => item.Distribution == PackageDistributionKind.Unknown),
        TimeSpan.FromSeconds(8)), "Removing the manifest clears prior provenance");
}
foreach (var malformed in new[]
{
    "{", Manifest("0.2.0", bundled),
    Manifest("0.1.0", bundled).Replace("Bundled/1.0.0/package.yaml", "../escape/package.yaml", StringComparison.Ordinal),
    Manifest("0.1.0", bundled, bundled)
})
{
    Publish("0.1.0", malformed);
    using var index = new LauncherPackageIndex(packages);
    Check(index.GetSnapshot("0.1.0").Packages.All(item => item.Distribution == PackageDistributionKind.Unknown),
        "Malformed distribution metadata leaves packages usable without claiming provenance");
}
File.WriteAllText(Path.Combine(root, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }));
Console.WriteLine($"HUB_PACKAGE_DISTRIBUTION_OK|checks={checks.Count}");
