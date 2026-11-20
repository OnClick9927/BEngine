using BEngine.Platform;
using BEngine.DependencyInjection;

namespace BEngine;

/// <summary>Generic resource reference facade; source formats are supplied by an optional resolver.</summary>
internal static class BAssetReferenceLoader
{
    private static readonly Dictionary<(string Path, Type Type), AssetCacheEntry> FileCache = [];
    private static long _generation;
    static BAssetReferenceLoader() => RuntimeAssemblyCleanup.Register(static _ => ClearFileCache());
    private static PathUtility Paths => RuntimeServices.PathService;
    private static PlatformUtility Platform => RuntimeServices.PlatformService;
    private static IAssetReferenceResolver? Resolver => RuntimeServiceContributions.GetDefault<IAssetReferenceResolver>();
    internal static long Generation => _generation + RuntimeServiceContributions.Version + (Resolver?.Generation ?? 0);
    internal static BAsset? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (Resolver is { } resolver) return resolver.Load(path, type);
        // Logical content can be supplied by an archive, bundle or remote
        // provider even when this asset type also has a physical-file decoder.
        // A source resolver, when installed, continues to own source precedence.
        if (!Paths.IsPathRooted(path) && ResourceLoader.LoadAsset(path, type, "Resources") is { } provided)
            return provided;
        if (RuntimeAssetCodecRegistry.HasFileLoader(type))
        {
            var key = (CanonicalPath(path), type);
            if (!FileCache.TryGetValue(key, out var entry)) FileCache.Add(key, entry = new());
            try
            {
                var loaded = entry.GetOrLoad((path, type), static state =>
                    RuntimeAssetCodecRegistry.LoadFile(state.path, state.type));
                if (loaded is null) FileCache.Remove(key);
                return loaded;
            }
            catch { FileCache.Remove(key); throw; }
        }
        return ResourceLoader.LoadAsset(path, type, "Resources");
    }
    internal static bool CanPrepareAsync(Type type) => Resolver?.CanPrepareAsync(type) == true;
    internal static BTask<BAsset?> LoadPreparedAsync(string path, Type type, AsyncLoadContext context) =>
        Resolver is { } resolver ? resolver.LoadPreparedAsync(path, type, context) : BTask<BAsset?>.FromResult(null);
    internal static BAsset? LoadByGuid(string guid, Type type) => Resolver is { } resolver ? resolver.LoadByGuid(guid, type) : ResourceLoader.LoadAsset($"guid:{guid}", type, "Resources");
    internal static BObject? LoadObjectReference(string path, Type type) =>
        typeof(BAsset).IsAssignableFrom(type) ? Load(path, type) : RuntimeObjectCodecs.Find(type)?.ReadReference(path);
    internal static BAsset? LoadSubAsset(string path, long id, Type type) => Resolver?.LoadSubAsset(path, id, type);
    internal static void Invalidate(string path)
    {
        var canonical = CanonicalPath(path);
        // Dictionary removal preserves active enumerators on supported runtimes.
        foreach (var pair in FileCache)
            if (pair.Key.Path == canonical) FileCache.Remove(pair.Key);
        _generation++;
        Resolver?.Invalidate(path);
    }
    internal static void Unload(BObject target)
    {
        foreach (var pair in FileCache)
            if (!pair.Value.TryGetTarget(out var asset) || ReferenceEquals(asset, target)) FileCache.Remove(pair.Key);
        Resolver?.Unload(target);
    }
    internal static void PruneMissingFiles()
    {
        // The usual all-live pass only needs values, avoiding key/value struct copies.
        foreach (var entry in FileCache.Values)
            if (!entry.TryGetTarget(out _))
            {
                RemoveDeadFileEntries();
                break;
            }
        Resolver?.PruneMissingFiles();
    }
    private static void RemoveDeadFileEntries()
    {
        foreach (var pair in FileCache)
            if (!pair.Value.TryGetTarget(out _)) FileCache.Remove(pair.Key);
    }
    internal static void Clear() { ClearFileCache(); Resolver?.Clear(); RuntimeObjectCodecs.ClearReferences(); }
    private static void ClearFileCache() { FileCache.Clear(); _generation++; }
    internal static string? ResolveGuidPath(Guid guid) => Resolver?.ResolveGuidPath(guid);
    internal static Guid ResolveIdentity(string path, Type type) => Resolver?.ResolveIdentity(path, type) ?? Guid.Empty;
    internal static Type? ResolveType(string name) => RuntimeTypeCache.ResolveSerializedType(name);

    internal static bool TryGetTarget<TKey, TAsset>(
        IReadOnlyDictionary<TKey, WeakReference<TAsset>> cache,
        TKey key,
        out TAsset asset) where TKey : notnull where TAsset : BObject
    {
        if (cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out asset!) && !asset.isDestroyed) return true;
        asset = null!;
        return false;
    }

    internal static bool TryParseSubAssetReference(string reference, out string path, out long localIdentifier)
    {
        const string separator = "#subasset=";
        var index = reference.LastIndexOf(separator, StringComparison.OrdinalIgnoreCase);
        if (index > 0 && long.TryParse(reference.AsSpan(index + separator.Length),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out localIdentifier) && localIdentifier > 0)
        {
            path = reference[..index];
            return true;
        }
        path = reference;
        localIdentifier = 0;
        return false;
    }

    internal static string CanonicalPath(string path)
    {
        var canonical = Paths.GetFullPath(path).Replace('\\', '/');
        return Platform.IsWindows ? canonical.ToUpperInvariant() : canonical;
    }

    internal static bool TryReadMeta(string path, out AssetReferenceMetadata meta)
    {
        if (Resolver is { } resolver) return resolver.TryReadMetadata(path, out meta);
        meta = new(); return false;
    }
    internal static AssetReferenceMetadata? ReadSubAssetMeta(string path) => Resolver?.ReadSubAssetMetadata(path);
}
