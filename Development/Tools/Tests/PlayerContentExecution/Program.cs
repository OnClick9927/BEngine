using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]);
var checks = new List<string>();
var diagnostics = new List<string>();
var observer = new PlayerRuntimeDiagnostics(failure: (phase, error) => diagnostics.Add(phase + ":" + error.Message));
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
void Drain(BTask operation)
{
    for (var i = 0; !operation.IsCompleted && i < 100; i++) BTaskScheduler.Main.Tick(TimeSpan.Zero);
    PlayerExecution.Complete(operation);
}
DotNetPlatformServices.Initialize();
var services = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
var workspace = RuntimeWorkspace.CreateRuntime(output, new RuntimeProjectData(), new RuntimeSettingsData(),
    services.GetRequiredService<FileUtility>(), services.GetRequiredService<PathUtility>());
Fixture Stage(string name) => new(name, observer);

// Teardown is ordered, idempotent, and remains safe when a release callback reenters.
var order = new List<string>();
using (var fixture = Stage("ordered"))
{
    var owner = new ProbeOwner(() => { Check(!fixture.Scene.isCreated, "Scene released before owner"); order.Add("owner"); });
    PlayerSceneStage? stage = null;
    BTask? reentered = null;
    stage = new PlayerSceneStage(fixture.Services, new SceneRuntimeFactory(), fixture.Manager, "ordered", () =>
    {
        Check(!fixture.Scene.isCreated, "Scene released before content");
        order.Add("content");
        reentered = stage!.DisposeAsync().AsBTask();
        return default;
    }, owner, observer);
    var disposal = stage.DisposeAsync().AsBTask();
    Drain(disposal);
    Drain(stage.DisposeAsync().AsBTask());
    Check(ReferenceEquals(disposal, reentered) && owner.Disposals == 1 && order.SequenceEqual(["content", "owner"]),
        "Reentrant stage disposal shares one operation and releases each owner once");
}
using (var fixture = Stage("failures"))
{
    var owner = new ProbeOwner(() => throw new IOException("owner"));
    var stage = new PlayerSceneStage(fixture.Services, new SceneRuntimeFactory(), fixture.Manager, "failure",
        () => BValueTask.FromException(new InvalidDataException("content")), owner, observer);
    try { Drain(stage.DisposeAsync().AsBTask()); throw new Exception("Expected teardown failure"); }
    catch (AggregateException failure)
    {
        Check(failure.InnerExceptions.Count == 2 && owner.Disposals == 1 && !fixture.Scene.isCreated,
            "Stage retains content and service failures after releasing scenes");
    }
}
using (var fixture = Stage("pending-release"))
{
    var release = new BTaskCompletionSource();
    var owner = new ProbeOwner();
    var stage = new PlayerSceneStage(fixture.Services, new SceneRuntimeFactory(), fixture.Manager, "pending",
        () => new BValueTask(release.Task), owner, observer);
    var operation = stage.DisposeAsync().AsBTask();
    Check(!operation.IsCompleted && owner.Disposals == 0 && !fixture.Scene.isCreated, "Pending content release defers owner disposal");
    release.SetResult(); Drain(operation);
    Check(owner.Disposals == 1, "Async content release completes before service owner");
}

