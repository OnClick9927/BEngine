using AssetMetaIdentity = BEngine.AssetReferenceMetadata;
using SubAssetIdentity = BEngine.AssetReferenceSubAsset;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine;

internal static class SourceAssetReferenceLoader
{
    private static FileUtility FileSystem => RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
    private static PathUtility Paths => RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
    private static PlatformUtility Platform => RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>();
    private static readonly Dictionary<CacheKey, AssetLoadSlot> Cache = [];
    private static readonly Dictionary<SubAssetCacheKey, WeakReference<BAsset>> SubAssetCache = [];

    private static readonly Dictionary<Guid, string> GuidPaths = [];
    private static readonly Dictionary<FileSubAssetKey, string> FileSubAssetPaths = [];
    private static string _metadataIndexDataPath = string.Empty;
    private static bool _metadataIndexReady;
    private static FileUtility? _cacheFileSystem;
    private static PathUtility? _cachePathService;
    private static PlatformUtility? _cachePlatformService;
    internal static long Generation { get; private set; }

    internal static BAsset? Load(string path, Type assetType)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException($"{assetType.FullName} is not a concrete BAsset type.", nameof(assetType));
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (TryParseSubAssetReference(path, out var mainPath, out var localIdentifier))
            return LoadSubAsset(mainPath, localIdentifier, assetType);
        var fullPath = RuntimeAssetReferences.ResolvePath(path);
        var fileBacked = FileSystem.FileExists(fullPath);
        if (!fileBacked) return Resources.LoadProvidedAsset(path, assetType);
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        if (!Cache.TryGetValue(key, out var pending))
        {
            pending = AssetLoadSlot.Create();
            Cache.Add(key, pending);
        }

