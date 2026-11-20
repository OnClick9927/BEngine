using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);DotNetPlatformServices.Initialize();
var checks=new List<string>();
void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
void Throws<T>(Action action,string text)where T:Exception{try{action();}catch(T){Check(true,text);return;}throw new Exception(text);}
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Audio" or "BEngine.Graphics" or "BEngine.Core.Editor"),"Neutral dynamic modules do not load consumer packages or a GUI");
var factory=RuntimeServices.GetRequiredService<IPlatformNativeLibraryFactory>();
Check(factory.GetType().Assembly.GetName().Name=="BEngine.Platform.DotNet","System module ownership belongs to the platform implementation");
Throws<ArgumentNullException>(()=>factory.Load(null!),"Null paths are rejected");
Throws<DllNotFoundException>(()=>factory.Load(Path.Combine(args[1],"absent.dll")),"Missing modules are rejected");
var first=factory.Load(Path.Combine(args[1],"fixture.dll"));var second=factory.Load(Path.Combine(args[1],"fixture.dll"));
var export=first.GetExport("fixture_add");
unsafe{var add=(delegate* unmanaged[Cdecl]<int,int,int>)export;Check(add(19,23)==42,"An independent consumer calls a non-audio native export");}
Throws<EntryPointNotFoundException>(()=>first.GetExport("absent_export"),"Unknown exports remain explicit failures");
Throws<ArgumentException>(()=>first.GetExport(" "),"Empty symbol names are rejected");
first.Dispose();first.Dispose();
Throws<ObjectDisposedException>(()=>first.GetExport("fixture_add"),"Disposed module leases reject export lookup");
unsafe{var add=(delegate* unmanaged[Cdecl]<int,int,int>)second.GetExport("fixture_add");Check(add(1,2)==3,"Disposing one lease preserves another independent lease");}
second.Dispose();
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks}));
Console.WriteLine("PLATFORM_MODULE_OK "+checks.Count);