long steadyBytes;
using (var fixture = Stage("plain"))
{
    var content = new Content(fixture.Stage);
    var owner = new ProbeOwner();
    var original = new ProbeOwner();
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(content), owner, original, diagnostics: observer);
    Check(!session.IsStartupPrepared && !session.IsStarted, "Prepared operation is not bound before Start");
    Throws<InvalidOperationException>(() => _ = session.Services, "Services unavailable before binding");
    session.Start();
    Check(session.IsStarted && session.IsStartupPrepared && session.CanRender && ReferenceEquals(session.ActiveScene, fixture.Scene),
        "Core starts custom content without a content package");
    Check(content.Commits == 1 && session.StartupControl is null && session.TransitionFlow is null, "Plain content commits once without a transition flow");
    session.Start();
    Check(content.Commits == 1, "Repeated Start does not commit twice");
    for (var i = 0; i < 100; i++) session.Tick(TimeSpan.Zero);
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) session.Tick(TimeSpan.Zero);
    steadyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(steadyBytes == 0, "1000 stable empty-scene frames allocate zero bytes");
    var frames = BTaskScheduler.Main.FrameCount;
    session.UpdateScenes(TimeSpan.Zero);
    Check(BTaskScheduler.Main.FrameCount == frames, "Native window scene update does not pump scheduler twice");
    Throws<ArgumentOutOfRangeException>(() => session.Tick(TimeSpan.FromTicks(-1)), "Negative frame rejected");
    session.Stop();
    Check(!session.IsStarted && !session.CanRender, "Stop clears running state");
    session.Dispose(); session.Dispose();
    Check(!session.IsDisposalPending && owner.Disposals == 1 && original.Disposals == 1 && content.Disposals == 1 && !fixture.Scene.isCreated,
        "Session owns transferred startup providers and content exactly once");
}
using (var fixture = Stage("delayed"))
{
    var completion = new BTaskCompletionSource<IPlayerRuntimeContent>();
    var content = new Content(fixture.Stage);
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(completion.Task), diagnostics: observer);
    session.Start(); session.Tick(TimeSpan.Zero);
    Check(!session.IsStartupPrepared && !session.CanRender, "Delayed preparation does not start an empty scene");
    completion.SetResult(content);
    for (var i = 0; i < 10 && !session.IsStartupPrepared; i++) session.Tick(TimeSpan.Zero);
    Check(session.IsStartupPrepared && session.CanRender && content.Commits == 1, "Delayed preparation binds and starts through Update");
}
using (var fixture = Stage("async-commit"))
{
    var commit = new BTaskCompletionSource();
    var content = new Content(fixture.Stage) { PendingCommit = commit.Task };
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(content), diagnostics: observer);
    session.Start(); session.Tick(TimeSpan.Zero);
    Check(content.Commits == 1 && session.IsStarted, "Async initial commit does not block or repeat");
    commit.SetResult(); session.Tick(TimeSpan.Zero);
    Check(content.Rollbacks == 0, "Async initial commit resumes through Update");
}
using (var fixture = Stage("async-failed-commit"))
{
    var commit = new BTaskCompletionSource();
    var content = new Content(fixture.Stage) { PendingCommit = commit.Task };
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(content), diagnostics: observer);
    session.Start(); commit.SetException(new IOException("async-commit"));
    Throws<IOException>(() => session.Tick(TimeSpan.Zero), "Async initial commit failure reaches host");
    Check(!session.IsStarted && !session.CanRender && content.Rollbacks == 1, "Async initial commit failure stops and rolls back once");
}
{
    BCancellationToken token = default;
    async BValueTask<IPlayerRuntimeContent> Never(BCancellationToken cancellation)
    {
        token = cancellation;
        await BTask.Delay(100000, cancellation);
        throw new InvalidOperationException("Should have canceled");
    }
    using var session = new PlayerRuntimeSession(workspace, services, Never, diagnostics: observer);
    session.Start(); session.Dispose();
    Check(token.IsCancellationRequested && !session.IsDisposalPending, "Shutdown cancels pending preparation and drains it");
}
{
    var owner = new ProbeOwner();
    using var session = new PlayerRuntimeSession(workspace, services,
        _ => BValueTask.FromException<IPlayerRuntimeContent>(new IOException("prepare")), owner, diagnostics: observer);
    Throws<IOException>(session.Start, "Preparation failure reaches caller");
    session.Dispose();
    Check(owner.Disposals == 1 && !session.IsDisposalPending, "Failed preparation still releases startup owner");
}

// Scene execution and compensated switching use the same Core session on all hosts.
foreach (var failCommit in new[] { false, true })
{
    using var first = Stage("initial");
    using var next = Stage("next");
    var content = new Content(first.Stage, next.Stage) { FailCommit = failCommit };
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(content), diagnostics: observer);
    session.Start();
    Check(content.Flow.Starts == 1 && content.Commits == 0, $"Initial stage starts policy before transition, fail={failCommit}");
    content.Flow.TransitionRequested = true;
    session.Tick(TimeSpan.Zero);
    if (failCommit)
    {
        Check(ReferenceEquals(session.ActiveScene, first.Scene) && session.CanRender && first.Scene.isCreated && !next.Scene.isCreated,
            "Commit failure restores initial scene and releases failed scene");
        Check(content.Rollbacks == 1 && content.Aborts == 1 && content.Flow.Failures == 1 && content.Releases == 0,
            "Commit failure rolls back policy and reports once");
    }
    else
    {
        Check(ReferenceEquals(session.ActiveScene, next.Scene) && session.CanRender && !first.Scene.isCreated && next.Scene.isCreated,
            "Successful transition switches scenes and releases previous scene");
        Check(content.Commits == 1 && content.Completions == 1 && content.Releases == 1 && content.Rollbacks == 0 && session.TransitionFlow is null,
            "Successful transition commits policy before releasing previous stage");
    }
    session.Dispose();
    Check(!next.Scene.isCreated && !first.Scene.isCreated && content.Disposals == 1, $"All stages released at shutdown, fail={failCommit}");
}
using (var first = Stage("pending-initial"))
using (var next = Stage("pending-next"))
{
    var prepared = new BTaskCompletionSource<PlayerSceneStage>();
    var content = new Content(first.Stage, next.Stage) { PendingNext = prepared.Task };
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(content), diagnostics: observer);
    session.Start(); content.Flow.TransitionRequested = true; session.Tick(TimeSpan.Zero);
    Check(ReferenceEquals(session.ActiveScene, first.Scene), "Pending transition keeps previous scene alive");
    prepared.SetResult(next.Stage);
    for (var i = 0; i < 10 && content.Releases == 0; i++) session.Tick(TimeSpan.Zero);
    Check(ReferenceEquals(session.ActiveScene, next.Scene) && content.Releases == 1, "Pending transition resumes without a blocking wait");
}
using (var fixture = Stage("borrowed-lifetime"))
using (var lifetime = HostRuntimeLifetime.Acquire())
{
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(new Content(fixture.Stage)),
        runtimeLifetime: lifetime, ownsRuntimeLifetime: false, diagnostics: observer);
    session.Start(); session.Dispose();
    lifetime.InitializeThrough(RuntimeInitializeLoadType.BeforeSceneLoad);
    Check(true, "Borrowed runtime lifetime remains usable after session disposal");
}
Check(!typeof(PlayerRuntimeSession).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
    .Any(a => a.AssemblyName == typeof(Content).Assembly.GetName().Name), "Probe uses no friend access");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.AssetBundles" or "BEngine.Graphics" or "BEngine.Rendering2D"),
    "Logic tests loaded no content or graphics package");
