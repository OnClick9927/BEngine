using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using BEngine.Editor;
using BEngine.DependencyInjection;
using BEngine.ProjectSystem;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32.SafeHandles;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var root=Path.Combine(output,"Run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",Path.Combine(root,"Hub"));
DotNetPlatformServices.Initialize();DotNetEditorPlatform.Initialize();
var files=DispatchProxy.Create<IFileSystem,FileProbe>();var fault=(FileProbe)(object)files;fault.Inner=PhysicalFileSystem.Shared;
using var host=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().AddSingleton(files).BuildServiceProvider();
using var attachment=EditorServiceScope.Attach(host);
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS|"+name);}
void Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
var type=typeof(BPackageManager).Assembly.GetType("BEngine.ProjectSystem.ProjectPackageCache",true)!;
object Call(object? instance,string name,params object?[] values)
{
 try{return type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,values)!;}
 catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
}
object Cache(ProjectWorkspace project)
{
 try{return Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,[project],null)!;}
 catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
}
ProjectWorkspace Project(string name)
{
 var directory=Path.Combine(root,name);Directory.CreateDirectory(Path.Combine(directory,"Assets"));
 File.WriteAllText(Path.Combine(directory,"Project.yaml"),"format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Cache access probe\n");
 return ProjectWorkspace.Open(directory);
}
const string id="com.fixture.cache";
string Package(string name,string version)
{
 var directory=Path.Combine(root,name);Directory.CreateDirectory(directory);
 File.WriteAllText(Path.Combine(directory,"package.yaml"),$"format: BEngine.Package\nversion: 1\nid: {id}\ndisplayName: Cache fixture\npackageVersion: {version}\ntype: package\nassembly: CacheFixture\n");
 File.WriteAllText(Path.Combine(directory,"a.txt"),"a"+version);
 File.WriteAllText(Path.Combine(directory,"same.dll"),"unchanged mapped contents");
 return directory;
}
void Sync(object cache,string source,Action<IReadOnlyDictionary<string,BPackageDefinition>>? apply=null)=>
 Call(cache,"Synchronize",new BPackageCatalog([Path.Combine(source,"package.yaml")]),new[]{id},apply??(_=>{}));
string Target(ProjectWorkspace project)=>Path.Combine(project.PackageCachePath,id);
bool Clean(ProjectWorkspace project)=>!Directory.EnumerateDirectories(project.PackageCachePath).Any(p=>Path.GetFileName(p).StartsWith('.'));

var old=Package("Old","1.0.0");var updated=Package("New","1.1.0");
File.WriteAllText(Path.Combine(old,"obsolete.txt"),"removed");File.WriteAllText(Path.Combine(updated,"new.txt"),"added");
var project=Project("Normal");var cache=Cache(project);Sync(cache,old);
Check(File.ReadAllText(Path.Combine(Target(project),"a.txt"))=="a1.0.0","Normal first installation");
Sync(cache,updated);Check(File.ReadAllText(Path.Combine(Target(project),"a.txt"))=="a1.1.0"&&Clean(project),"Normal replacement commits and removes transaction folders");
Throws<InvalidOperationException>(()=>Sync(cache,old,_=>throw new InvalidOperationException("activation failed")),"Normal activation failure is reported");
Check(File.ReadAllText(Path.Combine(Target(project),"a.txt"))=="a1.1.0"&&Clean(project),"Normal activation rollback restores previous package");

