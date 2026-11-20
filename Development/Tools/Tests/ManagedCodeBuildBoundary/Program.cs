using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using BEngine;
using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Editor.Build.ProjectSystem;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;

var output = Path.GetFullPath(args[0]);
var root = Path.Combine(output, "Run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(root, "Hub"));
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var runner = new LinkerProbe();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton(new ProcessUtility(runner)).BuildServiceProvider();
using var attached = EditorServiceScope.Attach(services);
var files = FileUtility.Current; var paths = PathUtility.Current;
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch(T) { Check(true, name); return; } throw new Exception(name + " did not throw"); }
async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception
{ try { await action(); } catch(T) { Check(true, name); return; } throw new Exception(name + " did not throw"); }
string Project(string name)
{
    var project = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(project, "Assets/AOT"));
    Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
    File.WriteAllText(Path.Combine(project, "Project.yaml"), "format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Code Graph\n");
    return project;
}
PlayerBuildContext Context(string project, string name, string target = "windows",
    PlayerManagedStrippingLevel stripping = PlayerManagedStrippingLevel.Full) => new(new PlayerBuildRequest
    { ProjectPath = project, TargetId = target, ManagedStripping = stripping, WebScriptCompilation = WebScriptCompilationMode.AheadOfTime },
    BuildTargetUtility.Current.Get(target), Path.Combine(root, name), null);
RuntimeManagedCodeReleaseInput Image(string name, string origin, string[] dependencies, bool native = false)
{
    var path = Path.Combine(root, "Images", name + ".dll"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var image = AssemblyDefinition.CreateAssembly(new(name, new Version(1,0)), name, ModuleKind.Dll);
    foreach (var dependency in dependencies) image.MainModule.AssemblyReferences.Add(new(dependency, new Version(1,0)));
    var type = new TypeDefinition(name, "Service", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract,
        image.MainModule.TypeSystem.Object);
    type.Interfaces.Add(new(image.MainModule.ImportReference(typeof(IEngineServiceModule))));
    image.MainModule.Types.Add(type);
    var marker = new MethodDefinition("Marker", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
        image.MainModule.TypeSystem.Void);
    marker.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "marker:" + name));
    marker.Body.Instructions.Add(Instruction.Create(OpCodes.Pop)); marker.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    type.Methods.Add(marker);
    if (native) image.CustomAttributes.Add(new(image.MainModule.ImportReference(typeof(RuntimeNativeExecutionAttribute).GetConstructor(Type.EmptyTypes)!)));
    image.Write(path);
    return new(name, name + "-build", path, null, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
        new FileInfo(path).Length, dependencies, origin);
}
var a = Image("PackageA", "package:a", []);
var b = Image("PackageB", "package:b", [a.Name]);
var dead = Image("Dead", "package:dead", []);
var entry = Image("AOT", "project:AOT", [a.Name]);
var game = Image("GameScripts", "project:GameScripts", [b.Name]);
var hostPath = typeof(BObject).Assembly.Location;
var host = new RuntimeManagedCodeReleaseInput("BEngine", "host", hostPath, null,
    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(hostPath))), new FileInfo(hostPath).Length, [], "host:core");
var project = Project("Project"); var workspace = ProjectWorkspace.Open(project);
foreach (var asset in new[]{"root", "late", "tail"}) File.WriteAllText(Path.Combine(project, "Assets/AOT/" + asset + ".yaml"), "value: 0\n");
var resourceDirectory = Path.Combine(project, "PackageResources"); Directory.CreateDirectory(resourceDirectory);
RuntimePackageResourceInput Resource(string package)
{
    var path = Path.Combine(resourceDirectory, package + ".txt"); File.WriteAllText(path, "resource " + package);
    return new(package, "Assets/Packages/" + package + "/data.txt", path,
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length);
}
var ra = Resource("a"); var rb = Resource("b");
var inputs = new RuntimeManagedCodeReleaseInputSet([host, a, b, dead, entry, game], [ra, rb], [resourceDirectory]);
Check(typeof(RuntimeManagedCodeReleaseInputCollector).Assembly == typeof(PlayerManagedCodeBuildPipeline).Assembly,
    "Input collection and code builds are owned by the PlayerBuild implementation assembly");
Check(Type.GetType("BEngine.DependencyInjection.IEngineServiceModule, BEngine",throwOnError:true)==typeof(IEngineServiceModule),
    "Legacy runtime DI identities forward to the minimal Core");
