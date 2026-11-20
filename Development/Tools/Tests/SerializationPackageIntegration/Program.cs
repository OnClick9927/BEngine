using System.Reflection;
using System.Runtime.ExceptionServices;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var packagesRoot = Path.GetFullPath(args[0]);
var projectRoot = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
File.WriteAllText(Path.Combine(projectRoot, "Project.yaml"),
    "format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Serialization integration\n");
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(projectRoot, "Hub"));
Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", packagesRoot);
DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
using var services = new ServiceCollection().AddBEnginePlatformServices()
    .AddBEngineEditorPlatformServices().BuildServiceProvider();
using var attachment = EditorServiceScope.Attach(services);

var manifests = Directory.EnumerateFiles(packagesRoot, "package.yaml", SearchOption.AllDirectories);
var catalog = new BPackageCatalog(manifests);
Check(!catalog.TryGet("com.bengine.serialization-core", out _), "Old package remains in catalog");
foreach (var id in new[] { "com.bengine.serialization", "com.bengine.foundation", "com.bengine.player", "com.bengine.compilation" })
    Check(!catalog.TryGet(id, out _), $"Built-in Core module remains a package: {id}");
Check(typeof(JsonSerialization).Assembly.GetName().Name == "BEngine.Core",
    "Serialization formats are owned by the built-in Core assembly");
Check(!catalog.TryGet("com.bengine.coroutines", out _), "Coroutine runtime remains an installable package");
var selected = new[] { catalog.GetRequired("com.bengine.reactive") };
Check(selected[0].Document.Runtime?.HostReferences.Contains("BEngine.Core") == true,
    "Feature package does not declare its Core host reference");

var workspace = ProjectWorkspace.Open(projectRoot);
Directory.CreateDirectory(workspace.PackagesPath);
var retiredCachePath = Path.Combine(workspace.PackageCachePath, "com.bengine.platform-contracts");
Directory.CreateDirectory(retiredCachePath);
File.WriteAllText(Path.Combine(retiredCachePath, "package.yaml"), YamlUtility.Serialize(new PackageDefinitionDocument
{
    Id = "com.bengine.platform-contracts",
    Runtime = new()
    {
        Assembly = "BEngine.Platform.Contracts", RootNamespace = "BEngine",
        Dependencies = [new() { PackageId = "com.bengine.foundation", Target = "runtime" }]
    }
}));
var builtInIds = new[]
{
    "com.bengine.serialization", "com.bengine.foundation", "com.bengine.player",
    "com.bengine.compilation", "com.bengine.coroutines",
    "com.bengine.platform-abstractions", "com.bengine.platform-contracts",
    "com.bengine.platform-build", "com.bengine.platform-dot-net", "com.bengine.platform-desktop",
    "com.bengine.platform-veldrid", "com.bengine.platform-windows", "com.bengine.platform-windows-build",
    "com.bengine.platform-linux", "com.bengine.platform-linux-build",
    "com.bengine.platformmac-os", "com.bengine.platformmac-osbuild",
    "com.bengine.platform-android", "com.bengine.platform-android-build",
    "com.bengine.platformi-os", "com.bengine.platformi-osbuild",
    "com.bengine.platform-web", "com.bengine.platform-web-build", "COM.BENGINE.PLATFORM-CONTRACTS"
};
File.WriteAllText(workspace.PackageManifestPath, YamlUtility.Serialize(new PackageManifestDocument
{
    Packages =
    [
        .. builtInIds.Select((id, index) => new PackageReferenceDocument { Id = id, Enabled = index % 2 == 0 }),
        new() { Id = "com.bengine.reactive", Version = selected[0].Document.PackageVersion, Enabled = true }
    ]
}));
using (var manager = BPackageManager.OpenManifest(workspace, catalog))
{
    Check(manager.packages.All(package => !builtInIds.Contains(package.Id, StringComparer.OrdinalIgnoreCase)),
        "Legacy built-in package references were not retired from the project manifest");
    Check(manager.IsEnabled("com.bengine.reactive"), "Migrating built-in references disabled a feature package");
}
Check(!Directory.Exists(retiredCachePath), "A retired Platform package survived in the project cache");
Check(YamlUtility.Load<PackageManifestDocument>(workspace.PackageManifestPath).Packages
        .All(package => !builtInIds.Contains(package.Id, StringComparer.OrdinalIgnoreCase)),
    "Retired built-in references remain in the saved manifest");
