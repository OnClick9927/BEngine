using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.SceneManagement;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SourceDatabase=BEngine.ProjectSystem.Editor.AssetDatabase;
using EditorDatabase=BEngine.Editor.AssetDatabase;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(root,"EditorData"));
Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH",Path.Combine(root,"Repository"));
Directory.CreateDirectory(Path.Combine(root,"Repository"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();AssetPipelineRuntime.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().AddSingleton<IPackageCatalogSource,EmptyCatalog>().AddSingleton<IEditorUtilityPlatform,TestEditorUtilityPlatform>().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var scope=EditorServiceScope.Attach(services);
var project=Path.Combine(root,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),YamlUtility.Serialize(new ProjectData{Name="Access",EngineVersion="0.1.0"}));
var workspace=ProjectWorkspace.Open(project);var database=new SourceDatabase(workspace);database.Refresh();
RuntimeTypeCache.RegisterAssemblies([Assembly.GetExecutingAssembly()]);
var checks=new List<string>();
void Check(bool result,string name){if(!result)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS|"+name);}
var assembly=typeof(EditorDatabase).Assembly;
var bridge=assembly.GetType("BEngine.Editor.EditorBridge",true)!;
var host=DispatchProxy.Create(assembly.GetType("BEngine.Editor.IEditorHost",true)!,typeof(ProbeHost));
var proxy=(ProbeHost)host;proxy.Database=database;proxy.Root=workspace.RootPath;
bridge.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);
try{
 if(args.Length>1 && args[1]=="embedded")goto EmbeddedStart;
 AssetTypeRegistry.Register<CacheAsset>(".stable","Stable",_=>new CacheAsset{Value=17});
 File.WriteAllText(Path.Combine(workspace.AssetsPath,"stable.stable"),"stable");database.ImportAsset(Path.Combine(workspace.AssetsPath,"stable.stable"));
 var stable=EditorDatabase.LoadAssetAtPath<CacheAsset>("Assets/stable.stable")!;var id=stable.Id;stable.Value=99;EditorUtility.SetDirty(stable);
 Check(EditorDatabase.RevertAsset(stable) && stable.Value==17 && stable.Id==id,"Stable revert restores values and preserves live identity");
 Check(ReferenceEquals(stable,EditorDatabase.LoadMainAssetAtPath("Assets/stable.stable")),"Stable revert publishes the existing caller-owned instance");
 Check(!EditorUtility.IsDirty(stable),"Successful revert clears the edited object's dirty state");
 AssetTypeRegistry.Register<CacheAsset>(".revert","Revert",_=>new CacheAsset{Value=1});
 File.WriteAllText(Path.Combine(workspace.AssetsPath,"data.revert"),"revert");database.ImportAsset(Path.Combine(workspace.AssetsPath,"data.revert"));
 var current=EditorDatabase.LoadAssetAtPath<CacheAsset>("Assets/data.revert")!;
 AssetTypeRegistry.Register<CacheAsset>(".revert","Revert",_=>{AssetTypeRegistry.Register<CacheAsset>(".revert","Revert",_=>new CacheAsset{Value=3});return new CacheAsset{Value=2};});
 Check(EditorDatabase.RevertAsset(current) && current.Value==2,"A revert caller retains its completed in-flight result");
 var replacement=EditorDatabase.LoadAssetAtPath<CacheAsset>("Assets/data.revert");
 Check(replacement is {Value:3} && !ReferenceEquals(current,replacement),"Reentrant revert cannot publish an obsolete provider result");
 if(args.Length>1 && args[1]=="revert")goto Complete;

 EmbeddedStart:
 AssetTypeRegistry.Register<CacheAsset>(".owner","Owner",_=>new CacheAsset());
 File.WriteAllText(Path.Combine(workspace.AssetsPath,"owner.owner"),"owner");database.ImportAsset(Path.Combine(workspace.AssetsPath,"owner.owner"));
 var embedded=new EmbeddedAsset{Value=42,name="Embedded"};EditorDatabase.AddObjectToAsset(embedded,"Assets/owner.owner");
 EditorDatabase.ImportAsset("Assets/owner.owner");
 EmbeddedAsset.OnConstruction=()=>{EmbeddedAsset.OnConstruction=null;AssetTypeRegistry.Register<CacheAsset>(".bump","Bump",_=>new CacheAsset());};
 var first=EditorDatabase.LoadAllAssetsAtPath("Assets/owner.owner").OfType<EmbeddedAsset>().Single();
 var second=EditorDatabase.LoadAllAssetsAtPath("Assets/owner.owner").OfType<EmbeddedAsset>().Single();
 Check(first.Value==42 && second.Value==42 && first.Id==second.Id,"Embedded reload preserves persisted values and identity across provider replacement");
 Check(!ReferenceEquals(first,second),"Reentrant embedded decoding cannot republish an invalidated child cache");
 Check(ReferenceEquals(second,EditorDatabase.LoadAllAssetsAtPath("Assets/owner.owner").OfType<EmbeddedAsset>().Single()),"Stable embedded decoding retains its normal cache identity");
 if(args.Length>1 && args[1]=="embedded")goto Complete;

 var detached=new EmptyComponent();Selection.activeObject=detached;
 Check(Selection.activeGameObject==null && Selection.GetFiltered<GameObject>(SelectionMode.Unfiltered).Length==0,"Selection safely handles unattached components through the public ownership query");
 using(var scene=new Scene("Selection")){var go=scene.CreateGameObject("Owner");var component=go.AddComponent<EmptyComponent>();Selection.activeObject=component;Check(Selection.activeGameObject==go,"Attached components retain their selected GameObject");}
 Selection.activeObject=null;
 using(var sourceScene=new Scene("Source")){
  var sourceRoot=sourceScene.CreateGameObject("SourceRoot");
  Check(EditorSceneManager.SaveScene(sourceScene) && proxy.SceneSaves==1,"Authoring scenes retain access to the editor save provider");
  var runtime=new SceneRuntime(sourceScene);runtime.Start();
  try{
   Check(!EditorSceneManager.SaveScene(sourceScene) && proxy.SceneSaves==1,"A running scene cannot reach the source save provider");
   try{PrefabUtility.SaveAsPrefabAsset(sourceRoot,"Assets/rejected.prefab.yaml");throw new Exception("Expected running-prefab rejection");}
   catch(InvalidOperationException){Check(!File.Exists(Path.Combine(workspace.AssetsPath,"rejected.prefab.yaml")),"Running scene objects cannot overwrite prefab source data");}
  }finally{runtime.Stop();}
 }
 var document=Path.Combine(workspace.AssetsPath,"custom.scene.yaml");File.WriteAllText(document,"custom source");
 var loader=(ISceneLoader)Activator.CreateInstance(assembly.GetType("BEngine.Editor.EditorProjectSceneLoader",true)!,BindingFlags.Instance|BindingFlags.NonPublic,null,[workspace.RootPath],null)!;
 var original=RuntimeServiceContributions.GetDefault<ISceneDocumentCodec>()!;var fallback=new SceneSpy(original);RuntimeServiceContributions.RegisterDefault<ISceneDocumentCodec>(fallback);
 using(var loaded=loader.LoadScene("Assets/custom.scene.yaml",services))Check(fallback.Loads==1 && loaded.path==document,"Editor scene loading uses the registered document provider and binds its source path");
 var scoped=new SceneSpy(original);using(var local=new ServiceCollection().AddSingleton<ISceneDocumentCodec>(scoped).BuildServiceProvider())using(var loaded=loader.LoadScene("custom",local))Check(scoped.Loads==1 && fallback.Loads==1 && ReferenceEquals(scoped.LastServices,local),"A supplied scene service scope can replace the default document codec");
 File.WriteAllText(Path.Combine(workspace.AssetsPath,"sample.asmdef.yaml"),"name: Sample\n");database.ImportAsset(Path.Combine(workspace.AssetsPath,"sample.asmdef.yaml"));
 var definition=EditorDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>("Assets/sample.asmdef.yaml");
 Check(definition is {assetType:"AssemblyDefinition"} && definition.Id==database.GetRecord("Assets/sample.asmdef.yaml")!.Guid,"Assembly definition documents retain public source identity and category binding");
 var before=fallback.Saves;var created=ProjectWorkspaceFactory.Create(Path.Combine(root,"Created"),"Created");
 Check(fallback.Saves==before+1 && File.Exists(created.StartupScenePath),"Project creation delegates its default scene to the document provider");
 Check(fallback.LastSaved is {isCreated:false},"Project creation disposes the provider's temporary scene");
 using(var saved=original.Load(created.StartupScenePath,services))Check(saved.name=="Main","Provider-created default scene remains a readable scene document");
 Check(File.Exists(created.PackageManifestPath) && ProbeTemplate.LastRoot!=null && File.Exists(Path.Combine(created.RootPath,"template.marker")),"Manifest-only project creation invokes optional template hooks in its staging workspace");
 using(var packages=BPackageManager.OpenManifest(created,new BPackageCatalog(Array.Empty<string>()))){Check(packages.definitions.Count==0 && packages.packages.Count==0,"Manifest editing can operate without package assemblies or source compilation");}
 var occupied=Path.Combine(root,"Occupied");Directory.CreateDirectory(occupied);File.WriteAllText(Path.Combine(occupied,"keep.txt"),"keep");
 try{ProjectWorkspaceFactory.Create(occupied,"No");throw new Exception("Expected occupied-project rejection");}catch(IOException){Check(File.ReadAllText(Path.Combine(occupied,"keep.txt"))=="keep","Project creation preserves an occupied destination");}
 ProbeTemplate.Fail=true;var rejected=Path.Combine(root,"Rejected");
 try{ProjectWorkspaceFactory.Create(rejected,"Rejected");throw new Exception("Expected template failure");}catch(IOException){Check(!Directory.Exists(rejected) && !Directory.EnumerateDirectories(root,".bengine-project-*").Any(),"Failed project initialization removes only its staging directory");}finally{ProbeTemplate.Fail=false;}
 Check(!typeof(BObject).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"The integration consumer has no private Core grant");
 Complete:;
}finally{RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());bridge.GetMethod("Detach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,RunDirectory=root},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("EDITOR_ACCESS_WORKFLOW_OK|"+checks.Count);

public sealed class CacheAsset:BAsset{public int Value;}
public sealed class EmbeddedAsset:BAsset{public static Action? OnConstruction;public EmbeddedAsset()=>OnConstruction?.Invoke();public int Value;}
public sealed class EmptyComponent:MonoBehaviour;
public sealed class TestEditorUtilityPlatform:IEditorUtilityPlatform
{
 public int DisplayDialog(string title,string message,IReadOnlyList<string> buttons)=>throw new NotSupportedException();
 public string OpenFilePanel(string title,string directory,string filter)=>throw new NotSupportedException();
 public string OpenFolderPanel(string title,string folder,string defaultName)=>throw new NotSupportedException();
 public void OpenWithDefaultApp(string target)=>throw new NotSupportedException();
 public void ShowProgress(EditorProgressInfo progress,Action? requestCancellation)=>throw new NotSupportedException();
 public void ClearProgress()=>throw new NotSupportedException();
}
public sealed class SceneSpy(ISceneDocumentCodec inner):ISceneDocumentCodec
{
 public int Saves,Loads;public Scene? LastSaved;public IServiceProvider? LastServices;
 public Scene Load(string path,IServiceProvider? services){Loads++;LastServices=services;return new Scene("Custom");}
 public Scene Deserialize(string text,IServiceProvider? services)=>inner.Deserialize(text,services);
 public string Serialize(Scene scene)=>inner.Serialize(scene);
 public void Save(Scene scene,string path){Saves++;LastSaved=scene;inner.Save(scene,path);}
}
public sealed class EmptyCatalog:IPackageCatalogSource{public PackageCatalogSnapshot GetSnapshot(string root,string version)=>new(){PackagesRoot=root,EngineVersion=version,Revision="test"};}
public sealed class ProbeTemplate:IProjectTemplate
{public static string? LastRoot;public static bool Fail;public void Initialize(ProjectWorkspace workspace){LastRoot=workspace.RootPath;if(Fail)throw new IOException("Template fixture failure");File.WriteAllText(Path.Combine(workspace.RootPath,"template.marker"),"initialized");}}
public class ProbeHost:DispatchProxy
{
 public SourceDatabase Database=null!;public string Root="";public BObject? Active;public int SceneSaves;
 protected override object? Invoke(MethodInfo? method,object?[]? args){
  AssetRecord? Find()=>args![0] is Guid guid?Database.GetRecord(guid):Database.GetRecord((string)args[0]!);
  EditorAssetInfo Info(AssetRecord r)=>new(r.Guid,r.AssetPath,r.SourcePath,r.ArtifactPath,r.AssetType,r.IsDirectory,r.ParentGuid,r.LocalIdentifier);
  switch(method!.Name){
   case "get_ProjectRootPath":return Root;case "get_AssetsRootPath":return Path.Combine(Root,"Assets");case "get_ActiveProjectFolderPath":return "Assets";
   case "get_ActiveObject":return Active;case "set_ActiveObject":Active=(BObject?)args![0];return null;
   case "SaveScene":SceneSaves++;return true;
   case "GetAsset":return Find() is {} r?(EditorAssetInfo?)Info(r):null;
   case "FindAssets":return Database.assets.Select(Info).ToArray();
   case "ImportAsset":Database.ImportAsset(Path.Combine(Root,(string)args![0]!));return null;
   default:return method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
  }
 }
}