Check(Type.GetType("BEngine.ProjectSystem.BPackageVersion, BEngine.Editor",throwOnError:true)==typeof(BPackageVersion),
    "Legacy editor package identities forward to the package management kernel");
var startup = PlayerManagedCodeBuildPipeline.SelectStartupInputs(inputs, paths);
Check(startup.Assemblies.Select(i=>i.Name).SequenceEqual(["PackageA", "AOT"]), "Startup selects entry dependencies without rooting every enabled package");
Check(startup.PackageResources.Count == 1 && startup.PackageResources[0] == ra, "Startup resource ownership follows selected package inputs");
Check(startup.PackageResourceRoots.SequenceEqual([resourceDirectory]), "Startup retains only occupied resource roots");
var deferred = PlayerManagedCodeBuildPipeline.SelectDeferredInputs(inputs);
Check(deferred.Assemblies.All(i=>i.Name is not ("AOT" or "BEngine")) && deferred.Assemblies.Any(i=>i.Name==game.Name),
    "Deferred inputs omit the replaced entry and static host images");
Check(deferred.Assemblies.Single(i=>i.Name==game.Name).Dependencies.SequenceEqual([b.Name]), "Deferred module dependencies remain within its release");
Check(deferred.PackageResources.Count == 2, "Deferred content retains its independently selected resources");
Throws<InvalidDataException>(()=>PlayerManagedCodeBuildPipeline.SelectStartupInputs(new([a,b],[],[]), paths), "Missing entry is rejected");
Throws<InvalidDataException>(()=>PlayerManagedCodeBuildPipeline.SelectStartupInputs(new([entry with {Dependencies=["Absent"]}],[],[]), paths), "Missing release dependency is rejected");
Throws<InvalidDataException>(()=>PlayerManagedCodeBuildPipeline.SelectStartupInputs(new([entry with {Dependencies=[game.Name]},game],[],[]), paths), "Startup cannot bind a deferred project assembly");
Throws<InvalidDataException>(()=>PlayerManagedCodeBuildPipeline.SelectDeferredInputs(new([entry,game with {Dependencies=["AOT"]}],[],[])), "Deferred code cannot depend on the replaced entry domain");
var assetFile = Path.Combine(project, "Assets/AOT/type.yaml"); File.WriteAllText(assetFile, "type: PackageB.Service\n");
var resourceRooted = PlayerManagedCodeBuildPipeline.SelectStartupInputs(inputs, paths, [assetFile]);
Check(resourceRooted.Assemblies.Any(i=>i.Name==b.Name) && resourceRooted.Assemblies.All(i=>i.Name!=game.Name),
    "Serialized package types add startup roots without activating deferred project code");
var reportContext = Context(project, "Deduplicate");
reportContext.StrippingReport = new PlayerStrippingReport { Assemblies = [new() {Name="BEngine",Included=true,PublishedBytes=4}] };
var noHosts = PlayerManagedCodeBuildPipeline.RemoveHostDuplicates(reportContext, inputs);
Check(noHosts.Assemblies.All(i=>i.Name!="BEngine"), "Content archives omit static host duplicate images");
Check(!reportContext.StrippingReport.Assemblies[0].Included && reportContext.StrippingReport.Assemblies[0].PublishedBytes==0,
    "Duplicate removal updates the build report");
Check(ReferenceEquals(PlayerManagedCodeBuildPipeline.PreserveNativeImages(reportContext, inputs, inputs), inputs),
    "CoreCLR keeps the selected images without rewriting them");

// The process fixture returns controlled linked images. Core still writes and
// reads real descriptors and PE metadata; actual SDK linking is covered by Players.
var tool = Path.Combine(workspace.PackageCachePath, "Probe/BuildTools/ILLink/illink.dll");
Directory.CreateDirectory(Path.GetDirectoryName(tool)!); File.WriteAllText(tool, "controlled linker process fixture");
runner.Source = inputs; runner.Reset();
var graph = new ResourceGraph(ra,rb);
var closureContext = Context(project, "Closure");
var reached = await PlayerManagedCodeBuildPipeline.BuildStartupClosureAsync(closureContext, workspace, inputs, graph, files, paths, default);
Check(runner.Calls == 3, "Reachability repeats until assets, package resources and activated modules are all stable");
Check(reached.AssetPaths.SequenceEqual(["Assets/AOT/late.yaml","Assets/AOT/root.yaml","Assets/AOT/tail.yaml"]), "Newly reached assets are retained and deterministically ordered");
Check(reached.Inputs.Assemblies.Any(i=>i.Name==b.Name) && reached.Inputs.Assemblies.All(i=>i.Name is not ("GameScripts" or "Dead")),
    "The converged startup graph admits reachable packages and excludes dead/deferred code");
