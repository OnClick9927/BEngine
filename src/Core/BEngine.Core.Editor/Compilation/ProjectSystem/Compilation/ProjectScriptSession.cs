using System.Reflection;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

/// <summary>Owner-thread compilation and loading for an isolated editor tool.
/// Disposing this session unloads only its own project scopes; existing editor graphs remain active.
/// Callers must release their own references to assemblies and objects before collection is possible.</summary>
public sealed class ProjectScriptSession : IDisposable
{
    private readonly List<AssemblyScope> _scopes = [];
    private IReadOnlyList<Assembly> _assemblies = [];
    private bool _disposed;
    private ProjectScriptSession() { }

    public IReadOnlyList<Assembly> Assemblies
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _assemblies; }
    }

    public static ProjectScriptSession Open(ProjectWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        var session = new ProjectScriptSession();
        try
        {
            using var packages = BPackageManager.OpenManifest(workspace);
            var runtime = RuntimePackageLoader.LoadEnabled(workspace, packages.definitions,
                applyRuntimeState: false, EditorServices.GetRequiredService<IPackageSourceCompiler>());
            cancellationToken.ThrowIfCancellationRequested();
            var preferred = ProjectScriptCompiler.PreferredAssemblyPaths(typeof(EditorServices).Assembly.Location);
            var editorReferences = EditorProjectScriptCompiler.PackageReferences(workspace, packages, preferred);
            var build = ProjectScriptBuildPipeline.Build(workspace, runtime.AssemblyReferences,
                editorReferences, ProcessUtility.Current, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var loadedRuntime = ProjectScriptCompiler.LoadCompiledGraph(build.Runtime, session._scopes);
            cancellationToken.ThrowIfCancellationRequested();
            var loadedEditor = ProjectScriptCompiler.LoadCompiledGraph(build.Editor, session._scopes);
            cancellationToken.ThrowIfCancellationRequested();
            session._assemblies = Array.AsReadOnly(loadedRuntime.LoadedAssemblies
                .Concat(loadedEditor.LoadedAssemblies).ToArray());
            return session;
        }
        catch (Exception failure)
        {
            try { session.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Compilation and session cleanup failed.", failure, cleanup); }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var scopes = _scopes.ToArray();
        _scopes.Clear();
        _assemblies = [];
        ProjectScriptCompiler.ReleaseOwnedLoadContexts(scopes);
    }
}
