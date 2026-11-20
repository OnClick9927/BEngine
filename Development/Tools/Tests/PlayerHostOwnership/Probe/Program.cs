using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.Build;
using BEngine.Hosting;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

var root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
DotNetPlatformServices.Initialize();
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject<T>(Action action,string name) where T:Exception
{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
var target=BuildTargetUtility.Current.InferCurrentDesktop();
BuildTargetManifestSerializer.SetCurrent(BuildTargetManifestSerializer.Serialize(BuildTargetManifest.FromDescriptor(target)));
var contributions=new LateContributions();
RuntimeServiceContributions.Register(contributions.First);
var contributed=new ServiceCollection();RuntimeServiceContributions.Configure(contributed);
Check(contributions.FirstCalls==1&&contributions.SecondCalls==1&&contributions.ThirdCalls==1,"Lazy prerequisite contributions complete in the same outer pass");
Check(contributed.Any(d=>d.ServiceType==typeof(LateContributions)),"A recursively requested prerequisite reaches the target collection");
Check(contributions.Reentries==1,"Nested configuration of the same collection does not recurse");
RuntimeServiceContributions.Configure(new ServiceCollection());
Check(contributions.FirstCalls==2&&contributions.SecondCalls==2&&contributions.ThirdCalls==2,"Registrations execute once per collection despite self-registration");
RuntimeServiceContributions.UnregisterAssembly(typeof(LateContributions).Assembly);
var retry=new RetryContribution();RuntimeServiceContributions.Register(retry.Configure);
Reject<IOException>(()=>RuntimeServiceContributions.Configure(contributed),"A failing contribution keeps its failure visible");
RuntimeServiceContributions.Configure(contributed);
Check(retry.Calls==2,"A failed pass releases its reentry guard for retry");
RuntimeServiceContributions.UnregisterAssembly(typeof(LateContributions).Assembly);
var workflow=new TestWorkflow(root);var host=new PlayerHost(workflow);
Check(host.ResolveDefaultProjectPath(root)==root,"Core resolves an independently supplied workspace");
using(var session=host.CreateRuntimeSession(root,persistentDataPath:Path.Combine(root,"Save")))
{
    session.Start();for(var i=0;!session.CanRender&&i<100;i++)session.Tick(TimeSpan.FromMilliseconds(16));
    Check(session.CanRender&&session.ActiveScene?.name=="Independent content","Core executes a scene supplied without AB or a graphics package");
    Check(ReferenceEquals(session.Services.GetService(typeof(IHostOutput)),workflow.Output),"Host services reach the supplied scene");
    session.Stop();
}
Check(workflow.Events.Contains("content-dispose"),"Core releases its prepared content");

workflow.Events.Clear();
Check(host.Run("--validate",root)==0,"Core command-line validation runs independent content");
Check(workflow.Output.Lines.Any(l=>l.StartsWith("BENGINE_PLAYER_VALIDATION_OK|")),"Validation preserves the host result protocol");
Check(workflow.Events.IndexOf("startup-resource")>=0&&workflow.Events.IndexOf("resource-dispose")>workflow.Events.IndexOf("content-dispose"),"Startup resources outlive validated content");
Check(workflow.Events.IndexOf("log-dispose")>workflow.Events.IndexOf("resource-dispose"),"Startup log outlives content resources");
Check(!workflow.Events.Contains("window-create"),"Validation does not create a window or GPU");

workflow.Events.Clear();
Check(host.Run(root)==0,"Core runs the window host with an injected window capability");
Check(workflow.Events.Contains("window-run")&&workflow.Events.Contains("window-dispose"),"Core owns window execution and teardown");
Check(workflow.Events.IndexOf("window-dispose")<workflow.Events.IndexOf("resource-dispose"),"Window scope ends before startup resources");

workflow.Events.Clear();workflow.FailOpen=true;
Check(host.Run(root)==1,"Workspace failure returns a failed host result");
Check(workflow.Events.Contains("resource-dispose")&&workflow.Events.Contains("log-dispose"),"Workspace failure releases earlier startup leases");
workflow.FailOpen=false;
workflow.Events.Clear();workflow.FailDiagnostics=true;
Check(host.Run(root)==1,"A startup-log failure returns a failed host result");
Check(workflow.Events.Contains("fallback-log")&&workflow.Events.Contains("resource-dispose"),"Fallback logging does not leak startup resources");
workflow.FailDiagnostics=false;

PlayerHostRegistry.RegisterContentWorkflow(workflow);
Check(PlayerHostRegistry.Current.GetType()==typeof(PlayerHost),"Default registry entry is the Core host, not a package host implementation");
Check(BEnginePlayer.ResolveDefaultProjectPath(root)==root,"Existing engine entry points consume the registered content workflow");
PlayerHostRegistry.Unregister(typeof(PlayerHost).Assembly);
Check(PlayerHostRegistry.Current is PlayerHost,"Unregistering Core does not incorrectly retire a content-owned registration");
PlayerHostRegistry.Unregister(workflow.GetType().Assembly);
Reject<InvalidOperationException>(()=>_ =PlayerHostRegistry.Current,"Unregistering a contributing module clears its workflow and Core host");

workflow.Events.Clear();workflow.FailResourceDispose=true;
Reject<AggregateException>(()=>host.Run(root),"Startup cleanup surfaces its aggregated failure");
Check(workflow.Events.Contains("resource-dispose")&&workflow.Events.Contains("log-dispose"),"A failing resource release does not skip log shutdown");
workflow.FailResourceDispose=false;
workflow.Events.Clear();workflow.FailResourceDispose=true;workflow.FailLogDispose=true;
try { host.Run(root);throw new Exception("Expected cleanup failures"); }
catch(AggregateException error)
{
    Check(error.InnerExceptions.Count==2&&error.InnerExceptions.All(e=>e is IOException),"Host retains both resource and log cleanup failures");
}
workflow.FailResourceDispose=false;workflow.FailLogDispose=false;

var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToArray();
Check(!loaded.Any(n=>n is "BEngine.AssetBundles" or "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.Application"),
    "Independent host loads no content, rendering or application-framework package");
File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks,LoadedAssemblies=loaded},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("CORE_PLAYER_HOST_OK|"+checks.Count);