project=Project("Locked");cache=Cache(project);Sync(cache,old);var target=Target(project);fault.DenyMove=target;
Directory.CreateDirectory(Path.Combine(target,"obj"));File.WriteAllText(Path.Combine(target,"obj","design.dll"),"IDE generated");
using(var unchanged=File.Open(Path.Combine(target,"same.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
{
 Sync(cache,updated);
 Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.1.0","A locked package root updates payload without renaming the root");
 Check(!File.Exists(Path.Combine(target,"obsolete.txt"))&&File.Exists(Path.Combine(target,"new.txt")),"Fallback adds and removes package files");
 Check(File.ReadAllText(Path.Combine(target,"obj","design.dll"))=="IDE generated","Fallback preserves unrelated IDE build outputs");
 Check(Clean(project),"Fallback commit removes snapshots and marker");
 Check(fault.MovesDenied==1,"The root move was actually denied");
 var denied=fault.MovesDenied;var oldTimestamp=File.GetLastWriteTimeUtc(Path.Combine(target,"same.dll"));
 File.SetLastWriteTimeUtc(Path.Combine(updated,"same.dll"),oldTimestamp.AddMinutes(5));
 Sync(cache,updated);Check(fault.MovesDenied==denied,"Byte-identical files with different timestamps do not force another replacement");
 Throws<InvalidOperationException>(()=>Sync(cache,old,_=>throw new InvalidOperationException("activation failed")),"Fallback activation failure is reported");
 Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.1.0"&&File.Exists(Path.Combine(target,"new.txt"))&&!File.Exists(Path.Combine(target,"obsolete.txt")),"Fallback activation rollback restores additions, removals and modified files");
 Check(Clean(project),"Fallback rollback removes completed snapshots");
}
fault.FailCopy=Path.Combine(target,"a.txt");
Throws<IOException>(()=>Sync(cache,old),"Changed-file IO failure is not hidden");
Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.1.0"&&Clean(project),"A failed copy rolls back the complete payload");
using(var changed=File.Open(Path.Combine(target,"a.txt"),FileMode.Open,FileAccess.Read,FileShare.Read))
{
 Throws<IOException>(()=>Sync(cache,old),"A genuinely locked changed payload file remains an error");
 Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.1.0"&&Clean(project),"Changed-file sharing failure preserves the previous working package");
}

fault.FailBackupDelete=true;
Throws<IOException>(()=>Sync(cache,old),"A post-commit cleanup failure is surfaced");
Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.0.0","A committed update retains its new content after cleanup failure");
cache=Cache(project);
Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.0.0"&&Clean(project),"Startup clears a committed backup without rolling back the new package");
Sync(cache,updated);

var backup=Path.Combine(project.PackageCachePath,".backup-"+DateTime.UtcNow.Ticks.ToString("D19")+"-"+Guid.NewGuid().ToString("N"));
Copy(updated,backup);File.WriteAllText(Path.Combine(backup,".package-cache-inplace-backup"),"1");
File.WriteAllText(Path.Combine(target,"a.txt"),"partially overwritten");File.WriteAllText(Path.Combine(target,"extra.txt"),"partial addition");File.Delete(Path.Combine(target,"new.txt"));
cache=Cache(project);
Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.1.0"&&!File.Exists(Path.Combine(target,"extra.txt"))&&File.Exists(Path.Combine(target,"new.txt")),"Startup recovers an interrupted in-place update even when package.yaml is valid");
Check(!Directory.Exists(backup)&&!File.Exists(Path.Combine(target,".package-cache-inplace-backup")),"Recovery does not leak its private marker into package payload");
fault.DenyMove=null;

var imageType=typeof(BPackageManager).Assembly.GetType("BEngine.ProjectSystem.PackageImageCache",true)!;
var publish=imageType.GetMethod("Publish",BindingFlags.Static|BindingFlags.NonPublic,null,[typeof(string),typeof(string)],null)!;
var imageRoot=Path.Combine(root,"Images");var staging=Path.Combine(root,"Shadow");
Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,"fixture.dll"),"first image");
object Publish()
{
 try{return publish.Invoke(null,[staging,imageRoot])!;}
 catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
}
string ImageRoot(object image)=>(string)image.GetType().GetProperty("Root")!.GetValue(image)!;
IDisposable Lease(object image)=>(IDisposable)image.GetType().GetProperty("Lease")!.GetValue(image)!;
fault.DenyMove=staging;
var image=Publish();var firstRoot=ImageRoot(image);
Check(File.ReadAllText(Path.Combine(firstRoot,"fixture.dll"))=="first image"&&File.Exists(Path.Combine(firstRoot,".lease")),"Image publication completes without renaming a PE staging directory");
Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,"fixture.dll"),"first image");
var reused=Publish();Check(ImageRoot(reused)==firstRoot,"An active identical image is reused after verification");Lease(reused).Dispose();
Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,"fixture.dll"),"second image");
var next=Publish();Check(ImageRoot(next)!=firstRoot&&File.ReadAllText(Path.Combine(firstRoot,"fixture.dll"))=="first image","Publishing another generation preserves a leased old image");
Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,"fixture.dll"),"third image");
fault.FailImageWrite=true;
Throws<IOException>(()=>Publish(),"Incomplete immutable image publication surfaces the write failure");
Check(Directory.EnumerateDirectories(imageRoot).Count(p=>!File.Exists(Path.Combine(p,".lease")))==1,"A partial image remains uncommitted without a lease marker");
var recovered=Publish();Check(File.ReadAllText(Path.Combine(ImageRoot(recovered),"fixture.dll"))=="third image"&&File.Exists(Path.Combine(ImageRoot(recovered),".lease")),"Retry repairs an incomplete image before exposing it to readers");
Lease(recovered).Dispose();Lease(next).Dispose();Lease(image).Dispose();fault.DenyMove=null;

if(OperatingSystem.IsWindows())
{
 project=Project("NativeLock");cache=Cache(project);Sync(cache,old);target=Target(project);
 using var locked=Native.OpenDirectory(target);
 Check(!locked.IsInvalid,"A real Windows directory handle is held without delete sharing");
 Throws<IOException>(()=>Directory.Move(target,target+".blocked"),"Native fixture reproduces Windows directory rename failure");
 Sync(cache,updated);
 Check(File.ReadAllText(Path.Combine(target,"a.txt"))=="a1.1.0"&&Clean(project),"Real Windows directory lock does not prevent package payload synchronization");
}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,RunDirectory=root},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PROJECT_PACKAGE_CACHE_ACCESS_OK|"+checks.Count);

static void Copy(string source,string target)
{
 Directory.CreateDirectory(target);foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)){var destination=Path.Combine(target,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);}
}
public class FileProbe:DispatchProxy
{
 public IFileSystem Inner=null!;public string? DenyMove,FailCopy;public int MovesDenied;public bool FailBackupDelete,FailImageWrite;
 protected override object? Invoke(MethodInfo? method,object?[]? values)
 {
  if(method!.Name=="MoveDirectory"&&string.Equals((string)values![0]!,DenyMove,StringComparison.OrdinalIgnoreCase)){MovesDenied++;throw new IOException("Directory is held by an IDE");}
  if(method.Name=="CopyFile"&&string.Equals((string)values![1]!,FailCopy,StringComparison.OrdinalIgnoreCase)){FailCopy=null;throw new IOException("Injected payload copy failure");}
  if(method.Name=="DeleteDirectory"&&FailBackupDelete&&Path.GetFileName((string)values![0]!).StartsWith(".backup-")){FailBackupDelete=false;throw new IOException("Injected cleanup failure");}
  if(method.Name=="WriteAllBytes"&&FailImageWrite){FailImageWrite=false;throw new IOException("Injected immutable image write failure");}
  try{return method.Invoke(Inner,values);}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
 }
}
static class Native
{
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 public static SafeFileHandle OpenDirectory(string path)=>CreateFile(path,1,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
}
