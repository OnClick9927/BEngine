using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SourceDatabase=BEngine.ProjectSystem.Editor.AssetDatabase;
using EditorDatabase=BEngine.Editor.AssetDatabase;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();AssetPipelineRuntime.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var scope=EditorServiceScope.Attach(services);
var project=Path.Combine(root,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),YamlUtility.Serialize(new ProjectData{Name="Metadata",EngineVersion="0.1.0"}));
var workspace=ProjectWorkspace.Open(project);var database=new SourceDatabase(workspace);database.Refresh();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS|"+name);}
var assembly=typeof(EditorDatabase).Assembly;
object? Call(string type,string name,params object?[] values){try{return assembly.GetType(type,true)!.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,values);}catch(TargetInvocationException error){throw error.InnerException??error;}}
var bridge=assembly.GetType("BEngine.Editor.EditorBridge",true)!;
var host=DispatchProxy.Create(assembly.GetType("BEngine.Editor.IEditorHost",true)!,typeof(ProbeHost));
var proxy=(ProbeHost)host;proxy.Database=database;proxy.Root=workspace.RootPath;
bridge.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);
var scenes=RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>()!;
var managed=RuntimeServiceContributions.GetDefault<IManagedAssetDocumentCodec>()!;
var sceneSpy=new SceneSpy(scenes);var managedSpy=new ManagedSpy(managed);
RuntimeServiceContributions.RegisterDefault<ISceneDocumentCodec>(sceneSpy);
RuntimeServiceContributions.RegisterDefault<IManagedAssetDocumentCodec>(managedSpy);
try{
 Check(!typeof(BObject).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"Authoring consumer has no private Core grant");
 var scenePath=(string)Call("BEngine.Editor.ProjectAssetCreation","CreateScene","Assets")!;
 Check(sceneSpy.Saves==1 && File.Exists(Path.Combine(workspace.RootPath,scenePath)),"Scene creation delegates persistence to the public document provider");
 Check(sceneSpy.Last is {isCreated:false},"Scene creation releases its temporary scene after a successful save");
 Check(database.GetRecord(scenePath)?.AssetType=="Scene","Provider-backed scene creation still imports a scene record");
 using(var scene=EditorDatabase.LoadAssetAtPath<Scene>(scenePath)!)Check(scene.name=="New Scene","Created scene remains loadable through normal registration");
 sceneSpy.Fail=true;
 try{Call("BEngine.Editor.ProjectAssetCreation","CreateScene","Assets");throw new Exception("Expected provider failure");}catch(IOException){Check(sceneSpy.Last is {isCreated:false},"Scene creation releases temporary state after provider failure");}
 sceneSpy.Fail=false;
 var entryType=assembly.GetType("BEngine.Editor.CreateAssetMenuEntry",true)!;
 var entry=Activator.CreateInstance(entryType,[typeof(MenuAsset),"Tests/Menu","Menu.asset.yaml",0])!;
 var path=(string)Call("BEngine.Editor.ProjectAssetCreation","CreateAttributed","Assets",entry)!;
 Check(managedSpy.Saves==1 && File.Exists(Path.Combine(workspace.RootPath,path)),"Menu asset creation delegates persistence to the public managed-document provider");
 Check(EditorDatabase.LoadAssetAtPath<MenuAsset>(path) is {Value:57},"Provider-written menu asset retains its concrete type and payload");
 var unknown=Path.Combine(workspace.AssetsPath,"unknown.asset.yaml");File.WriteAllText(unknown,"format: BEngine.ManagedAsset\nversion: 2\ntypeName: Missing.Namespace.UnknownAsset, Retired.Assembly\ngraph: {}\n");
 var record=database.ImportAsset(unknown);Check(record.AssetType=="UnknownAsset","Source classification keeps an unloaded type's stored name");
 var metadata=YamlUtility.Load<AssetMetaDocument>(unknown+".meta");var id=metadata.Guid;metadata.Settings["userData"]="keep";YamlUtility.Save(metadata,unknown+".meta");
 database.ImportAsset(unknown);Check(YamlUtility.Load<AssetMetaDocument>(unknown+".meta") is {} meta && meta.Guid==id && meta.Settings["userData"]=="keep","Metadata-only classification preserves GUID and user settings");
 var icon=Path.Combine(root,"Editor","Icons","metadata.svg");Directory.CreateDirectory(Path.GetDirectoryName(icon)!);File.WriteAllText(icon,"<svg xmlns='http://www.w3.org/2000/svg'/>");
 EditorResource.RegisterResourceRoot(root);EditorIconRegistry.Register(typeof(MenuAsset),"Icons/metadata.svg");
 var iconAsset=Path.Combine(workspace.AssetsPath,"icon.asset.yaml");var retired=typeof(MenuAsset).FullName+", Retired.Assembly";
 File.WriteAllText(iconAsset,$"format: BEngine.ManagedAsset\ntypeName: '{retired}'\ngraph: {{ ignored: payload }}\n");
 var constructions=MenuAsset.Constructions;var resolvedIcon=EditorAssetIcons.GetIconPath(iconAsset);
 Check(Path.GetFullPath(resolvedIcon).Equals(Path.GetFullPath(icon),StringComparison.OrdinalIgnoreCase) && MenuAsset.Constructions==constructions,"Managed icon lookup uses active serialized type resolution without constructing the asset");

 Hook.Trace.Clear();var before=Hook.Constructions;
 Call("BEngine.Editor.AssetPostprocessorDispatcher","Notify",new[]{path},Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>());
 Check(before>=1 && Hook.Constructions==before+1,"Private postprocessor constructor is used for the probe and actual imported asset");
 Check(Hook.Trace.SequenceEqual(new[]{"pre:"+path,"post:"+path,"all:1"}),"Declared private instance and static postprocessor callbacks remain bound");
 before=Hook.Constructions;Hook.Trace.Clear();
 Call("BEngine.Editor.AssetPostprocessorDispatcher","Notify",new[]{path},Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>());
 Check(Hook.Constructions==before+1,"Stable callback generation does not recreate registration probes");
 Check(InheritedBase.Calls==0,"Inherited private callback is not implicitly promoted into the derived declaration");
 Check(NoDefaultHook.Constructions==0,"A postprocessor without a default constructor is skipped");
 Modification.Calls.Clear();
 Call("BEngine.Editor.AssetModificationProcessorDispatcher","OnWillCreateAsset","Assets/new.txt");
 var approved=(string[])Call("BEngine.Editor.AssetModificationProcessorDispatcher","OnWillSaveAssets",(object)new[]{"Assets/a"})!;
 Check(Modification.Calls.SequenceEqual(new[]{"create:Assets/new.txt","save:1"}) && approved.SequenceEqual(new[]{"Assets/a"}),"Private static modification callbacks retain signature and result behavior");
 RuntimeTypeCache.UnregisterAssemblies([Assembly.GetExecutingAssembly()]);Hook.Trace.Clear();
 Call("BEngine.Editor.AssetPostprocessorDispatcher","Notify",new[]{path},Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>());
 Check(Hook.Trace.Count==0,"Callback discovery releases removed domain types on generation change");
}finally{
 RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
 bridge.GetMethod("Detach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);
}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,RunDirectory=root},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ASSET_METADATA_WORKFLOW_OK|"+checks.Count);

