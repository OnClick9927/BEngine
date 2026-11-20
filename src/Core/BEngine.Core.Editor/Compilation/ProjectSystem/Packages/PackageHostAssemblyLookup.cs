using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

// Keep host names once rather than allocating AssemblyName objects for every
// package image and dependency. Dynamic collectible hosts keep weak identities.
// Load notifications only invalidate; no reflection runs in a loader callback.
internal sealed class PackageHostAssemblyLookup : IDisposable
{
    private readonly AssemblyLoadingUtility _loading;
    private readonly PlatformUtility _platform;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, WeakReference<Assembly>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeakReference<Assembly>> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<Assembly, HostAssemblyName> _weakMetadata = new();
    private int _loadVersion;
    private int _observedVersion = -1;

    internal PackageHostAssemblyLookup(AssemblyLoadingUtility loading, PlatformUtility platform)
    {
        _loading = loading;
        _platform = platform;
        _platform.AssemblyLoaded += OnAssemblyLoaded;
    }

    internal Assembly? Find(string? name, bool ignoreCase)
    {
        if (name is null) return null;
        lock (_gate)
        {
            Refresh();
            var index = ignoreCase ? _names : _exact;
            if (!index.TryGetValue(name, out var weak)) return null;
            if (weak.TryGetTarget(out var hosted)) return hosted;
            // A collectible first match may disappear without another load event.
            // Re-enumerate so a later host with the same name can take its place.
            Refresh(force: true);
            return index.TryGetValue(name, out weak) && weak.TryGetTarget(out hosted) ? hosted : null;
        }
    }

    internal IReadOnlySet<string> GetSimpleNames()
    {
        lock (_gate)
        {
            Refresh();
            // Preparation uses a stable set while PE workers may load additional
            // hosted dependencies. The next Apply captures their names as well.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, weak) in _names)
                if (weak.TryGetTarget(out _)) names.Add(name);
            return names;
        }
    }

    private void Refresh(bool force = false)
    {
        var version = Volatile.Read(ref _loadVersion);
        if (!force && _observedVersion == version) return;
        _exact.Clear();
        _names.Clear();
        foreach (var assembly in _loading.DefaultAssemblies)
        {
            var metadata = _weakMetadata.GetValue(assembly, static value => new(value.GetName().Name, new(value)));
            if (metadata.Name is not { } name) continue;
            // Preserve the resolver's original first-enumeration-match rule,
            // including duplicate names and exact/case-insensitive differences.
            _exact.TryAdd(name, metadata.Assembly);
            _names.TryAdd(name, metadata.Assembly);
        }
        // A concurrent load after enumeration remains dirty for the next query.
        _observedVersion = version;
    }

    private void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args) =>
        Interlocked.Increment(ref _loadVersion);

    public void Dispose() => _platform.AssemblyLoaded -= OnAssemblyLoaded;

    private sealed record HostAssemblyName(string? Name, WeakReference<Assembly> Assembly);
}
