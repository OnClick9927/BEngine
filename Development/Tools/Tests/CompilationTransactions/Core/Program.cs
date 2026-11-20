using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.Editor;
using BEngine.Platform;

BEngine.Platform.DotNet.DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
var previousLog = Debug.logger.logHandler; var previousIsolation = EditorFeatureIsolation.Current;
var log = new CapturedLog(); Debug.logger.logHandler = log; EditorFeatureIsolation.Current = null;
try
{
    Check(!CompilationPipeline.isCompiling, "Core-only process starts idle");
    Throws<ArgumentNullException>(() => CompilationPipeline.BeginCompilation(null!), "Null contexts are rejected without a transaction");
    Check(!CompilationPipeline.isCompiling, "Argument failure keeps idle state");
    Check(!typeof(CompilationPipeline).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == typeof(CapturedLog).Assembly.GetName().Name), "Consumer has no friend grant");
    var context = new object(); var notifications = new List<(string Event, object Context, int Thread, bool Active)>();
    Action<object> started = value => notifications.Add(("start", value, Environment.CurrentManagedThreadId, CompilationPipeline.isCompiling));
    Action<object> finished = value => notifications.Add(("finish", value, Environment.CurrentManagedThreadId, CompilationPipeline.isCompiling));
    CompilationPipeline.compilationStarted += started; CompilationPipeline.compilationFinished += finished;
    try
    {
        var scope = CompilationPipeline.BeginCompilation(context);
        Check(CompilationPipeline.isCompiling && notifications.Count == 1, "Begin sets state and emits one start");
        Check(ReferenceEquals(notifications[0].Context, context) && notifications[0].Active, "Start sees the actual context and compiling state");
        Throws<InvalidOperationException>(() => CompilationPipeline.BeginCompilation(context), "Nested transactions cannot replace the active owner");
        Task.Run(() => Throws<InvalidOperationException>(() => CompilationPipeline.BeginCompilation(new object()), "Concurrent transactions cannot steal active ownership")).GetAwaiter().GetResult();
        Task.Run(() => Throws<InvalidOperationException>(scope.Dispose, "Workers cannot end editor-thread notification scopes")).GetAwaiter().GetResult();
        Check(notifications.Count == 1 && CompilationPipeline.isCompiling, "Rejected callers emit no finish or extra start");
        var visible = Task.Run(() => CompilationPipeline.isCompiling).GetAwaiter().GetResult();
        Check(visible, "Worker readers see active state");
        scope.Dispose();
        Check(!CompilationPipeline.isCompiling && notifications.Count == 2, "Dispose emits one finish then returns to idle");
        Check(notifications[1].Active && ReferenceEquals(notifications[1].Context, context), "Finish sees matching context while still compiling");
        Check(notifications.All(n => n.Thread == Environment.CurrentManagedThreadId), "Both callbacks run on the owning editor thread");
        scope.Dispose(); Check(notifications.Count == 2, "Duplicate disposal never emits a duplicate finish");
        var next = CompilationPipeline.BeginCompilation(new object()); scope.Dispose();
        Check(CompilationPipeline.isCompiling && notifications.Count == 3, "A stale handle cannot end a later transaction");
        next.Dispose(); Check(notifications.Count == 4, "A later transaction completes normally");
        try { using var failing = CompilationPipeline.BeginCompilation(new object()); throw new IOException("compile-failure"); }
        catch (IOException) { }
        Check(!CompilationPipeline.isCompiling && notifications.Count == 6, "Compiler failure still balances state and callbacks");
        try { using var cancelled = CompilationPipeline.BeginCompilation(new object()); throw new OperationCanceledException(); }
        catch (OperationCanceledException) { }
        Check(!CompilationPipeline.isCompiling && notifications.Count == 8, "Cancellation still balances state and callbacks");
    }
    finally { CompilationPipeline.compilationStarted -= started; CompilationPipeline.compilationFinished -= finished; }

    var tailCalls = 0;
    Action<object> badStart = _ => throw new IOException("start-listener-failure");
    Action<object> exitStart = _ => throw new EditorCallbackExitException();
    Action<object> badFinish = _ => throw new IOException("finish-listener-failure");
    Action<object> tail = _ => tailCalls++;
    CompilationPipeline.compilationStarted += badStart; CompilationPipeline.compilationStarted += exitStart; CompilationPipeline.compilationStarted += tail;
    CompilationPipeline.compilationFinished += badFinish; CompilationPipeline.compilationFinished += tail;
    try
    {
        using (CompilationPipeline.BeginCompilation(new object())) Check(tailCalls == 1, "Failing start subscribers do not skip later subscribers");
        Check(tailCalls == 2 && !CompilationPipeline.isCompiling, "Failing finish subscribers cannot leave compiling state set");
        Check(log.Errors.Count == 2, "Ordinary subscriber failures are reported and exit signals are contained");
    }
    finally
    {
        CompilationPipeline.compilationStarted -= badStart; CompilationPipeline.compilationStarted -= exitStart; CompilationPipeline.compilationStarted -= tail;
        CompilationPipeline.compilationFinished -= badFinish; CompilationPipeline.compilationFinished -= tail;
    }

    CompilationPipeline.CompilationScope? reentrant = null; var finishes = 0;
    Action<object> reenter = _ => { finishes++; reentrant!.Dispose(); Throws<InvalidOperationException>(() => CompilationPipeline.BeginCompilation(new object()), "Finish callbacks cannot interleave another compilation"); };
    CompilationPipeline.compilationFinished += reenter;
    try { reentrant = CompilationPipeline.BeginCompilation(new object()); reentrant.Dispose(); Check(finishes == 1 && !CompilationPipeline.isCompiling, "Reentrant completion does not recurse or notify twice"); }
    finally { CompilationPipeline.compilationFinished -= reenter; }

    Action<object> noop = _ => { };
    var isolation = new BrokenIsolation(); EditorFeatureIsolation.Current = isolation;
    CompilationPipeline.compilationStarted += noop; CompilationPipeline.compilationFinished += noop;
    try
    {
        isolation.FailBegin = true;
        Throws<AggregateException>(() => CompilationPipeline.BeginCompilation(new object()), "Infrastructure failure during both notifications preserves both errors");
        Check(!CompilationPipeline.isCompiling, "Failed start infrastructure releases global ownership");
        isolation.FailBegin = false;
        var scope = CompilationPipeline.BeginCompilation(new object()); isolation.FailRestore = true;
        Throws<IOException>(scope.Dispose, "Finish infrastructure failure remains observable");
        Check(!CompilationPipeline.isCompiling, "Finish infrastructure failure still releases ownership");
        scope.Dispose(); Check(!CompilationPipeline.isCompiling, "Faulted finish remains idempotent");
        isolation.FailRestore = false;
    }
    finally { CompilationPipeline.compilationStarted -= noop; CompilationPipeline.compilationFinished -= noop; EditorFeatureIsolation.Current = null; }
    var retained = Capture();
    for (var i = 0; i < 8 && retained.Context.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    Check(!retained.Context.IsAlive, "Retained disposed scope releases project context objects");
    GC.KeepAlive(retained.Scope);
    using (CompilationPipeline.BeginCompilation(new object())) Check(CompilationPipeline.isCompiling, "All failure paths permit later compilation");
    Check(!CompilationPipeline.isCompiling, "Final scope leaves the process idle");
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => new[]{"BEngine.Core.Editor", "BEngine.EditorWorkbench.Editor", "BEngine.EditorWorkbench.Host", "BEngine.IMGUI.Editor", "BEngine.Graphics", "BEngine.Rendering2D", "BEngine.Application"}.Contains(a.GetName().Name)), "Transaction mechanism needs no workbench, graphics, UI or game-framework package");
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"COMPILATION_TRANSACTION_CORE_OK {checks.Count}");
}
finally { Debug.logger.logHandler = previousLog; EditorFeatureIsolation.Current = previousIsolation; }

[MethodImpl(MethodImplOptions.NoInlining)]
static (CompilationPipeline.CompilationScope Scope, WeakReference Context) Capture()
{
    var context = new object(); var weak = new WeakReference(context);
    var scope = CompilationPipeline.BeginCompilation(context); scope.Dispose(); return (scope, weak);
}
sealed class CapturedLog : ILogHandler
{
    public List<Exception> Errors = [];
    public void LogFormat(LogType type, BObject? context, string format, params object?[] args) { }
    public void LogException(Exception exception, BObject? context) => Errors.Add(exception);
}
sealed class BrokenIsolation : IEditorFeatureIsolation
{
    public bool FailBegin, FailRestore;
    public int Begin() { if (FailBegin) throw new IOException("begin-infrastructure"); return 0; }
    public void Restore(int token, bool succeeded) { if (FailRestore) throw new IOException("restore-infrastructure"); }
}
