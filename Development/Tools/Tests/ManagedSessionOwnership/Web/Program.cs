using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine.Platform;
using BEngine;
using BEngine.Build;
using BEngine.Player;
using BEngine.Player.HotUpdate;
if(OperatingSystem.IsBrowser())await Probe.Run();
[SupportedOSPlatform("browser")]
static partial class Probe
{
    [JSImport("globalThis.hashProbeResult")]private static partial void Report(string result);
    [JSImport("globalThis.sessionProbeBaseUrl")]private static partial string BaseUrl();
    [JSImport("globalThis.hashProbeMode")]private static partial void Mode(string mode);
    [JSImport("globalThis.hashProbeSettle")]private static partial void Settle();
    [JSImport("globalThis.hashProbeDiagnostics")]private static partial string Diagnostics();
    static readonly List<string> Checks=[];
    static void Check(bool value,string name){if(!value)throw new Exception(name);Checks.Add(name);}
    static async Task Pump(IPlatformSha256Operation op)
    {var timer=Stopwatch.StartNew();while(op.Status==PlatformHashOperationStatus.Running&&timer.Elapsed.TotalSeconds<10){op.Poll();await Task.Delay(1);}Check(op.Status!=PlatformHashOperationStatus.Running,"Browser digest terminates");}
    internal static async Task Run()
    {
        try
        {
            WebPlatform.Initialize();
            var service=PlatformRuntime.GetRequiredService<IPlatformSha256Service>();
            Check(service is WebSha256Service,"Actual browser startup selects native SHA service");
            Check(!service.CanComputeAsync(-1)&&!service.CanComputeAsync(64L*1024*1024+1),"Native admission bounds input without allocating it");
            foreach(var length in new[]{0,1,55,56,63,64,65,65537,1048577})
            {
                var bytes=new byte[length];new System.Random(1937+length).NextBytes(bytes);
                var expected=Convert.ToHexStringLower(SHA256.HashData(bytes));
                using(var native=service.TryBegin(bytes)!)
                {
                    Check(native is not null,"Actual WebCrypto input admitted "+length);
                    bytes.AsSpan().Fill(0); // The borrowed WASM view must have been copied before return.
                    await Pump(native!);
                    Check(native!.Status==PlatformHashOperationStatus.Succeeded&&native.Result==expected,"Native digest owns stable copied input "+length);
                    native.Dispose();native.Dispose();
                }
                // The bounded managed implementation is reused through the same contract.
                new System.Random(1937+length).NextBytes(bytes);
                using var managed=service.CreateIncremental();
                for(int at=0;at<length;at+=Math.Min(8191,length-at))managed.Append(bytes.AsSpan(at,Math.Min(8191,length-at)));
                var digest=new byte[32];managed.FinalizeHash(digest);
                Check(Convert.ToHexStringLower(digest)==expected,"Actual WASM managed streaming digest "+length);
            }
            using(var realDiag=JsonDocument.Parse(Diagnostics()))
                Check(realDiag.RootElement.GetProperty("Active").GetInt32()==0&&realDiag.RootElement.GetProperty("ReservedBytes").GetInt64()==0,"Completed native leases release all reservations");
            foreach(var mode in new[]{"failure","short"})
            {
                Mode(mode);using var op=service.TryBegin("abc"u8)!;await Pump(op);
                Check(op.Status==PlatformHashOperationStatus.Failed&&op.Exception is not null,"Accepted native error stays failed: "+mode);
            }
            Mode("stall");var held=new List<IPlatformSha256Operation>();
            for(int i=0;i<32;i++)held.Add(service.TryBegin([1])!);
            Check(held.All(op=>op is not null)&&service.TryBegin([1]) is null,"At most 32 native jobs admitted");
            foreach(var op in held){op.Dispose();op.Dispose();}
            using(var retiring=JsonDocument.Parse(Diagnostics()))
                Check(retiring.RootElement.GetProperty("Active").GetInt32()==0&&retiring.RootElement.GetProperty("Pending").GetInt32()==32&&retiring.RootElement.GetProperty("ReservedBytes").GetInt64()==32,"Canceled unabortable digests retain admission until settlement");
            Check(service.TryBegin([1]) is null,"Cancellation cannot bypass the native job budget");
            Settle();await Task.Delay(10);
            using(var retired=JsonDocument.Parse(Diagnostics()))
                Check(retired.RootElement.GetProperty("Pending").GetInt32()==0&&retired.RootElement.GetProperty("ReservedBytes").GetInt64()==0,"Late digest settlement releases all charged bytes");
            Mode("unavailable");Check(!service.CanComputeAsync(3),"Unavailable native API retains managed fallback");
            using(var fallback=service.CreateIncremental()){fallback.Append("abc"u8);var result=new byte[32];fallback.FinalizeHash(result);Check(Convert.ToHexStringLower(result)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","Fallback remains valid with no browser crypto API");}
            await Sessions();
            Mode("real");Report(JsonSerializer.Serialize(new{Passed=true,Count=Checks.Count,Checks,Diagnostics=Diagnostics()}));
        }
        catch(Exception e){Report(JsonSerializer.Serialize(new{Passed=false,Checks,Error=e.ToString()}));}
    }

    private static async Task Sessions()
    {
        using var http=new HttpClient();
        var bytes=await http.GetByteArrayAsync(new Uri(new Uri(BaseUrl()),"ManagedSessionFixture.dll"));
        Check(typeof(BuildTargetManifest).Assembly==typeof(ManagedCodePlayerSession).Assembly,"Actual WASM uses Core-owned deployment and session");
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        var factory=new PlayerManagedCodeRuntimeFactory([new CoreClrManagedCodeRuntimeProvider(),new AotInterpreterManagedCodeRuntimeProvider()]);
        var release=new ManagedCodeRelease("browser-session",[new ManagedCodeModule("ManagedSessionFixture","test",bytes)]);
        var preparing=new HotUpdateActivationGate(factory).PrepareAsync(release,new(ManagedCodeRuntimeKind.AotInterpreter)).AsBTask();
        await Drain(preparing);
        var prepared=preparing.GetAwaiter().GetResult();
        Check(prepared.ModuleIds.SequenceEqual(new[]{"fixture"}),"Actual WASM discovers a module in the fetched PE image");
        var module=prepared.Assemblies.Single().GetType("SessionFixture.Module",true)!;
        int Counter(string name)=>(int)module.GetField(name)!.GetValue(null)!;
        HotUpdateDomain? domain=null;
        using var session=new ManagedCodePlayerSession(prepared,activated=>domain=activated);
        var host=new EmptyServices();await Drain(session.ActivateAsync(host).AsBTask());
        Check(Counter("Configured")==1&&Counter("Started")==1&&Counter("Stopped")==0,"Actual WASM activates the interpreted module");
        Check(ReferenceEquals(domain!.Services.GetService(typeof(IServiceProvider)),host),"Actual WASM exports the neutral host service");
        await Drain(session.DisposeAsync().AsBTask());
        Check(Counter("Stopped")==1&&session.Assemblies.Count==0,"Actual WASM stops the interpreted module and releases its session");
    }
    private static async Task Drain(BTask task)
    {for(int i=0;!task.IsCompleted&&i<1000;i++){BTaskScheduler.Main.Tick(TimeSpan.Zero);await Task.Delay(1);}Check(task.IsCompleted,"WASM session operation completes");task.GetAwaiter().GetResult();}
    private sealed class EmptyServices:IServiceProvider {public object? GetService(Type type)=>null;}
}
