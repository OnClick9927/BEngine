using System.Reflection;
using System.Text.Json;
using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
var files=FileUtility.Current;var paths=PathUtility.Current;
var checks=new List<string>();var skipped=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Throws<T>(Action action,string name)where T:Exception{
 try{action();}catch(TargetInvocationException e)when(e.InnerException is T){Check(true,name);return;}
 catch(T){Check(true,name);return;}throw new Exception(name+" did not throw "+typeof(T).Name);
}
var project=Path.Combine(root,"Project");Directory.CreateDirectory(Path.Combine(project,"Assets/Scenes"));
File.WriteAllText(Path.Combine(project,"Project.yaml"),"format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Staging Test\n");
File.WriteAllText(Path.Combine(project,"Assets/Scenes/Main.scene.yaml"),"{}\n");
var raw=Path.Combine(project,"Assets/StreamingAssets");Directory.CreateDirectory(raw);
foreach(var relative in new[]{"Case Folder/Upper.DAT","plain.bin","asset.meta.yaml"}){
 var path=Path.Combine(raw,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,relative);
}
foreach(var relative in new[]{"Editor/secret.txt",".git/object",".svn/object",".hg/object","__MACOSX/file","asset.meta","asset.meta.lock-123",".atomic.tmp","~atomic"}){
 var path=Path.Combine(raw,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"excluded");
}
var workspace=ProjectWorkspace.Open(project,files,paths);var target=BuildTargetUtility.Current.Get("windows");
var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length;
var pure=PlayerBuildLayout.Create(workspace,Path.Combine(root,"Pure"),files,paths);
Check(!Directory.Exists(pure.RootDirectory)&&Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==before,"Read-only layout calculation has no filesystem side effects");
Check(pure.GetExecutablePath(BuildTargetPlatform.Windows).EndsWith("staging test.exe",StringComparison.Ordinal)&&PlayerBuildLayout.SanitizePlayerName("CON")=="con-game","Public layout preserves executable naming and reserved-name policy");
var stages=typeof(PlayerBuildPipeline).Assembly.GetType("BEngine.Editor.Build.PlayerBuildStaging",true)!;
var prepare=stages.GetMethod("Prepare",BindingFlags.Static|BindingFlags.NonPublic)!;
var complete=stages.GetMethod("Complete",BindingFlags.Static|BindingFlags.NonPublic)!;
PlayerBuildContext Context(string name,bool splash=true)=>new(new PlayerBuildRequest{ProjectPath=project,TargetId="windows",GraphicsBackends=target.GraphicsBackends,SplashScreenEnabled=splash,BuildVersion="9.8.7",DevelopmentBuild=true,WritePlayerLog=false,HotUpdateStartupScene="Assets/Scenes/Main.scene.yaml"},target,Path.Combine(root,name),null);
void Prepare(PlayerBuildContext c,CancellationToken token=default)=>prepare.Invoke(null,[workspace,PlayerBuildLayout.Create(workspace,c.StagingDirectory,files,paths),c,files,paths,token]);
var disabled=Context("Disabled",false);Prepare(disabled);
Check(disabled.StartupLogoPng.IsEmpty&&disabled.ApplicationIconPng.IsEmpty&&!File.Exists(PlayerBuildLayout.Create(workspace,disabled.StagingDirectory,files,paths).SplashImageStagingPath),"Disabled staging never requests, reads or writes startup images");
Check(disabled.DeploymentManifest is{SplashScreenEnabled:false,SplashImageResource:""},"Disabled staging retains empty splash resource identity");
var image=new Images();RuntimeServiceContributions.RegisterDefault<IPlayerStartupImageBuilder>(image);
var context=Context("Prepared");Prepare(context);var layout=PlayerBuildLayout.Create(workspace,context.StagingDirectory,files,paths);
Check(image.Calls==1&&context.StartupLogoPng.Span.SequenceEqual(new byte[]{1,2,3})&&context.ApplicationIconPng.Span.SequenceEqual(new byte[]{4,5}),"Core transports opaque startup image bytes without decoding or composing them");
Check(File.ReadAllBytes(layout.SplashImageStagingPath).SequenceEqual(new byte[]{1,2,3}),"Core stages the exact provided logo bytes");
Check(Directory.GetFiles(layout.StreamingAssetsDirectory,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(layout.StreamingAssetsDirectory,p).Replace('\\','/')).Order().SequenceEqual(new[]{"Case Folder/Upper.DAT","asset.meta.yaml","plain.bin"}.Order()),"Raw staging preserves case and ordinary data while excluding auxiliary and editor entries");
Check(File.ReadAllText(Path.Combine(layout.StreamingAssetsDirectory,"Case Folder/Upper.DAT"))=="Case Folder/Upper.DAT","Raw files retain their exact content");
var manifest=context.DeploymentManifest!;
Check(manifest.ProductName=="Staging Test"&&manifest.BuildVersion=="9.8.7"&&manifest.DevelopmentBuild&&!manifest.WritePlayerLog&&manifest.Executable=="staging test.exe"&&manifest.ApplicationScene=="Assets/Scenes/Main.scene.yaml","Core deployment manifest retains project and request settings");
Check(manifest.NativeLibraryDirectory=="staging test_data/assembly"&&manifest.PlayerResourceArchive=="staging test_data/resources/player.bresources"&&manifest.SplashImageResource==HostPackagedResourceAddresses.SplashImage,"Core deployment addresses retain the existing host contract");
var targetManifest=BuildTargetManifestSerializer.Deserialize(File.ReadAllBytes(layout.BuildTargetManifestStagingPath));
Check(targetManifest.TargetId==target.TargetId&&targetManifest.GraphicsBackends.SequenceEqual(target.GraphicsBackends),"Core stages the exact target and backend selection");
complete.Invoke(null,[layout,files]);complete.Invoke(null,[layout,files]);
Check(!File.Exists(layout.BuildTargetManifestStagingPath)&&!File.Exists(layout.SplashImageStagingPath)&&File.Exists(Path.Combine(layout.StreamingAssetsDirectory,"plain.bin")),"Core staging cleanup is idempotent and leaves raw payloads intact");
File.WriteAllText(Path.Combine(layout.ResourcesDirectory,"custom.bin"),"content");
void Validate()=>PlayerBuildOutputValidation.ValidateResourceDirectories(layout,["custom.bin"],files,paths);
Validate();Check(true,"Generic output validation accepts provider-defined resource formats");
File.WriteAllText(Path.Combine(layout.DataDirectory,"loose.bin"),"bad");Throws<InvalidDataException>(Validate,"Reject unexpected files in the data root");File.Delete(Path.Combine(layout.DataDirectory,"loose.bin"));
Directory.CreateDirectory(Path.Combine(layout.ResourcesDirectory,"extra"));Throws<InvalidDataException>(Validate,"Reject directories in the resource file set");Directory.Delete(Path.Combine(layout.ResourcesDirectory,"extra"));
File.Delete(Path.Combine(layout.ResourcesDirectory,"custom.bin"));Throws<InvalidDataException>(Validate,"Reject missing provider resources");
Directory.CreateDirectory(Path.Combine(layout.ResourcesDirectory,"custom.bin"));Throws<InvalidDataException>(Validate,"A resource filename cannot be replaced by a directory");Directory.Delete(Path.Combine(layout.ResourcesDirectory,"custom.bin"));
using(var cancelled=new CancellationTokenSource()){
 cancelled.Cancel();var calls=image.Calls;Throws<OperationCanceledException>(()=>Prepare(Context("Cancelled"),cancelled.Token),"Pre-cancelled staging rejects all work");
 Check(image.Calls==calls&&!Directory.Exists(Path.Combine(root,"Cancelled")),"Pre-cancelled staging has no image or filesystem side effects");
}
var link=Path.Combine(raw,"linked");
try{
 Directory.CreateSymbolicLink(link,Path.Combine(project,"Assets/Scenes"));
 Throws<InvalidDataException>(()=>Prepare(Context("Linked")),"Reject linked StreamingAssets entries");
}catch(UnauthorizedAccessException){skipped.Add("Symbolic-link creation requires an unavailable OS privilege");}
finally{if(Directory.Exists(link))Directory.Delete(link);}
// Exercise the public transaction using a non-AB content provider and no drawing package.
async Task Transaction(string name,string? failure=null){
 var events=new List<string>();var content=new Content(events,files,paths,failure);
 image.Fail=failure=="image";
 var pipeline=new PlayerBuildPipelineAdapter(ProcessUtility.Current,ApplicationPathUtility.Current,files,paths,PlatformUtility.Current,new PlatformPlayerBuildCapabilities(files,paths),content);
 foreach(var provider in pipeline.RegisteredProviders)pipeline.UnregisterProvider(provider.ProviderId);
 pipeline.RegisterProvider(new Platform(events,files,paths,failure));var published=Path.Combine(root,name.ToLowerInvariant());
 Directory.CreateDirectory(published);File.WriteAllText(Path.Combine(published,"old.txt"),"previous");
 var request=new PlayerBuildRequest{ProjectPath=project,TargetId="windows",OutputDirectory=published,ReplaceExisting=true,CompileShaders=false};
 try{await pipeline.BuildAsync(request);if(failure is not null)throw new Exception("Expected failure");}
 catch(ProbeFailure)when(failure is not null){ }
 finally{image.Fail=false;}
 if(failure is null){
  Check(events.SequenceEqual(new[]{"prepare","finalize","platform"}),"Content preparation, engine staging, finalization and platform execution remain ordered");
  Check(File.Exists(Path.Combine(published,"staging test_data/resources/custom.bin"))&&!File.Exists(Path.Combine(published,"old.txt")),"Successful transaction publishes a custom content format atomically");
 }else{
  var expected=failure is "prepare" or "image"?new[]{"prepare"}:failure=="platform"?new[]{"prepare","finalize","platform"}:new[]{"prepare","finalize"};
  Check(events.SequenceEqual(expected),"Failed build phase cannot reach later phases: "+failure);
  Check(File.ReadAllText(Path.Combine(published,"old.txt"))=="previous"&&Directory.GetFiles(published).Length==1,"Failure preserves the previously published application: "+failure);
 }
 Check(!Directory.EnumerateDirectories(root,".*.staging").Any(),"Transaction staging is removed: "+name);
}
await Transaction("Success");await Transaction("PrepareFailure","prepare");await Transaction("FinalizeFailure","finalize");
await Transaction("ImageFailure","image");await Transaction("PlatformFailure","platform");
RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
Throws<InvalidOperationException>(()=>Prepare(Context("NoBuilder")),"Enabled splash fails explicitly when no drawing contribution exists");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Graphics" or "BEngine.Graphics.Editor" or "BEngine.AssetBundles" or "BEngine.AssetBundles.Editor" or "BEngine.IMGUI.Editor"),"Core staging and custom content publication load no concrete graphics, AB or UI package");
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,Skipped=skipped},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PLAYER_STAGING_CORE_OK|"+checks.Count);
sealed class Images:IPlayerStartupImageBuilder{
 public int Calls;public bool Fail;public PlayerStartupImages Build(ProjectWorkspace w,PlayerBuildRequest r){Calls++;if(Fail)throw new ProbeFailure();return new(new byte[]{1,2,3},new byte[]{4,5});}
}
sealed class ProbeFailure:Exception;
sealed class Content(List<string> events,FileUtility files,PathUtility paths,string? failure):IPlayerBuildContentProvider{
 public Task PrepareAsync(PlayerBuildContext c,CancellationToken token){events.Add("prepare");if(failure=="prepare")throw new ProbeFailure();return Task.CompletedTask;}
 public Task FinalizeAsync(PlayerBuildContext c,CancellationToken token){
  events.Add("finalize");var layout=PlayerBuildLayout.Create(ProjectWorkspace.Open(c.Request.ProjectPath,files,paths),c.StagingDirectory,files,paths);
  if(c.DeploymentManifest is null||c.StartupLogoPng.IsEmpty||!File.Exists(layout.BuildTargetManifestStagingPath)||!File.Exists(Path.Combine(layout.StreamingAssetsDirectory,"plain.bin")))throw new Exception("Engine staging missing during finalization");
  if(failure=="finalize")throw new ProbeFailure();File.WriteAllText(Path.Combine(layout.ResourcesDirectory,"custom.bin"),"content");return Task.CompletedTask;
 }
}
sealed class Platform(List<string> events,FileUtility files,PathUtility paths,string? failure):IPlayerBuildTargetProvider{
 public string ProviderId=>"test.staging";public bool CanBuild(BuildTargetDescriptor t,out string reason){reason="";return true;}
 public Task BuildAsync(PlayerBuildContext c,CancellationToken token=default){
  events.Add("platform");var layout=PlayerBuildLayout.Create(ProjectWorkspace.Open(c.Request.ProjectPath,files,paths),c.StagingDirectory,files,paths);
  if(File.Exists(layout.SplashImageStagingPath)||File.Exists(layout.BuildTargetManifestStagingPath))throw new Exception("Core temporary files leaked to platform execution");
  PlayerBuildOutputValidation.ValidateResourceDirectories(layout,["custom.bin"],files,paths);if(failure=="platform")throw new ProbeFailure();return Task.CompletedTask;
 }
}
