namespace BEngine.ProjectSystem.Editor;
public interface IProjectSourceChangeMonitorFactory
{
    IProjectSourceChangeMonitor Create(ProjectWorkspace workspace);
}
