namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectSourceChangeMonitor : IDisposable
{
    private const int DebounceMilliseconds = 250;
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private bool _pending;
    private bool _scriptsChanged;
    private bool _shadersChanged;
    private long _lastChangeTick;

    internal ProjectSourceChangeMonitor(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _watcher = new FileSystemWatcher(workspace.AssetsPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*",
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    internal bool TryDequeue(out bool scriptsChanged, out bool shadersChanged)
    {
        lock (_gate)
        {
            if (!_pending || Environment.TickCount64 - _lastChangeTick < DebounceMilliseconds)
            {
                scriptsChanged = false;
                shadersChanged = false;
                return false;
            }

            scriptsChanged = _scriptsChanged;
            shadersChanged = _shadersChanged;
            _pending = false;
            _scriptsChanged = false;
            _shadersChanged = false;
            return true;
        }
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => Queue(args.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        Queue(args.OldFullPath);
        Queue(args.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        lock (_gate)
        {
            _pending = true;
            _scriptsChanged = true;
            _shadersChanged = true;
            _lastChangeTick = Environment.TickCount64;
        }
    }

    private void Queue(string path)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).StartsWith('~')) return;
        lock (_gate)
        {
            _pending = true;
            _scriptsChanged |= IsScriptPath(path);
            _shadersChanged |= ProjectShaderCompiler.IsShaderPath(path);
            _lastChangeTick = Environment.TickCount64;
        }
    }

    internal static bool IsScriptPath(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".asmdef.yaml", StringComparison.OrdinalIgnoreCase);
}
