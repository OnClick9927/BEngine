using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;

new Probe(args[0], Path.GetFullPath(args[1])).Run();

sealed class Probe(string mode, string output)
{
    readonly List<string> checks = [];
    readonly List<Assembly> loaded = [];
    string root = "";
    ProjectWorkspace workspace = null!;
    BlockingRunner runner = null!;
    void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException(name); }
    Assembly Latest(string name) => loaded.Last(a => a.GetName().Name == name);
    int Value(Assembly assembly, string type) => (int)assembly.GetType(type)!.GetProperty("Value")!.GetValue(null)!;
    void OnLoad(object? sender, AssemblyLoadEventArgs e)
    { if (e.LoadedAssembly.GetName().Name is "GameScripts" or "GameEditorScripts") loaded.Add(e.LoadedAssembly); }
    public void Run()
    {
        Directory.CreateDirectory(output);
        root = Path.Combine("E:/Project/_BZP/BEngine/Temp/HS36", Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY", Path.Combine(root, "Hub"));
        Environment.SetEnvironmentVariable("BENGINE_PACKAGES_PATH", Path.Combine(root, "Hub/Packages"));
        DotNetPlatformServices.Initialize(); DotNetEditorPlatform.Initialize();
        using var original = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices().BuildServiceProvider();
        runner = new BlockingRunner(original.GetRequiredService<IProcessRunner>());
        using var services = new ServiceCollection().AddBEnginePlatformServices().AddBEngineEditorPlatformServices()
            .AddBEngineEditorCompilation().AddSingleton<IPackageCatalogSource>(new EmptyCatalog())
            .AddSingleton<IProcessRunner>(runner).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        PlatformRuntime.Initialize(services); using var attachment = EditorServiceScope.Attach(services);
        Check(services.GetService<IEditorNativeWindowFactory>() is null, "A compiler host requires no window or GPU backend");
        Check(!typeof(EditorServices).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Any(a => a.AssemblyName.Split(',')[0] == typeof(Probe).Assembly.GetName().Name), "Public consumer has no private access grant");
        Directory.CreateDirectory(Path.Combine(root, "Assets/Editor")); Directory.CreateDirectory(Path.Combine(root, "Packages"));
        File.WriteAllText(Path.Combine(root, "Project.yaml"), "format: BEngine.Project\nversion: 1\nname: ScriptHostFixture\n");
        File.WriteAllText(Path.Combine(root, "Packages/manifest.yaml"), "packages: []\n");
        SetRuntime(1); SetEditor(); workspace = ProjectWorkspace.Open(root);
        AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
        try
        {
            switch (mode)
            {
                case "Lifecycle": Lifecycle(); break;
                case "Failure": Failure(); break;
                case "Cancellation": Cancellation(); break;
                case "Ownership": Ownership(); break;
                case "Collection": Collection(); break;
                case "Transactions": Transactions(); break;
                default: throw new ArgumentException(mode);
            }
        }
        finally { AppDomain.CurrentDomain.AssemblyLoad -= OnLoad; }
        Check(EditorInstanceContext.current is null, "The caller retains and releases the project lock");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "BEngine.EditorWorkbench.Host" or "BEngine.Application"), "Generic compilation does not load the workbench or framework");
        File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Names = checks, Fixture = root }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCRIPT_HOST_{mode.ToUpperInvariant()}_OK {checks.Count}");
    }
    void SetRuntime(int value) => File.WriteAllText(Path.Combine(root, "Assets/Runtime.cs"), $"namespace HS36; public static class Runtime {{ public static int Value => {value}; }} public sealed class RuntimeAsset : BEngine.BAsset {{ }}");
    void SetEditor() => File.WriteAllText(Path.Combine(root, "Assets/Editor/Tool.cs"), "namespace HS36; public static class Editor { public static int Value => Runtime.Value; } public sealed class EditorAsset : BEngine.BAsset { }");
    void Lifecycle()
    {
        using var packages = BPackageManager.OpenManifest(workspace);
        Throws<InvalidOperationException>(() => ProjectScriptHostSession.Attach(workspace, packages), "Attach requires an active project instance");
        using var instance = EditorInstanceContext.Create(root);
        using var host = ProjectScriptHostSession.Attach(workspace, packages);
        Throws<InvalidOperationException>(() => ProjectScriptHostSession.Attach(workspace, packages), "Only one live compiler host may attach per instance");
        Throws<InvalidOperationException>(host.CompileEditor, "Editor compilation requires its Runtime graph");
        host.CompileRuntime(); var runtime = Latest("GameScripts");
        Check(Value(runtime, "HS36.Runtime") == 1, "Initial Runtime source is compiled and loaded");
        host.CompileEditor(); var editor = Latest("GameEditorScripts");
        Check(Value(editor, "HS36.Editor") == 1, "Editor resolves its host's exact Runtime assembly");
        Throws<InvalidOperationException>(host.CompileEditor, "Duplicate initial Editor load is rejected");
        SetRuntime(2);
        var request = host.PrepareCompilation(); var before = loaded.Count;
        var result = Task.Run(() => request.Build()).GetAwaiter().GetResult();
        Check(loaded.Count == before && Value(runtime, "HS36.Runtime") == 1, "Background build never loads or replaces running assemblies");
        Check(result.RuntimeAssemblyCount == 1 && result.EditorAssemblyCount == 1, "Opaque result reports the actual Runtime and Editor assembly counts");
        Throws<InvalidOperationException>(() => request.Build(), "A build request cannot run twice");
        host.Apply(result);
        Check(Value(Latest("GameScripts"), "HS36.Runtime") == 2 && Value(Latest("GameEditorScripts"), "HS36.Editor") == 2, "Apply replaces both graphs with matching Runtime identity");
        Throws<InvalidOperationException>(() => host.Apply(result), "A result cannot be applied twice");
        var calls = runner.Calls;
        var unchanged = host.PrepareCompilation().Build(); host.Apply(unchanged);
        Check(runner.Calls == calls, "Unchanged source and configuration skip actual compiler subprocesses");
        File.AppendAllText(Path.Combine(root, "Assets/Editor/Tool.cs"), " public static class EditorOnlyRevision { }");
        var editorOnly = host.PrepareCompilation().Build(); host.Apply(editorOnly);
        Check(runner.Calls == calls + 1, "Changing only project Editor source compiles one assembly and skips Runtime");
        Check(File.Exists(Path.Combine(instance.scriptAssembliesPath, "GameScripts/Current.yaml")) || Directory.GetFiles(instance.scriptAssembliesPath, "GameScripts.dll", SearchOption.AllDirectories).Length > 0, "Applied Runtime is published to the instance's compiler output");
        host.Release(); host.CompileRuntime(); host.CompileEditor();
        Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 2, "Released host can compile again after package reload");
        host.Dispose(); host.Dispose();
        Throws<ObjectDisposedException>(() => host.PrepareCompilation(), "Disposed hosts reject new work");
        using var replacement = ProjectScriptHostSession.Attach(workspace, packages);
        Check(ReferenceEquals(EditorInstanceContext.current, instance), "Compiler disposal preserves the caller's editor instance");
    }
    void Failure()
    {
        using var instance = EditorInstanceContext.Create(root);
        using var packages = BPackageManager.OpenManifest(workspace);
        using var host = ProjectScriptHostSession.Attach(workspace, packages);
        host.CompileRuntime(); var runtime = Latest("GameScripts");
        File.WriteAllText(Path.Combine(root, "Assets/Editor/Tool.cs"), "invalid C#");
        Throws<InvalidOperationException>(host.CompileEditor, "Actual initial Editor compiler failure is reported");
        Check(Value(runtime, "HS36.Runtime") == 1, "Initial Editor failure leaves the successful Runtime graph alive");
        SetEditor(); host.CompileEditor();
        SetRuntime(3); File.WriteAllText(Path.Combine(root, "Assets/Editor/Tool.cs"), "invalid C#");
        Throws<InvalidOperationException>(() => host.PrepareCompilation().Build(), "Transactional Editor build failure is reported");
        Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 1, "A failed background compilation keeps both old graphs running");
        SetEditor(); var stale = host.PrepareCompilation().Build(); var current = host.PrepareCompilation();
        Throws<InvalidOperationException>(() => host.Apply(stale), "Newer preparation rejects an older result");
        host.Release(); Throws<InvalidOperationException>(() => current.Build(), "Release invalidates requests that have not started");
        var result = host.PrepareCompilation().Build();
        Task.Run(() => Throws<InvalidOperationException>(() => host.Apply(result), "Worker threads cannot apply code or mutate editor ownership")).GetAwaiter().GetResult();
        host.Apply(result); Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 3, "A repaired graph applies after failure and release");
        var previousImages = Directory.GetFiles(instance.cachePath, "GameEditorScripts.dll", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        File.AppendAllText(Path.Combine(root, "Assets/Editor/Tool.cs"), " public static class FreshEditorImage { }");
        var failedApply = host.PrepareCompilation().Build();
        // Corrupt only the prepared fixture's private Editor image to force an actual load failure.
        var image = Directory.GetFiles(instance.cachePath, "GameEditorScripts.dll", SearchOption.AllDirectories).Single(path => !previousImages.Contains(path));
        var previous = new[] { Latest("GameScripts"), Latest("GameEditorScripts") };
        var retired = new List<Assembly>();
        RuntimeAssemblyCleanup.Register(retired.Add);
        var bytes = File.ReadAllBytes(image); File.WriteAllText(image, "not a managed image");
        try { Throws<BadImageFormatException>(() => host.Apply(failedApply), "Actual staged Editor load failure reaches the caller"); }
        finally { File.WriteAllBytes(image, bytes); }
        Check(!previous.Any(retired.Contains), "Failed staged loading does not retire either previous graph");
        Check(previous.All(a => RuntimeTypeCache.GetAllTypes().Any(t => t.Assembly == a)), "Failed staged loading restores previous type registrations");
        host.Apply(host.PrepareCompilation().Build());
        Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 3, "Compilation recovers after restoring a failed staged image");
        var oldRuntime = Latest("GameScripts");
        RuntimeAssemblyCleanup.Register(assembly => { if (ReferenceEquals(assembly, oldRuntime)) throw new InvalidOperationException("fixture retirement failure"); });
        SetRuntime(4);
        var committed = host.PrepareCompilation().Build();
        Throws<AggregateException>(() => host.Apply(committed), "Cleanup failure after commit is reported to the host");
        Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 4, "Retirement failure does not roll back the successfully committed new graph");
        Check(RuntimeTypeCache.GetAllTypes().Any(t => t.Assembly == Latest("GameEditorScripts")), "New type registrations remain active after old cleanup throws");
        var activeRuntime = Latest("GameScripts");
        RuntimeAssemblyCleanup.Register(assembly => { if (ReferenceEquals(assembly, activeRuntime)) host.Dispose(); });
        Throws<AggregateException>(host.Release, "A cleanup callback cannot recursively dispose a releasing host or deadlock its build gate");
        host.CompileRuntime(); host.CompileEditor();
        Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 4, "A reentrant cleanup failure still releases scopes and leaves the host reusable");
    }
    void Cancellation()
    {
        using var instance = EditorInstanceContext.Create(root);
        using var packages = BPackageManager.OpenManifest(workspace);
        var host = ProjectScriptHostSession.Attach(workspace, packages);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var request = host.PrepareCompilation();
        Throws<OperationCanceledException>(() => request.Build(canceled.Token), "Pre-canceled requests do not launch a compiler");
        runner.Block = true;
        var build = Task.Run(() => request.Build());
        Check(runner.Entered.Wait(TimeSpan.FromSeconds(20)), "The real compiler request reaches the injected process boundary");
        host.Release();
        Throws<OperationCanceledException>(() => build.GetAwaiter().GetResult(), "Release cancels an accepted build's process token");
        Check(runner.Drained, "Release waits until the accepted process request has drained");
        runner.Reset();
        var next = host.PrepareCompilation(); var pending = Task.Run(() => next.Build());
        Check(runner.Entered.Wait(TimeSpan.FromSeconds(20)), "A reused host accepts the next compiler request");
        var retained = host.PrepareCompilation();
        host.Dispose();
        Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult(), "Dispose cancels an active build");
        Check(runner.Drained, "Dispose drains work before the caller can dispose package state");
        Throws<InvalidOperationException>(() => next.Build(), "A completed request cannot reactivate its disposed owner");
        Throws<ObjectDisposedException>(() => retained.Build(), "An unstarted retained request cannot reactivate its disposed owner");
        Check(ReferenceEquals(EditorInstanceContext.current, instance), "Cancellation does not dispose the caller's project instance");
        using var replacement = ProjectScriptHostSession.Attach(workspace, packages);
        runner.Reset(); using var external = new CancellationTokenSource();
        var externalRequest = replacement.PrepareCompilation(); var externalBuild = Task.Run(() => externalRequest.Build(external.Token));
        Check(runner.Entered.Wait(TimeSpan.FromSeconds(20)), "Caller cancellation reaches an accepted process request");
        external.Cancel();
        Throws<OperationCanceledException>(() => externalBuild.GetAwaiter().GetResult(), "Caller cancellation is propagated by the real compilation pipeline");
        Check(runner.Drained, "Caller-canceled compiler requests release the build gate");
        runner.Block = false;
        replacement.Apply(replacement.PrepareCompilation().Build());
        Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 1, "A fresh request succeeds after caller cancellation");
    }
    void Ownership()
    {
        using var isolated = ProjectScriptSession.Open(workspace);
        var unrelated = isolated.Assemblies.Single(a => a.GetName().Name == "GameScripts");
        using var instance = EditorInstanceContext.Create(root);
        using var packages = BPackageManager.OpenManifest(workspace);
        var first = ProjectScriptHostSession.Attach(workspace, packages);
        first.CompileRuntime(); first.CompileEditor();
        Check(!ReferenceEquals(unrelated, Latest("GameScripts")), "Live and isolated sessions use distinct project assembly scopes");
        var foreign = first.PrepareCompilation().Build(); first.Dispose();
        using var second = ProjectScriptHostSession.Attach(workspace, packages);
        Throws<InvalidOperationException>(() => second.Apply(foreign), "A result from a retired host cannot apply to its replacement");
        second.CompileRuntime(); second.CompileEditor(); second.Release();
        Check(Value(unrelated, "HS36.Runtime") == 1 && isolated.Assemblies.Contains(unrelated), "Live host compilation and release preserve unrelated isolated assemblies");
        Task.Run(() => Throws<InvalidOperationException>(second.Release, "Workers cannot release an editor host's graphs")).GetAwaiter().GetResult();
        var pending = second.PrepareCompilation(); instance.Dispose();
        Throws<InvalidOperationException>(() => pending.Build(), "A prematurely disposed parent instance rejects pending compilation");
        second.Dispose(); Check(true, "Compiler cleanup remains possible after parent disposal");
    }
    void Transactions()
    {
        using var instance = EditorInstanceContext.Create(root);
        using var packages = BPackageManager.OpenManifest(workspace);
        using var host = ProjectScriptHostSession.Attach(workspace, packages);
        var starts = 0; var finishes = 0; var assemblyEvents = 0;
        var notificationThread = Environment.CurrentManagedThreadId;
        var notifications = new List<bool>();
        Action<object> started = value => { notifications.Add(ReferenceEquals(value, workspace) && CompilationPipeline.isCompiling && Environment.CurrentManagedThreadId == notificationThread); starts++; };
        Action<object> finished = value => { notifications.Add(ReferenceEquals(value, workspace) && CompilationPipeline.isCompiling && Environment.CurrentManagedThreadId == notificationThread); finishes++; };
        Action<string> assemblyStarted = _ => { notifications.Add(CompilationPipeline.isCompiling && Environment.CurrentManagedThreadId == notificationThread); assemblyEvents++; };
        CompilationPipeline.compilationStarted += started;
        CompilationPipeline.compilationFinished += finished;
        CompilationPipeline.assemblyCompilationStarted += assemblyStarted;
        try
        {
            using (CompilationPipeline.BeginCompilation(workspace)) { host.CompileRuntime(); host.CompileEditor(); }
            Check(starts == 1 && finishes == 1 && assemblyEvents > 0 && !CompilationPipeline.isCompiling, "Initial Runtime and Editor load share one balanced transaction");
            var calls = runner.Calls;
            using (CompilationPipeline.BeginCompilation(workspace))
            {
                var request = host.PrepareCompilation();
                var result = Task.Run(() => request.Build()).GetAwaiter().GetResult();
                Check(CompilationPipeline.isCompiling, "The editor transaction remains active across worker compilation");
                host.Apply(result);
            }
            Check(runner.Calls == calls && starts == 2 && finishes == 2, "Unchanged compilation keeps incrementality and balanced notifications");
            File.AppendAllText(Path.Combine(root, "Assets/Editor/Tool.cs"), " invalid C# syntax");
            var failed = false;
            try
            {
                using var transaction = CompilationPipeline.BeginCompilation(workspace);
                var request = host.PrepareCompilation();
                host.Apply(Task.Run(() => request.Build()).GetAwaiter().GetResult());
            }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && starts == 3 && finishes == 3 && !CompilationPipeline.isCompiling, "Actual compiler failure completes the transaction");
            Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 1, "Failed background compilation leaves current code active");
            SetEditor(); SetRuntime(2); runner.Block = true; runner.Reset();
            using (CompilationPipeline.BeginCompilation(workspace))
            {
                var request = host.PrepareCompilation(); var build = Task.Run(() => request.Build());
                Check(runner.Entered.Wait(TimeSpan.FromSeconds(20)), "Cancellation test reaches the actual compiler process boundary");
                host.Release();
                Throws<OperationCanceledException>(() => build.GetAwaiter().GetResult(), "Host cancellation propagates inside the transaction");
                Check(runner.Drained && CompilationPipeline.isCompiling, "Canceled work drains before its transaction finishes");
            }
            Check(starts == 4 && finishes == 4 && !CompilationPipeline.isCompiling, "Canceled compilation leaves no stale active state");
            runner.Block = false;
            using (CompilationPipeline.BeginCompilation(workspace)) host.Apply(host.PrepareCompilation().Build());
            Check(Value(Latest("GameEditorScripts"), "HS36.Editor") == 2 && starts == finishes && !CompilationPipeline.isCompiling, "Compilation recovers after both failure and cancellation");
            Check(notifications.Count > 10 && notifications.All(valid => valid), "Every real compilation notification observes owner-thread active state and matching context");
        }
        finally
        {
            CompilationPipeline.compilationStarted -= started;
            CompilationPipeline.compilationFinished -= finished;
            CompilationPipeline.assemblyCompilationStarted -= assemblyStarted;
        }
    }
    void Collection()
    {
        var weak = CreateRetired(); loaded.Clear();
        for (var i = 0; i < 20 && weak.Any(w => w.IsAlive); i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(20); }
        Check(weak.All(w => !w.IsAlive), "Retired host project assemblies and collectible load contexts are collected");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] WeakReference[] CreateRetired()
    {
        using var instance = EditorInstanceContext.Create(root);
        using var packages = BPackageManager.OpenManifest(workspace);
        using var host = ProjectScriptHostSession.Attach(workspace, packages);
        host.CompileRuntime(); host.CompileEditor();
        var runtime = Latest("GameScripts"); var editor = Latest("GameEditorScripts");
        WeakReference[] result = [new(runtime), new(editor), new(AssemblyLoadContext.GetLoadContext(runtime)), new(AssemblyLoadContext.GetLoadContext(editor))];
        return result;
    }
}
sealed class EmptyCatalog : IPackageCatalogSource
{
    public PackageCatalogSnapshot GetSnapshot(string root, string version) => new() { PackagesRoot = root, EngineVersion = version, Revision = "empty", Packages = [] };
}
sealed class BlockingRunner(IProcessRunner implementation) : IProcessRunner
{
    public bool Block;
    public bool Drained;
    public int Calls;
    public ManualResetEventSlim Entered { get; } = new();
    public void Reset() { Drained = false; Entered.Reset(); }
    public ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        if (!Block) return implementation.Run(request, cancellationToken);
        Entered.Set();
        try { cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)); cancellationToken.ThrowIfCancellationRequested(); throw new TimeoutException("Fixture process was not canceled."); }
        finally { Drained = true; }
    }
    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => Task.Run(() => Run(request, cancellationToken), cancellationToken);
}
