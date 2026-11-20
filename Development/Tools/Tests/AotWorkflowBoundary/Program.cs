using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.AssetBundles;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Player;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
#if APPLICATION
using BEngine.App.Startup;
#endif

var output = Path.GetFullPath(args[0]);
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks.Add(name);
}
Check(new[] { typeof(RuntimeAssemblyAliasAttribute), typeof(RuntimeTypeAliasAttribute), typeof(PreserveNativeCodeAttribute),
        typeof(BEngine.Hosting.RuntimeNativeExecutionAttribute) }
        .All(type => type.Assembly.GetName().Name == "BEngine.Core" &&
            Type.GetType($"{type.FullName}, BEngine.Core") == type),
    "Runtime identity and AOT metadata belong to Attributes and preserve old assembly names");
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}
DotNetPlatformServices.Initialize();
using var services = new ServiceCollection().AddBEngineDotNetPlatform().BuildServiceProvider();
var platform = services.GetRequiredService<PlatformUtility>();
var context = new AssetBundleStartupContext(_ => throw new NotSupportedException(),
    (_, _, _) => throw new NotSupportedException(), _ => new((true, "")), true, static _ => null);
var core = typeof(IPlayerStageTransitionFlow).Assembly;
Check(!core.GetReferencedAssemblies().Any(a => a.Name?.StartsWith("BEngine.App", StringComparison.Ordinal) == true || a.Name == "BEngine.AssetBundles"),
    "Core stage execution has no Application or AssetBundles assembly reference");
Check(!core.GetTypes().Any(t => t.Name.StartsWith("AotStartup", StringComparison.Ordinal) || t.Name == "IAotStartupFlow"),
    "Default AOT UI protocol is absent from Core");
Check(!typeof(AssetBundleStartupContext).Assembly.GetReferencedAssemblies().Any(a => a.Name == "BEngine.Application"),
    "AB has no reverse Application reference");
Check(!typeof(AssetBundleStartupContext).Assembly.GetTypes().Any(t => t.Name == "PlayerAotStartupFlow"),
    "AB no longer implements the default game policy");
Throws<ArgumentNullException>(() => AssetBundleStartupWorkflows.Register(null!), "Null factory rejected");
Throws<ArgumentNullException>(() => AssetBundleStartupWorkflows.Create(null!), "Null context rejected");
using (var first = AssetBundleStartupWorkflows.Register(_ => new ProbeWorkflow("first"), 100))
{
    Check(((ProbeWorkflow)AssetBundleStartupWorkflows.Create(context)).Name == "first", "Registered custom workflow selected");
    using (AssetBundleStartupWorkflows.Register(_ => new ProbeWorkflow("lower"), 99))
        Check(((ProbeWorkflow)AssetBundleStartupWorkflows.Create(context)).Name == "first", "Lower priority does not override active workflow");
    using (AssetBundleStartupWorkflows.Register(_ => new ProbeWorkflow("last"), 100))
        Check(((ProbeWorkflow)AssetBundleStartupWorkflows.Create(context)).Name == "last", "Latest equal priority registration wins");
    Check(((ProbeWorkflow)AssetBundleStartupWorkflows.Create(context)).Name == "first", "Disposal restores previous factory");
    using (AssetBundleStartupWorkflows.Register(_ => null!, 101))
        Throws<InvalidOperationException>(() => AssetBundleStartupWorkflows.Create(context), "Null workflow rejected");
    first.Dispose(); first.Dispose();
}
using (AssetBundleStartupWorkflows.Register(_ => new ProbeWorkflow("unloaded"), 100))
{
    RuntimeAssemblyCleanup.UnregisterExtensions(typeof(ProbeWorkflow).Assembly);
    using var normal = AssetBundleStartupWorkflows.Register(_ => new ProbeWorkflow("normal"));
    Check(((ProbeWorkflow)AssetBundleStartupWorkflows.Create(context)).Name == "normal", "Unloaded owner factory no longer shadows replacements");
}

#if APPLICATION
var noUpdates = Plan(false);
var updates = Plan(true);
AssetBundleStartupContext Context(
    Func<BCancellationToken, BValueTask<AssetBundleUpdatePlan>>? check = null,
    Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, BCancellationToken, BValueTask>? apply = null,
    Func<BCancellationToken, BValueTask<(bool IsValid, string Error)>>? validate = null,
    bool fail = true, Func<string, string?>? readOption = null) => new(check ?? (_ => new(noUpdates)), apply ?? ((_, _, _) => default),
        validate ?? (_ => new((true, ""))), fail, readOption ?? (static _ => null), new PlayerRuntimeDiagnostics(failure: (_, _) => { }));
