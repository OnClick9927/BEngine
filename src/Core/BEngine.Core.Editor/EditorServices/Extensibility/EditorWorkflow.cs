using System.Reflection;
using BEngine.DependencyInjection;
using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.IProjectAssetDatabase;

namespace BEngine.Editor;

/// <summary>Optional project policy and play workflow. The default executes Core scenes directly.</summary>
public class EditorWorkflow
{
    private static readonly EditorWorkflow Default = new();
    public static EditorWorkflow Current => RuntimeServiceContributions.GetDefault<EditorWorkflow>() ?? Default;
    public virtual void EnsureProject(ProjectWorkspace workspace) { }
    public virtual bool IsProtectedAssetPath(string? path) => false;
    public virtual IEditorScriptingDefinePersistence CreateDefinePersistence(string projectPath) => EmptyPersistence.Instance;
    public virtual IEditorPlayContentSession CreateContent(ProjectWorkspace workspace, ProjectAssetDatabase assets) => new EmptyContent();
    public virtual BValueTask<IEditorPlayCodeSession?> CreateCodeAsync(ProjectWorkspace workspace, IEditorPlayContentSession content) => new((IEditorPlayCodeSession?)null);
    private sealed class EmptyPersistence : IEditorScriptingDefinePersistence
    {
        public static readonly EmptyPersistence Instance = new();
        public void Save(IReadOnlyList<string> symbols) { }
    }
    private sealed class EmptyContent : IEditorPlayContentSession
    {
        public BValueTask PrepareAsync(BCancellationToken cancellationToken = default) => BValueTask.CompletedTask;
        public void Dispose() { }
    }
}
public interface IEditorPlayContentSession : IDisposable
{
    BValueTask PrepareAsync(BCancellationToken cancellationToken = default);
}
public interface IEditorPlayCodeSession : IDisposable
{
    string ReleaseId { get; }
    IReadOnlyList<Assembly> Assemblies { get; }
    IServiceProvider Services { get; }
    BValueTask DisposeAsync();
}

/// <summary>Package-owned command line workflows, independent of the editor shell.</summary>
public static class EditorCommandExtensions
{
    public delegate bool Handler(IReadOnlyList<string> arguments, out int exitCode);
    public static event Handler? Requested;
    public static bool TryRun(IReadOnlyList<string> args, out int result)
    {
        foreach (var handler in Delegate.EnumerateInvocationList(Requested))
            if (handler(args, out result)) return true;
        result = 0;
        return false;
    }
}

