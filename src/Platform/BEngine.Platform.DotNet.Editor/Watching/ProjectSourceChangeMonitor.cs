namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectSourceChangeMonitor : IProjectSourceChangeMonitor
{
    private const int DebounceMilliseconds = 250;
    private readonly Lock _gate = new();
    private readonly FileSystemWatcher[] _watchers;
    private readonly BEngine.Platform.PlatformUtility _platform;
    private bool _disposed;
    private bool _pending;
    private bool _scriptsChanged;
    private bool _shadersChanged;
    private long _lastChangeTick;

    internal ProjectSourceChangeMonitor(ProjectWorkspace workspace, BEngine.Platform.PlatformUtility platform)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _watchers = workspace.AssetRoots.Select(root => new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*",
            EnableRaisingEvents = false
        }).ToArray();
        foreach (var watcher in _watchers)
        {
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
        }
    }

    public bool TryDequeue(out bool scriptsChanged, out bool shadersChanged)
    {
        lock (_gate)
        {
            if (_disposed || !_pending || _platform.TickCount64 - _lastChangeTick < DebounceMilliseconds)
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
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = false;
        }
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnChanged;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Error -= OnError;
            watcher.Dispose();
        }
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
            if (_disposed) return;
            _pending = true;
            _scriptsChanged = true;
            _shadersChanged = true;
            _lastChangeTick = _platform.TickCount64;
        }
    }

    private void Queue(string path)
    {
        if (ProjectAssetPathFilter.IsIgnored(path)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _pending = true;
            _scriptsChanged |= ProjectSourceChangeClassifier.IsScriptPath(path);
            _shadersChanged |= ProjectShaderCompiler.IsShaderPath(path);
            _lastChangeTick = _platform.TickCount64;
        }
    }

}