void Pump(IPlayerStageTransitionFlow flow) { BTaskScheduler.Main.Tick(TimeSpan.Zero); flow.Pump(); }
Check(typeof(IAotStartupFlow).Assembly == typeof(AotStartupFlow).Assembly, "Workflow contract and implementation belong to Application");
Check(RuntimeTypeCache.FindType("BEngine.Player.Startup.AotStartupPhase, BEngine.Player") == typeof(AotStartupPhase),
    "Legacy serialized phase identity resolves through the owning package");
Check(RuntimeTypeCache.FindType("BEngine.Player.Startup.AotStartupSnapshot, BEngine.Player") == typeof(AotStartupSnapshot),
    "Legacy serialized snapshot identity resolves through the owning package");
using (var registered = AssetBundleStartupWorkflows.Create(Context()))
    Check(registered is AotStartupFlow, "Application registers the default workflow through the AB extension point");
using (var flow = new AotStartupFlow(Context()))
{
    var stage = (IPlayerStageTransitionFlow)flow;
    var host = stage.StartupControl!;
    var observed = new List<AotStartupSnapshot>();
    flow.Changed += observed.Add;
    stage.Start(); stage.Start(); Pump(stage);
    Check(flow.Current.CanCheckForUpdates && observed.Count == 1, "Idempotent Start publishes the waiting state once");
    var borrowed = new ServiceCollection(); flow.ConfigureServices(borrowed);
    using (var provider = borrowed.BuildServiceProvider())
        Check(ReferenceEquals(provider.GetRequiredService<IAotStartupFlow>(), flow), "AOT scene receives the same borrowed flow service");
    flow.EnterGame();
    Check(!stage.TransitionRequested, "Game cannot start before successful validation");
    host.Advance(); Pump(stage);
    Check(flow.Current.Phase == AotStartupPhase.Ready && flow.Current.Progress == 1 && host.Current.CanAdvance,
        "No-update path validates installed content and reaches full progress");
    Check(observed.Any(s => s.Phase == AotStartupPhase.CheckingForUpdates), "Checking state remains observable to AOT UI");
    Check(!host.Current.IsFailure && host.Current.Phase == "Ready", "Host sees neutral state without interpreting AOT phases");
    Read(host); Read(host);
    var before = GC.GetAllocatedBytesForCurrentThread(); var sum = Read(host);
    Check(sum == 100000 && GC.GetAllocatedBytesForCurrentThread() == before, "100000 host state reads allocate zero bytes");
    host.Advance(); Check(stage.TransitionRequested, "Neutral host advancement requests the Core stage transition");
    stage.ReportTransitionFailure(new IOException("stage failed")); Pump(stage);
    Check(!stage.TransitionRequested && host.Current.IsFailure && flow.Current.Error == "stage failed",
        "Transition failure invalidates readiness and supports retry");
    flow.Retry(); Pump(stage);
    Check(flow.Current.CanEnterGame, "Retry revalidates content after transition failure");
}
using (var flow = new AotStartupFlow(Context(check: _ => new(updates))))
{
    var stage = (IPlayerStageTransitionFlow)flow; stage.Start(); Pump(stage);
    flow.CheckForUpdates(); Pump(stage);
    Check(flow.Current.RequiresUpdateConfirmation && flow.Current.CanDeclineUpdate && !flow.Current.CanEnterGame,
        "Update confirmation preserves installed-content deferral policy");
    stage.StartupControl!.Advance(preferExistingContent: true); Pump(stage);
    Check(flow.Current.CanEnterGame && flow.Current.Phase == AotStartupPhase.Ready, "Optional update may be deferred to valid installed content");
}
var applies = 0;
using (var flow = new AotStartupFlow(Context(check: _ => new(updates),
    apply: (_, p, _) => { applies++; p.Report(new(AssetBundleUpdatePhase.Downloading, 0, 1, 50, 100));
        p.Report(new(AssetBundleUpdatePhase.Verifying, 1, 1, 100, 100)); return default; })))
{
    var stage = (IPlayerStageTransitionFlow)flow; var history = new List<AotStartupSnapshot>(); flow.Changed += history.Add;
    stage.Start(); Pump(stage); flow.CheckForUpdates(); flow.CheckForUpdates(); Pump(stage);
    stage.StartupControl!.Advance(); flow.ConfirmUpdate(); Pump(stage);
    Check(applies == 1 && flow.Current.CanEnterGame, "Repeated commands cannot apply the same update twice");
    Check(history.Any(s => s.Phase == AotStartupPhase.Downloading && s.CompletedBytes == 50 && s.TotalBytes == 100),
        "Download byte counts and intermediate progress survive migration");
    Check(history.Any(s => s.Phase == AotStartupPhase.Verifying) && flow.Current.Progress == 1,
        "Verification and completion phases remain available to UI");
}
using (var flow = new AotStartupFlow(Context(check: _ => new(updates), validate: _ => new((false, "missing")))))
{
    var stage = (IPlayerStageTransitionFlow)flow; stage.Start(); Pump(stage); flow.CheckForUpdates(); Pump(stage);
    Check(!flow.Current.CanDeclineUpdate, "Missing installed content forbids deferring a required update");
    flow.DeclineUpdate(); Pump(stage);
    Check(flow.Current.RequiresUpdateConfirmation, "Forbidden decline preserves pending update");
    stage.StartupControl!.Advance(true); Pump(stage);
    Check(flow.Current.Phase == AotStartupPhase.GameContentMissing && stage.StartupControl.Current.IsFailure,
        "Host preference cannot bypass required update or missing post-update game content");
}
using (var flow = new AotStartupFlow(Context(validate: _ => new((false, "no game")))))
{
    var stage = (IPlayerStageTransitionFlow)flow; stage.Start(); Pump(stage); flow.CheckForUpdates(); Pump(stage);
    Check(flow.Current.CanRetry && !flow.Current.CanEnterGame && flow.Current.Error == "no game", "No-update missing content exposes an actionable failure");
}
foreach (var fail in new[] { true, false })
using (var flow = new AotStartupFlow(Context(check: _ => throw new IOException("offline"), fail: fail)))
{
    var stage = (IPlayerStageTransitionFlow)flow; stage.Start(); Pump(stage); flow.CheckForUpdates(); Pump(stage);
    Check(flow.Current.CanEnterGame == !fail && stage.StartupControl!.Current.IsFailure == fail,
        "Update failure " + (fail ? "stops when configured" : "falls back only after local validation"));
}
using (var flow = new AotStartupFlow(Context(check: _ => throw new IOException("offline"),
    validate: _ => new((false, "damaged")), fail: false)))
{
    var stage = (IPlayerStageTransitionFlow)flow; stage.Start(); Pump(stage); flow.CheckForUpdates(); Pump(stage);
    Check(!flow.Current.CanEnterGame && flow.Current.Error == "offline", "Fallback does not accept damaged local content");
}
using (var flow = new AotStartupFlow(Context(check: _ => new(updates), apply: (_, _, _) => throw new IOException("download failed"))))
{
    var stage = (IPlayerStageTransitionFlow)flow; stage.Start(); Pump(stage); flow.CheckForUpdates(); Pump(stage);
    flow.ConfirmUpdate(); Pump(stage);
    Check(flow.Current.Phase == AotStartupPhase.Failed && flow.Current.Error == "download failed", "Apply failure remains retryable");
}
var pending = new BTaskCompletionSource(); var canceled = false; var notifications = 0;
async BValueTask<AssetBundleUpdatePlan> CheckPending(BCancellationToken token)
{
    using var registration = token.Register(() => canceled = true);
    await pending.Task; token.ThrowIfCancellationRequested(); return noUpdates;
}
var disposed = new AotStartupFlow(Context(check: CheckPending));
var disposableStage = (IPlayerStageTransitionFlow)disposed; disposableStage.Start(); Pump(disposableStage);
disposed.CheckForUpdates(); disposed.Changed += _ => notifications++;
disposed.Dispose(); disposed.Dispose(); pending.SetResult(); BTaskScheduler.Main.Tick(TimeSpan.Zero);
Check(canceled && notifications == 0, "Disposal cancels in-flight BTask work and stops scene notifications");
Throws<ObjectDisposedException>(() => disposableStage.Pump(), "Disposed workflow cannot pump");
Throws<ObjectDisposedException>(disposed.CheckForUpdates, "Disposed workflow cannot restart");
foreach (var preferExisting in new[] { true, false })
{
    var autoApplies = 0;
    using var flow = new AotStartupFlow(Context(check: _ => new(updates),
        apply: (_, _, _) => { autoApplies++; return default; },
        readOption: name => name == "BENGINE_AOT_AUTO_DECLINE_UPDATE" ? (preferExisting ? "1" : null) : "1"));
    var stage = (IPlayerStageTransitionFlow)flow;
    stage.Start(); for (var i = 0; i < 4; i++) Pump(stage);
    Check(stage.TransitionRequested && autoApplies == (preferExisting ? 0 : 1),
        "Automation compatibility: check, " + (preferExisting ? "defer" : "confirm") + " and continue");
}
// Exercise the actual Core session with the Application policy, rather than testing their halves only.
using (var flow = new AotStartupFlow(Context()))
using (var initial = new StageFixture("aot", flow))
using (var game = new StageFixture("game"))
{
    var workspace = RuntimeWorkspace.CreateRuntime(output, new RuntimeProjectData(), new RuntimeSettingsData(),
        services.GetRequiredService<FileUtility>(), services.GetRequiredService<PathUtility>());
    var content = new WorkflowContent(initial.Stage, game.Stage, flow);
    using var session = new PlayerRuntimeSession(workspace, services, _ => new(content));
    session.Start(); session.Tick(TimeSpan.Zero);
    Check(ReferenceEquals(session.StartupControl, flow) && ReferenceEquals(session.ActiveScene, initial.Scene),
        "Core session exposes the Application host control while running the AOT scene");
    session.StartupControl!.Advance(); session.Tick(TimeSpan.Zero);
    Check(flow.Current.CanEnterGame && content.Transitions == 0 && ReferenceEquals(session.ActiveScene, initial.Scene),
        "Content readiness alone cannot bypass the explicit transition request");
    session.StartupControl.Advance();
    for (var i = 0; i < 4; i++) session.Tick(TimeSpan.Zero);
    Check(content.Transitions == 1 && content.Releases == 1 && session.TransitionFlow is null &&
        ReferenceEquals(session.ActiveScene, game.Scene) && !initial.Scene.isCreated,
        "Application request drives exactly one Core transition and releases the old stage");
    session.Stop(); session.Dispose();
    Check(!game.Scene.isCreated && content.Disposals == 1 && !session.CanRender,
        "Core shutdown releases the content stages and their borrowed workflow exactly once");
}
#else
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BEngine.Application"), "Content extension works with no Application assembly loaded");
Throws<InvalidOperationException>(() => AssetBundleStartupWorkflows.Create(context), "AB requires a caller-provided policy instead of imposing a game workflow");
#endif

Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} AOT workflow boundary checks");

