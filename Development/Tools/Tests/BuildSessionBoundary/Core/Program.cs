using System.Reflection;
using System.Text.Json;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Editor.Build;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;

var output = Path.GetFullPath(args[0]);
var root = Path.Combine(output, "Run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(root, "Hub"));
DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new Exception(name + " did not throw " + typeof(T).Name);
}
var notices = new List<string>();
Action<string> started = path => notices.Add("start:" + path);
Action<string, bool> finished = (path, success) => notices.Add("finish:" + path + ":" + success);
EditorBuildEvents.buildStarted += started;
EditorBuildEvents.buildFinished += finished;
try
{
    Throws<ArgumentNullException>(() => EditorBuildEvents.BeginBuild(null!), "Null output rejected before starting");
    Throws<ArgumentException>(() => EditorBuildEvents.BeginBuild(""), "Empty output rejected before starting");
    Throws<ArgumentException>(() => EditorBuildEvents.BeginBuild("  "), "Whitespace output rejected before starting");
    Check(notices.Count == 0, "Invalid requests do not create lifecycle notices");
    using (var session = EditorBuildEvents.BeginBuild("relative/Output"))
        Check(session.OutputPath == "relative/Output", "Session preserves the caller output identity");
    Check(notices.SequenceEqual(["start:relative/Output", "finish:relative/Output:False"]), "Disposal reports unfinished work as failed");
    notices.Clear();
    using (var session = EditorBuildEvents.BeginBuild("complete"))
    { session.Complete(true); session.Complete(false); session.Dispose(); }
    Check(notices.SequenceEqual(["start:complete", "finish:complete:True"]), "Success completes exactly once despite later disposal");
    notices.Clear();
    using (var session = EditorBuildEvents.BeginBuild("failed")) { session.Complete(false); session.Complete(true); }
    Check(notices.SequenceEqual(["start:failed", "finish:failed:False"]), "Explicit failure cannot be overwritten by success");
    notices.Clear();
    try { using var session = EditorBuildEvents.BeginBuild("exception"); throw new IOException("work"); }
    catch (IOException) { }
    Check(notices.SequenceEqual(["start:exception", "finish:exception:False"]), "Exceptions still close the session");
    notices.Clear();
    using (var outer = EditorBuildEvents.BeginBuild("outer"))
    { using (var inner = EditorBuildEvents.BeginBuild("inner")) { inner.Complete(true); } outer.Complete(true); }
    Check(notices.SequenceEqual(["start:outer", "start:inner", "finish:inner:True", "finish:outer:True"]), "Nested operations retain balanced independent lifecycles");
    notices.Clear();
    EditorBuildSession? reentrant = null;
    Action<string, bool> reenter = (path, _) => { if (path == "reentrant") { reentrant!.Complete(false); reentrant.Dispose(); } };
    EditorBuildEvents.buildFinished += reenter;
    try { reentrant = EditorBuildEvents.BeginBuild("reentrant"); reentrant.Complete(true); reentrant.Dispose(); }
    finally { EditorBuildEvents.buildFinished -= reenter; }
    Check(notices.SequenceEqual(["start:reentrant", "finish:reentrant:True"]), "Completion state is committed before reentrant subscribers");
    notices.Clear();
    var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task Work() { using var session = EditorBuildEvents.BeginBuild("async"); await pending.Task; session.Complete(true); }
    var operation = Work();
    Check(notices.SequenceEqual(["start:async"]) && !operation.IsCompleted, "Async work keeps its session open while pending");
    pending.SetResult(); await operation;
    Check(notices.SequenceEqual(["start:async", "finish:async:True"]), "Async completion closes the same session");
    notices.Clear();
    using (var cancellation = new CancellationTokenSource())
    {
        try { using var session = EditorBuildEvents.BeginBuild("cancel"); cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException) { }
    }
    Check(notices.SequenceEqual(["start:cancel", "finish:cancel:False"]), "Cancellation reports one failed completion");
    notices.Clear();
    Action<string> brokenStart = _ => throw new ProbeFailure("start callback");
    Action<string, bool> brokenFinish = (_, _) => throw new ProbeFailure("finish callback");
    EditorBuildEvents.buildStarted += brokenStart;
    EditorBuildEvents.buildFinished += brokenFinish;
    var laterStart = 0; var laterFinish = 0;
    Action<string> afterStart = _ => laterStart++;
    Action<string, bool> afterFinish = (_, _) => laterFinish++;
    EditorBuildEvents.buildStarted += afterStart;
    EditorBuildEvents.buildFinished += afterFinish;
    try { using var session = EditorBuildEvents.BeginBuild("callbacks"); session.Complete(true); }
    finally
    {
        EditorBuildEvents.buildStarted -= brokenStart; EditorBuildEvents.buildFinished -= brokenFinish;
        EditorBuildEvents.buildStarted -= afterStart; EditorBuildEvents.buildFinished -= afterFinish;
    }
    Check(laterStart == 1 && laterFinish == 1 && notices.SequenceEqual(["start:callbacks", "finish:callbacks:True"]), "Faulty subscribers do not prevent later subscribers or change the outcome");
    notices.Clear();
    // Exercise the real public Player transaction, including a filesystem failure
    // after publication. No content format or drawing package participates.
    var project = Path.Combine(root, "Project"); Directory.CreateDirectory(Path.Combine(project, "Assets/Scenes"));
    File.WriteAllText(Path.Combine(project, "Assets/Scenes/Main.scene.yaml"), "{}\n");
    File.WriteAllText(Path.Combine(project, "Project.yaml"), "format: BEngine.Project\nversion: 1\nengineVersion: 0.1.0\nname: Build Session Test\n");
    RuntimeServiceContributions.RegisterDefault<IPlayerStartupImageBuilder>(new Images());
    var paths = PathUtility.Current;
    foreach (var mode in new[] { "success", "prepare", "finalize", "platform", "cancel", "cleanup", "failed-cleanup", "progress" })
    {
        notices.Clear();
        var disk = new Disk(); var files = new FileUtility(disk);
        var content = new Content(mode, disk, files, paths);
        var pipeline = new PlayerBuildPipelineAdapter(ProcessUtility.Current, ApplicationPathUtility.Current,
            files, paths, PlatformUtility.Current, new PlatformPlayerBuildCapabilities(files, paths), content);
        foreach (var provider in pipeline.RegisteredProviders) pipeline.UnregisterProvider(provider.ProviderId);
        pipeline.RegisterProvider(new Platform(mode));
        var destination = Path.Combine(root, mode);
        Exception? failure = null;
        try { await pipeline.BuildAsync(new PlayerBuildRequest { ProjectPath = project, TargetId = "windows", OutputDirectory = destination, CompileShaders = false }, mode == "progress" ? new BrokenProgress() : null); }
        catch (Exception error) { failure = error; }
        if (failure is not null) Console.WriteLine("CASE|" + mode + "|" + failure);
        Check(mode == "success" ? failure is null : mode == "cancel" ? failure is OperationCanceledException : mode.EndsWith("cleanup") ? failure is IOException : failure is ProbeFailure,
            "Player retains the actual build or cleanup outcome: " + mode);
        Check(notices.SequenceEqual(["start:" + destination, "finish:" + destination + ":" + (mode == "success")]),
            "Player transaction sends exactly one balanced outcome: " + mode);
        if (mode == "success")
            Check(File.Exists(Path.Combine(destination, "build session test_data/resources/custom.bin")), "Successful publication remains intact");
        if (mode == "cleanup")
            Check(File.Exists(Path.Combine(destination, "build session test_data/resources/custom.bin")) && failure!.Message == "cleanup probe",
                "Post-publication cleanup failure reports failure without erasing the published output");
        if (disk.FailingDirectory is { } remaining && Directory.Exists(remaining)) Directory.Delete(remaining, true);
    }
    RuntimeServiceContributions.UnregisterAssembly(Assembly.GetExecutingAssembly());
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.AssetBundles" or "BEngine.AssetBundles.Editor" or "BEngine.Graphics" or "BEngine.Graphics.Editor" or "BEngine.IMGUI.Editor"),
        "Core lifecycle and actual Player transactions require no concrete content or drawing package");
}
finally { EditorBuildEvents.buildStarted -= started; EditorBuildEvents.buildFinished -= finished; }
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("BUILD_SESSION_CORE_OK|" + checks.Count);

