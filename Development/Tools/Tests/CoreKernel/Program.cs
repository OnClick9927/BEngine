using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using BEngine.DependencyInjection;
using BEngine.Editor.Documents;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var checks = new List<string>();
var timer = Stopwatch.StartNew();
void Check(bool valid, string name)
{
    if (!valid) throw new Exception("FAILED: " + name);
    checks.Add(name);
}
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { checks.Add(name); return; }
    throw new Exception("Did not reject: " + name);
}
BPackageDefinition Package(string id, string version = "1.0.0", bool editor = false,
    params PackageDependencyDocument[] dependencies) => new(id + "/package.yaml", new()
    {
        Id = id, PackageVersion = version, MinimumEngineVersion = "0.1.0",
        Runtime = new() { Assembly = id + ".Runtime", RootNamespace = id, Dependencies = [.. dependencies] },
        Editor = editor ? new() { Assembly = id + ".Editor", RootNamespace = id } : null
    });
PackageDependencyDocument Dep(string id, bool optional = false, string target = "runtime") =>
    new() { PackageId = id, Optional = optional, Target = target };

Check(BPackageVersion.Compare("1.0.0-alpha.9", "1.0.0-alpha.10") < 0, "semantic numeric prerelease ordering");
Check(BPackageVersion.Compare("1.0.0-alpha", "1.0.0") < 0, "stable follows prerelease");
Check(BPackageVersion.Compare("1.0.0+one", "1.0.0+two") == 0, "build metadata does not change precedence");
Reject(() => BPackageVersion.Validate("1.0.0-01"), "invalid leading zero rejected");

var a = Package("A", editor: true, dependencies: [Dep("B")]);
var b = Package("B", editor: true);
var c = Package("C", dependencies: [Dep("absent", optional: true)]);
var plan = PackageAssemblyPlan.Create([a, c, b]);
Check(plan.Count == 5, "all selected runtime and editor assemblies represented");
Check(plan[0].Package.Document.Id == "B" && plan[0].Target == "runtime", "dependency precedes consumer");
Check(plan.Take(3).All(s => s.Target == "runtime"), "all runtime steps precede editor steps");
Check(plan.Select(s => s.Package.Document.Id + "/" + s.Target).SequenceEqual(
    PackageAssemblyPlan.Create([b, c, a]).Select(s => s.Package.Document.Id + "/" + s.Target)), "deterministic shuffled input");
Check(PackageAssemblyPlan.Create([a, b, c], includeEditor: false).Count == 3, "runtime-only plan excludes editor");
Reject(() => PackageAssemblyPlan.Create([a]), "missing required dependency rejected");
Reject(() => PackageAssemblyPlan.Create([a, b, Package("A", "2.0.0")]), "duplicate selected releases rejected");
Reject(() => PackageAssemblyPlan.Create([Package("X", dependencies: [Dep("Y")]), Package("Y", dependencies: [Dep("X")])]), "cycle rejected");
Reject(() => PackageAssemblyPlan.Create([Package("X", dependencies: [Dep("Y", target: "editor")]), Package("Y", editor: true)]), "runtime cannot depend on editor");
Reject(() => PackageAssemblyPlan.Create([Package("X", dependencies: [Dep("X")])]), "self cycle rejected");
Check(PackageAssemblyPlan.Create([]).Count == 0, "empty package set supported");

var old = new PackageDefinitionDocument { Version = 1, Id = "Legacy", Assembly = "Legacy.Runtime", Dependencies = ["Shared"] };
var normalized = PackageDefinitionRules.Normalize(old);
Check(normalized.Version == 2 && normalized.Runtime!.Dependencies.Single().PackageId == "Shared", "legacy manifest normalized");
Check(old.Version == 1 && old.Runtime is null, "normalization does not mutate source");
var releases = BPackageReleaseResolver.Resolve([Package("V", "1.0.0"), Package("V", "2.0.0")]);
Check(releases.Single().Document.PackageVersion == "2.0.0", "version resolver selects newest compatible release");

