using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.Build;
using BEngine.Platform;
using BEngine.Player;
using BEngine.Player.HotUpdate;
using BEngine.DependencyInjection;

RuntimeServiceContributions.RegisterDefault(new PlatformUtility(DispatchProxy.Create<IPlatformService, TestPlatform>()));
RuntimeServiceContributions.RegisterDefault(new RenderSettingsUtility(DispatchProxy.Create<IRenderSettingsService, TestPlatform>()));

var checks = new List<string>();
void Check(bool value, string description)
{ if (!value) throw new Exception(description); checks.Add(description); }
void Drain(BTask task)
{
    for (var i=0; !task.IsCompleted && i<1000; i++) BTaskScheduler.Main.Tick(TimeSpan.Zero);
    Check(task.IsCompleted, "Bounded scheduler completion"); task.GetAwaiter().GetResult();
}
T Value<T>(BTask<T> task) { Drain(task); return task.GetAwaiter().GetResult(); }
void Reject<T>(Action action, string description) where T: Exception
{ try { action(); } catch (T) { Check(true, description); return; } throw new Exception(description); }

Check(typeof(BuildTargetManifest).Assembly == typeof(ManagedCodePlayerSession).Assembly,
    "Deployment and managed session belong to Core Player");
Check(typeof(PlayerManagedCodeRuntimeFactory).Assembly == typeof(ManagedCodePlayerSession).Assembly,
    "Runtime strategy belongs to Core Player");
Check(!new CoreClrManagedCodeRuntimeProvider().IsAvailable,
    "No native assembly service is implicitly initialized");
Reject<PlatformNotSupportedException>(() => new PlayerManagedCodeRuntimeFactory([]).CreateRuntime(new(ManagedCodeRuntimeKind.CoreClr)),
    "Missing host capability fails explicitly without a platform backend");

var events = new List<string>();
ManagedCodePlayerSession Prepare(params TestModule[] modules)
{
    var runtime = new TestRuntime(modules, events);
    var prepared = Value(new HotUpdateActivationGate(new TestFactory(runtime))
        .PrepareAsync(new ManagedCodeRelease("test", []), new(ManagedCodeRuntimeKind.CoreClr)).AsBTask());
    return new(prepared, domain => events.Add("activated:"+domain.Release.ReleaseId));
}

var a = new TestModule("a", events);
var b = new TestModule("b", events, ["a"]);
var session = Prepare(b, a);
session.OwnRuntimeFactoryServices(new Owner(events));
Drain(session.ActivateAsync(new EmptyServices()).AsBTask());
Check(events.SequenceEqual(new[]{"load","configure:a","configure:b","start:a","start:b","activated:test"}),
    "Dependency order controls module activation independent of input order");
Drain(session.ActivateAsync(new EmptyServices()).AsBTask());
var disposal = session.DisposeAsync().AsBTask(); Drain(disposal);
Check(events.TakeLast(4).SequenceEqual(new[]{"stop:b","stop:a","runtime","owner"}),
    "Modules stop in reverse order before the native runtime and factory services");
Check(ReferenceEquals(disposal,session.DisposeAsync().AsBTask()), "Repeated disposal returns the same operation");
Reject<ObjectDisposedException>(()=>Drain(session.ActivateAsync(new EmptyServices()).AsBTask()), "Disposed session cannot reactivate");
Reject<ObjectDisposedException>(()=>session.OwnRuntimeFactoryServices(new Owner(events)), "Disposed session cannot accept ownership");

events.Clear(); session=Prepare(new TestModule("prepared",events));
Drain(session.DisposeAsync().AsBTask());
Check(events.SequenceEqual(new[]{"load","runtime"}), "Unactivated preparation releases its runtime without starting modules");

events.Clear(); session=Prepare(new TestModule("failure",events){FailStart=true});
session.OwnRuntimeFactoryServices(new Owner(events));
Reject<InvalidOperationException>(()=>Drain(session.ActivateAsync(new EmptyServices()).AsBTask()), "Activation failure reaches its caller");
Drain(session.DisposeAsync().AsBTask());
Check(events.SequenceEqual(new[]{"load","configure:failure","start:failure","stop:failure","runtime","owner"}),
    "Activation rollback releases a configured module and retains factory ownership until shutdown");

events.Clear(); session=Prepare(new TestModule("cancelled",events));
using(var cancellation=new BCancellationSource())
{
    cancellation.Cancel();
    Reject<BOperationCanceledException>(()=>Drain(session.ActivateAsync(new EmptyServices(),cancellation.Token).AsBTask()),
        "Pre-cancelled activation releases preparation");
}
Drain(session.DisposeAsync().AsBTask());
Check(events.SequenceEqual(new[]{"load","runtime"}), "Cancellation does not start modules or leak the prepared runtime");

