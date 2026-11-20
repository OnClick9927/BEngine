using System.Reflection;
using BEngine.Editor;

namespace BEngine.ProjectSystem.Editor;

/// <summary>Exclusive, owner-thread project activation for editor tools without a workbench.
/// Open activates package extensions; callers can then apply their own project policy before
/// InitializeScripts compiles and initializes the project. Dispose retires only owned code.</summary>
public sealed class ProjectToolSession : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly ProjectWorkspace _workspace;
    private readonly List<AssemblyScope> _scopes = [];
    private EditorInstanceContext? _instance;
    private BPackageManager? _packages;
    private Dictionary<string, bool>? _previousPackages;
    private string? _previousDataPath;
    private bool _initialized;
    private bool _disposed;

    private ProjectToolSession(ProjectWorkspace workspace) => _workspace = workspace;

    public static ProjectToolSession Open(ProjectWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        var session = new ProjectToolSession(workspace);
        try
        {
            // Reject an active editor/tool before changing any process-wide project state.
            session._instance = EditorInstanceContext.Create(workspace.RootPath);
            session._previousDataPath = Application.dataPath;
            session._previousPackages = RuntimePackageState.Capture();
            Application.dataPath = workspace.AssetsPath;
            BAsset.ClearLoadedAssets();
            session._packages = new BPackageManager(workspace);
            cancellationToken.ThrowIfCancellationRequested();
            return session;
        }
        catch (Exception failure) { session.RetireAfterFailure(failure); throw; }
    }

    /// <summary>Compile Runtime then Editor scripts, run owned module initializers, publish
    /// compiler outputs and initialize the enabled editor extensions. May be called once.</summary>
    public void InitializeScripts(CancellationToken cancellationToken = default)
    {
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("Project scripts have already been initialized.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compiler = EditorServiceAccess.GetRequiredService<IProjectScriptCompiler>();
            var runtime = compiler.ResolveRuntimePackageReferences(_workspace, _packages!.definitions);
            var editor = compiler.PackageReferences(_workspace, _packages);
            var build = compiler.Build(_workspace, runtime.AssemblyReferences, editor, cancellationToken);
            var assemblies = ProjectScriptBuildPipeline.LoadOwned(_workspace, build, _scopes, cancellationToken);
            // Do not initialize unrelated isolated compilation sessions in this process.
            EditorInitialization.Run(_packages.LoadedAssemblies.Concat(assemblies)
                .Append(typeof(EditorServices).Assembly), scriptsReloaded: false);
            cancellationToken.ThrowIfCancellationRequested();
            _initialized = true;
        }
        catch (Exception failure) { RetireAfterFailure(failure); throw; }
    }

    public void Dispose()
    {
        CheckOwner();
        if (_disposed) return;
        _disposed = true;
        if (_instance is null) return;
        List<Exception>? failures = null;
        Try(BAsset.ClearLoadedAssets);
        var scopes = _scopes.ToArray();
        _scopes.Clear();
        Try(() => ProjectScriptCompiler.ReleaseOwnedLoadContexts(scopes));
        var packages = _packages;
        _packages = null;
        Try(() => packages?.Dispose());
        if (_previousPackages is { } previous)
        {
            _previousPackages = null;
            Try(() => RuntimePackageState.Restore(previous));
        }
        if (_previousDataPath is { } path)
        {
            _previousDataPath = null;
            Try(() => Application.dataPath = path);
        }
        var instance = _instance;
        _instance = null;
        Try(() => instance?.Dispose());
        if (failures is { Count: > 0 }) throw new AggregateException("Project tool cleanup failed.", failures);

        void Try(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }

    private void RetireAfterFailure(Exception failure)
    {
        try { Dispose(); }
        catch (Exception cleanup) { throw new AggregateException("Project tool operation and cleanup failed.", failure, cleanup); }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Project tool sessions must be used on their owning engine thread.");
    }
}