Check(reached.Inputs.PackageResources.Select(r=>r.PackageId).SequenceEqual(["a","b"]), "Resource reachability participates in code convergence");
Check(runner.RootedA.SequenceEqual([false,true,true]), "Package module roots activate only after a prior pass reaches the module");
Check(runner.RootedDead.All(rooted=>!rooted), "Enabled dead packages never acquire module roots");
Check(runner.OriginalReferences.All(value=>value), "Each partial link starts from original code images");
Check(closureContext.StrippingReport!.DependencyPasses==3 && closureContext.StrippingReport.Resources.Count==2,
    "Reports retain the actual link pass count and only final resource results");
Check(File.Exists(closureContext.StrippingReportPath) && File.Exists(Path.ChangeExtension(closureContext.StrippingReportPath,".aot-roots.xml")),
    "Core owns the stripping report and root descriptor snapshots");
Check(closureContext.RuntimeManagedCodeAssemblyPaths.Contains(game.AssemblyPath) &&
    closureContext.RuntimeManagedCodeAssemblyPaths.Count(path=>path==hostPath)==1 &&
    closureContext.RuntimeManagedCodeAssemblyPaths.Contains(reached.Inputs.Assemblies.Single(i=>i.Name==a.Name).AssemblyPath),
    "Host compilation retains deferred contracts alongside reachable startup contracts");
var cancelled = new CancellationToken(true); runner.Reset(); var cancelledGraph = new ResourceGraph(ra,rb);
await ThrowsAsync<OperationCanceledException>(()=>PlayerManagedCodeBuildPipeline.BuildStartupClosureAsync(
    Context(project,"Cancel"),workspace,inputs,cancelledGraph,files,paths,cancelled), "Cancellation aborts the closure build");
Check(runner.Calls==0 && cancelledGraph.Collections==0, "Pre-cancelled work does not scan or launch a linker");
runner.Reset(); runner.Fail=true; var failed = Context(project,"Failure");
await ThrowsAsync<InvalidOperationException>(()=>PlayerManagedCodeBuildPipeline.BuildStartupClosureAsync(
    failed,workspace,inputs,new ResourceGraph(ra,rb),files,paths,default), "Linker failure is propagated");
Check(failed.StrippingReport!.DependencyPasses==1 && failed.StrippingReport.LinkerLog.Contains("probe-error"),
    "Failed linking preserves diagnostics and actual pass count");
runner.Reset(); var brokenGraph = new ResourceGraph(ra,rb) { ThrowAtCollection=2 };
await ThrowsAsync<IOException>(()=>PlayerManagedCodeBuildPipeline.BuildStartupClosureAsync(
    Context(project,"GraphFailure"),workspace,inputs,brokenGraph,files,paths,default), "Content dependency failures propagate across the generic build boundary");
Check(runner.Calls==1, "Content failure stops further linker passes");

var web = Context(project,"WebImages","web",PlayerManagedStrippingLevel.Conservative);
var images = await PlayerManagedCodeBuildPipeline.FinalizeImagesAsync(web,workspace,startup,inputs,true,[],[],files,paths,default);
Check(images.Inputs.Assemblies.Single(i=>i.Name==a.Name).AssemblyPath==a.AssemblyPath,
    "Precompiled Web stages share one original package image in non-Full builds");
Check(ReferenceEquals(images.AllInputs,inputs) && ReferenceEquals(images.Inputs.PackageResources,startup.PackageResources),
    "Image canonicalization does not broaden content roots");
Check(web.WebPrecompiledSharedManagedCodeAssemblyPaths.SequenceEqual([a.AssemblyPath]), "Shared native Web metadata uses startup-admitted packages");
Check(web.WebPrecompiledManagedCodeAssemblyPaths.Contains(game.AssemblyPath) && !web.WebPrecompiledManagedCodeAssemblyPaths.Contains(a.AssemblyPath),
    "Deferred Web code and shared metadata retain distinct compilation roles");
