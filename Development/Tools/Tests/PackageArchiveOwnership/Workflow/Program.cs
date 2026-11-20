using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var run=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",Path.Combine(run,"EditorData"));
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(run,"Hub"));
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();AssetPipelineRuntime.Initialize();
var files=DispatchProxy.Create<IFileSystem,FileProbe>();var probe=(FileProbe)(object)files;
probe.Inner=EditorServices.GetRequiredService<IFileSystem>();
var archive=new BPackageArchiveService(new FileUtility(files),EditorServices.GetRequiredService<PathUtility>(),EditorServices.GetRequiredService<PlatformUtility>(),EditorServices.GetRequiredService<WaitUtility>());
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().AddSingleton<IBPackageArchiveService>(archive).BuildServiceProvider();
using var attached=EditorServiceScope.Attach(services);
// This consumer exercises archive IO without an IMGUI frame; actual Editor smoke
// covers the installed presentation backend separately.
EditorFeatureIsolation.Current=null;
var checks=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS|"+name);}
void Throws<T>(string name,Action action) where T:Exception
{try{action();}catch(T){Check(name,true);return;}throw new Exception(name);}
ProjectWorkspace Project(string name)
{
 var path=Path.Combine(run,name);Directory.CreateDirectory(Path.Combine(path,"Assets"));
 File.WriteAllText(Path.Combine(path,"Project.yaml"),YamlUtility.Serialize(new ProjectData{Name=name,EngineVersion="0.1.0"}));
 return ProjectWorkspace.Open(path);
}
string Asset(ProjectWorkspace workspace,string path)=>Path.Combine(workspace.AssetsPath,path.Replace('/',Path.DirectorySeparatorChar));
void Write(ProjectWorkspace workspace,string path,string value){var file=Asset(workspace,path);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,value);}
string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void Finish(){File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,RunDirectory=run},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PACKAGE_ARCHIVE_WORKFLOW_OK|"+checks.Count);}

var source=Project("Source");
const string guid="1234567890abcdef1234567890abcdef";
Write(source,"Pack/hello.txt","hello archive\n中文内容\n");
Write(source,"Pack/hello.txt.meta",$"format: BEngine.AssetMeta\nversion: 1\nguid: {guid}\nimporter: TextImporter\nassetType: Text\nsettings: {{}}\n");
Write(source,"Pack/obsolete.txt","obsolete");
var first=Path.Combine(run,"first.bpackage");var second=Path.Combine(run,"second.bpackage");
var options=new BPackageExportOptions{Name="Archive Fixture",Description="compatibility",PackageVersion="1.0.0"};
var progress=new List<BPackageProgress>();
var manifest=BPackageArchive.ExportPackage(source,["Assets/Pack"],first,options,progress.Add);
BPackageArchive.ExportPackage(source,["Assets/Pack"],second,options);
Check("Canonical exports are deterministic",Hash(first)==Hash(second));
Check("Manifest retains v1 identity and metadata",manifest.Format=="BEngine.BPackage"&&manifest.Version==1&&manifest.Name=="Archive Fixture"&&manifest.PackageVersion=="1.0.0");
Check("Manifest includes payload and GUID metadata",manifest.Entries.Any(e=>e.RelativePath=="Pack/hello.txt")&&manifest.Entries.Any(e=>e.RelativePath=="Pack/hello.txt.meta"));
Check("Progress finishes at completion",progress.Count>1&&progress[^1].Phase==BPackageProgressPhase.Completed&&progress[^1].Fraction==1);
File.Copy(first,Path.Combine(output,"snapshot.bpackage"),overwrite:true);
if(args.Length>1&&args[1]=="--export-only"){Finish();return;}
if(args.Length>1){Check("Exports match the actual legacy engine byte-for-byte",Hash(first)==Hash(args[1]));first=args[1];}
foreach(var name in new[]{"BPackageImportPlan","BPackageImportPlanEntry","BPackageImportAction","BPackageArchiveFormat","BPackagePathUtility"})
 Check("Private archive implementation belongs to workbench: "+name,typeof(BPackageArchiveService).Assembly.GetType("BEngine.Editor."+name) is {IsPublic:false}&&typeof(BPackageArchive).Assembly.GetType("BEngine.Editor."+name)==null);
