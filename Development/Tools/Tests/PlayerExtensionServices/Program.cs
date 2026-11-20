using System.Reflection;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.Player.HotUpdate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var checks = new List<string>();
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks.Add(name);
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}
var canceled = new BTaskCompletionSource();
canceled.SetCanceled();
var failure = new IOException("test failure");
PlayerExecution.Complete(BTask.CompletedTask);
PlayerExecution.Complete(BValueTask.CompletedTask);
Check(PlayerExecution.Complete(BTask.FromResult(17)) == 17, "Completed generic BTask result");
Check(PlayerExecution.Complete(BValueTask.FromResult(23)) == 23, "Completed generic BValueTask result");
Throws<BOperationCanceledException>(() => PlayerExecution.Complete(canceled.Task), "Completed cancellation");
Throws<IOException>(() => PlayerExecution.Complete(BTask.FromException(failure)), "Completed failure");
Throws<IOException>(() => PlayerExecution.Complete(BValueTask.FromException<int>(failure)), "Generic value failure");
var pending = new BTaskCompletionSource();
var pendingValue = new BTaskCompletionSource<int>();
var frameBefore = BTaskScheduler.Main.FrameCount;
Throws<InvalidOperationException>(() => PlayerExecution.Complete(pending.Task), "Pending BTask rejected");
Throws<InvalidOperationException>(() => PlayerExecution.Complete(new BValueTask(pending.Task)), "Pending value rejected");
Throws<InvalidOperationException>(() => PlayerExecution.Complete(pendingValue.Task), "Pending generic task rejected");
Throws<InvalidOperationException>(() => PlayerExecution.Complete(new BValueTask<int>(pendingValue.Task)), "Pending generic value rejected");
Check(frameBefore == BTaskScheduler.Main.FrameCount && !pending.IsCompleted, "Completion never pumps scheduler");
Throws<ArgumentNullException>(() => PlayerExecution.Complete((BTask)null!), "Null task rejected");

var failures = new List<Exception>();
var updates = 0;
var shutdown = new BTaskCompletionSource();
Check(PlayerExecution.TryCompleteShutdown(shutdown.Task, delta =>
{
    if (delta != TimeSpan.Zero) throw new InvalidOperationException("Unexpected delta");
    if (++updates == 3) shutdown.SetResult();
}, "probe", failures.Add, 3) && updates == 3 && failures.Count == 0, "Shutdown uses owner Update");
updates = 0;
Check(!PlayerExecution.TryCompleteShutdown(pending.Task, _ => updates++, "probe", failures.Add, 4)
      && updates == 4 && failures.Single() is TimeoutException, "Shutdown bounded timeout");
failures.Clear();
Check(!PlayerExecution.TryCompleteShutdown(pending.Task, _ => throw failure, "probe", failures.Add)
      && ReferenceEquals(failures.Single().InnerException, failure), "Shutdown Update failure retained");
failures.Clear();
Check(PlayerExecution.TryCompleteShutdown(BTask.FromException(failure), _ => throw new Exception(), "probe", failures.Add)
      && ReferenceEquals(failures.Single(), failure), "Completed shutdown failure reported once");
failures.Clear();
Check(PlayerExecution.TryCompleteShutdown(canceled.Task, _ => throw new Exception(), "probe", failures.Add)
      && failures.Count == 0, "Canceled shutdown consumed");
Throws<ArgumentOutOfRangeException>(() => PlayerExecution.TryCompleteShutdown(pending.Task, _ => { }, "probe", failures.Add, 0), "Invalid shutdown bound");

var sequence = new List<string>();
BValueTask Commit() { sequence.Add("commit"); return default; }
BValueTask Rollback(Exception error) { sequence.Add("rollback"); return default; }
var transition = PlayerExecution.RunTransitionAsync(() => sequence.Add("start"), Commit,
    () => sequence.Add("complete"), Rollback, () => sequence.Add("restore"));
Check(PlayerExecution.Complete(transition) is null && sequence.SequenceEqual(["start", "commit", "complete"]), "Transition success order");
sequence.Clear();
transition = PlayerExecution.RunTransitionAsync(() => { sequence.Add("start"); throw failure; }, Commit,
    () => sequence.Add("complete"), Rollback, () => sequence.Add("restore"));
Check(ReferenceEquals(PlayerExecution.Complete(transition), failure)
      && sequence.SequenceEqual(["start", "rollback", "restore"]), "Activation failure rolls back");
sequence.Clear();
transition = PlayerExecution.RunTransitionAsync(() => sequence.Add("start"), () => BValueTask.FromException(failure),
    () => sequence.Add("complete"), Rollback, () => sequence.Add("restore"));
Check(ReferenceEquals(PlayerExecution.Complete(transition), failure)
      && sequence.SequenceEqual(["start", "rollback", "restore"]), "Commit failure rolls back");
