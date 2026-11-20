using System.Reflection;
using System.Runtime.CompilerServices;
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
using TextAsset=BEngine.Editor.TextAsset;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var run=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(run,"Hub"));
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(run,"EditorData"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();AssetPipelineRuntime.Initialize();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
PlatformRuntime.Initialize(services);using var scope=EditorServiceScope.Attach(services);EditorFeatureIsolation.Current=null;
var project=Path.Combine(run,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),YamlUtility.Serialize(new ProjectData{Name="Formats",EngineVersion="0.1.0"}));
var workspace=ProjectWorkspace.Open(project);var db=new SourceDatabase(workspace);
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS|"+name);}
Check(AssetTypeRegistry.ResolveAssetType("Assets/source.scene.yaml")==typeof(Scene),"Source format provider preserves the scene loader");
Check(AssetTypeRegistry.ResolveAssetType("Assets/source.prefab.yaml")==typeof(PrefabAsset),"Source format provider preserves the prefab loader");
Check(AssetTypeRegistry.ResolveAssetType("Assets/source.cs")==typeof(MonoScript) && AssetTypeRegistry.ResolveImporterType("Assets/source.cs")==typeof(ScriptImporter),"Source format provider preserves the script loader and importer");
void Write(string path,string contents)=>File.WriteAllText(Path.Combine(workspace.AssetsPath,path),contents);
Assembly Load(string name){var a=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,name+".dll"));RuntimeHelpers.RunModuleConstructor(a.ManifestModule.ModuleHandle);return a;}
foreach(var (suffix,kind) in new[]{("json","JSON"),("xml","XML"),("md","Markdown"),("txt","Text"),("yaml","YamlAsset"),("html","HTML Document"),("htm","HTML Document")})Write("source."+suffix,"hello 世界");
Write("entry.cs","class Entry {}");
File.WriteAllBytes(Path.Combine(workspace.AssetsPath,"icon.png"),Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
Write("program.shader","shader fixture");Write("layout.bxml","UI fixture");Write("example.bpackage","archive fixture");
Write("data.asset.yaml",ManagedAssetSerialization.Serialize(new FormatAsset{Value=7}));
var legacyGuid=Guid.Parse("02c17c30-737f-4637-963e-c793dcba4a7e");
var iconMeta=Path.Combine(workspace.AssetsPath,"icon.png.meta");
YamlUtility.Save(new AssetMetaDocument{Guid=legacyGuid.ToString("N"),AssetType="Texture",Settings=new(){["userData"]="保留 settings"}},iconMeta);
db.Refresh();
foreach(var (suffix,kind) in new[]{("json","JSON"),("xml","XML"),("md","Markdown"),("txt","Text"),("yaml","YamlAsset"),("html","HTML Document"),("htm","HTML Document")})Check(db.GetRecord("Assets/source."+suffix)?.AssetType==kind,"Baseline category "+suffix);
Check(db.GetRecord("Assets/entry.cs")?.AssetType=="Script","Script category remains available without graphics");
Check(db.GetRecord("Assets/data.asset.yaml")?.AssetType==nameof(FormatAsset),"Managed declaration overrides generic YAML category");
Check(db.GetRecord("Assets/icon.png")?.AssetType=="DefaultAsset" && db.GetRecord("Assets/program.shader")?.AssetType=="DefaultAsset" && db.GetRecord("Assets/layout.bxml")?.AssetType=="DefaultAsset" && db.GetRecord("Assets/example.bpackage")?.AssetType=="DefaultAsset","Absent feature providers do not manufacture feature categories");
var guid=db.GetRecord("Assets/icon.png")!.Guid;var shaderGuid=db.GetRecord("Assets/program.shader")!.Guid;
Check(guid==legacyGuid && YamlUtility.Load<AssetMetaDocument>(iconMeta).Settings["userData"]=="保留 settings","Legacy metadata retains its GUID and settings while optional provider is absent");
var graphics=Load("BEngine.Graphics.Editor");db.RefreshRegisteredTypes();
Check(db.GetRecord("Assets/icon.png") is {AssetType:"Texture"} icon && icon.Guid==guid,"Late graphics registration reclassifies without replacing GUID");
Check(YamlUtility.Load<AssetMetaDocument>(iconMeta).Settings["userData"]=="保留 settings","Late importer preserves existing user settings");
Check(db.GetRecord("Assets/program.shader") is {AssetType:"Shader"} shader && shader.Guid==shaderGuid,"Late shader registration retains metadata identity");
var shaderRecord=db.GetRecord("Assets/program.shader")!;
Check(AssetTypeRegistry.LoadFallback(new(shaderRecord.Guid,shaderRecord.AssetPath,shaderRecord.SourcePath,shaderRecord.AssetType,shaderRecord.ArtifactPath)) is TextAsset{text:"shader fixture"},"Graphics owns shader text recovery");
Load("BEngine.UIElements.Editor");db.RefreshRegisteredTypes();
Check(db.GetRecord("Assets/layout.bxml")?.AssetType=="UI Document","UI provider supplies its own category");
var ui=db.GetRecord("Assets/layout.bxml")!;
Check(AssetTypeRegistry.LoadFallback(new(ui.Guid,ui.AssetPath,ui.SourcePath,ui.AssetType,ui.ArtifactPath)) is TextAsset{text:"UI fixture"},"UI provider owns text recovery");
Load("BEngine.EditorWorkbench.Editor");db.RefreshRegisteredTypes();
Check(db.GetRecord("Assets/example.bpackage")?.AssetType=="BEnginePackage","Workbench supplies the archive category");
Check(db.RefreshRegisteredTypes().Count==0,"Stable registrations do not reimport managed documents through generic YAML");

// Reflection is confined to attaching a fake internal editor host for facade integration;
// format providers above and below use public registration APIs and receive no friend grant.
var bridge=typeof(EditorDatabase).Assembly.GetType("BEngine.Editor.EditorBridge",true)!;
var hostType=typeof(EditorDatabase).Assembly.GetType("BEngine.Editor.IEditorHost",true)!;
var host=DispatchProxy.Create(hostType,typeof(FormatHost));var proxy=(FormatHost)host;proxy.Database=db;proxy.Root=project;
bridge.GetMethod("Attach",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[host]);
try{
 foreach(var suffix in new[]{"json","xml","md","txt","yaml","html","htm"})Check(EditorDatabase.LoadMainAssetAtPath("Assets/source."+suffix) is TextAsset{text:"hello 世界"} text && text.name=="source."+suffix,"Baseline facade text and name "+suffix);
 AssetTypeRegistry.Register<FormatAsset>(".custom","Custom",_=>throw new InvalidDataException("bad format"),null,null,c=>new TextAsset(File.ReadAllText(c.ImportedPath)));
 Write("data.custom","recover me");var custom=db.ImportAsset(Path.Combine(workspace.AssetsPath,"data.custom"));
 var result=EditorDatabase.LoadMainAssetAtPath("Assets/data.custom");
 Check(result is TextAsset{text:"recover me"} && result.Id==custom.Guid && result.name=="data.custom","Facade honors independent owner recovery and binds original GUID/name");
 AssetTypeRegistry.Register<FormatAsset>(".opaque","Opaque",_=>throw new InvalidDataException("bad format"));
 Write("data.opaque","opaque");db.ImportAsset(Path.Combine(workspace.AssetsPath,"data.opaque"));
 var opaque=EditorDatabase.LoadMainAssetAtPath("Assets/data.opaque");
 Check(opaque is DefaultAsset,"A format without recovery remains a default asset after primary failure");
 AssetTypeRegistry.Register<FormatAsset>(".opaque","Opaque",_=>new FormatAsset{Value=17});
 Check(EditorDatabase.LoadMainAssetAtPath("Assets/data.opaque") is FormatAsset{Value:17} replacement && replacement.Id==opaque!.Id,"Replacing a loader invalidates the cached facade object without replacing its GUID");
 AssetTypeRegistry.Register<FormatAsset>(".reentrant","Reentrant",_=>{
  AssetTypeRegistry.Register<FormatAsset>(".reentrant","Reentrant",_=>new FormatAsset{Value=2});
  return new FormatAsset{Value=1};
 });
 Write("data.reentrant","reentrant");db.ImportAsset(Path.Combine(workspace.AssetsPath,"data.reentrant"));
 Check(EditorDatabase.LoadMainAssetAtPath("Assets/data.reentrant") is FormatAsset{Value:1},"In-flight caller retains its result after reentrant registration");
 Check(EditorDatabase.LoadMainAssetAtPath("Assets/data.reentrant") is FormatAsset{Value:2},"Reentrant replacement prevents stale cache publication");
 RuntimeAssemblyCleanup.UnregisterExtensions(Assembly.GetExecutingAssembly());db.RefreshRegisteredTypes();
 Check(db.GetRecord("Assets/data.custom") is {AssetType:"DefaultAsset"} removed && removed.Guid==custom.Guid,"Removed provider is reclassified without losing its GUID");
 Check(EditorDatabase.LoadMainAssetAtPath("Assets/data.custom") is DefaultAsset,"Unloading a provider does not return its still-live cached recovery object");
 RuntimeAssemblyCleanup.UnregisterExtensions(graphics);db.RefreshRegisteredTypes();
 Check(db.GetRecord("Assets/icon.png") is {AssetType:"DefaultAsset"} plain && plain.Guid==guid && AssetTypeRegistry.Resolve("Assets/icon.png")==null,"Graphics cleanup removes category and preserves metadata GUID");
 Check(YamlUtility.Load<AssetMetaDocument>(iconMeta).Settings["userData"]=="保留 settings","Provider removal preserves user settings for later reinstallation");
 Check(AssetTypeRegistry.LoadFallback(new(shaderGuid,"Assets/program.shader",shaderRecord.SourcePath,"Shader",shaderRecord.ArtifactPath))==null,"Graphics cleanup releases its recovery policy");
 Check(db.GetRecord("Assets/data.asset.yaml")?.AssetType==nameof(FormatAsset),"Provider cleanup preserves declared managed type");
 Check(!typeof(EditorDatabase).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "BEngine.Graphics.Editor" or "BEngine.UIElements.Editor"),"Shared facade has no concrete format-provider references");
}finally{bridge.GetMethod("Detach",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[host]);}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,RunDirectory=run},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("ASSET_FORMAT_WORKFLOW_OK|"+checks.Count);
public sealed class FormatAsset:BAsset{public int Value;}
public class FormatHost:DispatchProxy
{
 public SourceDatabase Database=null!;public string Root="";
 protected override object? Invoke(MethodInfo? method,object?[]? args){
  AssetRecord? Find()=>args![0] is Guid guid?Database.GetRecord(guid):Database.GetRecord((string)args[0]!);
  EditorAssetInfo Info(AssetRecord r)=>new(r.Guid,r.AssetPath,r.SourcePath,r.ArtifactPath,r.AssetType,r.IsDirectory,r.ParentGuid,r.LocalIdentifier);
  return method!.Name switch{
   "get_ProjectRootPath"=>Root,"get_AssetsRootPath"=>Path.Combine(Root,"Assets"),
   "GetAsset"=>Find() is {} r?(EditorAssetInfo?)Info(r):null,
   "FindAssets"=>Database.assets.Select(Info).ToArray(),
   _=>method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null};
 }
}
