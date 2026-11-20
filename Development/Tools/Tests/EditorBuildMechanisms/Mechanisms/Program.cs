using System.Reflection;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);DotNetPlatformServices.Initialize();
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
T Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T e){Check(true,name);return e;}throw new Exception(name);}

// Shared project conventions compare only the final path component, on both separator forms.
foreach(var name in new[]{"data.meta","data.META","a.meta.lock","a.META.LOCK-123","~atomic",".atomic.tmp",".ATOMIC.TMP"})
 Check(ProjectFileConventions.IsAuxiliaryPath("root/sub/"+name)&&ProjectFileConventions.IsAuxiliaryPath(@"root\sub\"+name),"Auxiliary entry: "+name);
foreach(var name in new[]{"data.yaml","data.meta.yaml",".gitignore","shader.tmp","folder.meta/asset.png","folder.meta\\asset.png"})
 Check(!ProjectFileConventions.IsAuxiliaryPath(name),"Ordinary entry remains visible: "+name);

var paths=PathUtility.Current;var root=Path.Combine(output,"ChildRoot");
Check(paths.ResolveChildPath(root,"a/b.txt")==Path.Combine(root,"a","b.txt"),"Forward separators resolve through the active platform");
Check(paths.ResolveChildPath(root,@"a\b.txt")==Path.Combine(root,"a","b.txt"),"Back separators resolve through the active platform");
Check(paths.ResolveChildPath(Path.GetPathRoot(root)!,"child.txt")==Path.Combine(Path.GetPathRoot(root)!,"child.txt"),"Volume roots retain their separator containment boundary");
foreach(var relative in new[]{""," ",".","..","a/../b","a/./b","a//b","a\\\\b","a/","a:b",root,root+"-sibling/file"})
 Throws<InvalidDataException>(()=>paths.ResolveChildPath(root,relative),"Reject invalid relative child: "+relative);
Check(!Directory.Exists(root),"Lexical resolution does not create directories");

var document=new AssemblyDefinitionDocument{Name="Game",RootNamespace="Demo.Game",References=["Engine"],DefineConstraints=["A","!B"]};document.Validate();
Check(true,"Assembly documents validate through their format owner");
void Invalid(Action<AssemblyDefinitionDocument> edit,string name){var d=new AssemblyDefinitionDocument();edit(d);Throws<InvalidDataException>(d.Validate,name);}
Invalid(x=>x.Format="Other","Reject foreign assembly format");Invalid(x=>x.Version=2,"Reject foreign assembly version");
Invalid(x=>x.Name=" Bad ","Reject untrimmed assembly identity");Invalid(x=>x.RootNamespace="9.Bad","Reject invalid root namespace");
Invalid(x=>x.References=["X","x"],"Reject case-insensitive duplicate references");Invalid(x=>x.References=["Game"],"Reject self reference");
Invalid(x=>{x.IncludePlatforms=["windows"];x.ExcludePlatforms=["web"];},"Reject simultaneous include and exclude lists");
Invalid(x=>x.DefineConstraints=["!bad-symbol"],"Reject invalid negative define");
Check(document.References.SequenceEqual(new[]{"Engine"})&&document.Name=="Game","Validation never normalizes or applies the caller's document");
var retired=new Dictionary<string,string>{{"source.custom","provider/retired.artifact"}};
var resultData=new ShaderCompilationResult(new Dictionary<string,string>(),retired,new Dictionary<string,string>());
Check(resultData.DeletedArtifacts["source.custom"]=="provider/retired.artifact"&&resultData.DeletedAssets.SequenceEqual(new[]{"source.custom"}),"A custom shader compiler can retrieve its own retirement payload without private access");
Check(resultData.Succeeded,"Retired artifacts do not imply compilation errors");

var owner=Environment.CurrentManagedThreadId;var pumps=0;var active=0;var peak=0;var started=0;
ThreadPool.GetMinThreads(out var threads,out var ports);ThreadPool.SetMinThreads(Math.Max(threads,4),ports);
using(var ready=new CountdownEvent(2))using(var release=new ManualResetEventSlim())
using(EditorExecutionPump.Register(()=>{CheckOwner();pumps++;if(ready.IsSet)release.Set();})){
 var results=EditorWorkBatch.Run(6,i=>{var running=Interlocked.Increment(ref active);int old;do{old=peak;}while(running>old&&Interlocked.CompareExchange(ref peak,running,old)!=old);
  try{if(Interlocked.Increment(ref started)<=2)ready.Signal();if(!release.Wait(5000))throw new TimeoutException("Owner pump did not release preparation");if(Environment.CurrentManagedThreadId==owner)throw new Exception("Worker ran on owner");return i*i;}
  finally{Interlocked.Decrement(ref active);}},null,maximumConcurrency:2);
 Check(results.SequenceEqual(new[]{0,1,4,9,16,25}),"Parallel preparation returns input-index order");
 Check(peak==2&&active==0,"Configured concurrency is respected and workers finish before return");
 Check(pumps>0,"Observer-free preparation continues pumping the owner thread");
}
var observed=new HashSet<int>();var callbackThreads=new HashSet<int>();
EditorWorkBatch.Run(12,i=>i,(completed,i)=>{observed.Add(i);callbackThreads.Add(Environment.CurrentManagedThreadId);},maximumConcurrency:2);
Check(observed.SetEquals(Enumerable.Range(0,12))&&callbackThreads.SetEquals(new[]{owner}),"Every input progress record reaches only the owning thread");
var workerError=new ProbeFailure();var thrown=Throws<ProbeFailure>(()=>EditorWorkBatch.Run<int>(1,_=>throw workerError,null),"Single worker exception is unwrapped");
Check(ReferenceEquals(thrown,workerError),"Worker exception identity is preserved");
var finished=false;var observerError=new ProbeFailure();
var error=Throws<ProbeFailure>(()=>EditorWorkBatch.Run(1,_=>{Thread.Sleep(40);finished=true;return 0;},(_,_)=>throw observerError),"Observer failure propagates");
Check(finished&&ReferenceEquals(error,observerError),"An observer failure cannot outlive unfinished worker scope");
finished=false;using(EditorExecutionPump.Register(()=>throw new ProbeFailure()))
 Throws<ProbeFailure>(()=>EditorWorkBatch.Run(1,_=>{Thread.Sleep(40);finished=true;return 0;},null),"Pump failure propagates after worker completion");
Check(finished,"A failing host pump cannot leave preparation running");
using(var cancelled=new CancellationTokenSource()){
 cancelled.Cancel();Throws<OperationCanceledException>(()=>EditorWorkBatch.Run(1,i=>i,null,cancelled.Token),"Pre-cancelled batches reject work");
 Check(EditorWorkBatch.Run(0,i=>i,null,cancelled.Token).Length==0,"Empty batches preserve their no-work fast path");
}
Throws<ArgumentOutOfRangeException>(()=>EditorWorkBatch.Run(1,i=>i,null,maximumConcurrency:0),"Invalid concurrency is rejected before work");
void CheckOwner(){if(Environment.CurrentManagedThreadId!=owner)throw new Exception("Pump owner changed");}

const string yaml="""
format: BEngine.Scene
version: 2
id: a0700100-0000-4000-8000-000000000001
name: Unknown types remain data
gameObjects:
- id: a0700100-0000-4000-8000-000000000010
  name: Object
  transform:
    id: a0700100-0000-4000-8000-000000000011
    type: BEngine.Transform
    localPosition: { x: 1, y: 2 }
    localRotation: '0'
    localScale: { x: 1, y: 1 }
    fields: {}
  components:
  - id: a0700100-0000-4000-8000-000000000012
    type: Missing.UnloadedComponent, Missing.Runtime
    enabled: false
    fields: { address: 'guid:00000000000000000000000000000001', text: '汉字 value' }
""";
var source=Encoding.UTF8.GetBytes(yaml);var initial=source.ToArray();var compiled=SceneBuildDataCompiler.CompileRuntimeBytes(source);
Check(BinaryDataSerializer.IsBinaryData(compiled),"Scene compilation emits the existing binary format");
Check(source.SequenceEqual(initial),"Scene compilation leaves authoring bytes unchanged");
var json=JsonSerializer.Serialize(BinaryDataSerializer.Deserialize<Dictionary<string,object?>>(compiled));
Check(json.Contains("Missing.UnloadedComponent")&&json.Contains("Missing.Runtime")&&json.Contains("guid:"),"Unknown component identities and resource references remain encoded data");
var copy=SceneBuildDataCompiler.CompileRuntimeBytes(compiled);Check(copy.SequenceEqual(compiled)&&!ReferenceEquals(copy,compiled),"Already compiled scenes retain exact bytes with independent ownership");
var objects=BEngine.Instrumentation.RuntimeObjectDiagnostics.CountCurrentDomainObjects();
SceneBuildDataCompiler.CompileRuntimeBytes(Encoding.UTF8.GetBytes(yaml.Replace("Missing.UnloadedComponent, Missing.Runtime",typeof(SceneProbe).AssemblyQualifiedName!)));
Check(SceneProbe.Constructions==0&&BEngine.Instrumentation.RuntimeObjectDiagnostics.CountCurrentDomainObjects()==objects,"Compiling known component identities never constructs live engine objects");
var large=Encoding.UTF8.GetBytes(yaml.Replace("Unknown types remain data",new string('x',1_048_577)));
Check(SceneBuildDataCompiler.CompileRuntimeBytes(large).SequenceEqual(large),"Valid scenes outside compact scalar limits preserve exact original YAML bytes");
Throws<InvalidDataException>(()=>SceneBuildDataCompiler.CompileRuntimeBytes(Encoding.UTF8.GetBytes(yaml.Replace("version: 2","version: 1"))),"Scene format validation precedes compilation");
using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Throws<OperationCanceledException>(()=>SceneBuildDataCompiler.CompileRuntimeBytes(source,cancelled.Token),"Cancelled scene compilation never proceeds");}
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "Missing.Runtime" or "BEngine.Graphics" or "BEngine.IMGUI.Editor"),"Document mechanisms do not load project types or presentation packages");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("EDITOR_BUILD_MECHANISMS_OK|"+checks.Count);
public sealed class ProbeFailure:Exception;
public sealed class SceneProbe:Component{public static int Constructions;public SceneProbe(){Constructions++;}}
