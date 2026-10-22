namespace BEngine.ProjectSystem.Editor;

/// <summary>Application-facing API; the platform host supplies IProjectSourceChangeMonitor through dependency injection.</summary>
public sealed class SourceChangeMonitor : IDisposable
{
    private readonly IProjectSourceChangeMonitor _implementation;

    public SourceChangeMonitor(IProjectSourceChangeMonitor implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public bool TryDequeue(out bool scriptsChanged, out bool shadersChanged) =>
        _implementation.TryDequeue(out scriptsChanged, out shadersChanged);
    public void Dispose() => _implementation.Dispose();
}
