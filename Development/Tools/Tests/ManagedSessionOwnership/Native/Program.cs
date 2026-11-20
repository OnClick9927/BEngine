using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.Build;
using BEngine.Player;
using BEngine.Player.HotUpdate;
using BEngine.Platform.DotNet;
DotNetPlatformServices.Initialize();AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
var checks=new List<string>();
void Check(bool value,string description){if(!value)throw new Exception(description);checks.Add(description);}
void Drain(BTask task){for(int i=0;!task.IsCompleted&&i<1000;i++)BTaskScheduler.Main.Tick(TimeSpan.Zero);Check(task.IsCompleted,"Native operation completes");task.GetAwaiter().GetResult();}
var image=File.ReadAllBytes(args[1]);
foreach(var kind in new[]{ManagedCodeRuntimeKind.CoreClr,ManagedCodeRuntimeKind.AotInterpreter})
{
    var release=new ManagedCodeRelease("native-"+kind,[new ManagedCodeModule("ManagedSessionFixture","test",image)]);
    var factory=new PlayerManagedCodeRuntimeFactory([new CoreClrManagedCodeRuntimeProvider(),new AotInterpreterManagedCodeRuntimeProvider()]);
    var preparing=new HotUpdateActivationGate(factory).PrepareAsync(release,new(kind)).AsBTask();Drain(preparing);
    var prepared=preparing.GetAwaiter().GetResult();Check(prepared.ModuleIds.SequenceEqual(new[]{"fixture"}),"Real PE module discovery: "+kind);
    Check(prepared.Assemblies.Count==1,"Exactly one release image is loaded: "+kind);
    var assembly=prepared.Assemblies.Single();var module=assembly.GetType("SessionFixture.Module",true)!;
    int Counter(string name)=>(int)module.GetField(name)!.GetValue(null)!;
    HotUpdateDomain? domain=null;using var session=new ManagedCodePlayerSession(prepared,activated=>domain=activated);
    var host=new HostServices();Drain(session.ActivateAsync(host).AsBTask());
    Check(Counter("Configured")==1&&Counter("Started")==1&&Counter("Stopped")==0,"Real module activation: "+kind);
    Check(domain!.RuntimeKind==kind&&ReferenceEquals(domain.Services.GetService(typeof(IServiceProvider)),host),"Real domain exports its host contract: "+kind);
    Drain(session.DisposeAsync().AsBTask());
    Check(Counter("Stopped")==1&&session.Assemblies.Count==0,"Real module shutdown and image release: "+kind);
}
File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("NATIVE_SESSION_OK|"+checks.Count);
sealed class HostServices:IServiceProvider {public object? GetService(Type type)=>null;}