var rollbackError = new InvalidDataException("rollback");
var restoreError = new InvalidOperationException("restore");
transition = PlayerExecution.RunTransitionAsync(() => throw failure, Commit, () => { },
    _ => BValueTask.FromException(rollbackError), () => throw restoreError);
Check(PlayerExecution.Complete(transition) is AggregateException aggregate
      && aggregate.InnerExceptions.SequenceEqual([failure, rollbackError, restoreError]), "All transition failures retained");
var commitPending = new BTaskCompletionSource();
sequence.Clear();
transition = PlayerExecution.RunTransitionAsync(() => sequence.Add("start"), () => new BValueTask(commitPending.Task),
    () => sequence.Add("complete"), Rollback, () => sequence.Add("restore"));
Check(!transition.IsCompleted && sequence.SequenceEqual(["start"]), "Asynchronous transition remains pending");
commitPending.SetResult();
for (int i = 0; i < 5 && !transition.IsCompleted; i++) BTaskScheduler.Main.Tick(TimeSpan.Zero);
Check(PlayerExecution.Complete(transition) is null && sequence.SequenceEqual(["start", "complete"]), "Asynchronous commit resumes through scheduler");

var paths = new ProbePaths();
var owningCollection = new ServiceCollection().AddBEngineDotNetPlatform();
owningCollection.Replace(ServiceDescriptor.Singleton<IApplicationPaths>(_ => paths));
using (var owner = owningCollection.BuildServiceProvider())
{
    _ = owner.GetRequiredService<IApplicationPaths>();
    var child = new ServiceCollection().AddBEngineDotNetPlatform();
    child.Replace(ServiceDescriptor.Singleton(new ApplicationPathUtility(paths)));
    var persistent = Path.Combine(Path.GetDirectoryName(output)!, "NotCreated", "Persistent");
    var configured = PlayerHostServices.ConfigurePersistentDataDirectory(child, persistent + Path.DirectorySeparatorChar, owner);
    using (var provider = child.BuildServiceProvider())
    {
        var scoped = provider.GetRequiredService<IApplicationPaths>();
        Check(configured == persistent && scoped.GetPersistentDataDirectory("a", "b") == persistent, "Persistent override normalized");
        Check(provider.GetRequiredService<ApplicationPathUtility>().GetPersistentDataDirectory("c", "d") == persistent, "Materialized path facade replaced");
        Check(scoped.BaseDirectory == paths.BaseDirectory && scoped.TemporaryDirectory == paths.TemporaryDirectory, "Other host paths preserved");
        Check(!Directory.Exists(persistent), "Configuration creates no directories");
    }
    Check(paths.Disposals == 0, "Borrowed host service not disposed by child");
    Throws<InvalidDataException>(() => PlayerHostServices.ConfigurePersistentDataDirectory(child, "relative/path", owner), "Relative persistent path rejected");
}
Check(paths.Disposals == 1, "Owner disposes custom path service once");

foreach (var background in new[] { false, true })
{
    var writer = new ProbeWriter();
    var fallback = new List<string>();
    var sink = PlayerHostServices.CreateBufferedLogWriter(writer, fallback.Add, background, 2, 8, 5);
    for (var i = 0; i < 200; i++) sink.Write(i.ToString(), i == 100);
    sink.Flush();
    Check(writer.Lines.SequenceEqual(Enumerable.Range(0, 200).Select(i => i.ToString())) && fallback.Count == 0,
        $"Log order and bounded saturation, background={background}");
    sink.Dispose();
    sink.Dispose();
    sink.Write("ignored");
    sink.Flush();
    Check(writer.Disposals == 1 && writer.Lines.Count == 200, $"Writer ownership and idempotent disposal, background={background}");
}
var broken = new ProbeWriter { Fail = true };
var recovered = new List<string>();
using (var sink = PlayerHostServices.CreateBufferedLogWriter(broken, recovered.Add, false)) sink.Write("recover me");
Check(recovered.Contains("recover me"), "Writer failure falls back");
var invalidWriter = new ProbeWriter();
Throws<ArgumentOutOfRangeException>(() => PlayerHostServices.CreateBufferedLogWriter(invalidWriter, _ => { }, false, 0), "Invalid log capacity");
Check(invalidWriter.Disposals == 0, "Failed construction does not take ownership");