Check(web.WebPrecompiledManagedCodeReferencePaths.Contains(a.AssemblyPath) && web.WebPrecompiledManagedCodeReferencePaths.Contains(b.AssemblyPath),
    "Web compilation references include both shared and deferred packages");
var plain = Context(project,"PlainImages");
var plainImages = await PlayerManagedCodeBuildPipeline.FinalizeImagesAsync(plain,workspace,deferred,inputs,false,[],[],files,paths,default);
Check(ReferenceEquals(plainImages.Inputs,deferred) && ReferenceEquals(plainImages.AllInputs,inputs), "Non-canonical builds preserve selected code images");
var native = Image("Native", "package:native", [a.Name], native:true);
var nativeOriginals = new RuntimeManagedCodeReleaseInputSet([entry,a,native],[],[]);
var changedPath = Path.Combine(root,"Linked/Native.dll"); Directory.CreateDirectory(Path.GetDirectoryName(changedPath)!);
using(var changedImage=AssemblyDefinition.ReadAssembly(native.AssemblyPath))
{
    changedImage.CustomAttributes.Clear(); changedImage.MainModule.Mvid=Guid.NewGuid(); changedImage.Write(changedPath);
}
var changed = native with {AssemblyPath = changedPath, AssemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(changedPath)))};
var nativeContext = Context(project,"Native","web",PlayerManagedStrippingLevel.Conservative);
var restored = PlayerManagedCodeBuildPipeline.PreserveNativeImages(nativeContext, new([entry,changed],[],[]), nativeOriginals);
Check(restored.Assemblies.Single(i=>i.Name==native.Name).AssemblyPath==native.AssemblyPath && restored.Assemblies.Any(i=>i.Name==a.Name),
    "Native requests restore original images and their complete managed dependency closure");

var legacyProject = Project("Legacy"); var legacy = ProjectWorkspace.Open(legacyProject);
Directory.CreateDirectory(legacy.ScriptAssembliesPath);
File.Copy(game.AssemblyPath,Path.Combine(legacy.ScriptAssembliesPath,"GameScripts.dll"));
File.Copy(b.AssemblyPath,Path.Combine(legacy.ScriptAssembliesPath,b.Name+".dll"));
File.Copy(a.AssemblyPath,Path.Combine(legacy.ScriptAssembliesPath,a.Name+".dll"));
var collected=RuntimeManagedCodeReleaseInputCollector.Collect(legacy,legacy.ScriptAssembliesPath,files,paths);
Check(collected.Assemblies.Select(i=>i.Name).SequenceEqual([a.Name,b.Name,game.Name]), "Core collects adjacent external dependencies in topological order");
Check(collected.Assemblies.All(i=>i.AssemblySha256==Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(i.AssemblyPath)))),
    "Collected inputs carry hashes of the actual selected code images");
var bootstrapProject = Project("Bootstrap"); var bootstrapWorkspace = ProjectWorkspace.Open(bootstrapProject);
var bootstrapPackage = Path.Combine(bootstrapWorkspace.PackageCachePath, "test.bootstrap");
Directory.CreateDirectory(bootstrapPackage);
Directory.CreateDirectory(Path.Combine(bootstrapProject,"Packages"));
File.WriteAllText(Path.Combine(bootstrapProject,"Packages/manifest.yaml"),
    "format: BEngine.Packages\nversion: 1\npackages:\n- id: test.bootstrap\n  version: 1.0.0\n  enabled: true\n");
File.WriteAllText(Path.Combine(bootstrapPackage,"package.yaml"),
    "format: BEngine.Package\nversion: 2\nid: test.bootstrap\npackageVersion: 1.0.0\nruntime:\n  assembly: BEngine\n  bootstrap: true\n");
var bootstrapInputs=RuntimeManagedCodeReleaseInputCollector.Collect(bootstrapWorkspace,
    bootstrapWorkspace.ScriptAssembliesPath,files,paths);
var bootstrapImage=bootstrapInputs.Assemblies.Single(input=>input.Name=="BEngine");
Check(bootstrapImage.AssemblyPath==hostPath && bootstrapImage.BuildId==bootstrapImage.AssemblySha256[..24],
    "Bootstrap package collection uses the installed image and its actual hash without a project cache pointer");
