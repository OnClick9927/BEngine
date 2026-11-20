namespace BEngine.ProjectSystem.Editor;

public interface IProjectSourceChangeMonitor : IDisposable
{
    bool TryDequeue(out bool scriptsChanged, out bool shadersChanged);
}