public sealed class MenuAsset:BAsset{public static int Constructions;public MenuAsset(){Constructions++;}public int Value=57;}
public sealed class SceneSpy(ISceneDocumentCodec inner):ISceneDocumentCodec
{
 public int Saves;public bool Fail;public Scene? Last;
 public Scene Load(string path,IServiceProvider? services)=>inner.Load(path,services);public Scene Deserialize(string text,IServiceProvider? services)=>inner.Deserialize(text,services);public string Serialize(Scene scene)=>inner.Serialize(scene);
 public void Save(Scene scene,string path){Saves++;Last=scene;if(Fail)throw new IOException("Test provider failure");inner.Save(scene,path);}
}
public sealed class ManagedSpy(IManagedAssetDocumentCodec inner):IManagedAssetDocumentCodec
{public int Saves;public BObject Load(string path)=>inner.Load(path);public void Save(BObject asset,string path){Saves++;inner.Save(asset,path);}}
public sealed class Hook:AssetPostprocessor
{
 public static readonly List<string> Trace=new();public static int Constructions;private Hook(){Constructions++;}
 private void OnPreprocessAsset()=>Trace.Add("pre:"+assetPath);private void OnPostprocessAsset()=>Trace.Add("post:"+assetPath);
 private static void OnPostprocessAllAssets(string[] a,string[] b,string[] c,string[] d)=>Trace.Add("all:"+a.Length);
}
public abstract class InheritedBase:AssetPostprocessor{public static int Calls;private void OnPreprocessAsset()=>Calls++;}
public sealed class InheritedHook:InheritedBase;
public sealed class NoDefaultHook:AssetPostprocessor{public static int Constructions;public NoDefaultHook(string value){Constructions++;}}
public sealed class Modification:AssetModificationProcessor
{public static readonly List<string> Calls=new();private static void OnWillCreateAsset(string path)=>Calls.Add("create:"+path);private static string[] OnWillSaveAssets(string[] paths){Calls.Add("save:"+paths.Length);return paths;}}
public class ProbeHost:DispatchProxy
{
 public SourceDatabase Database=null!;public string Root="";
 protected override object? Invoke(MethodInfo? method,object?[]? args){
  AssetRecord? Find()=>args![0] is Guid guid?Database.GetRecord(guid):Database.GetRecord((string)args[0]!);
  EditorAssetInfo Info(AssetRecord r)=>new(r.Guid,r.AssetPath,r.SourcePath,r.ArtifactPath,r.AssetType,r.IsDirectory,r.ParentGuid,r.LocalIdentifier);
  switch(method!.Name){
   case "get_ProjectRootPath":return Root;case "get_AssetsRootPath":return Path.Combine(Root,"Assets");case "get_ActiveProjectFolderPath":return "Assets";
   case "GetAsset":return Find() is {} r?(EditorAssetInfo?)Info(r):null;
   case "FindAssets":return Database.assets.Select(Info).ToArray();
   case "ImportAsset":Database.ImportAsset(Path.Combine(Root,(string)args![0]!));return null;
   default:return method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
  }
 }
}
