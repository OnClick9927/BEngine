using System.Diagnostics;
using System.Text.Json;
using BEngine.Platform;
var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
var files=PhysicalFileSystem.Shared;
var source=Path.Combine(output,"staging.txt");var dest=Path.Combine(output,"artifact.txt");var backup=Path.Combine(output,"backup.txt");
File.WriteAllText(source,"new");File.WriteAllText(dest,"old");files.ReplaceFile(source,dest,backup,true);
Check(File.ReadAllText(dest)=="new"&&File.ReadAllText(backup)=="old"&&!File.Exists(source),"Successful atomic replacement preserves backup semantics");
File.WriteAllText(source,"pending");
using(var locked=File.Open(dest,FileMode.Open,FileAccess.Read,FileShare.Read))
{
 var timer=Stopwatch.StartNew();
 try{files.ReplaceFile(source,dest,null,true);throw new Exception("A held read lock was bypassed");}
 catch(IOException error){Check((error.HResult&0xffff) is 32 or 33 or 1175,"Persistent sharing conflict remains an IO failure");}
 Check(timer.Elapsed.TotalSeconds<2,"Persistent conflict retries are bounded");
 Check(File.ReadAllText(dest)=="new"&&File.ReadAllText(source)=="pending","Failed replacement preserves destination and staging bytes");
}
var transient=File.Open(dest,FileMode.Open,FileAccess.Read,FileShare.Read);
try{File.Replace(source,dest,null,true);throw new Exception("Fixture is not locked");}catch(IOException){Check(true,"Transient fixture begins with a real native sharing conflict");}
var release=Task.Run(()=>{Thread.Sleep(10);transient.Dispose();});
try{files.ReplaceFile(source,dest,null,true);}finally{release.GetAwaiter().GetResult();transient.Dispose();}
Check(File.ReadAllText(dest)=="pending"&&!File.Exists(source),"A briefly held native read lock recovers with atomic replacement");
for(var i=0;i<80;i++){File.WriteAllText(source,"value-"+i);files.ReplaceFile(source,dest,null,true);if(File.ReadAllText(dest)!="value-"+i)throw new Exception("Repeated replacement lost data");}
Check(true,"80 successive replacements retain exact final content");
File.WriteAllText(source,"survivor");
try{files.ReplaceFile(source,Path.Combine(output,"missing.txt"),null,true);throw new Exception("Missing destination accepted");}catch(IOException){Check(File.ReadAllText(source)=="survivor","Non-sharing failure propagates and preserves staging content");}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PHYSICAL_REPLACE_RETRY_OK|"+checks.Count);