#if APPLICATION
[MethodImpl(MethodImplOptions.NoInlining)]
static int Read(IPlayerStartupControl control)
{
    var count = 0;
    for (var i = 0; i < 100000; i++) if (control.Current.CanAdvance) count++;
    return count;
}
static AssetBundleUpdatePlan Plan(bool updates)
{
    var hash = new string('a', 64);
    var bundle = new AssetBundleDescriptor { Name = "game", Sha256 = hash, FileName = hash + ".bassetbundle", Size = 100 };
    var catalog = new AssetBundleCatalog { PackageName = "test", Version = "1.0.0", Bundles = [bundle] };
    var bytes = AssetBundleCatalogSerializer.SerializeCatalog(catalog);
    var version = new AssetBundleVersion { PackageName = "test", Version = "1.0.0", CatalogSha256 = Convert.ToHexString(SHA256.HashData(bytes)), CatalogSize = bytes.Length };
    // The manager normally produces this immutable plan; keep its construction API internal.
    return (AssetBundleUpdatePlan)typeof(AssetBundleUpdatePlan).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke(
        [version, catalog, bytes, updates ? new[] { bundle } : Array.Empty<AssetBundleDescriptor>(), updates, null]);
}
#endif
sealed class ProbeWorkflow(string name) : IAssetBundleStartupWorkflow
{
    public string Name => name;
    public bool TransitionRequested => false;
    public void Start() { }
    public void Pump() { }
    public void ReportTransitionFailure(Exception failure) { }
    public void ConfigureServices(IServiceCollection services) { }
    public void Dispose() { }
}
#if APPLICATION
sealed class StageFixture : IDisposable
{
    public Scene Scene { get; }
    public PlayerSceneStage Stage { get; }
    public StageFixture(string name, AotStartupFlow? flow = null)
    {
        var collection = new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton<IRuntimeSceneManager, RuntimeSceneManager>();
        flow?.ConfigureServices(collection);
        var provider = collection.BuildServiceProvider();
        var manager = provider.GetRequiredService<IRuntimeSceneManager>();
        Scene = new Scene(name, provider); manager.RegisterScene(Scene, true);
        Stage = new PlayerSceneStage(provider, new SceneRuntimeFactory(), manager, name, serviceOwner: provider);
    }
    public void Dispose() => PlayerExecution.Complete(Stage.DisposeAsync());
}
sealed class WorkflowContent(PlayerSceneStage initial, PlayerSceneStage game, AotStartupFlow flow) : IPlayerRuntimeContent
{
    public int Transitions, Releases, Disposals;
    public PlayerSceneStage InitialStage => initial;
    public IPlayerStageTransitionFlow TransitionFlow => flow;
    public BValueTask<PlayerSceneStage> PrepareTransitionAsync(BCancellationToken token = default) => new(game);
    public PlayerSceneStage CommitTransition(PlayerSceneStage next) { Transitions++; return initial; }
    public BValueTask ReleasePreviousStageAsync(PlayerSceneStage previous) { Releases++; return previous.DisposeAsync(); }
    public async BValueTask DisposeAsync()
    {
        Disposals++;
        try { await initial.DisposeAsync(); await game.DisposeAsync(); }
        finally { flow.Dispose(); }
    }
}
#endif