using (var reopened = BPackageManager.OpenManifest(workspace, catalog))
    Check(reopened.IsEnabled("com.bengine.reactive"), "Migrated manifest cannot reopen");

var missingRoot = Path.Combine(projectRoot, "MissingPackage");
Directory.CreateDirectory(Path.Combine(missingRoot, "Packages"));
File.Copy(workspace.ProjectFilePath, Path.Combine(missingRoot, "Project.yaml"), overwrite: true);
var missingWorkspace = ProjectWorkspace.Open(missingRoot);
File.WriteAllText(missingWorkspace.PackageManifestPath, YamlUtility.Serialize(new PackageManifestDocument
{
    Packages = [new() { Id = "com.bengine.platform-custom", Enabled = true }]
}));
try
{
    using var missing = BPackageManager.OpenManifest(missingWorkspace, catalog);
    throw new Exception("An unknown platform package was silently retired");
}
catch (InvalidDataException exception) when (exception.Message.Contains("com.bengine.platform-custom", StringComparison.Ordinal))
{
    Check(YamlUtility.Load<PackageManifestDocument>(missingWorkspace.PackageManifestPath).Packages
        .Single().Id == "com.bengine.platform-custom", "An unknown missing package was removed from the manifest");
}
var compiler = typeof(BPackageCatalog).Assembly.GetType("BEngine.ProjectSystem.PackageSourceCompiler", true)!;
var method = compiler.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
    .Single(item => item.Name == "CompileEnabled" && item.GetParameters().Length == 8);
object compilation;
try
{
    compilation = method.Invoke(null,
        [workspace, selected, ProcessUtility.Current,
            null, false, null, CancellationToken.None, true])!;
}
catch (TargetInvocationException exception) when (exception.InnerException is not null)
{
    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
    throw;
}
var assemblies = (IReadOnlyDictionary<string, string>)compilation.GetType()
    .GetProperty("AssemblyPaths", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
    .GetValue(compilation)!;
foreach (var name in new[] { "BEngine.Reactive" })
    Check(assemblies.TryGetValue(name, out var path) && File.Exists(path), $"Missing compiled {name}");
Check(File.Exists(typeof(DotNetPlatformServices).Assembly.Location), "Missing platform host assembly");
Check(HostAssemblyRegistry.Contains(typeof(YamlSerialization).Assembly), "YAML host assembly was not registered");
Check(RuntimeTypeCache.ResolveSerializedType("BEngine.Fix64, BEngine.Math") == typeof(Fix64),
    "Legacy Math assembly-qualified type resolves into Core");
Check(RuntimeTypeCache.ResolveSerializedType("BEngine.GameObject, BEngine.Core") == typeof(GameObject),
    "Legacy Foundation assembly-qualified type resolves into Core");
using var scene = new Scene("Serialization formats");
var gameObject = scene.CreateGameObject("Round trip");
var snapshot = ObjectStateSnapshot.Capture(gameObject);
var json = JsonSerialization.Serialize(snapshot);
var xml = XmlSerialization.Serialize(snapshot);
Check(JsonSerialization.Deserialize<ObjectStateSnapshot>(json)?.MemberCount == snapshot.MemberCount,
    "JSON cannot restore an engine object state adapter");
Check(XmlSerialization.Deserialize<ObjectStateSnapshot>(xml)?.MemberCount == snapshot.MemberCount,
    "XML cannot restore an engine object state adapter");
Console.WriteLine($"SERIALIZATION_PACKAGE_INTEGRATION_OK|packages={catalog.packages.Count}|assemblies={assemblies.Count}");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
