using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.AssetBundles;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Instrumentation;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var attachment=EditorServiceScope.Attach(services);
var editor=Assembly.Load("BEngine.Rendering2D.Editor");RuntimeHelpers.RunModuleConstructor(editor.ManifestModule.ModuleHandle);
var checks=new List<string>();void Check(bool v,string n){if(!v)throw new Exception(n);checks.Add(n);}
void Throws<T>(Action f,string n)where T:Exception{try{f();}catch(TargetInvocationException e)when(e.InnerException is T){Check(true,n);return;}catch(T){Check(true,n);return;}throw new Exception(n);}
var sprite=new Sprite{PivotX=Fix64.Parse("0.25"),PivotY=Fix64.Parse("0.75"),LocalIdentifier=21300004};
var provider=new ProbeResources{Value=sprite};var id=Guid.NewGuid();var componentId=Guid.NewGuid();
var yaml=$$"""
format: BEngine.Scene
version: 2
id: {{id}}
name: Render reference
gameObjects:
- id: {{Guid.NewGuid()}}
  name: Sprite
  active: true
  transform: { id: '{{Guid.NewGuid()}}' }
  components:
  - id: {{componentId}}
    type: '{{typeof(SpriteRenderer).AssemblyQualifiedName}}'
    enabled: true
    fields: { sprite: 'Assets/Missing.sprite' }
prefabInstances: []
""";
foreach(var bytes in new[]{Encoding.UTF8.GetBytes(yaml),BinaryDataSerializer.CompileYaml(Encoding.UTF8.GetBytes(yaml))})
{
 var data=SceneAssetLoading.ReadData(bytes);
 using(var scene=SceneAssetLoading.Restore(data,services,provider))
 {
  var renderer=scene.gameObjects.Single().GetComponent<SpriteRenderer>()!;
  Check(ReferenceEquals(renderer.sprite,sprite),"Content provider restores the renderer's exact Sprite instance");
  Check(scene.Id==id&&renderer.Id==componentId,"Sprite migration preserves scene and component GUIDs");
  Check(renderer.sprite!.pivot==new Vector2(Fix64.Parse("0.25"),Fix64.Parse("0.75"))&&renderer.sprite.LocalIdentifier==21300004,"Sprite subasset and pivot metadata are retained");
 }
 var before=RuntimeObjectDiagnostics.CountCurrentDomainObjects();provider.Value=null;
 Throws<InvalidDataException>(()=>SceneAssetLoading.Restore(data,services,provider),"Missing content and source Sprite fail explicitly");
 Check(RuntimeObjectDiagnostics.CountCurrentDomainObjects()==before,"Sprite restoration failure destroys the partial scene");
 provider.Value=new BEngine.TextAsset("wrong","wrong.txt");
 Throws<InvalidDataException>(()=>SceneAssetLoading.Restore(data,services,provider),"Incorrect resource object type is rejected");
 BObject.DestroyImmediate(provider.Value);provider.Value=sprite;
 // Exercise the source fallback on the same owner callback, without relying on a global provider.
 var ownerType=editor.GetType("BEngine.Editor.SpriteSceneReferenceRestorer",true)!;
 var restorer=(SceneReferenceRestorer)Activator.CreateInstance(ownerType,nonPublic:true)!;
 var view=(SceneComponentReferences)typeof(SceneComponentReferences).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single().Invoke([new Dictionary<string,string>{{"sprite","Assets/Missing.sprite"}}]);
 using var fallback=new Scene("fallback",services);var existing=fallback.CreateGameObject("Existing").AddComponent<SpriteRenderer>();existing.sprite=sprite;
 provider.Value=null;restorer.Restore(existing,view,provider);
 Check(ReferenceEquals(existing.sprite,sprite),"Existing source Sprite remains when the content store has no replacement");
 provider.Value=sprite;
}
var project=Path.Combine(output,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets"));Directory.CreateDirectory(Path.Combine(project,"Packages"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),"format: BEngine.Project\nversion: 1\nname: ReferenceProbe\n");File.WriteAllText(Path.Combine(project,"Packages/manifest.yaml"),"packages: []\n");
var workspace=ProjectWorkspace.Open(project);var manager=new ProbeManager();
var plain=Encoding.UTF8.GetBytes($"format: BEngine.Scene\nversion: 2\nid: {Guid.NewGuid()}\nname: Plain\ngameObjects: []\nprefabInstances: []\n");
manager.Files["Assets/One/Main.scene.yaml"]=plain;
var loaderType=Assembly.Load("BEngine.AssetBundles.Editor").GetType("BEngine.Editor.Build.EditorPlayModeSceneLoader",true)!;
var loader=(ISceneLoader)Activator.CreateInstance(loaderType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[workspace,manager,PathUtility.Current],null)!;
using(var scene=loader.LoadScene("Main",services))Check(scene.path=="Assets/One/Main.scene.yaml"&&scene.name=="Plain","AB scene loader uses the public opaque document API and binds its address");
using(var scene=loader.LoadScene(Path.Combine(project,"Assets/One/Main.scene.yaml"),services))Check(scene.path=="Assets/One/Main.scene.yaml","Absolute project scene path resolves without changing its address");
Throws<FileNotFoundException>(()=>loader.LoadScene("Unknown",services),"Missing AB scene is reported");
manager.Files["Assets/Two/Main.scene.yaml"]=plain;
Throws<InvalidOperationException>(()=>loader.LoadScene("Main",services),"Ambiguous AB scene name is rejected");
manager.Files["Assets/Two/Broken.scene.yaml"]=[];manager.FailRead=true;
Throws<FileNotFoundException>(()=>loader.LoadScene("Broken",services),"Unreadable AB scene is reported");
manager.FailRead=false;
const string address="Assets/Resources/Shared.sprite";
manager.ActiveCatalog=new AssetBundleCatalog{Assets=[new(){Address=address,Guid=Guid.NewGuid(),AssetType="Sprite"}]};
manager.Files[address]=[1,2,3];
var codec=new ProbeSpriteCodec();RuntimeResourceCodecs.Register(codec);
var sceneResources=(IResourceObjectProvider)loaderType.GetField("_resources",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(loader)!;
Check(sceneResources.TryLoadObject(address,"Resources",typeof(Sprite),out var first)&&sceneResources.TryLoadObject(address,"Resources",typeof(Sprite),out var second),"Scene resource adapter forwards complete catalog addresses");
sceneResources.TryLoadObject(address,"Resources",typeof(Sprite),out var next);
Check(!ReferenceEquals(first,next),"Scene migration retains independently decoded Sprite identities");
Check(codec.Created.Count==3,"Each successful scene resource lookup invokes the format decoder");
Check(!sceneResources.TryLoadObject("Shared","Resources",typeof(Sprite),out _),"Scene references do not acquire Resources directory search semantics");
manager.FailRead=true;
Check(!sceneResources.TryLoadObject(address,"Resources",typeof(Sprite),out _),"A prior object does not mask a later payload read failure");
manager.FailRead=false;manager.Files["Assets/One/Rendered.scene.yaml"]=Encoding.UTF8.GetBytes(yaml.Replace("Assets/Missing.sprite",address));
using(var one=loader.LoadScene("Rendered",services))using(var two=loader.LoadScene("Rendered",services))
 Check(!ReferenceEquals(one.gameObjects.Single().GetComponent<SpriteRenderer>()!.sprite,two.gameObjects.Single().GetComponent<SpriteRenderer>()!.sprite),"Separate AB scene loads retain the original per-load Sprite identity");
RuntimeResourceCodecs.UnregisterAssembly(typeof(ProbeSpriteCodec).Assembly);
foreach(var created in codec.Created)BObject.DestroyImmediate(created);
Check(!typeof(SceneReferenceRestorers).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "BEngine.Rendering2D" or "BEngine.AssetBundles"),"Core scene references remain independent of their consumers");
BObject.DestroyImmediate(sprite);
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("SCENE_REFERENCE_RENDERING_OK|"+checks.Count);

sealed class ProbeResources:IResourceObjectProvider
{public BObject? Value;public bool TryLoadObject(string p,string f,Type t,out BObject value){value=Value!;return Value is not null;}}
sealed class ProbeManager:IAssetBundleManager
{
 public Dictionary<string,byte[]> Files {get;}=new(StringComparer.OrdinalIgnoreCase);public bool FailRead;
 public AssetBundleCatalog? ActiveCatalog{get;set;}public AssetBundleVersion? ActiveVersion=>null;public bool IsInitialized=>true;
 public IReadOnlyList<string> EnumerateAddresses(string p="")=>Files.Keys.Where(k=>k.StartsWith(p,StringComparison.OrdinalIgnoreCase)).ToArray();
 public bool TryLoadBytes(string a,out byte[] b){if(FailRead){b=[];return false;}return Files.TryGetValue(a,out b!);}
 public BValueTask InitializeAsync(BCancellationToken c=default)=>default;public BValueTask<AssetBundleUpdatePlan> CheckForUpdatesAsync(BCancellationToken c=default)=>throw new NotSupportedException();
 public BValueTask<AssetBundleUpdateResult> ApplyUpdateAsync(AssetBundleUpdatePlan p,IProgress<AssetBundleUpdateProgress>? g=null,BCancellationToken c=default)=>throw new NotSupportedException();
 public BValueTask<bool> RollbackAsync(BCancellationToken c=default)=>throw new NotSupportedException();public BValueTask<AssetBundleHandle<byte[]>> LoadBytesAsync(string a,BCancellationToken c=default)=>throw new NotSupportedException();
 public BValueTask<AssetBundleHandle<string>> LoadTextAsync(string a,BCancellationToken c=default)=>throw new NotSupportedException();public int UnloadUnused()=>0;public BValueTask<int> CleanupAsync(BCancellationToken c=default)=>default;public BValueTask DisposeAsync()=>default;public void Dispose(){}
}
sealed class ProbeSpriteCodec:RuntimeResourceCodec
{
 public List<Sprite> Created{get;}=[];public override Type ObjectType=>typeof(Sprite);
 public override bool CanDecode(in RuntimeResourceMetadata metadata)=>metadata.AssetType=="Sprite";
 public override BObject Decode(ReadOnlyMemory<byte> bytes,in RuntimeResourceMetadata metadata){var value=new Sprite();Created.Add(value);return value;}
}