sealed class TestWorkflow(string root):IPlayerContentWorkflow
{
    public readonly List<string> Events=[];public readonly Output Output=new();
    public bool FailOpen,FailDiagnostics,FailResourceDispose,FailLogDispose;
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPlayerStartupResolver>(new Resolver(root));services.AddSingleton<IHostOutput>(Output);
        services.AddSingleton<INativeLibraryLoader>(new NoNative());
        services.AddSingleton<IHostWindowFactory>(new WindowFactory(Events));
        services.AddSingleton<IHostRenderContextFactory>(DispatchProxy.Create<IHostRenderContextFactory, UnusedCapability>());
        services.AddSingleton<IHostInputContextFactory>(DispatchProxy.Create<IHostInputContextFactory, UnusedCapability>());
    }
    public RuntimeWorkspace OpenWorkspace(string path,IServiceProvider services)
    {
        Events.Add("workspace");if(FailOpen)throw new InvalidDataException("workspace");
        return RuntimeWorkspace.CreateRuntime(path,new RuntimeProjectData(),new RuntimeSettingsData(),
            services.GetRequiredService<FileUtility>(),services.GetRequiredService<PathUtility>());
    }
    public IDisposable? ActivateStartupResources(PlayerStartupDescriptor startup,IServiceProvider services)
    {Events.Add("startup-resource");return new Lease(()=>{Events.Add("resource-dispose");if(FailResourceDispose)throw new IOException("resource");});}
    public IPlayerStartupLog StartDiagnostics(PlayerStartupDescriptor startup,string? persistentDataPath)
    {if(FailDiagnostics)throw new IOException("log");Events.Add("startup-log");return CreateLog();}
    public IPlayerStartupLog StartFailureDiagnostics(string root){Events.Add("fallback-log");return CreateLog();}
    private IPlayerStartupLog CreateLog()=>new Log(()=>{Events.Add("log-dispose");if(FailLogDispose)throw new IOException("log-dispose");});
    public PlayerRuntimeDiagnostics Diagnostics {get;}=new(failure:(_,_)=>{});
    public BValueTask<IPlayerRuntimeContent> PrepareAsync(RuntimeWorkspace workspace,string? persistentDataPath,
        IReadOnlyList<ServiceDescriptor> hostServices,IServiceProvider startupServices,BCancellationToken token)
    {
        token.ThrowIfCancellationRequested();Events.Add("prepare");
        var services=new ServiceCollection();foreach(var descriptor in hostServices)((ICollection<ServiceDescriptor>)services).Add(descriptor);
        services.AddSingleton<IRuntimeSceneManager,RuntimeSceneManager>();var provider=services.BuildServiceProvider();
        var manager=provider.GetRequiredService<IRuntimeSceneManager>();manager.RegisterScene(new Scene("Independent content",provider),true);
        var stage=new PlayerSceneStage(provider,new SceneRuntimeFactory(),manager,"independent",serviceOwner:provider);
        return new(new Content(stage,Events));
    }
}
sealed class Content(PlayerSceneStage stage,List<string> events):IPlayerRuntimeContent
{public PlayerSceneStage InitialStage=>stage;public BValueTask DisposeAsync(){events.Add("content-dispose");return stage.DisposeAsync();}}
sealed class Resolver(string root):IPlayerStartupResolver
{public string BaseDirectory=>root;public string? PersistentDataPath=>null;public PlayerStartupDescriptor Resolve(IReadOnlyList<string> arguments)=>new(root,root,null);public string ResolveDefaultProjectPath(string? directory=null)=>root;}
sealed class Output:IHostOutput
{public readonly List<string> Lines=[];public void WriteLine(string line)=>Lines.Add(line);public void WriteErrorLine(string line)=>Lines.Add(line);}
sealed class NoNative:INativeLibraryLoader
{public void Initialize(string directory,bool reportDiagnostics=false){}public nint ResolveImport(string name,Assembly assembly,System.Runtime.InteropServices.DllImportSearchPath? path)=>0;}
sealed class Lease(Action release):IDisposable{public void Dispose()=>release();}
sealed class Log(Action release):IPlayerStartupLog{public string LogPath=>"test-log";public void Dispose()=>release();}
public class UnusedCapability:DispatchProxy
{protected override object? Invoke(MethodInfo? method,object?[]? arguments)=>throw new Exception("Unexpected native capability: "+method!.Name);}
sealed class LateContributions
{
    public int FirstCalls,SecondCalls,ThirdCalls,Reentries;
    public void First(IServiceCollection services)
    {
        FirstCalls++;RuntimeServiceContributions.Register(Second);
        RuntimeServiceContributions.Register(First);
        RuntimeServiceContributions.Configure(services);Reentries++;
    }
    public void Second(IServiceCollection services){SecondCalls++;RuntimeServiceContributions.Register(Third);}
    public void Third(IServiceCollection services){ThirdCalls++;services.AddSingleton(this);}
}
sealed class RetryContribution
{public int Calls;public void Configure(IServiceCollection services){if(++Calls==1)throw new IOException("contribution");}}
sealed class WindowFactory(List<string> events):IHostWindowFactory
{public IHostWindow Create(HostWindowOptions options){events.Add("window-create");return new Window(events);}}
#pragma warning disable CS0067
sealed class Window(List<string> events):IHostWindow
{
    public event Action? Loaded,Closing;public event Action<double>? Updating,Rendering;public event Action<bool>? FocusChanged;
    public HostWindowSize Size=>new(800,600);public HostWindowSize FramebufferSize=>Size;public bool IsFullscreen=>false;public bool VSync=>false;
    public void Run()=>events.Add("window-run");public void Close(){}public void Dispose()=>events.Add("window-dispose");
}