Throws<InvalidOperationException>("Context export still requires an open project",()=>archive.ExportPackage(["Assets/Pack"],Path.Combine(run,"no-context.bpackage")));
Check("Public manifest reader preserves canonical entries",BPackageArchive.ReadManifest(first).Entries.Select(e=>e.RelativePath).SequenceEqual(manifest.Entries.Select(e=>e.RelativePath)));
var target=Project("Target");
Check("Fresh target is not installed",archive.GetImportStatus(target,first,"fixture","Data").State==BPackageImportState.NotInstalled);
var imported=archive.ImportPackage(target,first,new BPackageImportOptions{DestinationDirectory="Data",InstallationId="fixture"});
Check("Legacy archive imports with tracking",imported.Receipt is {InstallationId:"fixture",Version:1}&&imported.ImportedPaths.Contains("Assets/Data/Pack/hello.txt"));
Check("Import preserves exact Unicode payload",File.ReadAllText(Asset(target,"Data/Pack/hello.txt"))=="hello archive\n中文内容\n");
Check("Import preserves resource GUID",File.ReadAllText(Asset(target,"Data/Pack/hello.txt.meta")).Contains(guid));
Check("Tracked status is installed",archive.GetImportStatus(target,first,"fixture","Data") is {State:BPackageImportState.Installed,IsTracked:true});
var receipt=archive.ReadImportReceipt(target,"fixture")!;
Check("Receipt retains format and source hash",receipt.Format=="BEngine.BPackageImportReceipt"&&receipt.Version==1&&receipt.ArchiveSha256==Hash(first));
Throws<InvalidOperationException>("Tracked import cannot silently overwrite receipt",()=>archive.ImportPackage(target,first,new BPackageImportOptions{DestinationDirectory="Data",InstallationId="fixture"}));
Write(target,"Data/Pack/hello.txt","locally modified");
Check("Local modification is detected",archive.GetImportStatus(target,first,"fixture","Data").ModifiedPaths.Contains("Assets/Data/Pack/hello.txt"));
Write(source,"Pack/hello.txt","version two");File.Delete(Asset(source,"Pack/obsolete.txt"));
var update=Path.Combine(run,"update.bpackage");
archive.ExportPackage(source,["Assets/Pack"],update,new BPackageExportOptions{Name="Archive Fixture",PackageVersion="2.0.0"});
var preserved=archive.ImportPackage(target,update,new BPackageImportOptions{DestinationDirectory="Data",InstallationId="fixture",Mode=BPackageImportMode.Reimport});
Check("Reimport preserves local file and paired GUID metadata",File.ReadAllText(Asset(target,"Data/Pack/hello.txt"))=="locally modified"&&preserved.PreservedModifiedPaths.Contains("Assets/Data/Pack/hello.txt")&&File.ReadAllText(Asset(target,"Data/Pack/hello.txt.meta")).Contains(guid));
Check("Reimport removes unmodified obsolete files",!File.Exists(Asset(target,"Data/Pack/obsolete.txt"))&&preserved.RemovedPaths.Contains("Assets/Data/Pack/obsolete.txt"));
var overwritten=archive.ImportPackage(target,update,new BPackageImportOptions{DestinationDirectory="Data",InstallationId="fixture",Mode=BPackageImportMode.Reimport,ModifiedFilePolicy=BPackageModifiedFilePolicy.Overwrite});
Check("Explicit overwrite replaces local payload",File.ReadAllText(Asset(target,"Data/Pack/hello.txt"))=="version two"&&overwritten.ImportedPaths.Contains("Assets/Data/Pack/hello.txt"));
File.Delete(Asset(target,"Data/Pack/hello.txt"));
Check("Missing imported files are detected",archive.GetImportStatus(target,update,"fixture","Data").MissingPaths.Contains("Assets/Data/Pack/hello.txt"));
archive.ImportPackage(target,update,new BPackageImportOptions{DestinationDirectory="Data",InstallationId="fixture",Mode=BPackageImportMode.Reimport,ModifiedFilePolicy=BPackageModifiedFilePolicy.Overwrite});
Check("Reimport repairs missing files",File.ReadAllText(Asset(target,"Data/Pack/hello.txt"))=="version two");
var conflicts=Project("Conflicts");Write(conflicts,"Pack/hello.txt","keep local");
Throws<IOException>("Default conflict policy rejects existing assets",()=>archive.ImportPackage(conflicts,first));
var skipped=archive.ImportPackage(conflicts,first,new BPackageImportOptions{ConflictPolicy=BPackageConflictPolicy.Skip});
Check("Skip policy preserves conflicting content",File.ReadAllText(Asset(conflicts,"Pack/hello.txt"))=="keep local"&&skipped.SkippedPaths.Contains("Assets/Pack/hello.txt"));
var legacy=Project("Untracked");archive.ImportPackage(legacy,first);
Check("Untracked import is recognized without creating a receipt",archive.GetImportStatus(legacy,first,"untracked") is {IsInstalled:true,IsTracked:false}&&archive.ReadImportReceipt(legacy,"untracked")==null);
Throws<InvalidDataException>("Destination traversal is rejected",()=>archive.ImportPackage(Project("Traversal"),first,new BPackageImportOptions{DestinationDirectory="../outside"}));
foreach(var name in new[]{"CON","PRN.txt","LPT1.dat","COM9.txt","NUL","name.","name "})
 Throws<InvalidDataException>("Nonportable archive destination rejected: "+name,()=>archive.ImportPackage(Project("Bad-"+Guid.NewGuid().ToString("N")),first,new BPackageImportOptions{DestinationDirectory=name}));
