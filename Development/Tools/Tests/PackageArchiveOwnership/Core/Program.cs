using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

BEngine.Platform.DotNet.DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var checks=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);}
void Throws<T>(string name,Action action) where T:Exception
{try{action();}catch(T){Check(name,true);return;}throw new Exception(name);}
var manifest=new BPackageManifest{Name="contract",PackageVersion="1.2.3"};
var receipt=new BPackageImportReceipt{InstallationId="contract",PackageName="contract"};
var result=new BPackageImportResult(manifest,["Assets/a"],["Assets/b"],["Assets/c"],["Assets/d"],["Assets/e"],receipt);
Check("Provider can construct completed import results",ReferenceEquals(result.Manifest,manifest)&&ReferenceEquals(result.Receipt,receipt));
Check("Result preserves every path category",result.ImportedPaths.SequenceEqual(["Assets/a"])&&result.SkippedPaths.SequenceEqual(["Assets/b"])&&result.UnchangedPaths.SequenceEqual(["Assets/c"])&&result.PreservedModifiedPaths.SequenceEqual(["Assets/d"])&&result.RemovedPaths.SequenceEqual(["Assets/e"]));
Throws<ArgumentNullException>("Result rejects null manifest",()=>new BPackageImportResult(null!,[],[],[],[],[],null));
Throws<ArgumentNullException>("Result rejects null imported paths",()=>new BPackageImportResult(manifest,null!,[],[],[],[],null));
Throws<ArgumentNullException>("Result rejects null skipped paths",()=>new BPackageImportResult(manifest,[],null!,[],[],[],null));
Throws<ArgumentNullException>("Result rejects null unchanged paths",()=>new BPackageImportResult(manifest,[],[],null!,[],[],null));
Throws<ArgumentNullException>("Result rejects null preserved paths",()=>new BPackageImportResult(manifest,[],[],[],null!,[],null));
Throws<ArgumentNullException>("Result rejects null removed paths",()=>new BPackageImportResult(manifest,[],[],[],[],null!,null));
var status=new BPackageImportStatus(BPackageImportState.Modified,receipt,["Assets/missing"],["Assets/modified"]);
Check("Provider can construct inspection status",status.State==BPackageImportState.Modified&&status.IsInstalled&&status.IsTracked&&ReferenceEquals(status.Receipt,receipt));
Check("Status preserves missing and modified paths",status.MissingPaths.SequenceEqual(["Assets/missing"])&&status.ModifiedPaths.SequenceEqual(["Assets/modified"]));
Check("Uninstalled untracked status remains representable",new BPackageImportStatus(BPackageImportState.NotInstalled,null,[],[]) is {IsInstalled:false,IsTracked:false});
Throws<ArgumentNullException>("Status rejects null missing paths",()=>new BPackageImportStatus(BPackageImportState.Installed,null,null!,[]));
Throws<ArgumentNullException>("Status rejects null modified paths",()=>new BPackageImportStatus(BPackageImportState.Installed,null,[],null!));
Check("Archive contract still belongs to Core Editor",typeof(IBPackageArchiveService).Assembly==typeof(EditorServiceScope).Assembly);
foreach(var name in new[]{"BPackageImportPlan","BPackageImportPlanEntry","BPackageImportAction","BPackageArchiveFormat","BPackagePathUtility"})
 Check("Core contains no workbench implementation: "+name,typeof(IBPackageArchiveService).Assembly.GetType("BEngine.Editor."+name)==null);
Throws<InvalidOperationException>("No provider does not load a workbench implicitly",()=>BPackageArchive.ReadManifest("absent.bpackage"));
var provider=new ProbeArchive(manifest,result,status);
using(var services=new ServiceCollection().AddSingleton<IBPackageArchiveService>(provider).BuildServiceProvider())
using(var attached=EditorServiceScope.Attach(services))
{
 Check("Facade resolves the active provider",ReferenceEquals(BPackageArchive.ReadManifest("one.bpackage"),manifest)&&provider.Call=="read:one.bpackage");
 Check("Export facade preserves path and destination",ReferenceEquals(BPackageArchive.ExportPackage(["Assets/a"],"out.bpackage"),manifest)&&provider.Call=="export:Assets/a:out.bpackage");
 Check("Workspace export facade dispatches independently",ReferenceEquals(BPackageArchive.ExportPackage(null!,["Assets/b"],"workspace.bpackage"),manifest)&&provider.Call=="workspace:Assets/b:workspace.bpackage");
 Check("Receipt facade preserves installation identity",ReferenceEquals(BPackageArchive.ReadImportReceipt(null!,"id"),receipt)&&provider.Call=="receipt:id");
 Check("Status facade preserves destination",ReferenceEquals(BPackageArchive.GetImportStatus(null!,"in.bpackage","id","dest"),status)&&provider.Call=="status:in.bpackage:id:dest");
 var options=new BPackageImportOptions{InstallationId="id"};Action<BPackageProgress> progress=static _=>{};
 Check("Import facade preserves options and progress identity",ReferenceEquals(BPackageArchive.ImportPackage(null!,"in.bpackage",options,progress),result)&&ReferenceEquals(provider.Options,options)&&ReferenceEquals(provider.Progress,progress));
}
Throws<InvalidOperationException>("Detached provider is no longer resolved",()=>BPackageArchive.ReadManifest("absent.bpackage"));
Check("Contract consumer loads no graphics or editor packages",!AppDomain.CurrentDomain.GetAssemblies().Any(a=>new[]{"BEngine.EditorWorkbench.Editor","BEngine.Core.Editor","BEngine.IMGUI.Editor","BEngine.Graphics","BEngine.Rendering2D"}.Contains(a.GetName().Name)));
File.WriteAllText(Path.Combine(args[0],"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PACKAGE_ARCHIVE_CORE_OK|"+checks.Count);

public sealed class ProbeArchive(BPackageManifest manifest,BPackageImportResult result,BPackageImportStatus status):IBPackageArchiveService
{
 public string Call="";public BPackageImportOptions? Options;public Action<BPackageProgress>? Progress;
 public BPackageManifest ReadManifest(string path){Call="read:"+path;return manifest;}
 public BPackageManifest ExportPackage(IEnumerable<string> paths,string destination,BPackageExportOptions? options=null,Action<BPackageProgress>? progress=null){Call=$"export:{string.Join(',',paths)}:{destination}";return manifest;}
 public BPackageManifest ExportPackage(ProjectWorkspace workspace,IEnumerable<string> paths,string destination,BPackageExportOptions? options=null,Action<BPackageProgress>? progress=null){Call=$"workspace:{string.Join(',',paths)}:{destination}";return manifest;}
 public BPackageImportReceipt? ReadImportReceipt(ProjectWorkspace workspace,string id){Call="receipt:"+id;return status.Receipt;}
 public BPackageImportStatus GetImportStatus(ProjectWorkspace workspace,string path,string id,string? destination=null){Call=$"status:{path}:{id}:{destination}";return status;}
 public BPackageImportResult ImportPackage(ProjectWorkspace workspace,string path,BPackageImportOptions? options=null,Action<BPackageProgress>? progress=null){Options=options;Progress=progress;return result;}
}