var services = new ServiceCollection();
services.AddBEngineCore(new(EngineHostKind.Runtime, "", "KernelProbe"), [new ProbeModule()]);
using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
{
    Check(provider.GetRequiredService<EngineServiceContext>().InstanceName == "KernelProbe", "neutral module context registered");
    var singleton = provider.GetRequiredService<SingletonProbe>();
    Check(ReferenceEquals(singleton, provider.GetRequiredService<SingletonProbe>()), "singleton identity stable");
    using var first = provider.CreateScope();
    using var second = provider.CreateScope();
    Check(ReferenceEquals(first.ServiceProvider.GetRequiredService<ScopedProbe>(), first.ServiceProvider.GetRequiredService<ScopedProbe>()), "scoped identity stable");
    Check(!ReferenceEquals(first.ServiceProvider.GetRequiredService<ScopedProbe>(), second.ServiceProvider.GetRequiredService<ScopedProbe>()), "scopes isolated");
    Check(!ReferenceEquals(provider.GetRequiredService<TransientProbe>(), provider.GetRequiredService<TransientProbe>()), "transients distinct");
}
Check(SingletonProbe.Disposals == 1 && ScopedProbe.Disposals == 2, "owned services disposed exactly once");

var contributionCalls = 0;
Action<IServiceCollection> contribution = _ => contributionCalls++;
RuntimeServiceContributions.Register(contribution);
RuntimeServiceContributions.Register(contribution);
RuntimeServiceContributions.Configure(new ServiceCollection());
Check(contributionCalls == 1, "same contribution registered once");
RuntimeServiceContributions.RegisterDefault(new SingletonProbe());
Check(RuntimeServiceContributions.GetDefault<SingletonProbe>() is not null, "DI default contribution accessible");
RuntimeServiceContributions.UnregisterAssembly(typeof(ProbeModule).Assembly);
RuntimeServiceContributions.Configure(new ServiceCollection());
Check(contributionCalls == 1 && RuntimeServiceContributions.GetDefault<SingletonProbe>() is null, "retiring module removes DI registrations");

var sourceRoot = args.Length > 0 ? args[0] : throw new ArgumentException("source root required");
var yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking().IgnoreUnmatchedProperties().Build();
var actual = Directory.GetDirectories(Path.Combine(sourceRoot, "Packages"))
    .Select(dir => Path.Combine(dir, "package.yaml")).Where(File.Exists)
    .Select(path => { try { return new BPackageDefinition(path, PackageDefinitionRules.Normalize(yaml.Deserialize<PackageDefinitionDocument>(File.ReadAllText(path)), path)); } catch (Exception ex) { throw new Exception("Invalid manifest: " + path, ex); } })
    .ToArray();
var realPlan = PackageAssemblyPlan.Create(BPackageReleaseResolver.Resolve(actual));
Check(realPlan.Count == actual.Sum(p => (p.Document.Runtime is not null ? 1 : 0) + (p.Document.Editor is not null ? 1 : 0)), "real catalog has no missing assemblies");
var positions = realPlan.Select((step, i) => (Key: step.Package.Document.Id + "/" + step.Target, Index: i))
    .ToDictionary(pair => pair.Key, pair => pair.Index, StringComparer.OrdinalIgnoreCase);
foreach (var step in realPlan)
{
    var declared = yaml.Deserialize<AssemblyDefinition>(File.ReadAllText(Path.Combine(
        Path.GetDirectoryName(step.Package.Path)!, step.Assembly.Assembly, step.Assembly.Assembly + ".asmdef.yaml")));
    var expected = step.Assembly.Dependencies.Where(d => positions.ContainsKey(d.PackageId + "/" + d.Target))
        .Select(d => realPlan.Single(s => s.Package.Document.Id == d.PackageId && s.Target == d.Target).Assembly.Assembly)
        .Concat(step.Assembly.HostReferences).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (step.Target == "editor" && step.Package.Document.Runtime is { } ownRuntime) expected.Add(ownRuntime.Assembly);
    Check(expected.SetEquals(declared.References), "asmdef matches manifest: " + step.Assembly.Assembly);
    foreach (var dependency in step.Assembly.Dependencies)
        if (positions.TryGetValue(dependency.PackageId + "/" + dependency.Target, out var index))
            Check(index < positions[step.Package.Document.Id + "/" + step.Target], "real dependency: " + step.Package.Document.Id + "/" + step.Target + " -> " + dependency.PackageId + "/" + dependency.Target);
}

