using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine("E:/Project/_BZP/BEngine/Temp/SR23",Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
var loading=new TrackedLoading(new SystemAssemblyLoadingService(FileUtility.Current,PathUtility.Current,PlatformUtility.Current));
using var host=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
 .AddSingleton<IAssemblyLoadingService>(loading).AddSingleton<IPackageCatalogSource,EmptyCatalog>().BuildServiceProvider();
PlatformRuntime.Initialize(host);using var attachment=EditorServiceScope.Attach(host);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
var project=Path.Combine(root,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets/Editor"));Directory.CreateDirectory(Path.Combine(project,"Packages"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: SessionProbe\n");
File.WriteAllText(Path.Combine(project,"Packages/manifest.yaml"),"packages: []\n");
File.WriteAllText(Path.Combine(project,"Assets/RuntimeProbe.cs"),"public sealed class RuntimeProbe { public static int Value => 41; }");
File.WriteAllText(Path.Combine(project,"Assets/Editor/EditorProbe.cs"),"public sealed class EditorProbe { public static System.Type RuntimeType => typeof(RuntimeProbe); }");
var workspace=ProjectWorkspace.Open(project);using var instance=EditorInstanceContext.Create(project);
using var canceled=new CancellationTokenSource();canceled.Cancel();
Throws<OperationCanceledException>(()=>ProjectScriptSession.Open(workspace,canceled.Token),"Canceled session performs no compilation or loading");
Throws<ArgumentNullException>(()=>ProjectScriptSession.Open(null!),"Null workspace rejected");
var existing=ProjectScriptCompiler.CompileAndLoad(workspace)!;
var existingType=existing.GetType("RuntimeProbe",true)!;
var existingScopes=loading.ProjectScopes.ToArray();
Check((int)existingType.GetProperty("Value")!.GetValue(null)! ==41,"Fixture has an existing editor-owned graph");
using var a=ProjectScriptSession.Open(workspace);var aScopes=loading.ProjectScopes.Except(existingScopes).ToArray();
using var b=ProjectScriptSession.Open(workspace);var bScopes=loading.ProjectScopes.Except(existingScopes).Except(aScopes).ToArray();
Check(aScopes.Length==2&&bScopes.Length==2,"Each compilation session owns its two loading scopes");
Check(a.Assemblies.Count==2&&b.Assemblies.Count==2,"Sessions expose exactly their runtime and editor project assemblies");
Type Runtime(ProjectScriptSession s)=>s.Assemblies.Select(x=>x.GetType("RuntimeProbe")).Single(t=>t is not null)!;
Type Editor(ProjectScriptSession s)=>s.Assemblies.Select(x=>x.GetType("EditorProbe")).Single(t=>t is not null)!;
var aRuntime=Runtime(a);var bRuntime=Runtime(b);
Check(aRuntime!=existingType&&bRuntime!=existingType&&aRuntime!=bRuntime,"Sessions do not reuse another owner's project assembly");
Check(ReferenceEquals(Editor(a).GetProperty("RuntimeType")!.GetValue(null),aRuntime),"Editor phase binds to its own runtime graph");
Check(ReferenceEquals(Editor(b).GetProperty("RuntimeType")!.GetValue(null),bRuntime),"Second session keeps its own runtime type identity");
a.Dispose();a.Dispose();
Check(aScopes.All(s=>s.Unloads==1)&&bScopes.All(s=>s.Unloads==0)&&existingScopes.All(s=>s.Unloads==0),"Disposal unloads exactly the retired owner's scopes once");
Throws<ObjectDisposedException>(()=>_=a.Assemblies,"Retired session rejects access and disposal is idempotent");
Check((int)bRuntime.GetProperty("Value")!.GetValue(null)! ==41,"Disposing one session preserves the other session");
Check((int)existingType.GetProperty("Value")!.GetValue(null)! ==41,"Disposing a session preserves the existing editor graph");
File.WriteAllText(Path.Combine(project,"Assets/Editor/EditorProbe.cs"),"this is not valid C#;");
Throws<InvalidOperationException>(()=>ProjectScriptSession.Open(workspace),"Compilation failure is reported");
Check((int)bRuntime.GetProperty("Value")!.GetValue(null)! ==41,"Failed compilation preserves a live session");
Check((int)existingType.GetProperty("Value")!.GetValue(null)! ==41,"Failed compilation preserves the editor-owned graph");
b.Dispose();
Check(bScopes.All(s=>s.Unloads==1)&&existingScopes.All(s=>s.Unloads==0),"Second disposal preserves every editor-owned scope");
typeof(ProjectScriptCompiler).GetMethod("ReleaseLoadContexts",BindingFlags.Static|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!.Invoke(null,null);
Check(existingScopes.All(s=>s.Unloads==1),"Live editor scopes are released only by their own owner");
Check(!typeof(ProjectScriptSession).Assembly.GetReferencedAssemblies().Any(x=>x.Name is "BEngine.Testing" or "BEngine.Testing.Editor"),"Core compilation session has no test-package dependency");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("SCENE_REFERENCE_EDITOR_OK|"+checks.Count);

sealed class EmptyCatalog:IPackageCatalogSource{public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version};}
sealed class TrackedLoading(IAssemblyLoadingService inner):IAssemblyLoadingService
{
 public List<TrackedScope> ProjectScopes {get;}=[];
 public IEnumerable<Assembly> DefaultAssemblies=>inner.DefaultAssemblies;
 public bool IsCollectible(Assembly a)=>inner.IsCollectible(a);
 public Assembly LoadDefault(AssemblyName n)=>inner.LoadDefault(n);
 public AssemblyName GetAssemblyName(string p)=>inner.GetAssemblyName(p);
 public IDisposable ObserveDefaultResolution(Func<AssemblyName,Assembly?> r)=>inner.ObserveDefaultResolution(r);
 public IAssemblyScope CreateProjectScope(IReadOnlyDictionary<string,string> r,IReadOnlyDictionary<string,Assembly> s){var scope=new TrackedScope(inner.CreateProjectScope(r,s));ProjectScopes.Add(scope);return scope;}
 public IAssemblyScope CreatePackageScope(string p,string d,Func<AssemblyName,Assembly?> r)=>inner.CreatePackageScope(p,d,r);
}
sealed class TrackedScope(IAssemblyScope inner):IAssemblyScope
{
 public int Unloads {get;private set;}
 public IEnumerable<Assembly> Assemblies=>inner.Assemblies;
 public WeakReference Lifetime=>inner.Lifetime;
 public Assembly LoadFromAssemblyPath(string p)=>inner.LoadFromAssemblyPath(p);
 public void Unload(){Unloads++;inner.Unload();}
}
