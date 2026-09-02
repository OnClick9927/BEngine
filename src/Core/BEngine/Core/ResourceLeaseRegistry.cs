namespace BEngine;

internal static class ResourceLeaseRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ResourceKey, Entry> Entries = [];

    internal static ResourceHandle<T> Acquire<T>(string path, string folderName) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var key = new ResourceKey(Normalize(path), folderName, typeof(T));
        Entry entry;
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out entry!))
            {
                var asset = ResourceLoader.Load<T>(path, folderName) ??
                            throw new FileNotFoundException(
                                $"Resource '{path}' of type {typeof(T).FullName} was not found.");
                entry = new Entry(asset);
                Entries.Add(key, entry);
            }
            checked { entry.ReferenceCount++; }
        }
        return new ResourceHandle<T>((T)entry.Asset, () => Release(key, entry));
    }

    internal static int GetReferenceCount(object asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        lock (Gate)
            return Entries.Values.Where(entry => ReferenceEquals(entry.Asset, asset))
                .Sum(entry => entry.ReferenceCount);
    }

    internal static int UnloadUnused()
    {
        lock (Gate)
        {
            var unused = Entries.Where(pair => pair.Value.ReferenceCount == 0)
                .Select(pair => pair.Key).ToArray();
            foreach (var key in unused) Entries.Remove(key);
            return unused.Length;
        }
    }

    private static void Release(ResourceKey key, Entry expected)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var entry) || !ReferenceEquals(entry, expected) ||
                entry.ReferenceCount <= 0) return;
            entry.ReferenceCount--;
        }
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private readonly record struct ResourceKey(string Path, string FolderName, Type AssetType);

    private sealed class Entry(object asset)
    {
        internal object Asset { get; } = asset;
        internal int ReferenceCount { get; set; }
    }
}