events.Clear(); var pending=new BTaskCompletionSource();
session=Prepare(new TestModule("pending",events){Start=pending.Task});
session.OwnRuntimeFactoryServices(new Owner(events));
var activating=session.ActivateAsync(new EmptyServices()).AsBTask();
Check(!activating.IsCompleted, "An asynchronous module can suspend activation");
Reject<InvalidOperationException>(()=>Drain(session.ActivateAsync(new EmptyServices()).AsBTask()), "Overlapping activation is rejected");
disposal=session.DisposeAsync().AsBTask();
Check(!disposal.IsCompleted && !events.Contains("owner"), "Shutdown waits for pending activation and retains its services");
pending.SetResult(); Drain(activating); Drain(disposal);
Check(events.Count(e=>e=="runtime")==1 && events.Count(e=>e=="stop:pending")==1 && events[^1]=="owner",
    "Shutdown during activation does not leak the resulting active domain");

events.Clear(); session=Prepare(new TestModule("reentrant",events));
Drain(session.ActivateAsync(new EmptyServices()).AsBTask());
BTask? observed=null;
session.OwnRuntimeFactoryServices(new Owner(events,()=>observed=session.DisposeAsync().AsBTask()));
disposal=session.DisposeAsync().AsBTask(); Drain(disposal);
Check(ReferenceEquals(observed,disposal), "Synchronous user teardown observes the already published shutdown operation");

events.Clear(); session=Prepare(new TestModule("bad-stop",events){FailStop=true});
session.OwnRuntimeFactoryServices(new Owner(events));
Drain(session.ActivateAsync(new EmptyServices()).AsBTask());
Reject<AggregateException>(()=>Drain(session.DisposeAsync().AsBTask()), "Shutdown propagates teardown failures");
Check(events[^2]=="runtime" && events[^1]=="owner", "A failing module stop does not skip remaining owners");

var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name!).ToArray();
Check(!loaded.Contains("BEngine.Platform.DotNet") && !loaded.Contains("BEngine.AssetBundles") && !loaded.Contains("BEngine.Graphics"),
    "Core-only consumer runs without packages, native windowing or a GPU");
File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,LoadedAssemblies=loaded},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("MANAGED_SESSION_OK|"+checks.Count);

sealed class EmptyServices : IServiceProvider { public object? GetService(Type type)=>null; }
public class TestPlatform : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method!.Name switch
    {
        "GetLoadedAssemblies" => Array.Empty<Assembly>(),
        "get_MonotonicTimestamp" => System.Diagnostics.Stopwatch.GetTimestamp(),
        "get_TimestampFrequency" => System.Diagnostics.Stopwatch.Frequency,
        "GetElapsedTime" => System.Diagnostics.Stopwatch.GetElapsedTime((long)arguments![0]!),
        "get_UtcNow" => DateTimeOffset.UtcNow,
        _ when method.ReturnType == typeof(void) => null,
        _ when method.ReturnType == typeof(bool) => false,
        _ when method.ReturnType == typeof(int) => 0,
        _ when method.ReturnType == typeof(long) => 0L,
        _ => throw new NotSupportedException("Unexpected platform capability: "+method.Name)
    };
}
sealed class Owner(List<string> events,Action? callback=null) : IDisposable
{ public void Dispose(){events.Add("owner");callback?.Invoke();} }
sealed class TestFactory(TestRuntime runtime) : IManagedCodeRuntimeFactory
{ public IManagedCodeRuntime CreateRuntime(ManagedCodeRuntimeRequest request)=>runtime; }
sealed class TestRuntime(TestModule[] modules,List<string> events) : IManagedCodeRuntime
{
    public ManagedCodeRuntimeKind Kind=>ManagedCodeRuntimeKind.CoreClr;
    public bool IsLoaded {get; private set;}
    public BValueTask<ManagedCodeLoadResult> LoadAsync(ManagedCodeRelease release,BCancellationToken cancellationToken=default)
    {events.Add("load");IsLoaded=true;return new(new ManagedCodeLoadResult(modules,[],[]));}
    public BValueTask DisposeAsync(){events.Add("runtime");IsLoaded=false;return BValueTask.CompletedTask;}
}
sealed class TestModule(string id,List<string> events,string[]? dependencies=null) : IHotUpdateModule
{
    public bool FailStart {get;init;} public bool FailStop {get;init;} public BTask? Start {get;init;}
    public HotUpdateModuleDescriptor Descriptor {get;}=new(){ModuleId=id,Dependencies=dependencies??[]};
    public BValueTask ConfigureAsync(IHotUpdateModuleContext context,BCancellationToken token=default)
    {events.Add("configure:"+id);return BValueTask.CompletedTask;}
    public BValueTask StartAsync(BCancellationToken token=default)
    {events.Add("start:"+id);if(FailStart)throw new InvalidOperationException("start");return Start is null ? BValueTask.CompletedTask : new(Start);}
    public BValueTask StopAsync(BCancellationToken token=default)
    {events.Add("stop:"+id);if(FailStop)throw new InvalidOperationException("stop");return BValueTask.CompletedTask;}
}