foreach(var name in new[]{"CONsole","COM0","COM10","LPT10",".hidden"})
{
 var portable=Project("Portable-"+name.TrimStart('.'));
 archive.ImportPackage(portable,first,new BPackageImportOptions{DestinationDirectory=name});
 Check("Portable destination remains accepted: "+name,File.Exists(Asset(portable,name+"/Pack/hello.txt")));
}
var limits=Project("Limits");
Throws<InvalidDataException>("Entry count limit is enforced",()=>archive.ImportPackage(limits,first,new BPackageImportOptions{MaximumEntryCount=1}));
Throws<InvalidDataException>("File size limit is enforced",()=>archive.ImportPackage(limits,first,new BPackageImportOptions{MaximumFileSize=1}));
Throws<InvalidDataException>("Total size limit is enforced",()=>archive.ImportPackage(limits,first,new BPackageImportOptions{MaximumTotalSize=1}));
Check("Rejected imports do not leave staged transactions",!Directory.EnumerateDirectories(limits.TempPath,"bpackage-*").Any());
void Rewrite(string destination,Func<string,string>? transform=null,bool corrupt=false,bool duplicate=false)
{
 using var input=new ZipArchive(File.OpenRead(first),ZipArchiveMode.Read);using var result=new ZipArchive(File.Create(destination),ZipArchiveMode.Create);
 foreach(var entry in input.Entries)
 {
  var name=transform?.Invoke(entry.FullName)??entry.FullName;var copy=result.CreateEntry(name);
  using(var from=entry.Open())using(var to=copy.Open())
  {if(corrupt&&entry.FullName=="Assets/Pack/hello.txt"){var bytes=new byte[entry.Length];from.ReadExactly(bytes);bytes[0]^=1;to.Write(bytes);}else from.CopyTo(to);}
  if(duplicate&&entry.FullName=="Assets/Pack/hello.txt"){var extra=result.CreateEntry(name);using var from=entry.Open();using var to=extra.Open();from.CopyTo(to);}
 }
}
var corrupt=Path.Combine(run,"corrupt.bpackage");Rewrite(corrupt,corrupt:true);
Throws<InvalidDataException>("Payload hash mismatch is rejected before application",()=>archive.ImportPackage(Project("Corrupt"),corrupt));
var duplicate=Path.Combine(run,"duplicate.bpackage");Rewrite(duplicate,duplicate:true);
Throws<InvalidDataException>("Duplicate ZIP payload entries are rejected",()=>archive.ReadManifest(duplicate));
var zipTraversal=Path.Combine(run,"zip-traversal.bpackage");Rewrite(zipTraversal,n=>n=="Assets/Pack/hello.txt"?"../escape.txt":n);
Throws<InvalidDataException>("Unexpected ZIP traversal entry is rejected",()=>archive.ReadManifest(zipTraversal));
var rollbackSource=Project("RollbackSource");Write(rollbackSource,"a.txt","new a");Write(rollbackSource,"z-fail.txt","new z");
var rollbackArchive=Path.Combine(run,"rollback.bpackage");archive.ExportPackage(rollbackSource,["Assets/a.txt","Assets/z-fail.txt"],rollbackArchive,new BPackageExportOptions{IncludeMetaFiles=false});
var rollbackTarget=Project("RollbackTarget");Write(rollbackTarget,"a.txt","original a");
probe.FailDestination=Asset(rollbackTarget,"z-fail.txt");probe.Armed=true;
Throws<IOException>("Injected write failure triggers transactional rollback",()=>archive.ImportPackage(rollbackTarget,rollbackArchive,new BPackageImportOptions{ConflictPolicy=BPackageConflictPolicy.Overwrite}));
Check("Rollback restores overwritten asset and removes partial writes",File.ReadAllText(Asset(rollbackTarget,"a.txt"))=="original a"&&!File.Exists(Asset(rollbackTarget,"z-fail.txt"))&&probe.Failures==1);
Check("Failed transactions release staging and backups",!Directory.EnumerateDirectories(rollbackTarget.TempPath,"bpackage-*").Any());
var linked=Project("Linked");Directory.CreateDirectory(Asset(linked,"Link"));probe.LinkPath=Asset(linked,"Link");
Throws<InvalidDataException>("Filesystem reparse metadata prevents traversal through a link",()=>archive.ImportPackage(linked,first,new BPackageImportOptions{DestinationDirectory="Link"}));probe.LinkPath=null;
Check("Link rejection creates no imported payload",!Directory.EnumerateFileSystemEntries(Asset(linked,"Link")).Any());
Finish();

public class FileProbe:DispatchProxy
{
 public IFileSystem Inner=null!;public bool Armed;public string? FailDestination,LinkPath;public int Failures;
 protected override object? Invoke(MethodInfo? method,object?[]? values)
 {
  if(method!.Name=="MoveFile"&&Armed&&string.Equals((string)values![1]!,FailDestination,StringComparison.OrdinalIgnoreCase)){Armed=false;Failures++;throw new IOException("Injected archive application failure");}
  if(method.Name=="GetAttributes"&&string.Equals((string)values![0]!,LinkPath,StringComparison.OrdinalIgnoreCase))return FileAttributes.Directory|FileAttributes.ReparsePoint;
  try{return method.Invoke(Inner,values);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
 }
}
