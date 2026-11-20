using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;
using Debug = BEngine.Debug;

BEngine.Platform.DotNet.DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var dialogs=new DialogProbe();
using var services=new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform>(dialogs).BuildServiceProvider();
using var attached=EditorServiceScope.Attach(services);
var checks=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);}
var assembly=typeof(EditorApplication).Assembly;
var bridge=assembly.GetType("BEngine.Editor.EditorBridge",true)!;
// Fixture-only host injection. Tested callback and cleanup operations use public contracts.
var host=DispatchProxy.Create(assembly.GetType("BEngine.Editor.IEditorHost",true)!,typeof(HostProbe));
var state=(HostProbe)host;
bridge.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);
var originalLog=Debug.logger.logHandler;var originalIsolation=EditorFeatureIsolation.Current;
Debug.logger.logHandler=new CapturedLog();
try
{
    EditorFeatureIsolation.Current=null;
    Check("Default workbench is not loaded initially", !AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BEngine.EditorWorkbench.Editor"));
    Check("Editor Services callback facade is active", EditorApplication.isPlaying && !EditorApplication.isPaused);
    Check("Headless loop failure is contained", !EditorCallbacks.Invoke("headless-services",Loop));
    Check("Editor Services does not display default loop dialogs", dialogs.Count==0);
    Check("Editor Services does not choose Play Mode pause policy", !state.Paused);
    var workbench=Assembly.Load("BEngine.EditorWorkbench.Editor");
    RuntimeHelpers.RunModuleConstructor(workbench.ManifestModule.ModuleHandle);
    // Presentation-state isolation is covered by the independent Core fixture and actual Editor.
    // This fixture has no GUI frame and exercises only workbench notification policy.
    EditorFeatureIsolation.Current=null;
    Check("Workbench loop failure is contained", !EditorCallbacks.Invoke("with-workbench",Loop));
    Check("Default workbench pauses playing host", state.Paused && EditorApplication.isPaused);
    Check("Default workbench displays exactly one dialog", dialogs.Count==1);
    Check("Loop dialog retains diagnostic source and limit", dialogs.Message.Contains("callback-workflow.cs:23") && dialogs.Message.Contains("1000") && dialogs.Title=="Script Execution Stopped");
    EditorApplication.isPaused=false;
    EditorCallbacks.Invoke("with-workbench",Loop);
    Check("Repeated alert remains deduplicated", dialogs.Count==1 && !state.Paused);
    state.Playing=false;
    EditorCallbacks.Invoke("not-playing",Loop);
    Check("Stopped editor still displays a fresh loop failure", dialogs.Count==2);
    Check("Stopped editor is not paused", !state.Paused);
    RuntimeAssemblyCleanup.UnregisterExtensions(workbench);
    state.Playing=true;
    EditorCallbacks.Invoke("after-workbench-unload",Loop);
    Check("Workbench cleanup removes dialog policy", dialogs.Count==2);
    Check("Workbench cleanup removes pause policy", !state.Paused);
    RuntimeAssemblyCleanup.UnregisterExtensions(workbench);
    Check("Repeated workbench cleanup is safe", true);
    Check("Loop guard still functions after workbench cleanup", !EditorCallbacks.Invoke("still-protected",Loop));
    File.WriteAllText(args[0],JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("EDITOR_CALLBACK_WORKFLOW_OK|"+checks.Count);
}
finally
{
    EditorApplication.isPaused=false;
    bridge.GetMethod("Detach",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[host]);
    Debug.logger.logHandler=originalLog;EditorFeatureIsolation.Current=originalIsolation;
}
static void Loop()
{
    var counter=new LoopExecutionGuard.Counter();
    for(var i=0;i<=LoopExecutionGuard.DefaultIterationLimit;i++)LoopExecutionGuard.Checkpoint(ref counter,"callback-workflow.cs",23);
}
public class HostProbe : DispatchProxy
{
    public bool Playing=true,Paused;
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch
    {
        "get_IsPlaying"=>Playing,"set_IsPlaying"=>SetPlaying((bool)args![0]!),
        "get_IsPaused"=>Paused,"set_IsPaused"=>SetPaused((bool)args![0]!),
        "get_IsChangingPlayMode"=>false,"get_TaskScheduler"=>null,
        _=>throw new NotSupportedException(method.Name)
    };
    private object? SetPlaying(bool value){Playing=value;return null;}
    private object? SetPaused(bool value){Paused=value;return null;}
}
public sealed class DialogProbe : IEditorUtilityPlatform
{
    public int Count;public string Title="",Message="";
    public int DisplayDialog(string title,string message,IReadOnlyList<string> buttons){Count++;Title=title;Message=message;return 0;}
    public string OpenFilePanel(string title,string directory,string filter)=>throw new NotSupportedException();
    public string OpenFolderPanel(string title,string folder,string defaultName)=>throw new NotSupportedException();
    public void OpenWithDefaultApp(string target)=>throw new NotSupportedException();
    public void ShowProgress(EditorProgressInfo progress,Action? requestCancellation)=>throw new NotSupportedException();
    public void ClearProgress()=>throw new NotSupportedException();
}
public sealed class CapturedLog : ILogHandler
{
    public void LogFormat(LogType type,BObject? context,string format,params object?[] args){}
    public void LogException(Exception exception,BObject? context){}
}