sealed class ProbeFailure(string message) : Exception(message);
sealed class BrokenProgress : IProgress<PlayerBuildProgress>
{
    public void Report(PlayerBuildProgress value) { if (value.Phase == PlayerBuildPhase.Completed) throw new ProbeFailure("progress probe"); }
}
sealed class Images : IPlayerStartupImageBuilder
{
    public PlayerStartupImages Build(ProjectWorkspace workspace, PlayerBuildRequest request) => new(new byte[] { 1 }, new byte[] { 2 });
}
sealed class Content(string mode, Disk disk, FileUtility files, PathUtility paths) : IPlayerBuildContentProvider
{
    public Task PrepareAsync(PlayerBuildContext context, CancellationToken token)
    {
        if (mode == "failed-cleanup") { disk.FailingDirectory = context.StagingDirectory; throw new ProbeFailure("prepare probe"); }
        if (mode == "prepare") throw new ProbeFailure("prepare probe");
        if (mode == "cancel") throw new OperationCanceledException(token);
        if (mode == "cleanup")
        {
            context.StrippingWorkDirectory = Path.Combine(context.Request.ProjectPath, "Temp/PlayerStripping", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(context.StrippingWorkDirectory); disk.FailingDirectory = context.StrippingWorkDirectory;
        }
        return Task.CompletedTask;
    }
    public Task FinalizeAsync(PlayerBuildContext context, CancellationToken token)
    {
        if (mode == "finalize") throw new ProbeFailure("finalize probe");
        var layout = PlayerBuildLayout.Create(ProjectWorkspace.Open(context.Request.ProjectPath, files, paths), context.StagingDirectory, files, paths);
        File.WriteAllText(Path.Combine(layout.ResourcesDirectory, "custom.bin"), "content");
        return Task.CompletedTask;
    }
}
sealed class Platform(string mode) : IPlayerBuildTargetProvider
{
    public string ProviderId => "test.lifecycle";
    public bool CanBuild(BEngine.Build.BuildTargetDescriptor target, out string reason) { reason = ""; return true; }
    public Task BuildAsync(PlayerBuildContext context, CancellationToken token = default)
    { if (mode == "platform") throw new ProbeFailure("platform probe"); return Task.CompletedTask; }
}
sealed class Disk : IFileSystem
{
    public string? FailingDirectory;
    public bool FileExists(string p) => File.Exists(p);
    public bool DirectoryExists(string p) => Directory.Exists(p);
    public void CreateDirectory(string p) => Directory.CreateDirectory(p);
    public IEnumerable<string> EnumerateFiles(string p, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly) => Directory.EnumerateFiles(p, pattern, option);
    public IEnumerable<string> EnumerateDirectories(string p, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly) => Directory.EnumerateDirectories(p, pattern, option);
    public IEnumerable<string> EnumerateFileSystemEntries(string p) => Directory.EnumerateFileSystemEntries(p);
    public void DeleteFile(string p) => File.Delete(p);
    public void DeleteDirectory(string p, bool recursive = false) { if (p == FailingDirectory) throw new IOException("cleanup probe"); Directory.Delete(p, recursive); }
    public void MoveFile(string a, string b, bool overwrite = false) => File.Move(a, b, overwrite);
    public void CopyFile(string a, string b, bool overwrite = false) => File.Copy(a, b, overwrite);
    public void MoveDirectory(string a, string b) => Directory.Move(a, b);
    public void ReplaceFile(string a, string b, string? backup = null, bool ignore = false) => File.Replace(a, b, backup, ignore);
    public Stream OpenRead(string p) => File.OpenRead(p);
    public Stream OpenFile(string p, FileMode m, FileAccess a, FileShare s, int buffer, FileOptions options) => new FileStream(p, m, a, s, buffer, options);
    public long GetFileLength(string p) => new FileInfo(p).Length;
    public DateTime GetLastWriteTimeUtc(string p) => File.GetLastWriteTimeUtc(p);
    public void SetLastWriteTimeUtc(string p, DateTime t) => File.SetLastWriteTimeUtc(p, t);
    public FileAttributes GetAttributes(string p) => File.GetAttributes(p);
    public byte[] ReadAllBytes(string p) => File.ReadAllBytes(p);
    public string ReadAllText(string p) => File.ReadAllText(p);
    public void WriteAllBytes(string p, byte[] bytes) => File.WriteAllBytes(p, bytes);
    public void WriteAllText(string p, string text) => File.WriteAllText(p, text);
    public void Flush(Stream stream, bool disk = false) { if (stream is FileStream file) file.Flush(disk); else stream.Flush(); }
}