        try
        {
            var asset = pending.GetOrLoad((fullPath, assetType), static state =>
                LoadUncached(state.fullPath, state.assetType));
            if (asset is not null) return asset;
            RemovePendingLoad(key, pending);
            return null;
        }
        catch
        {
            RemovePendingLoad(key, pending);
            throw;
        }
    }

    internal static bool CanPrepareAsync(Type type) => RuntimeAssetDecoding.HasPreparedDecoder(type) &&
        type != typeof(TextAsset) && type != typeof(Scene) && type != typeof(PrefabAsset);

    internal static async BTask<BAsset?> LoadPreparedAsync(string fullPath, Type assetType, AsyncLoadContext context)
    {
        EnsureServiceContext();
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        if (!Cache.TryGetValue(key, out var pending)) Cache.Add(key, pending = AssetLoadSlot.Create());
        if (pending.TryGetTarget(out var cached)) return cached;
        var generation = Generation;
        var codecPath = RuntimeAssetDecoding.ResolveArtifactPath(fullPath, assetType) is { Length: > 0 } imported &&
            FileSystem.FileExists(imported) ? imported : fullPath;
        var stamp = FileSystem.GetLastWriteTimeUtc(codecPath);
        using var stream = FileSystem.OpenRead(codecPath);
        var bytes = await context.ReadAllBytesAsync(stream);
        return await RuntimeAssetDecoding.DecodeRegisteredAsync(bytes, fullPath, assetType, context, factory =>
        {
            if (generation != Generation || FileSystem.GetLastWriteTimeUtc(codecPath) != stamp)
                throw new InvalidOperationException("The asset changed while it was being prepared.");
            return pending.GetOrLoad(() =>
            {
                var asset = factory();
                RuntimeAssetReferences.BindSource(asset, RuntimeAssetReferences.ToReferencePath(fullPath), fullPath, codecPath,
                    ResolveIdentity(fullPath, assetType), assetType.Name);
                if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
                return asset;
            })!;
        });
    }

    private static BAsset? LoadUncached(string fullPath, Type assetType)
    {
        var codecPath = fullPath;
        if (RuntimeAssetDecoding.HasDecoder(assetType) &&
            RuntimeAssetDecoding.ResolveArtifactPath(fullPath, assetType) is { Length: > 0 } importedPath &&
            FileSystem.FileExists(importedPath))
            codecPath = importedPath;
        var asset = RuntimeAssetDecoding.HasFileLoader(assetType) ? RuntimeAssetDecoding.LoadFile(fullPath, assetType) :
            LoadKnown(fullPath, assetType) ?? LoadRegisteredCodec(codecPath, assetType, fullPath) ?? InvokeTypeLoader(fullPath, assetType) ??
              LoadManagedOrYaml(fullPath, assetType);
        if (asset is null) return null;
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Asset '{fullPath}' contains {asset.GetType().FullName}, expected {assetType.FullName}.");
        var identity = asset is PrefabAsset documentPrefab && !FileSystem.FileExists(fullPath + ".meta")
            ? documentPrefab.assetId : ResolveIdentity(fullPath, assetType);
        RuntimeAssetReferences.BindSource(asset, RuntimeAssetReferences.ToReferencePath(fullPath), fullPath, codecPath, identity, assetType.Name);
        if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
        if (asset is Scene scene) RuntimeAssetReferences.BindScenePath(scene, asset.assetPath);
        return asset;
    }

    private static BAsset? LoadRegisteredCodec(string fullPath, Type assetType, string sourcePath) =>
        RuntimeAssetDecoding.HasDecoder(assetType) &&
        RuntimeAssetDecoding.TryDecodeRegistered(FileSystem.ReadAllBytes(fullPath), sourcePath, assetType, out var asset)
            ? asset
            : null;

    internal static BAsset? LoadByGuid(string guid, Type assetType)
    {
        EnsureServiceContext();
        if (!Guid.TryParse(guid, out var id)) return null;
        var path = ResolveGuidPath(id);
        return path is null ? Resources.LoadProvidedAsset($"guid:{id:N}", assetType) : Load(path, assetType);
    }

    internal static BObject? LoadObjectReference(string path, Type objectType)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(objectType);
        if (typeof(BAsset).IsAssignableFrom(objectType)) return Load(path, objectType);
        return RuntimeObjectCodecs.Find(objectType)?.ReadReference(path);
    }

    internal static BAsset? LoadSubAsset(string path, long localIdentifier, Type assetType)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract || localIdentifier <= 0 ||
            string.IsNullOrWhiteSpace(path)) return null;
        string mainFullPath;
        Guid requestedOwnerGuid = Guid.Empty;
        if (path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(path.AsSpan("guid:".Length), out requestedOwnerGuid)) return null;
            if (ResolveGuidPath(requestedOwnerGuid) is not { } resolvedPath)
                return LoadBundledSubAsset(requestedOwnerGuid, localIdentifier, assetType);
            mainFullPath = resolvedPath;
        }
        else mainFullPath = RuntimeAssetReferences.ResolvePath(path);
        if (!FileSystem.FileExists(mainFullPath))
            return requestedOwnerGuid == Guid.Empty
                ? null
                : LoadBundledSubAsset(requestedOwnerGuid, localIdentifier, assetType);
        var mainReference = RuntimeAssetReferences.ToReferencePath(mainFullPath);
        var mainMeta = ReadSubAssetMeta(mainFullPath + ".meta");
        if (mainMeta is null || !Guid.TryParse(mainMeta.Guid, out var parentGuid))
            return requestedOwnerGuid == Guid.Empty
                ? null
                : LoadBundledSubAsset(requestedOwnerGuid, localIdentifier, assetType);
        var cacheKey = new SubAssetCacheKey(CanonicalPath(mainFullPath), localIdentifier, assetType);
        if (TryGetTarget(SubAssetCache, cacheKey, out var cached)) return cached;

        BAsset? asset = null;
        var embedded = mainMeta.SubAssets?.FirstOrDefault(item => item.LocalIdentifier == localIdentifier);
        if (embedded is not null && Guid.TryParse(embedded.Guid, out var objectId) &&
            !string.IsNullOrWhiteSpace(embedded.Data))
        {
            var storedType = ResolveType(embedded.TypeName);
            if (storedType is not null && assetType.IsAssignableFrom(storedType))
                asset = YamlUtility.Deserialize(embedded.Data, storedType) as BAsset;
            if (asset is not null)
                RuntimeAssetReferences.BindSubAsset(asset, mainReference, parentGuid, localIdentifier, objectId);
        }
        else if (ResolveFileSubAsset(parentGuid, localIdentifier) is { } childPath)
        {
            asset = LoadUncached(childPath, assetType);
            if (asset is not null) RuntimeAssetReferences.BindSubAsset(asset, mainReference, parentGuid, localIdentifier, asset.Id);
        }
        if (asset is null)
            return requestedOwnerGuid == Guid.Empty
                ? null
                : LoadBundledSubAsset(requestedOwnerGuid, localIdentifier, assetType);
        if (TryGetTarget(SubAssetCache, cacheKey, out var existing)) return existing;
        SubAssetCache[cacheKey] = new WeakReference<BAsset>(asset);
        return asset;
    }

    private static BAsset? LoadBundledSubAsset(Guid ownerGuid, long localIdentifier, Type assetType)
    {
        var reference = $"guid:{ownerGuid:N}#subasset={localIdentifier}";
        BAsset? asset;
        try { asset = Resources.LoadProvidedAsset(reference, assetType); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                          ArgumentException or IOException)
        {
            return null;
        }
        if (asset is null || !assetType.IsInstanceOfType(asset)) return null;
        if (!RuntimeAssetReferences.TryGetSubAssetIdentity(asset, out var currentOwner, out var currentLocalId) ||
            currentOwner != ownerGuid || currentLocalId != localIdentifier)
            RuntimeAssetReferences.BindSubAsset(asset, 
                string.IsNullOrWhiteSpace(asset.assetPath) ? $"guid:{ownerGuid:N}" : asset.assetPath,
                ownerGuid,
                localIdentifier,
                asset.Id);
        RuntimeAssetReferences.EnsureAssetType(asset, assetType.Name);
        return asset;
    }

    internal static void Invalidate(string path)
    {
        Generation++;
        EnsureServiceContext();
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = RuntimeAssetReferences.ResolvePath(path);
        var canonicalPath = CanonicalPath(fullPath);
        var meta = ReadSubAssetMeta(fullPath + ".meta");
        var assetGuid = meta is not null && Guid.TryParse(meta.Guid, out var parsedAssetGuid)
            ? parsedAssetGuid
            : Guid.Empty;
        var parentGuid = meta is not null && Guid.TryParse(meta.ParentGuid, out var parsedParentGuid)
            ? parsedParentGuid
            : Guid.Empty;
        List<CacheKey>? staleKeys = null;
        foreach (var (key, pending) in Cache)
        {
            if (!pending.TryGetTarget(out var asset))
            {
                (staleKeys ??= []).Add(key);
                continue;
            }
            RuntimeAssetReferences.TryGetSubAssetIdentity(asset, out var owner, out _);
            var matchesAsset = assetGuid != Guid.Empty && (asset.Id == assetGuid || owner == assetGuid);
            var matchesParent = parentGuid != Guid.Empty && (asset.Id == parentGuid || owner == parentGuid);
            if (key.Path != canonicalPath && !matchesAsset && !matchesParent) continue;
            (staleKeys ??= []).Add(key);
        }
        foreach (var key in staleKeys ?? []) Cache.Remove(key);

        // A sub-asset cache key is rooted at the main asset, so invalidating either a
        // file-backed child or its owner must discard the resolved representation.
        SubAssetCache.Clear();
        ResetMetadataIndex();
    }

    internal static void Unload(BObject target)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(target);
        RemoveCachedValue(Cache, target, static pending =>
            pending.TryGetTarget(out var asset) ? asset : null);
        RemoveCachedValue(SubAssetCache, target, static reference =>
            reference.TryGetTarget(out var asset) ? asset : null);
    }

    internal static void PruneMissingFiles()
    {
        EnsureServiceContext();
        RemoveMissingPaths(Cache, static key => key.Path);
        RemoveMissingPaths(SubAssetCache, static key => key.Path);
        RemoveDeadReferences(Cache, static entry => entry.TryGetTarget(out _));
        RemoveDeadReferences(SubAssetCache, static reference => reference.TryGetTarget(out _));
        ResetMetadataIndex();
    }

    internal static void Clear()
    {
        Generation++;
        _cacheFileSystem = RuntimeServices.GetService<FileUtility>();
        _cachePathService = RuntimeServices.GetService<PathUtility>();
        _cachePlatformService = RuntimeServices.GetService<PlatformUtility>();
        ClearCaches();
    }

    private static void ClearCaches()
    {
        Cache.Clear();
        SubAssetCache.Clear();
        ResetMetadataIndex();
    }

    private static void EnsureServiceContext()
    {
        var fileSystem = FileSystem;
        var paths = Paths;
        var platform = Platform;
        if (ReferenceEquals(_cacheFileSystem, fileSystem) &&
            ReferenceEquals(_cachePathService, paths) &&
            ReferenceEquals(_cachePlatformService, platform)) return;
        ClearCaches();
        _cacheFileSystem = fileSystem;
        _cachePathService = paths;
        _cachePlatformService = platform;
    }

    private static void RemovePendingLoad(CacheKey key, AssetLoadSlot pending)
    {
        // A rejected recursive load must not evict the outer operation's pending entry.
        // Its decoder can recover from that rejection and still publish one cached asset.
        if (pending.IsLoading) return;
        if (Cache.TryGetValue(key, out var current) && current.Equals(pending))
            Cache.Remove(key);
    }

    private static void RemoveCachedValue<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        BObject target,
        Func<TValue, BObject?> valueSelector) where TKey : notnull
    {
        List<TKey>? removals = null;
        foreach (var (key, value) in cache)
            if (ReferenceEquals(valueSelector(value), target)) (removals ??= []).Add(key);
        foreach (var key in removals ?? []) cache.Remove(key);
    }

    private static void RemoveMissingPaths<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        Func<TKey, string> pathSelector) where TKey : notnull
    {
        List<TKey>? removals = null;
        foreach (var key in cache.Keys)
            if (!FileSystem.FileExists(pathSelector(key))) (removals ??= []).Add(key);
        foreach (var key in removals ?? []) cache.Remove(key);
    }

    private static void RemoveDeadReferences<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        Func<TValue, bool> isAlive) where TKey : notnull
    {
        List<TKey>? removals = null;
        foreach (var (key, value) in cache)
            if (!isAlive(value)) (removals ??= []).Add(key);
        foreach (var key in removals ?? []) cache.Remove(key);
    }

    internal static bool TryGetTarget<TKey, TAsset>(
        IReadOnlyDictionary<TKey, WeakReference<TAsset>> cache,
        TKey key,
        out TAsset asset) where TKey : notnull where TAsset : BObject
    {
        if (cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out asset!) && !asset.isDestroyed) return true;
        asset = null!;
        return false;
    }

    internal static string? ResolveGuidPath(Guid guid)
    {
        EnsureMetadataIndex();
        return GuidPaths.GetValueOrDefault(guid);
    }

    private static string? ResolveFileSubAsset(Guid parentGuid, long localIdentifier)
    {
        EnsureMetadataIndex();
        return FileSubAssetPaths.GetValueOrDefault(new FileSubAssetKey(parentGuid, localIdentifier));
    }

    private static void EnsureMetadataIndex()
    {
        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath)) return;
        var assetsRoot = Paths.GetFullPath(dataPath);
        var canonicalDataPath = CanonicalPath(assetsRoot);
        if (_metadataIndexReady && _metadataIndexDataPath == canonicalDataPath) return;
        ResetMetadataIndex();
        _metadataIndexDataPath = canonicalDataPath;

        IndexMetadataRoot(assetsRoot);
        var projectRoot = Paths.GetFileName(assetsRoot).Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? Paths.GetDirectoryName(assetsRoot)
            : null;
        if (projectRoot is not null)
        {
            IndexMetadataRoot(Paths.Combine(projectRoot, "Packages"));
            IndexMetadataRoot(Paths.Combine(projectRoot, "Library", "Artifacts"));
        }
        _metadataIndexReady = true;
    }

    private static void IndexMetadataRoot(string root)
    {
        if (!FileSystem.DirectoryExists(root)) return;
        try
        {
            foreach (var metaPath in FileSystem.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
            {
                var meta = ReadMetadataIndex(metaPath);
                if (meta is null) continue;
                var sourcePath = metaPath[..^".meta".Length];
                Guid.TryParse(meta.Guid, out var assetGuid);
                Guid.TryParse(meta.ParentGuid, out var parentGuid);

                if (assetGuid != Guid.Empty) GuidPaths.TryAdd(assetGuid, sourcePath);
                if (parentGuid == Guid.Empty) continue;
                if (meta.LocalIdentifier > 0)
                    FileSubAssetPaths.TryAdd(new FileSubAssetKey(parentGuid, meta.LocalIdentifier), sourcePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A concurrently removed package or artifact root is ignored until the next invalidation.
        }
    }

    private static AssetMetaIndexIdentity? ReadMetadataIndex(string metaPath)
    {
        try { return YamlUtility.Deserialize<AssetMetaIndexIdentity>(FileSystem.ReadAllText(metaPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or FormatException) { return null; }
    }

    private static void ResetMetadataIndex()
    {
        GuidPaths.Clear();
        FileSubAssetPaths.Clear();
        _metadataIndexDataPath = string.Empty;
        _metadataIndexReady = false;
    }

    internal static AssetMetaIdentity? ReadSubAssetMeta(string metaPath)
    {
        if (!FileSystem.FileExists(metaPath)) return null;
        try { return YamlUtility.Deserialize<AssetMetaIdentity>(FileSystem.ReadAllText(metaPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or FormatException) { return null; }
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

    private static BAsset? LoadKnown(string fullPath, Type assetType)
    {
        if (assetType == typeof(TextAsset)) return new TextAsset(FileSystem.ReadAllText(fullPath), fullPath);
        if (assetType == typeof(Scene)) return SceneAssetLoading.LoadSource(fullPath);
        if (assetType == typeof(PrefabAsset)) return PrefabDocumentSnapshot.LoadSource(fullPath);
        return null;
    }

    private static BAsset? InvokeTypeLoader(string fullPath, Type assetType)
    {
        var load = assetType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static |
                                              BindingFlags.DeclaredOnly, binder: null,
            types: [typeof(string)], modifiers: null);
        if (load is null || !assetType.IsAssignableFrom(load.ReturnType)) return null;
        try { return load.Invoke(null, [fullPath]) as BAsset; }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }

    private static BAsset? LoadManagedOrYaml(string fullPath, Type assetType) =>
        RuntimeAssetDecoding.DecodeFundamental(FileSystem.ReadAllText(fullPath), fullPath, assetType);

    internal static Type? ResolveType(string assemblyQualifiedName)
        => RuntimeTypeCache.ResolveSerializedType(assemblyQualifiedName);

    internal static Guid ResolveIdentity(string fullPath, Type assetType)
    {
        var metaPath = fullPath + ".meta";
        if (FileSystem.FileExists(metaPath))
        {
            try
            {
                var meta = YamlUtility.Deserialize<AssetMetaIdentity>(FileSystem.ReadAllText(metaPath));
                if (Guid.TryParse(meta.Guid, out var guid)) return guid;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
            {
                Debug.LogWarning($"Could not read asset identity from {metaPath}: {exception.Message}");
            }
        }
        var stableReference = RuntimeAssetReferences.ToReferencePath(fullPath);
        var identity = Encoding.UTF8.GetBytes($"{stableReference}\n{assetType.AssemblyQualifiedName}");
        var hash = SHA256.HashData(identity);
        return new Guid(hash.AsSpan(0, 16));
    }

    internal static bool TryReadMeta(string fullPath, out AssetMetaIdentity meta)
    {
        var metaPath = fullPath + ".meta";
        if (!FileSystem.FileExists(metaPath))
        {
            meta = null!;
            return false;
        }
        try
        {
            meta = YamlUtility.Deserialize<AssetMetaIdentity>(FileSystem.ReadAllText(metaPath));
            meta.Settings ??= [];
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            Debug.LogWarning($"Could not read asset metadata from {metaPath}: {exception.Message}");
            meta = null!;
            return false;
        }
    }

    internal static string CanonicalPath(string path)
    {
        var canonical = Paths.GetFullPath(path).Replace('\\', '/');
        return Platform.IsWindows ? canonical.ToUpperInvariant() : canonical;
    }

    private static string AssetName(string path)
    {
        var name = Paths.GetFileNameWithoutExtension(path);
        return name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? Paths.GetFileNameWithoutExtension(name)
            : name;
    }

    private readonly record struct CacheKey(string Path, Type AssetType);
    private readonly record struct SubAssetCacheKey(string Path, long LocalIdentifier, Type AssetType);
    private readonly record struct FileSubAssetKey(Guid OwnerGuid, long LocalIdentifier);





    private sealed class AssetMetaIndexIdentity
    {
        public string Guid { get; set; } = string.Empty;
        public string ParentGuid { get; set; } = string.Empty;
        public long LocalIdentifier { get; set; }
    }




}
