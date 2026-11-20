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
        _watchers = Gather762(workspace.AssetRoots);
        [global::BEngine.SkipLoopGuard]
        global::System.IO.FileSystemWatcher[] Gather762(global::System.Collections.Generic.IEnumerable<string> items762)
        {
            ArgumentNullException.ThrowIfNull(items762, "source");
            global::System.IO.FileSystemWatcher Evaluate762(string root) => new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*",
                EnableRaisingEvents = false
            };
            global::System.ReadOnlySpan<string> values;
            if (items762.GetType() == typeof(string[]))
                values = (string[])items762;
            else if (items762.GetType() == typeof(global::System.Collections.Generic.List<string>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)items762);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items762, Evaluate762));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<global::System.IO.FileSystemWatcher>();
            var result = new global::System.IO.FileSystemWatcher[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate762(values[index]);
            return result;
        }
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
        if (ProjectFileConventions.IsAuxiliaryPath(path)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _pending = true;
            _scriptsChanged |= ProjectCompilationInputs.IsScriptPath(path);
            _shadersChanged |= ProjectShaderCompiler.IsShaderPath(path);
            _lastChangeTick = _platform.TickCount64;
        }
    }

}
