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
            entry = new Entry(key, asset);
            Entries[key] = entry;
        }
        checked { entry.ReferenceCount++; }
        return new ResourceHandle<T>((T)entry.Asset, entry.ReleaseReference);
    }

    internal static int GetReferenceCount(object asset)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(asset);
        var count = 0;
        foreach (var entry in Entries.Values)
            if (ReferenceEquals(entry.Asset, asset))
                checked { count += entry.ReferenceCount; }
        return count;
    }

    internal static ResourceHandle<T> AcquireLoaded<T>(string path, string folderName, T asset) where T : class
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(asset);
        var key = new ResourceKey(Normalize(path), folderName, typeof(T));
        if (!Entries.TryGetValue(key, out var entry) || entry.Asset is BObject { isDestroyed: true })
        {
            entry = new Entry(key, asset);
            Entries[key] = entry;
        }
        checked { entry.ReferenceCount++; }
        return new ResourceHandle<T>((T)entry.Asset, entry.ReleaseReference);
    }

    internal static int UnloadUnused()
    {
        EnsureServiceContext();
        var removed = 0;
        // .NET Dictionary.Remove keeps active enumerators valid. No callbacks
        // run here, so eviction does not require a second collection of keys.
        foreach (var pair in Entries)
            if (pair.Value.ReferenceCount == 0 && Entries.Remove(pair.Key)) removed++;
        return removed;
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
        return RuntimeServices.PlatformService.IsWindows ? normalized.ToUpperInvariant() : normalized;
    }

    private static void EnsureServiceContext()
    {
        var fileSystem = RuntimeServices.FileSystem;
        var paths = RuntimeServices.PathService;
        var platform = RuntimeServices.PlatformService;
        if (ReferenceEquals(_cacheFileSystem, fileSystem) &&
            ReferenceEquals(_cachePathService, paths) &&
            ReferenceEquals(_cachePlatformService, platform)) return;
        Entries.Clear();
        _cacheFileSystem = fileSystem;
        _cachePathService = paths;
        _cachePlatformService = platform;
    }

    private readonly record struct ResourceKey(string Path, string FolderName, Type AssetType);

    private sealed class Entry
    {
        internal Entry(ResourceKey key, object asset)
        {
            Asset = asset;
            ReleaseReference = () => Release(key, this);
        }

        internal object Asset { get; }
        internal Action ReleaseReference { get; }
        internal int ReferenceCount { get; set; }
    }
}
