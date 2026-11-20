using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using BEngine.AssetBundles;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]);
var root = Path.Combine(output, "Run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(root, "Hub"));
DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
var presentation = new Presentation();
using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
    .AddSingleton<IEditorUtilityPlatform>(presentation).BuildServiceProvider();
using var attachment = EditorServiceScope.Attach(services);
var editorAssembly = typeof(EditorApplication).Assembly;
var bridge = editorAssembly.GetType("BEngine.Editor.EditorBridge", true)!;
var host = DispatchProxy.Create(editorAssembly.GetType("BEngine.Editor.IEditorHost", true)!, typeof(Host));
var scheduler = ((Host)host).Scheduler;
bridge.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
var command = typeof(AssetBundleBuilder).Assembly.GetType("BEngine.Editor.AssetBundleMenuCommands", true)!;
var finish = command.GetMethod("FinishBuild", BindingFlags.Static | BindingFlags.NonPublic)!;
var observe = command.GetMethod("ObserveBuildAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
var start = command.GetMethod("StartBuild", BindingFlags.Static | BindingFlags.NonPublic)!;
var active = command.GetField("_activeBuild", BindingFlags.Static | BindingFlags.NonPublic)!;
var notices = new List<string>(); var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
Action<string> started = path => notices.Add("start:" + path);
Action<string, bool> finished = (path, success) => notices.Add("finish:" + path + ":" + success);
EditorBuildEvents.buildStarted += started; EditorBuildEvents.buildFinished += finished;
try
{
    var project = Path.Combine(root, "Project"); Directory.CreateDirectory(Path.Combine(project, "Assets"));
    File.WriteAllText(Path.Combine(project, "Project.yaml"), "format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Build Session AB\n");
    var workspace = ProjectWorkspace.Open(project);
    var database = DispatchProxy.Create<IProjectAssetDatabase, Database>();
    var builder = new AssetBundleBuildPipelineAdapter(FileUtility.Current, PathUtility.Current);
    var result = builder.Build(workspace, database, [], new AssetBundleBuildOptions
    {
        PackageName = "probe", Version = "v1", OutputDirectory = Path.Combine(root, "Bundles"),
        AdditionalBundles = [new() { Name = "payload", PackageName = "probe", Assets = [new()
        { Address = "data/probe.bin", AssetType = "Binary", Importer = "test", Content = [1, 2, 3] }] }]
    });
    Check(result.Catalog.Assets.Count == 1 && File.Exists(Path.Combine(result.VersionDirectory, "catalog.bin")), "Direct AB build still publishes actual content");
    Check(notices.Count == 0, "Low-level AB construction does not add duplicate editor build notices");
    foreach (var mode in new[] { "success", "failure", "cancel", "no-result", "callback-failure", "progress-failure" })
    {
        notices.Clear();
        var path = "menu/" + mode;
        var session = EditorBuildEvents.BeginBuild(path); var cancellation = new CancellationTokenSource();
        active.SetValue(null, cancellation); var clears = presentation.Clears;
        Exception? failure = mode == "cancel" ? new OperationCanceledException() : mode == "failure" ? new IOException("build probe") : null;
        var content = mode is "success" or "callback-failure" or "progress-failure" ? result : null;
        var callbacks = 0;
        Action<AssetBundleBuildResult?, Exception?> callback = (value, error) =>
        {
            callbacks++;
            if (!ReferenceEquals(value, content) || !ReferenceEquals(error, failure)) throw new Exception("Completion arguments changed");
            if (mode == "callback-failure") throw new IOException("completion probe");
        };
        presentation.FailClear = mode == "progress-failure";
        Exception? thrown = null;
        try { finish.Invoke(null, [content, failure, session, cancellation, callback]); }
        catch (TargetInvocationException error) { thrown = error.InnerException; }
        finally { presentation.FailClear = false; session.Dispose(); }
        Check(mode is "callback-failure" or "progress-failure" ? thrown is IOException : thrown is null, "AB completion preserves caller/presentation failure: " + mode);
        Check(callbacks == 1 && notices.SequenceEqual(["start:" + path, "finish:" + path + ":" + (content is not null && failure is null)]), "AB reports its build outcome once: " + mode);
        Check(active.GetValue(null) is null && presentation.Clears == clears + 1, "AB clears progress and active state on every exit: " + mode);
        var disposed = false; try { _ = cancellation.Token; } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "AB disposes cancellation state even when a callback or progress cleanup throws: " + mode);
    }
    // The real async observer posts completion instead of closing the session on
    // its worker continuation. Only the queued main-thread callback may finish it.
    foreach (var successful in new[] { true, false })
    {
        notices.Clear(); var path = "async/" + successful;
        var session = EditorBuildEvents.BeginBuild(path); var cancellation = new CancellationTokenSource(); active.SetValue(null, cancellation);
        var pending = new TaskCompletionSource<AssetBundleBuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        var task = (Task)observe.Invoke(null, [pending.Task, session, cancellation, (Action<AssetBundleBuildResult?, Exception?>)((_, _) => callbacks++)])!;
        Check(!task.IsCompleted && notices.Count == 1 && scheduler.PendingMainThreadCount == 0, "Pending AB work keeps session and CTS active: " + successful);
        if (successful) pending.SetResult(result); else pending.SetException(new OperationCanceledException());
        await task;
        Check(notices.Count == 1 && callbacks == 0 && ReferenceEquals(active.GetValue(null), cancellation) && scheduler.PendingMainThreadCount == 1,
            "AB observer only schedules main-thread completion: " + successful);
        scheduler.PumpMainThread(TimeSpan.FromSeconds(1)); session.Dispose();
        Check(notices.SequenceEqual(["start:" + path, "finish:" + path + ":" + successful]) && callbacks == 1 && active.GetValue(null) is null,
            "Pumping AB completion ends the original session exactly once: " + successful);
    }
    notices.Clear();
    using (var session = EditorBuildEvents.BeginBuild("retired-host"))
    {
        var cancellation = new CancellationTokenSource(); active.SetValue(null, cancellation);
        var clears = presentation.Clears; scheduler.FailPost = true;
        try { await (Task)observe.Invoke(null, [Task.FromResult(result), session, cancellation, null])!; }
        finally { scheduler.FailPost = false; }
        Check(notices.SequenceEqual(["start:retired-host", "finish:retired-host:False"]) && active.GetValue(null) is null,
            "A retired dispatcher cannot leave an AB lifecycle or active state open");
        var disposed = false; try { _ = cancellation.Token; } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed && presentation.Clears == clears && scheduler.PendingMainThreadCount == 0,
            "Rejected main-thread completion releases CTS without touching retired UI");
    }
    notices.Clear(); var preparationCallbacks = 0;
    // This host deliberately has no ProjectWorkspace service. Preparation fails
    // before any asset operation; StartBuild must still close its session/CTS.
    start.Invoke(null, ["menu/preparation", Array.Empty<string>(), null, false,
        (Action<AssetBundleBuildResult?, Exception?>)((value, error) => { if (value is not null || error is null) throw new Exception("Expected preparation failure"); preparationCallbacks++; })]);
    Check(preparationCallbacks == 1 && notices.SequenceEqual(["start:menu/preparation", "finish:menu/preparation:False"]) && active.GetValue(null) is null,
        "Synchronous AB preparation failure closes the same lifecycle and active state");
    Check(!typeof(EditorBuildEvents).Assembly.GetReferencedAssemblies().Any(a => a.Name is "BEngine.AssetBundles" or "BEngine.AssetBundles.Editor"), "Core lifecycle does not reference the AB package");
}
finally
{
    EditorBuildEvents.buildStarted -= started; EditorBuildEvents.buildFinished -= finished;
    bridge.GetMethod("Detach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host]);
}
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("BUILD_SESSION_AB_OK|" + checks.Count);

public class Database : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "get_assets" ? Array.Empty<AssetRecord>() : throw new NotSupportedException(method.Name);
}
public class Host : DispatchProxy
{
    public QueueScheduler Scheduler { get; } = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    { "get_TaskScheduler" => Scheduler, "get_ProjectRootPath" => "test", _ => throw new NotSupportedException(method.Name) };
}
public sealed class QueueScheduler : IEditorTaskScheduler
{
    private readonly ConcurrentQueue<Action> _queue = new();
    public bool FailPost;
    public int WorkerCount => 0; public bool IsMainThread => true; public int PendingBackgroundCount => 0; public int PendingMainThreadCount => _queue.Count;
    public void Post(Action callback, string? name = null) { if (FailPost) throw new ObjectDisposedException("editor dispatcher"); _queue.Enqueue(callback); }
    public int PumpMainThread(TimeSpan budget, int max = 128) { var count = 0; while (count < max && _queue.TryDequeue(out var callback)) { callback(); count++; } return count; }
    public void Dispose() { }
    public Task ScheduleAsync(string name, Action<CancellationToken> operation, EditorTaskPriority priority = EditorTaskPriority.Normal, CancellationToken token = default) => throw new NotSupportedException();
    public Task<T> ScheduleAsync<T>(string name, Func<CancellationToken, T> operation, EditorTaskPriority priority = EditorTaskPriority.Normal, CancellationToken token = default) => throw new NotSupportedException();
    public Task ScheduleAsync(string name, Func<CancellationToken, Task> operation, EditorTaskPriority priority = EditorTaskPriority.Normal, CancellationToken token = default) => throw new NotSupportedException();
    public Task<T> ScheduleAsync<T>(string name, Func<CancellationToken, Task<T>> operation, EditorTaskPriority priority = EditorTaskPriority.Normal, CancellationToken token = default) => throw new NotSupportedException();
}
public sealed class Presentation : IEditorUtilityPlatform
{
    public int Clears; public bool FailClear;
    public void ClearProgress() { Clears++; if (FailClear) throw new IOException("progress cleanup probe"); }
    public void ShowProgress(EditorProgressInfo progress, Action? cancel) { }
    public int DisplayDialog(string title, string message, IReadOnlyList<string> buttons) => throw new NotSupportedException();
    public string OpenFilePanel(string title, string directory, string filter) => throw new NotSupportedException();
    public string OpenFolderPanel(string title, string folder, string name) => throw new NotSupportedException();
    public void OpenWithDefaultApp(string target) => throw new NotSupportedException();
}