var runtimeCore = typeof(IEngineServiceModule).Assembly;
Check(typeof(BEngine.Coroutine).Assembly == runtimeCore, "coroutines are compiled into runtime Core");
Check(typeof(BEngine.JsonSerialization).Assembly == runtimeCore, "serialization is compiled into runtime Core");
Check(!runtimeCore.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Core.Editor"), "runtime Core excludes editor package management");
Check(typeof(PackageAssemblyDocument).Assembly == typeof(PackageAssemblyPlan).Assembly, "package data and dependency rules belong to editor kernel");
var coreProjects = Directory.GetFiles(Path.Combine(sourceRoot, "Core"), "*.csproj", SearchOption.AllDirectories);
Check(coreProjects.Select(Path.GetFileNameWithoutExtension).OrderBy(name => name).SequenceEqual(
    new[] { "BEngine.Core", "BEngine.Core.Editor", "BEngine.Player" }.OrderBy(name => name)),
    "Core contains exactly three assembly projects");
foreach (var file in coreProjects)
{
    var project = System.Xml.Linq.XDocument.Load(file);
    foreach (var reference in project.Descendants("ProjectReference"))
    {
        var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, (string)reference.Attribute("Include")!));
        Check(!target.Contains(Path.DirectorySeparatorChar + "Packages" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "Core project has no feature package dependency");
    }
}
var builtInModules = new[] { "Attributes", "Math", "Async", "BuildCore", "Collections",
    "Serialization", "Instrumentation", "Foundation", "Compilation", "EditorServices",
    "AssetPipeline", "Player", "Pooling", "Coroutines" };
Check(!Directory.Exists(Path.Combine(sourceRoot, "Core", "Modules")), "Core has no shared source module directory");
foreach (var name in builtInModules)
{
    var assembly = name is "Compilation" or "EditorServices" or "Player" ? "BEngine.Core.Editor" : "BEngine.Core";
    Check(Directory.Exists(Path.Combine(sourceRoot, "Core", assembly, name)), "built-in module belongs to its assembly: " + name);
    Check(!File.Exists(Path.Combine(sourceRoot, "Packages", name, "package.yaml")), "built-in module is not a package: " + name);
}
Check(Directory.Exists(Path.Combine(sourceRoot, "Core", "BEngine.Player", "Hosting")), "Player host composition lives in its assembly directory");
Check(Directory.Exists(Path.Combine(sourceRoot, "Core", "BEngine.Core.Editor", "AssetPipeline")), "Asset authoring lives in Core Editor");

var core = typeof(PackageAssemblyPlan).Assembly;
using var stream = File.OpenRead(core.Location);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
Check(!references.Any(n => n.StartsWith("BEngine.") && n != "BEngine.Core" && n != "BEngine.Player" && n != "BEngine.Platform.Contracts" && n != "BEngine.Editor.Host.Contracts"),
    "Core Editor has no legacy Core assembly dependencies");
timer.Stop();
var result = new { Checks = checks.Count, Passed = true, Packages = actual.Length, AssemblySteps = realPlan.Count,
    RuntimeCoreBytes = new FileInfo(runtimeCore.Location).Length,
    CoreBytes = new FileInfo(core.Location).Length, CoreReferences = references, ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds, Details = checks };
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "KernelResult.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"CORE_KERNEL_OK|checks={checks.Count}|packages={actual.Length}|steps={realPlan.Count}|bytes={result.CoreBytes}");

sealed class ProbeModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context)
    { services.AddSingleton<SingletonProbe>(); services.AddScoped<ScopedProbe>(); services.AddTransient<TransientProbe>(); }
}
sealed class SingletonProbe : IDisposable { public static int Disposals; public void Dispose() => Disposals++; }
sealed class ScopedProbe : IDisposable { public static int Disposals; public void Dispose() => Disposals++; }
sealed class TransientProbe { }
sealed class AssemblyDefinition { public List<string> References { get; set; } = []; }
