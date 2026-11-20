using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Platform.DotNet;
using BEngine.Serialization;
using BEngine.Instrumentation;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
T Complete<T>(BTask<T> task){var timer=Stopwatch.StartNew();while(!task.IsCompleted&&timer.ElapsedMilliseconds<10000){BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(1));Thread.Yield();}if(!task.IsCompleted)throw new TimeoutException();return task.GetAwaiter().GetResult();}
var owner=typeof(ProbeRestorer).Assembly;var resources=new ProbeResources();
SceneReferenceRestorers.Register(new ProbeRestorer());
Throws<ArgumentNullException>(()=>SceneReferenceRestorers.Register(null!),"Null registration rejected");
Throws<ArgumentException>(()=>SceneReferenceRestorers.Register(new InvalidRestorer()),"Non-component registration rejected");
Throws<InvalidOperationException>(()=>default(SceneComponentReferences).TryGetValue("payload",out _),"Default reference view rejected");
Throws<ArgumentNullException>(()=>SceneAssetLoading.Restore(default,null,null!),"Null provider rejected");
Throws<ArgumentException>(()=>SceneAssetLoading.Restore(default,null,resources),"Unparsed scene input rejected");
var id=Guid.NewGuid();var child=Guid.NewGuid();var component=Guid.NewGuid();
var yaml=$$"""
format: BEngine.Scene
version: 2
id: {{id}}
name: Reference probe
gameObjects:
- id: {{child}}
  name: Root
  active: true
  transform:
    id: {{Guid.NewGuid()}}
    localPosition: { x: '12.5', y: '-3.25' }
  components:
  - id: {{component}}
    type: '{{typeof(ProbeComponent).AssemblyQualifiedName}}'
    enabled: true
    fields: { value: '7', payload: 'logical/resource' }
prefabInstances: []
""";
foreach(var bytes in new[]{Encoding.UTF8.GetBytes(yaml),BinaryDataSerializer.CompileYaml(Encoding.UTF8.GetBytes(yaml))})
{
 var data=SceneAssetLoading.ReadData(bytes);ProbeRestorer.Calls=0;
 using(var normal=SceneAssetLoading.Restore(data))
 {Check(normal.gameObjects.Single().GetComponent<ProbeComponent>()!.value==7,"Ordinary hydration retains fields");Check(ProbeRestorer.Calls==0,"Ordinary hydration does not invoke content migration");}
 using(var scene=SceneAssetLoading.Restore(data,null,resources))
 {var probe=scene.gameObjects.Single().GetComponent<ProbeComponent>()!;Check(ProbeRestorer.Calls==1&&probe.value==19,"Explicit content migration runs once");Check(scene.Id==id&&probe.Id==component&&scene.gameObjects.Single().Id==child,"Migration preserves scene and object identities");Check(probe.Deserialized,"Migration follows serialization callbacks");}
 var progress=new List<float>();
 using(var scene=Complete(SceneAssetLoading.RestoreAsync(data,null,new(default,progress.Add),resources)))
 {Check(scene.gameObjects.Single().GetComponent<ProbeComponent>()!.value==19,"Asynchronous hydration uses the same migration");Check(progress.Count>1&&progress[^1]==1&&progress.Zip(progress.Skip(1)).All(p=>p.First<=p.Second),"Asynchronous migration reports monotonic completion");}
 SceneReferenceRestorers.Register(new ProbeRestorer());ProbeRestorer.Calls=0;
 using(var scene=SceneAssetLoading.Restore(data,null,resources))Check(ProbeRestorer.Calls==1,"Repeated module registration replaces its previous implementation");
 var before=RuntimeObjectDiagnostics.CountCurrentDomainObjects();ProbeRestorer.Fail=true;
 Throws<InvalidDataException>(()=>SceneAssetLoading.Restore(data,null,resources),"Migration errors are propagated");
 Check(RuntimeObjectDiagnostics.CountCurrentDomainObjects()==before,"Failed migration destroys the partial scene");ProbeRestorer.Fail=false;
 using var cancel=new BCancellationSource();cancel.Cancel();
 Throws<BOperationCanceledException>(()=>Complete(SceneAssetLoading.RestoreAsync(data,null,new(cancel.Token),resources)),"Canceled asynchronous migration is rejected");
 Check(RuntimeObjectDiagnostics.CountCurrentDomainObjects()==before,"Canceled migration creates no objects");
 RuntimeAssemblyCleanup.UnregisterExtensions(owner);ProbeRestorer.Calls=0;
 using(var scene=SceneAssetLoading.Restore(data,null,resources))Check(ProbeRestorer.Calls==0&&scene.gameObjects.Single().GetComponent<ProbeComponent>()!.value==7,"Owner cleanup retires registrations and cached resolutions");
 SceneReferenceRestorers.Register(new ProbeRestorer());
}
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.AssetBundles" or "BEngine.IMGUI.Editor"),"Core reference migration loads no rendering, archive or UI packages");
Check(!typeof(SceneComponentReferences).GetFields(BindingFlags.Public|BindingFlags.Instance).Any(),"Reference views expose no document fields");
RuntimeAssemblyCleanup.UnregisterExtensions(owner);
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("SCENE_REFERENCE_CORE_OK|"+checks.Count);

public sealed class ProbeComponent:Component,ISerializationCallbackReceiver
{public int value;public string payload="";[NonSerialized]public bool Deserialized;public void OnBeforeSerialize(){}public void OnAfterDeserialize()=>Deserialized=true;}
sealed class ProbeRestorer:SceneReferenceRestorer
{
 public static int Calls;public static bool Fail;public override Type ComponentType=>typeof(ProbeComponent);
 public override void Restore(Component component,SceneComponentReferences references,IResourceObjectProvider resources)
 {Calls++;if(Fail)throw new InvalidDataException("Fixture failure");var probe=(ProbeComponent)component;if(!probe.Deserialized||!references.TryGetValue("payload",out var path)||path!="logical/resource"||!resources.TryLoadObject(path,"Resources",typeof(TextAsset),out var asset)||asset is not TextAsset{text:"verified"})throw new Exception("Reference context or order is wrong");probe.value=19;}
}
sealed class InvalidRestorer:SceneReferenceRestorer
{public override Type ComponentType=>typeof(string);public override void Restore(Component c,SceneComponentReferences r,IResourceObjectProvider p){} }
sealed class ProbeResources:IResourceObjectProvider
{private readonly TextAsset _asset=new("verified", "probe.txt");public bool TryLoadObject(string path,string folder,Type type,out BObject value){value=_asset;return path=="logical/resource"&&folder=="Resources"&&type==typeof(TextAsset);}}