File.WriteAllText(Path.Combine(bootstrapPackage,"package.yaml"),
    "format: BEngine.Package\nversion: 2\nid: test.bootstrap\npackageVersion: 1.0.0\nruntime:\n  assembly: Missing.Bootstrap\n  bootstrap: true\n");
try { RuntimeManagedCodeReleaseInputCollector.Collect(bootstrapWorkspace,bootstrapWorkspace.ScriptAssembliesPath,files,paths);
    throw new InvalidOperationException("Missing bootstrap image was accepted"); }
catch(FileNotFoundException) { Check(true,"Missing installed bootstrap images fail rather than using arbitrary loaded assemblies"); }
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly=>assembly.GetName().Name is "BEngine.AssetBundles" or "BEngine.AssetBundles.Editor" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"),
    "Code graph collection, convergence and image finalization need no concrete content or drawing package");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new {Passed=true,Checks=checks.Count,Details=checks,
    Scope="Core public code pipeline with real PE/descriptor files and controlled linker process results. Actual SDK linking is verified separately by Windows/Web Player builds."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("MANAGED_CODE_BUILD_CORE_OK|"+checks.Count);

sealed class ResourceGraph(RuntimePackageResourceInput first,RuntimePackageResourceInput second):IPlayerManagedCodeResourceGraph
{
    public int Collections; public int ThrowAtCollection;
    public IReadOnlyList<string> CollectAssets(IReadOnlyList<string> assemblies,IReadOnlyCollection<string> resources,
        PlayerStrippingSettings settings,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if(++Collections==ThrowAtCollection)throw new IOException("content probe");
        var assets=new List<string>{"Assets/AOT/root.yaml"};
        if(resources.Contains(first.FilePath))assets.Add("Assets/AOT/late.yaml");
        if(resources.Contains(second.FilePath))assets.Add("Assets/AOT/tail.yaml");
        return assets;
    }
    public IReadOnlyList<RuntimePackageResourceInput> FilterResources(IReadOnlyList<RuntimeManagedCodeReleaseInput> assemblies,
        IReadOnlyList<RuntimePackageResourceInput> availableResources,IReadOnlyList<string> files,
        PlayerStrippingSettings settings,PlayerStrippingReport report)
    {
        var names=assemblies.Select(i=>i.Name).ToHashSet(); var selected=new List<RuntimePackageResourceInput>();
        if(names.Contains("PackageA"))selected.Add(first); if(names.Contains("PackageB"))selected.Add(second);
        foreach(var item in selected)report.Resources.Add(new(){Name=item.Address,Included=true,OriginalBytes=item.Size,PublishedBytes=item.Size});
        return selected;
    }
}
sealed class LinkerProbe:IProcessRunner
{
    public RuntimeManagedCodeReleaseInputSet Source=null!;
    public int Calls;public bool Fail;public List<bool> RootedA=new(),RootedDead=new(),OriginalReferences=new();
    public void Reset(){Calls=0;Fail=false;RootedA.Clear();RootedDead.Clear();OriginalReferences.Clear();}
    public ProcessRunResult Run(ProcessRunRequest request,CancellationToken token=default)=>RunAsync(request,token).GetAwaiter().GetResult();
    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();Calls++;
        if(Fail)return Task.FromResult(new ProcessRunResult(23,"probe-output","probe-error"));
        var args=request.Arguments;var descriptor=XDocument.Load(args[args.IndexOf("-x")+1]);
        bool Rooted(string name)=>descriptor.Descendants("assembly").Any(a=>(string?)a.Attribute("fullname")==name&&a.Elements("type").Any());
        RootedA.Add(Rooted("PackageA"));RootedDead.Add(Rooted("Dead"));
        var references=Enumerable.Range(0,args.Count-1).Where(i=>args[i]=="-reference").Select(i=>args[i+1]).ToHashSet();
        OriginalReferences.Add(Source.Assemblies.Where(i=>i.Origin!="project:GameScripts").All(i=>references.Contains(i.AssemblyPath)));
        var destination=args[args.IndexOf("-out")+1];Directory.CreateDirectory(destination);
        foreach(var input in Source.Assemblies.Where(i=>i.Name is "AOT" or "PackageA" || Calls>=2&&i.Name=="PackageB"))
            File.Copy(input.AssemblyPath,Path.Combine(destination,input.Name+".dll"));
        return Task.FromResult(new ProcessRunResult(0,"controlled linker outputs",""));
    }
}