Module[] modules = [new("z", ["B"]), new("b", ["A"]), new("a", []), new("standalone", [])];
Module[] Order(IEnumerable<Module> values) => ManagedCodeServices.OrderModules(values, v => v.Name, v => v.Dependencies);
Check(Order(modules).Select(m => m.Name).SequenceEqual(["a", "b", "standalone", "z"]), "Deterministic case insensitive module ordering");
Check(Order(modules.Reverse()).Select(m => m.Name).SequenceEqual(Order(modules).Select(m => m.Name)), "Module input permutation stable");
Throws<InvalidDataException>(() => Order([new("a", ["missing"])]), "Missing module rejected");
Throws<InvalidDataException>(() => Order([new("a", ["b"]), new("b", ["a"])]), "Module cycle rejected");
Throws<ArgumentException>(() => Order([new("a", []), new("A", [])]), "Duplicate module rejected");
Throws<ArgumentNullException>(() => ManagedCodeServices.ReleaseRegistrations(null!), "Null cleanup collection rejected");
ManagedCodeServices.ReleaseRegistrations(Array.Empty<Assembly>());
Check(true, "Empty cleanup safe");
var victim = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CleanupVictim"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
var cleanups = 0;
RuntimeAssemblyCleanup.Register(assembly => { if (assembly == victim) cleanups++; });
ManagedCodeServices.ReleaseRegistrations([victim, victim]);
Check(cleanups == 1, "Unleased registrations released once");
var failingVictim = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FailingCleanupVictim"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
RuntimeAssemblyCleanup.Register(assembly => { if (assembly == failingVictim) throw failure; });
Throws<AggregateException>(() => ManagedCodeServices.ReleaseRegistrations([failingVictim]), "Registration cleanup failures propagated");

PlayerStartupPerformanceDiagnostics.Enabled = false;
PlayerStartupPerformanceDiagnostics.Reset();
MeasureCompletedPath();
MeasureCompletedPath();
var allocated = MeasureCompletedPath();
Console.WriteLine($"MEASURE|completed-and-disabled|iterations=100000|bytes={allocated}");
Check(allocated == 0, "Completed task facade and disabled sampling allocate zero bytes");
PlayerStartupPerformanceDiagnostics.Enabled = true;
var outer = PlayerStartupPerformanceDiagnostics.Begin("outer");
var inner = PlayerStartupPerformanceDiagnostics.Begin("inner");
inner.Dispose(); outer.Dispose(); outer.Dispose();
var samples = PlayerStartupPerformanceDiagnostics.Snapshot;
Check(samples.Length == 2 && samples[1].ParentId == samples[0].Id && samples.All(s => s.Milliseconds >= 0), "Nested sampling and idempotent completion");
var old = PlayerStartupPerformanceDiagnostics.Begin("old");
PlayerStartupPerformanceDiagnostics.Reset();
var current = PlayerStartupPerformanceDiagnostics.Begin("current");
old.Dispose();
Check(PlayerStartupPerformanceDiagnostics.Snapshot.Length == 0, "Old generation cannot complete new sample");
current.Dispose();
Check(PlayerStartupPerformanceDiagnostics.Snapshot.Single().Name == "current", "Current generation remains intact");
Check(typeof(PlayerStartupPerformanceDiagnostics.Scope).GetConstructors().Length == 0, "Sampling tokens cannot be forged publicly");
PlayerStartupPerformanceDiagnostics.Reset();
for (var i = 0; i < 200; i++)
{
    using var sample = PlayerStartupPerformanceDiagnostics.Begin("bounded");
}
Check(PlayerStartupPerformanceDiagnostics.Snapshot.Length == 128, "Startup sample storage remains bounded");
PlayerStartupPerformanceDiagnostics.Enabled = false;
PlayerStartupPerformanceDiagnostics.Reset();
var friends = typeof(PlayerExecution).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>();
Check(!friends.Any(f => f.AssemblyName == typeof(ProbePaths).Assembly.GetName().Name), "Probe has no friend access");
var packageNames = File.ReadAllLines(args[1]).ToHashSet(StringComparer.Ordinal);
foreach (var foundation in new[] { typeof(PlayerExecution).Assembly, typeof(DotNetPlatformServices).Assembly })
{
    Check(!foundation.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
        .Any(f => packageNames.Contains(f.AssemblyName)), foundation.GetName().Name + " grants no package internal access");
}

File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, AllocatedBytes = allocated, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: {checks.Count} public Player extension checks; allocation={allocated}");

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static long MeasureCompletedPath()
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100000; i++)
    {
        PlayerExecution.Complete(BTask.CompletedTask);
        using var sample = PlayerStartupPerformanceDiagnostics.Begin("disabled");
    }
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

record Module(string Name, string[] Dependencies);
sealed class ProbePaths : IApplicationPaths, IDisposable
{
    public int Disposals;
    public string BaseDirectory => "borrowed-base";
    public string CurrentDirectory => "borrowed-current";
    public string TemporaryDirectory => "borrowed-temp";
    public string? ProcessPath => "borrowed-process";
    public string GetPersistentDataDirectory(string company, string product) => "original";
    public void Dispose() => Disposals++;
}
sealed class ProbeWriter : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;
    public readonly List<string> Lines = [];
    public int Disposals;
    public bool Fail;
    public override void WriteLine(string? value)
    {
        if (Fail) throw new IOException("writer failed");
        Lines.Add(value!);
    }
    protected override void Dispose(bool disposing) => Disposals++;
}
