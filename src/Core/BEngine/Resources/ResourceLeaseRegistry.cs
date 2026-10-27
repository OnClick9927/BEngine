using BEngine.Platform;

namespace BEngine;

internal static class ResourceLeaseRegistry
{
    private static readonly Dictionary<ResourceKey, Entry> Entries = [];
    private static FileUtility? _cacheFileSystem;
    private static PathUtility? _cachePathService;
    private static PlatformUtility? _cachePlatformService;

    internal static ResourceHandle<T> Acquire<T>(string path, string folderName) where T : class
    {
        EnsureServiceContext();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var key = new ResourceKey(Normalize(path), folderName, typeof(T));
        if (!Entries.TryGetValue(key, out var entry) || entry.Asset is BObject { isDestroyed: true })
        {
            var asset = ResourceLoader.Load<T>(path, folderName) ??
                        throw new FileNotFoundException(
                            $"Resource '{path}' of type {typeof(T).FullName} was not found.");
            entry = new Entry(asset);
            Entries[key] = entry;
        }
        checked { entry.ReferenceCount++; }
        return new ResourceHandle<T>((T)entry.Asset, () => Release(key, entry));
    }

    internal static int GetReferenceCount(object asset)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(asset);
        return Entries.Values.Where(entry => ReferenceEquals(entry.Asset, asset))
            .Sum(entry => entry.ReferenceCount);
    }

    internal static ResourceHandle<T> AcquireLoaded<T>(string path, string folderName, T asset) where T : class
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(asset);
        var key = new ResourceKey(Normalize(path), folderName, typeof(T));
        if (!Entries.TryGetValue(key, out var entry) || entry.Asset is BObject { isDestroyed: true })
        {
            entry = new Entry(asset);
            Entries[key] = entry;
        }
        checked { entry.ReferenceCount++; }
        return new ResourceHandle<T>((T)entry.Asset, () => Release(key, entry));
    }

    internal static int UnloadUnused()
    {
        EnsureServiceContext();
        var unused = Entries.Where(pair => pair.Value.ReferenceCount == 0)
            .Select(pair => pair.Key).ToArray();
        foreach (var key in unused) Entries.Remove(key);
        return unused.Length;
    }

    private static void Release(ResourceKey key, Entry expected)
    {
        if (!Entries.TryGetValue(key, out var entry) || !ReferenceEquals(entry, expected) ||
            entry.ReferenceCount <= 0) return;
        entry.ReferenceCount--;
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        return RuntimeServiceAccess.PlatformService.IsWindows ? normalized.ToUpperInvariant() : normalized;
    }

    private static void EnsureServiceContext()
    {
        var fileSystem = RuntimeServiceAccess.FileSystem;
        var paths = RuntimeServiceAccess.PathService;
        var platform = RuntimeServiceAccess.PlatformService;
        if (ReferenceEquals(_cacheFileSystem, fileSystem) &&
            ReferenceEquals(_cachePathService, paths) &&
            ReferenceEquals(_cachePlatformService, platform)) return;
        Entries.Clear();
        _cacheFileSystem = fileSystem;
        _cachePathService = paths;
        _cachePlatformService = platform;
    }

    private readonly record struct ResourceKey(string Path, string FolderName, Type AssetType);

    private sealed class Entry(object asset)
    {
        internal object Asset { get; } = asset;
        internal int ReferenceCount { get; set; }
    }
}
