namespace BEngine.ProjectSystem.Editor;

public sealed class SystemProjectSourceChangeMonitorFactory(BEngine.Platform.PlatformUtility platform) : IProjectSourceChangeMonitorFactory
{
    public IProjectSourceChangeMonitor Create(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new ProjectSourceChangeMonitor(workspace, platform);
    }
}
