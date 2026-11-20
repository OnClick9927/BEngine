using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;
using Debug = BEngine.Debug;

BEngine.Platform.DotNet.DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var platform = DispatchProxy.Create<IPlatformService, ClockPlatform>();
var clock = (ClockPlatform)(object)platform;
clock.Inner = EditorServices.GetRequiredService<IPlatformService>();
using var provider = new ServiceCollection().AddSingleton(new PlatformUtility(platform)).BuildServiceProvider();
using var scope = EditorServiceScope.Attach(provider);
var checks = new List<string>();
void Check(string name, bool ok) { if (!ok) throw new Exception(name); checks.Add(name); }
void Throws<T>(string name, Action operation) where T : Exception
{
    try { operation(); } catch (T) { Check(name, true); return; }
    throw new Exception(name);
}
var previousHandler = Debug.logger.logHandler; var previousIsolation = EditorFeatureIsolation.Current;
var log = new LogProbe(); Debug.logger.logHandler = log;
try
{
    EditorFeatureIsolation.Current = null;
    var called = 0;
    Check("Headless callback succeeds", EditorCallbacks.Invoke("headless", () => called++));
    Check("Headless callback executes once", called == 1);
    Throws<ArgumentException>("Empty feature rejected", () => EditorCallbacks.Invoke("", static () => { }));
    Throws<ArgumentNullException>("Null callback rejected", () => EditorCallbacks.Invoke("null", null!));
    Throws<ArgumentException>("Empty report feature rejected", () => EditorCallbacks.Report("", new Exception()));
    Throws<ArgumentNullException>("Null reported exception rejected", () => EditorCallbacks.Report("report", null!));
    Throws<ArgumentException>("Invalid loop scope rejected", () => EditorCallbacks.BeginLoopScope(""));
    EditorCallbacks.Dispatch((Action?)null, "null"); EditorCallbacks.Dispatch<int>(null, 1, "null");
    EditorCallbacks.Dispatch<int,int>(null, 1, 2, "null"); EditorCallbacks.Dispatch<int,int,int>(null, 1, 2, 3, "null");
    Check("Null notifications do no work", called == 1 && log.Exceptions.Count == 0);
    var isolation = new IsolationProbe(); EditorFeatureIsolation.Current = isolation;
    Check("Isolated callback succeeds", EditorCallbacks.Invoke("isolated", static () => { }));
    Check("Successful callback restores UI state", isolation.Restored.Last() == (0, true) && isolation.Depth == 0);
    Check("Ordinary callback failure is contained", !EditorCallbacks.Invoke("failure", static () => throw new InvalidOperationException("failure")));
    Check("Failed callback restores unsuccessful UI state", isolation.Restored.Last() == (0, false) && isolation.Depth == 0);
    Check("Failure is logged with feature identity", log.Exceptions.Last().Message.Contains("failure") && log.Exceptions.Last().InnerException is InvalidOperationException);
    Check("Result callback succeeds", EditorCallbacks.TryInvoke("result", static () => 13, -1, out var value) && value == 13);
    Check("Result callback uses supplied fallback", !EditorCallbacks.TryInvoke<int>("result-failure", static () => throw new Exception("bad"), 91, out value) && value == 91);
    Check("State action executes", EditorCallbacks.Invoke("state-action", 7, n => called += n) && called == 8);
    Check("State action failure is contained", !EditorCallbacks.Invoke("state-failure", 7, static n => throw new Exception(n.ToString())));
    Check("State result preserves values", EditorCallbacks.TryInvoke("state-result", 6, static n => n * 3, -1, out value) && value == 18);
    Check("State result fallback is preserved", !EditorCallbacks.TryInvoke<int,int>("state-result-failure", 6, static n => throw new Exception(n.ToString()), 23, out value) && value == 23);
    Check("Owner-labelled invocation succeeds", EditorCallbacks.Invoke(typeof(LogProbe), "owned", static () => { }));
    Check("Null owner supports a generic identity", EditorCallbacks.Invoke(null, "owned", static () => { }));
    var depth = 0;
    Check("Nested invocation succeeds", EditorCallbacks.Invoke("outer", () =>
    {
        depth = isolation.Depth;
        Check("Inner invocation executes within nested isolation", EditorCallbacks.Invoke("inner", () => depth += isolation.Depth));
    }));
    Check("Nested isolation unwinds in order", depth == 3 && isolation.Depth == 0);
    var replacement = new IsolationProbe(); var restoredBefore = isolation.Restored.Count;
    EditorCallbacks.Invoke("backend-swap", () => EditorFeatureIsolation.Current = replacement);
    Check("Callback restores the backend it entered", isolation.Restored.Count == restoredBefore + 1 && replacement.Restored.Count == 0);
    EditorFeatureIsolation.Current = isolation;
    Throws<EditorCallbackExitException>("Exit signal propagates from invocation", () => EditorCallbacks.Invoke("exit", static () => throw new EditorCallbackExitException()));
    Check("Exit still restores unsuccessful UI state", isolation.Depth == 0 && isolation.Restored.Last() == (0, false));
    Throws<EditorCallbackExitException>("Exit propagates from result callback", () => EditorCallbacks.TryInvoke<int>("result-exit", static () => throw new EditorCallbackExitException(), 7, out _));
    Throws<EditorCallbackExitException>("Exit propagates from state action", () => EditorCallbacks.Invoke("state-exit", 2, static _ => throw new EditorCallbackExitException()));
    Throws<EditorCallbackExitException>("Exit propagates from state result", () => EditorCallbacks.TryInvoke<int,int>("state-result-exit", 2, static _ => throw new EditorCallbackExitException(), 7, out _));
    var sequence = new List<int>();
    Action actions = () => sequence.Add(1); actions += static () => throw new Exception("dispatch-failure");
    actions += static () => throw new EditorCallbackExitException(); actions += () => sequence.Add(4);
    EditorCallbacks.Dispatch(actions, "zero-argument");
    Check("Notification failures and exit do not skip other handlers", sequence.SequenceEqual([1,4]));
    Action<int> one = static _ => throw new Exception("one"); one += n => sequence.Add(n);
    EditorCallbacks.Dispatch(one, 5, "one-argument");
    Action<int,string> two = static (_,_) => throw new EditorCallbackExitException(); two += (n,s) => sequence.Add(n+s.Length);
    EditorCallbacks.Dispatch(two, 7, "abc", "two-arguments");
    Action<int,string,int> three = static (_,_,_) => throw new Exception("three"); three += (n,s,m) => sequence.Add(n+s.Length+m);
    EditorCallbacks.Dispatch(three, 8, "x", 3, "three-arguments");
    Check("All notification arities preserve argument order", sequence.SequenceEqual([1,4,5,10,12]));
    Check("Notification isolation remains balanced", isolation.Depth == 0);
    var firstHandler = (Action)(static () => throw new InvalidOperationException("same"));
    var secondHandler = (Action)(static () => throw new InvalidOperationException("same"));
    var logStart = log.Exceptions.Count;
    EditorCallbacks.Dispatch(firstHandler + secondHandler, "handler-identity");
    Check("Different handlers retain separate fault identities", log.Exceptions.Count == logStart+2 && log.Exceptions[^1].Message != log.Exceptions[^2].Message);
    var m=firstHandler.Method;
    Check("Handler identity preserves MVID and metadata token", log.Exceptions[^2].Message.Contains($"[{m.Module.ModuleVersionId:N}:{m.MetadataToken}]"));
    EditorCallbacks.Dispatch(firstHandler + secondHandler, "handler-identity");
    Check("Repeated identical handler failures are throttled", log.Exceptions.Count == logStart+2);
    logStart = log.Exceptions.Count;
    var error = new TargetInvocationException(new InvalidOperationException("underlying"));
    EditorCallbacks.Report("throttle", error); EditorCallbacks.Report("throttle", error);
    Check("Reported invocation errors unwrap their cause", log.Exceptions.Last().InnerException is InvalidOperationException);
    Check("Repeated external reports are throttled", log.Exceptions.Count == logStart+1);
    clock.Ticks += 4999; EditorCallbacks.Report("throttle", error);
    Check("Throttle interval remains five seconds", log.Exceptions.Count == logStart+1);
    clock.Ticks++; EditorCallbacks.Report("throttle", error);
    Check("Failure is logged again when throttle expires", log.Exceptions.Count == logStart+2);
    EditorCallbacks.Invoke("throttle", static () => { }); EditorCallbacks.Report("throttle", error);
    Check("Successful retry clears only its feature fault", log.Exceptions.Count == logStart+3);
    log.Throw = true;
    EditorCallbacks.Report("broken-logger", new Exception("expected logging failure"));
    log.Throw = false;
    Check("Logger failures do not escape report", true);
    var stopped = new List<LoopExecutionLimitException>(); Action<LoopExecutionLimitException> listener=stopped.Add;
    EditorCallbacks.LoopExecutionStopped += listener;
    try
    {
        Check("Loop overflow is contained", !EditorCallbacks.Invoke("loop", Loop));
        Check("Loop notification carries source and iteration limit", stopped.Count==1 && stopped[0].SourcePath=="callback-contract.cs" && stopped[0].Line==17 && stopped[0].IterationLimit==1000);
        EditorCallbacks.Invoke("loop", Loop);
        Check("Repeated loop alerts remain deduplicated", stopped.Count==1);
    }
    finally { EditorCallbacks.LoopExecutionStopped -= listener; }
    EditorCallbacks.Invoke("unsubscribed-loop", Loop);
    Check("Loop listener can unsubscribe", stopped.Count==1);
    var threw = false;
    using (EditorCallbacks.BeginLoopScope("manual-scope"))
    {
        try { Loop(); } catch (LoopExecutionLimitException) { threw=true; }
    }
    Check("Explicit loop scope protects unwrapped work", threw);
    Loop(); Check("Disposed scope does not affect later unguarded work", true);
    Check("Core execution needs no graphics or editor packages", !AppDomain.CurrentDomain.GetAssemblies().Any(a=>new[]{"BEngine.Core.Editor","BEngine.EditorWorkbench.Editor","BEngine.IMGUI.Editor","BEngine.Graphics","BEngine.Rendering2D"}.Contains(a.GetName().Name)));
    File.WriteAllText(args[0], JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("EDITOR_CALLBACK_CORE_OK|"+checks.Count);
}
finally { Debug.logger.logHandler=previousHandler; EditorFeatureIsolation.Current=previousIsolation; }
static void Loop()
{
    var counter = new LoopExecutionGuard.Counter();
    for(var i=0;i<=LoopExecutionGuard.DefaultIterationLimit;i++) LoopExecutionGuard.Checkpoint(ref counter,"callback-contract.cs",17);
}
public class ClockPlatform : DispatchProxy
{
    public IPlatformService Inner=null!; public long Ticks=10000;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name=="get_TickCount64"?Ticks:method.Invoke(Inner,args);
}
public sealed class LogProbe : ILogHandler
{
    public List<Exception> Exceptions=[]; public bool Throw;
    public void LogFormat(LogType type,BObject? context,string format,params object?[] args) { }
    public void LogException(Exception exception,BObject? context) { if(Throw)throw new IOException("logger failure"); Exceptions.Add(exception); }
}
public sealed class IsolationProbe : IEditorFeatureIsolation
{
    public int Depth; public List<(int Token,bool Success)> Restored=[];
    public int Begin()=>Depth++;
    public void Restore(int token,bool succeeded)
    {
        if(token!=Depth-1)throw new Exception("Unbalanced isolation");
        Depth=token; Restored.Add((token,succeeded));
    }
}
