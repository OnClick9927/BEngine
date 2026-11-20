using BEngine.Editor.Documents;
using BEngine.ProjectSystem;

namespace BEngine.Launcher;

/// <summary>One scan on startup, then debounced filesystem changes. Reads never search the repository.</summary>
internal sealed class LauncherPackageIndex : IDisposable
{
    private readonly string _root;
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _timer;
    private readonly Dictionary<string, PackageCatalogSnapshot> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<LauncherPackageInfo>> _views = new(StringComparer.Ordinal);
    private BPackageDefinition[] _releases = [];
    private string _revision = string.Empty;
    private string? _error;
    private bool _disposed;
    internal event Action? Changed;
    internal int ScanCount { get; private set; }
    internal void Refresh() => Rebuild(null);
    internal IReadOnlyList<BPackageDefinition> GetDefinitions()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_error is not null) throw new InvalidDataException(_error);
            return _releases.ToArray();
        }
    }

    internal LauncherPackageIndex(string root)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _timer = new System.Threading.Timer(Rebuild, null, Timeout.Infinite, Timeout.Infinite);
        // Watch an existing ancestor as well, so a new/deleted Packages directory is detected.
        var ancestor = Path.GetDirectoryName(_root) ?? _root;
        while (!Directory.Exists(ancestor)) ancestor = Path.GetDirectoryName(ancestor)
            ?? throw new DirectoryNotFoundException(_root);
        _watcher = new FileSystemWatcher(ancestor)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            InternalBufferSize = 32 * 1024
        };
        _watcher.Created += OnChange;
        _watcher.Changed += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnRename;
        _watcher.Error += (_, _) => Schedule(); // Overflow requires one fresh snapshot, not polling.
        _watcher.EnableRaisingEvents = true;
        Rebuild(null);
    }

    internal PackageCatalogSnapshot GetSnapshot(string engineVersion)
    {
        BPackageVersion.Validate(engineVersion);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_error is not null) throw new InvalidDataException(_error);
            if (_versions.TryGetValue(engineVersion, out var snapshot)) return snapshot;
            var compatible = _releases.Where(item => BPackageCompatibility.SupportsEngine(item.Document, engineVersion)).ToArray();
            LauncherPackageValidation.ValidateCompatibleReleases(compatible);
            var distribution = LauncherBundledPackages.Load(_root, engineVersion);
            snapshot = new PackageCatalogSnapshot
            {
                PackagesRoot = _root,
                EngineVersion = engineVersion,
                Revision = _revision,
                Packages = ProjectItems2795()
            };
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageCatalogEntry> ProjectItems2795()
            {
                var projectionSource2795 = compatible;
                ArgumentNullException.ThrowIfNull(projectionSource2795, "source");
                global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> projectionValues2795 = projectionSource2795;
                var projectionOutput2795 = new global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageCatalogEntry>(projectionValues2795.Length);
                global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(projectionOutput2795, projectionValues2795.Length);
                var projectionTarget2795 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionOutput2795);
                var projectionIndex2795 = 0;
                foreach (var item in projectionValues2795)
                {
                    projectionTarget2795[projectionIndex2795++] = new PackageCatalogEntry
                    {
                        Path = item.Path,
                        Document = item.Document,
                        Distribution = distribution.Classify(item)
                    };
                }
            
                return projectionOutput2795;
            }
            _versions.Add(engineVersion, snapshot);
            return snapshot;
        }
    }

    internal IReadOnlyList<LauncherPackageInfo> GetPackages(string version)
    {
        lock (_gate)
        {
            var snapshot = GetSnapshot(version);
            if (_views.TryGetValue(version, out var view)) return view;
            view = LauncherPackageCatalog.BuildPackageInfos(
                snapshot.Packages.Select(item => new BPackageDefinition(item.Path, item.Document)),
                snapshot.Packages.ToDictionary(item => item.Path, item => item.Distribution, StringComparer.OrdinalIgnoreCase));
            _views.Add(version, view);
            return view;
        }
    }

    private bool Relevant(string path)
    {
        var installation = Path.GetDirectoryName(_root)!;
        var distributionPath = Path.GetRelativePath(Path.Combine(installation, "BEgine"), path).Replace('\\', '/');
        var parts = distributionPath.Split('/');
        if (distributionPath == "." || parts[0].StartsWith("BEgine-", StringComparison.Ordinal) &&
            (parts.Length == 1 || parts.Length == 2 && parts[1] == LauncherBundledPackages.FileName)) return true;
        var relative = Path.GetRelativePath(_root, path);
        return relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Query5048(relative.Split('/', '\\'));
        [global::BEngine.SkipLoopGuard]
        bool Query5048(global::System.Collections.Generic.IEnumerable<string> querySource5048)
        {
            ArgumentNullException.ThrowIfNull(querySource5048, "source");
            bool Matches5048(string segment) => segment.StartsWith('.') || segment is "bin" or "obj";
            global::System.ReadOnlySpan<string> queryValues5048;
            if (querySource5048.GetType() == typeof(string[]))
                queryValues5048 = (string[])querySource5048;
            else if (querySource5048.GetType() == typeof(global::System.Collections.Generic.List<string>))
                queryValues5048 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource5048);
            else
            {
                foreach (var segment in querySource5048)
                    if (Matches5048(segment))
                        return true;
                return false;
            }
        
            foreach (var segment in queryValues5048)
                if (Matches5048(segment))
                    return true;
            return false;
        }
    }
    private void OnChange(object sender, FileSystemEventArgs args) { if (Relevant(args.FullPath)) Schedule(); }
    private void OnRename(object sender, RenamedEventArgs args)
    { if (Relevant(args.FullPath) || Relevant(args.OldFullPath)) Schedule(); }

    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(400, Timeout.Infinite);
        }
    }

    private void Rebuild(object? state)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                ScanCount++;
                var releases = Directory.Exists(_root)
                    ? Directory.EnumerateFiles(_root, "package.yaml", SearchOption.AllDirectories)
                        .Where(Relevant).Order(StringComparer.OrdinalIgnoreCase).Select(path =>
                            new BPackageDefinition(Path.GetFullPath(path), PackageDefinitionLoader.Normalize(
                                YamlUtility.Load<PackageDefinitionDocument>(path), path))).ToArray()
                    : [];
                _releases = releases;
                _revision = Guid.NewGuid().ToString("N");
                _versions.Clear();
                _views.Clear();
                _error = null;
            }
            catch (Exception exception)
            {
                // Never replace a complete catalog with a partial traversal.
                _error = $"Could not index packages in '{_root}': {exception.Message}";
            }
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.Dispose();
            _timer.Dispose();
            _versions.Clear();
            _views.Clear();
            _releases = [];
        }
    }
}
