using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

/// <summary>
/// Owns the project script graphs of a live editor instance. Prepare, load, release and dispose
/// on the attaching thread; build a prepared request on a worker. Dispose before its package
/// manager and editor instance. Reload policy and editor presentation remain with the caller.
/// </summary>
public sealed class ProjectScriptHostSession : IDisposable
{
    private static readonly ConditionalWeakTable<EditorInstanceContext, ProjectScriptHostSession> Hosts = new();
    private static readonly object HostsGate = new();
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private readonly EditorInstanceContext _instance;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private ProjectWorkspace? _workspace;
    private BPackageManager? _packages;
    private ProcessUtility? _processRunner;
    private List<AssemblyScope> _scopes = [];
    private Assembly? _runtime;
    private bool _runtimeReady;
    private bool _editorReady;
    private bool _retiring;
    private CancellationTokenSource _lifetime = new();
    private long _revision;
    private bool _disposed;

    private ProjectScriptHostSession(ProjectWorkspace workspace, BPackageManager packages,
        EditorInstanceContext instance)
    {
        _workspace = workspace;
        _packages = packages;
        _instance = instance;
        _processRunner = ProcessUtility.Current;
    }

    public static ProjectScriptHostSession Attach(ProjectWorkspace workspace, BPackageManager packages)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(packages);
        var instance = EditorInstanceContext.current ??
            throw new InvalidOperationException("An active editor instance is required.");
        if (!string.Equals(instance.projectRootPath, workspace.RootPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(packages.Workspace.RootPath, workspace.RootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The instance, workspace and package manager must belong to the same project.");
        lock (HostsGate)
        {
            if (Hosts.TryGetValue(instance, out _))
                throw new InvalidOperationException("This editor instance already owns a project script host session.");
            var session = new ProjectScriptHostSession(workspace, packages, instance);
            Hosts.Add(instance, session);
            return session;
        }
    }

    /// <summary>Releases this host's previous graphs, then compiles and loads Runtime scripts.</summary>
    public void CompileRuntime()
    {
        VerifyOwner();
        Release();
        var references = RuntimePackageLoader.ResolveEnabledReferences(_workspace!, _packages!.definitions);
        _runtime = ProjectScriptCompiler.CompileAndLoad(_workspace!, references, _processRunner!, _scopes);
        _runtimeReady = true;
    }

    /// <summary>Compiles Editor scripts against this host's Runtime graph. Failure keeps Runtime active.</summary>
    public void CompileEditor()
    {
        VerifyOwner();
        if (!_runtimeReady) throw new InvalidOperationException("Compile Runtime scripts first.");
        if (_editorReady) throw new InvalidOperationException("Editor scripts have already been loaded.");
        EditorProjectScriptCompiler.CompileAndLoad(_workspace!, _runtime,
            typeof(EditorServices).Assembly.Location, _processRunner!, _packages, _scopes);
        _editorReady = true;
    }

    /// <summary>Prepares the reflection index and editor extension warmups before host
    /// presentation is created. Does not initialize extensions or publish reload events.</summary>
    public void PrepareEditorExtensions()
    {
        VerifyOwner();
        TypeCache.Warmup();
    }

    /// <summary>Initializes default-context engine extensions and this host's active
    /// package/project assemblies. Unrelated collectible tool graphs are excluded.
    /// Synchronization can be skipped after PrepareEditorExtensions when no assembly
    /// changed in between. The caller owns lifecycle notification and presentation timing.</summary>
    public void InitializeEditorExtensions(bool synchronizeReflection = true)
    {
        VerifyOwner();
        if (synchronizeReflection) EditorExtensionLifecycle.SynchronizeReflection();
        var owned = _packages!.LoadedAssemblies.Concat(_scopes.SelectMany(scope => scope.Assemblies)).ToArray();
        var names = owned.Select(assembly => assembly.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = owned.Concat(EditorHostServices.Platform.GetLoadedAssemblies()
            .Where(assembly => !assembly.IsCollectible && !names.Contains(assembly.GetName().Name))).ToArray();
        EditorCallbacks.Invoke("EditorInitialization.Run", () => EditorInitialization.Run(candidates));
    }

    /// <summary>Captures package references on the owner thread and invalidates older requests/results.</summary>
    public CompilationRequest PrepareCompilation()
    {
        VerifyOwner();
        var preferred = ProjectScriptCompiler.PreferredAssemblyPaths(typeof(EditorServices).Assembly.Location);
        var editor = EditorProjectScriptCompiler.PackageReferences(_workspace!, _packages!, preferred);
        var runtime = RuntimePackageLoader.ResolveEnabledReferences(_workspace!, _packages!.definitions).AssemblyReferences;
        lock (_stateGate)
            return new CompilationRequest(this, ++_revision,
                new Dictionary<string, string>(runtime, StringComparer.OrdinalIgnoreCase), editor);
    }

    /// <summary>
    /// Replaces this host's loaded graphs after both new graphs load successfully. A failed load
    /// preserves the previous graphs. A result can be applied once, only by its preparing host.
    /// Published compiler files are not a filesystem transaction.
    /// </summary>
    public void Apply(CompilationResult result)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(result);
        lock (_stateGate)
        {
            Validate(result.Owner, result.Revision);
            if (Interlocked.Exchange(ref result.Consumed, 1) != 0)
                throw new InvalidOperationException("This compilation result has already been applied.");
        }
        var build = result.Build;
        var artifacts = build.Runtime.CompiledAssemblies.Concat(build.Editor.CompiledAssemblies)
            .Where(artifact => artifact.CompiledThisBuild).ToArray();
        foreach (var artifact in artifacts) CompilationPipeline.RaiseAssemblyCompilationStarted(artifact.AssemblyPath);
        var staged = new List<AssemblyScope>();
        try
        {
            try
            {
                ProjectScriptCompiler.LoadCompiledGraph(build.Runtime, staged);
                ProjectScriptCompiler.LoadCompiledGraph(build.Editor, staged);
                ProjectScriptCompiler.PublishCompiledAssemblies(_workspace!, build.Runtime.CompiledAssemblies, true);
                ProjectScriptCompiler.PublishCompiledAssemblies(_workspace!, build.Editor.CompiledAssemblies, false);
            }
            catch (Exception failure)
            {
                try { ProjectScriptCompiler.ReleaseOwnedLoadContexts(staged); }
                catch (Exception cleanup) { throw new AggregateException("Loading and staged graph cleanup failed.", failure, cleanup); }
                finally { RuntimeTypeCache.RegisterAssemblies(_scopes.SelectMany(scope => scope.Assemblies)); }
                throw;
            }
            var previous = _scopes;
            _scopes = staged;
            _runtime = null;
            _runtimeReady = true;
            _editorReady = true;
            // Retiring old callbacks may throw. New graphs are already committed and must not
            // be unloaded by the load-failure rollback above.
            ProjectScriptCompiler.ReleaseOwnedLoadContexts(previous);
        }
        catch (Exception failure)
        {
            foreach (var artifact in artifacts)
                CompilationPipeline.RaiseAssemblyCompilationFinished(artifact.AssemblyPath,
                    [new CompilerMessage(failure.Message, artifact.Node.DefinitionPath, 0, 0, CompilerMessageType.Error)]);
            throw;
        }
        foreach (var artifact in artifacts) CompilationPipeline.RaiseAssemblyCompilationFinished(artifact.AssemblyPath, []);
    }

    /// <summary>Cancels and drains accepted builds before unloading only this host's script graphs.</summary>
    public void Release()
    {
        VerifyOwner();
        Retire(dispose: false);
    }

    public void Dispose()
    {
        VerifyThread();
        lock (_stateGate)
        {
            if (_disposed) return;
            if (_retiring) throw new InvalidOperationException("The project script host is already releasing its graphs.");
        }
        try { Retire(dispose: true); }
        finally
        {
            _workspace = null;
            _packages = null;
            _processRunner = null;
            lock (HostsGate) Hosts.Remove(_instance);
        }
    }

    private void Retire(bool dispose)
    {
        CancellationTokenSource previous;
        lock (_stateGate)
        {
            previous = _lifetime;
            ++_revision;
            _disposed = dispose;
            _retiring = true;
            if (!dispose) _lifetime = new CancellationTokenSource();
            previous.Cancel();
        }
        _buildGate.Wait();
        try
        {
            var scopes = _scopes;
            _scopes = [];
            _runtime = null;
            _runtimeReady = false;
            _editorReady = false;
            ProjectScriptCompiler.ReleaseOwnedLoadContexts(scopes);
        }
        finally
        {
            _buildGate.Release();
            previous.Dispose();
            lock (_stateGate) _retiring = false;
        }
    }

    private CompilationResult Build(CompilationRequest request, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        ProjectWorkspace workspace;
        ProcessUtility process;
        lock (_stateGate)
        {
            Validate(request.Owner, request.Revision);
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            workspace = _workspace!;
            process = _processRunner!;
        }
        using (linked)
        {
            _buildGate.Wait(linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                lock (_stateGate) Validate(request.Owner, request.Revision);
                var build = ProjectScriptBuildPipeline.Build(workspace, request.RuntimeReferences,
                    request.EditorReferences, process, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                lock (_stateGate) Validate(request.Owner, request.Revision);
                return new CompilationResult(this, request.Revision, build);
            }
            finally { _buildGate.Release(); }
        }
    }

    private void Validate(ProjectScriptHostSession owner, long revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retiring) throw new InvalidOperationException("The project script host is releasing its graphs.");
        if (!ReferenceEquals(owner, this) || revision != _revision)
            throw new InvalidOperationException("The compilation belongs to a different host or an obsolete request.");
        if (!ReferenceEquals(EditorInstanceContext.current, _instance))
            throw new InvalidOperationException("The owning editor instance is no longer active.");
    }

    private void VerifyThread()
    {
        if (_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("This operation must run on the attaching thread.");
    }

    private void VerifyOwner()
    {
        VerifyThread();
        lock (_stateGate) Validate(this, _revision);
    }

    public sealed class CompilationRequest
    {
        internal readonly ProjectScriptHostSession Owner;
        internal readonly long Revision;
        internal readonly IReadOnlyDictionary<string, string> RuntimeReferences;
        internal readonly IReadOnlyDictionary<string, string> EditorReferences;
        private int _started;
        internal CompilationRequest(ProjectScriptHostSession owner, long revision,
            IReadOnlyDictionary<string, string> runtimeReferences, IReadOnlyDictionary<string, string> editorReferences)
        { Owner = owner; Revision = revision; RuntimeReferences = runtimeReferences; EditorReferences = editorReferences; }

        /// <summary>Builds once on a worker without loading assemblies or applying editor changes.</summary>
        public CompilationResult Build(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _started, 1) != 0)
                throw new InvalidOperationException("This compilation request has already been built.");
            return Owner.Build(this, cancellationToken);
        }
    }

    public sealed class CompilationResult
    {
        internal readonly ProjectScriptHostSession Owner;
        internal readonly long Revision;
        internal readonly ProjectScriptBuildResult Build;
        internal int Consumed;
        internal CompilationResult(ProjectScriptHostSession owner, long revision, ProjectScriptBuildResult build)
        { Owner = owner; Revision = revision; Build = build; }
        public int RuntimeAssemblyCount => Build.Runtime.CompiledAssemblies.Count;
        public int EditorAssemblyCount => Build.Editor.CompiledAssemblies.Count;
    }
}
