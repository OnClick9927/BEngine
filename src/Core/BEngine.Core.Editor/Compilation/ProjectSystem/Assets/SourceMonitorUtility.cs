namespace BEngine.ProjectSystem.Editor;

/// <summary>Application-facing API; the platform host supplies IProjectSourceChangeMonitorFactory through dependency injection.</summary>
public sealed class SourceMonitorUtility
{
    private readonly IProjectSourceChangeMonitorFactory _implementation;

    public SourceMonitorUtility(IProjectSourceChangeMonitorFactory implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static SourceMonitorUtility Current => BEngine.Editor.EditorServices.GetRequiredService<SourceMonitorUtility>();

    public SourceChangeMonitor Create(ProjectWorkspace workspace) =>
        new(_implementation.Create(workspace));
}