services.Dispose();
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new
{
    Passed = true, Count = checks.Count, Checks = checks, SteadyFrameBytes = steadyBytes, SteadyFrames = 1000, Diagnostics = diagnostics
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PLAYER_CONTENT_EXECUTION_OK|checks={checks.Count}|stableFrames=1000|bytes={steadyBytes}");

sealed class ProbeOwner(Action? action = null) : IDisposable
{
    public int Disposals;
    public void Dispose() { Disposals++; action?.Invoke(); }
}
sealed class Fixture : IDisposable
{
    public ServiceProvider Services { get; }
    public RuntimeSceneManager Manager { get; }
    public Scene Scene { get; }
    public PlayerSceneStage Stage { get; }
    public Fixture(string name, PlayerRuntimeDiagnostics observer)
    {
        Services = new ServiceCollection().AddBEngineDotNetPlatform()
            .AddSingleton<IRuntimeSceneManager, RuntimeSceneManager>().BuildServiceProvider();
        Manager = (RuntimeSceneManager)Services.GetRequiredService<IRuntimeSceneManager>();
        Scene = new Scene(name, Services);
        Manager.RegisterScene(Scene, true);
        Stage = new PlayerSceneStage(Services, new SceneRuntimeFactory(), Manager, name, serviceOwner: Services, diagnostics: observer);
    }
    public void Dispose() => PlayerExecution.Complete(Stage.DisposeAsync());
}
sealed class Flow : IPlayerStageTransitionFlow
{
    public bool TransitionRequested { get; set; }
    public int Starts, Failures;
    public void Start() => Starts++;
    public void Pump() { }
    public void ReportTransitionFailure(Exception failure) { Failures++; TransitionRequested = false; }
}
sealed class Content(PlayerSceneStage initial, PlayerSceneStage? next = null) : IPlayerRuntimeContent
{
    public PlayerSceneStage InitialStage => initial;
    public Flow Flow { get; } = new();
    public IPlayerStageTransitionFlow? TransitionFlow => next is null ? null : Flow;
    public BTask<PlayerSceneStage>? PendingNext;
    public BTask? PendingCommit;
    public bool FailCommit;
    public int Commits, Rollbacks, Aborts, Completions, Releases, Disposals;
    public BValueTask CommitStartupAsync(BCancellationToken cancellationToken = default)
    {
        Commits++;
        return FailCommit ? BValueTask.FromException(new IOException("commit")) : PendingCommit is null ? default : new(PendingCommit);
    }
    public BValueTask RollbackStartupAsync(Exception failure, BCancellationToken cancellationToken = default) { Rollbacks++; return default; }
    public BValueTask<PlayerSceneStage> PrepareTransitionAsync(BCancellationToken cancellationToken = default) =>
        PendingNext is null ? new(next!) : new(PendingNext);
    public PlayerSceneStage CommitTransition(PlayerSceneStage stage) { Completions++; Flow.TransitionRequested = false; return initial; }
    public BValueTask AbortTransitionAsync(PlayerSceneStage stage) { Aborts++; return stage.DisposeAsync(); }
    public BValueTask ReleasePreviousStageAsync(PlayerSceneStage stage) { Releases++; return stage.DisposeAsync(); }
    public async BValueTask DisposeAsync()
    {
        if (Disposals++ != 0) throw new InvalidOperationException("Repeated content release");
        if (next is not null) await next.DisposeAsync();
        await initial.DisposeAsync();
    }
}
