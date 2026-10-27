using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BEngine.Serialization;
using BEngine.Platform;

namespace BEngine;

internal static class BAssetReferenceLoader
{
    private static FileUtility FileSystem => RuntimeServiceAccess.FileSystem;
    private static PathUtility Paths => RuntimeServiceAccess.PathService;
    private static PlatformUtility Platform => RuntimeServiceAccess.PlatformService;
    private static readonly Dictionary<CacheKey, AssetCacheEntry> Cache = [];
    private static readonly Dictionary<SubAssetCacheKey, WeakReference<BAsset>> SubAssetCache = [];
    private static readonly Dictionary<SubAssetCacheKey, WeakReference<Sprite>> SpriteSubAssetCache = [];
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
        var fullPath = AssetReferencePath.Resolve(path);
        var fileBacked = FileSystem.FileExists(fullPath);
        if (!fileBacked) return ResourceLoader.LoadAsset(path, assetType, "Resources");
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        if (!Cache.TryGetValue(key, out var pending))
        {
            pending = new AssetCacheEntry();
            Cache.Add(key, pending);
        }

        try
        {
            var asset = pending.GetOrLoad(() => LoadUncached(fullPath, assetType));
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

    internal static bool CanPrepareAsync(Type type) => RuntimeAssetCodecRegistry.HasPreparedDecoder(type) &&
        type != typeof(Texture) && type != typeof(Font) && type != typeof(Script) && type != typeof(Shader) &&
        type != typeof(TextAsset) && type != typeof(Scene) && type != typeof(PrefabAsset);

    internal static async BTask<BAsset?> LoadPreparedAsync(string fullPath, Type assetType, AsyncLoadContext context)
    {
        EnsureServiceContext();
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        if (!Cache.TryGetValue(key, out var pending)) Cache.Add(key, pending = new AssetCacheEntry());
        if (pending.TryGetTarget(out var cached)) return cached;
        var generation = Generation;
        var codecPath = ImportedAssetResolver.ResolveArtifactPath?.Invoke(fullPath, assetType) is { Length: > 0 } imported &&
            FileSystem.FileExists(imported) ? imported : fullPath;
        var stamp = FileSystem.GetLastWriteTimeUtc(codecPath);
        using var stream = FileSystem.OpenRead(codecPath);
        var bytes = await context.ReadAllBytesAsync(stream);
        return await RuntimeAssetCodecRegistry.DecodeAsync(bytes, fullPath, assetType, context, factory =>
        {
            if (generation != Generation || FileSystem.GetLastWriteTimeUtc(codecPath) != stamp)
                throw new InvalidOperationException("The asset changed while it was being prepared.");
            return pending.GetOrLoad(() =>
            {
                var asset = factory();
                asset.BindAssetFile(AssetReferencePath.ToReference(fullPath), fullPath, codecPath,
                    ResolveIdentity(fullPath, assetType), assetType.Name);
                if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
                return asset;
            })!;
        });
    }

    private static BAsset? LoadUncached(string fullPath, Type assetType)
    {
        var codecPath = fullPath;
        if (RuntimeAssetCodecRegistry.HasDecoder(assetType) &&
            ImportedAssetResolver.ResolveArtifactPath?.Invoke(fullPath, assetType) is { Length: > 0 } importedPath &&
            FileSystem.FileExists(importedPath))
            codecPath = importedPath;
        var asset = assetType == typeof(Texture)
            ? LoadKnown(fullPath, assetType)
            : LoadKnown(fullPath, assetType) ?? LoadRegisteredCodec(codecPath, assetType, fullPath) ?? InvokeTypeLoader(fullPath, assetType) ??
              LoadManagedOrYaml(fullPath, assetType);
        if (asset is null) return null;
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Asset '{fullPath}' contains {asset.GetType().FullName}, expected {assetType.FullName}.");
        var identity = asset is PrefabAsset documentPrefab && !FileSystem.FileExists(fullPath + ".meta")
            ? documentPrefab.assetId : ResolveIdentity(fullPath, assetType);
        asset.BindAssetReference(AssetReferencePath.ToReference(fullPath), identity);
        if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
        asset.sourcePath = fullPath;
        asset.artifactPath = codecPath;
        asset.assetType = assetType.Name;
        if (asset is Scene scene) scene.path = asset.assetPath;
        if (asset is PrefabAsset prefab) prefab.assetPath = asset.assetPath;
        return asset;
    }

    private static BAsset? LoadRegisteredCodec(string fullPath, Type assetType, string sourcePath) =>
        RuntimeAssetCodecRegistry.HasDecoder(assetType) &&
        RuntimeAssetCodecRegistry.TryDecode(FileSystem.ReadAllBytes(fullPath), sourcePath, assetType, out var asset)
            ? asset
            : null;

    internal static BAsset? LoadByGuid(string guid, Type assetType)
    {
        EnsureServiceContext();
        if (!Guid.TryParse(guid, out var id)) return null;
        var path = ResolveGuidPath(id);
        return path is null ? ResourceLoader.LoadAsset($"guid:{id:N}", assetType, "Resources") : Load(path, assetType);
    }

    internal static BObject? LoadObjectReference(string path, Type objectType)
    {
        EnsureServiceContext();
        ArgumentNullException.ThrowIfNull(objectType);
        if (typeof(BAsset).IsAssignableFrom(objectType)) return Load(path, objectType);
        if (objectType != typeof(Sprite) ||
            !TryParseSubAssetReference(path, out var mainPath, out var localIdentifier)) return null;
        return LoadSpriteSubAsset(mainPath, localIdentifier);
    }

    private static Sprite? LoadSpriteSubAsset(string path, long localIdentifier)
    {
        if (localIdentifier <= 0 || string.IsNullOrWhiteSpace(path)) return null;
        string mainFullPath;
        if (path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(path.AsSpan("guid:".Length), out var ownerGuid) ||
                ResolveGuidPath(ownerGuid) is not { } resolvedPath)
                return LoadBundledSprite(ownerGuid, localIdentifier);
            mainFullPath = resolvedPath;
        }
        else mainFullPath = AssetReferencePath.Resolve(path);
        if (!FileSystem.FileExists(mainFullPath))
            return path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                   Guid.TryParse(path.AsSpan("guid:".Length), out var missingOwnerGuid)
                ? LoadBundledSprite(missingOwnerGuid, localIdentifier)
                : null;

        var mainMeta = ReadSubAssetMeta(mainFullPath + ".meta");
        if (mainMeta is null || !Guid.TryParse(mainMeta.Guid, out var parentGuid) ||
            !IsKnownImportedRepresentation(mainMeta, localIdentifier, typeof(Sprite))) return null;
        var cacheKey = new SubAssetCacheKey(CanonicalPath(mainFullPath), localIdentifier, typeof(Sprite));
        if (TryGetTarget(SpriteSubAssetCache, cacheKey, out var cached)) return cached;

        if (Load(mainFullPath, typeof(Texture)) is not Texture texture) return null;
        var pivot = new Vector2(
            ReadPivot(mainMeta.Settings, "spritePivotX"),
            ReadPivot(mainMeta.Settings, "spritePivotY"));
        var sprite = texture.CreateSprite(pivot, localIdentifier);
        sprite.border = new(ReadBorder("spriteBorderLeft"), ReadBorder("spriteBorderBottom"),
            ReadBorder("spriteBorderRight"), ReadBorder("spriteBorderTop"));
        sprite.BindSourceIdentity(parentGuid.ToString("N"), localIdentifier,
            AssetReferencePath.ToReference(mainFullPath));
        if (TryGetTarget(SpriteSubAssetCache, cacheKey, out var existing)) return existing;
        SpriteSubAssetCache[cacheKey] = new WeakReference<Sprite>(sprite);
        return sprite;

        Fix64 ReadBorder(string key) => mainMeta.Settings.TryGetValue(key, out var text) &&
            Fix64.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? Fix64.Max(0, value) : Fix64.Zero;
    }

    private static Sprite? LoadBundledSprite(Guid ownerGuid, long localIdentifier)
    {
        if (ownerGuid == Guid.Empty || localIdentifier <= 0) return null;
        var reference = $"guid:{ownerGuid:N}#subasset={localIdentifier}";
        try { return ResourceLoader.Load(reference, typeof(Sprite), "Resources") as Sprite; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                          ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
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
        else mainFullPath = AssetReferencePath.Resolve(path);
        if (!FileSystem.FileExists(mainFullPath))
            return requestedOwnerGuid == Guid.Empty
                ? null
                : LoadBundledSubAsset(requestedOwnerGuid, localIdentifier, assetType);
        var mainReference = AssetReferencePath.ToReference(mainFullPath);
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
                asset.BindSubAssetReference(mainReference, parentGuid, localIdentifier, objectId);
        }
        else if (ResolveFileSubAsset(parentGuid, localIdentifier) is { } childPath)
        {
            asset = LoadUncached(childPath, assetType);
            asset?.BindSubAssetReference(mainReference, parentGuid, localIdentifier, asset.Id);
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
        try { asset = ResourceLoader.LoadAsset(reference, assetType, "Resources"); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                          ArgumentException or IOException)
        {
            return null;
        }
        if (asset is null || !assetType.IsInstanceOfType(asset)) return null;
        if (asset.parentAssetGuid != ownerGuid || asset.localIdentifier != localIdentifier)
            asset.BindSubAssetReference(
                string.IsNullOrWhiteSpace(asset.assetPath) ? $"guid:{ownerGuid:N}" : asset.assetPath,
                ownerGuid,
                localIdentifier,
                asset.Id);
        if (string.IsNullOrWhiteSpace(asset.assetType)) asset.assetType = assetType.Name;
        return asset;
    }

    private static bool IsKnownImportedRepresentation(
        AssetMetaIdentity mainMeta,
        long localIdentifier,
        Type assetType) =>
        localIdentifier == 21300000 && assetType == typeof(Sprite) &&
        mainMeta.Settings.TryGetValue("textureType", out var textureType) &&
        textureType.Equals(nameof(Sprite), StringComparison.OrdinalIgnoreCase);

    internal static void Invalidate(string path)
    {
        Generation++;
        EnsureServiceContext();
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = AssetReferencePath.Resolve(path);
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
            var matchesAsset = assetGuid != Guid.Empty &&
                               (asset.Id == assetGuid || asset.parentAssetGuid == assetGuid);
            var matchesParent = parentGuid != Guid.Empty &&
                                (asset.Id == parentGuid || asset.parentAssetGuid == parentGuid);
            if (key.Path != canonicalPath && !matchesAsset && !matchesParent) continue;
            (staleKeys ??= []).Add(key);
        }
        foreach (var key in staleKeys ?? []) Cache.Remove(key);

        // A sub-asset cache key is rooted at the main asset, so invalidating either a
        // file-backed child or its owner must discard the resolved representation.
        SubAssetCache.Clear();
        SpriteSubAssetCache.Clear();
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
        RemoveCachedValue(SpriteSubAssetCache, target, static reference =>
            reference.TryGetTarget(out var asset) ? asset : null);
    }

    internal static void PruneMissingFiles()
    {
        EnsureServiceContext();
        RemoveMissingPaths(Cache, static key => key.Path);
        RemoveMissingPaths(SubAssetCache, static key => key.Path);
        RemoveMissingPaths(SpriteSubAssetCache, static key => key.Path);
        RemoveDeadReferences(Cache, static entry => entry.TryGetTarget(out _));
        RemoveDeadReferences(SubAssetCache, static reference => reference.TryGetTarget(out _));
        RemoveDeadReferences(SpriteSubAssetCache, static reference => reference.TryGetTarget(out _));
        ResetMetadataIndex();
    }

    internal static void Clear()
    {
        Generation++;
        _cacheFileSystem = FileSystem;
        _cachePathService = Paths;
        _cachePlatformService = Platform;
        ClearCaches();
    }

    private static void ClearCaches()
    {
        Cache.Clear();
        SubAssetCache.Clear();
        SpriteSubAssetCache.Clear();
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

    private static void RemovePendingLoad(CacheKey key, AssetCacheEntry pending)
    {
        // A rejected recursive load must not evict the outer operation's pending entry.
        // Its decoder can recover from that rejection and still publish one cached asset.
        if (pending.IsLoading) return;
        if (Cache.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
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

    private static bool TryGetTarget<TKey, TAsset>(
        IReadOnlyDictionary<TKey, WeakReference<TAsset>> cache,
        TKey key,
        out TAsset asset) where TKey : notnull where TAsset : BObject
    {
        if (cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out asset!) && !asset.isDestroyed) return true;
        asset = null!;
        return false;
    }

    private static string? ResolveGuidPath(Guid guid)
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
                                          InvalidDataException or FormatException or
                                          YamlDotNet.Core.YamlException) { return null; }
    }

    private static void ResetMetadataIndex()
    {
        GuidPaths.Clear();
        FileSubAssetPaths.Clear();
        _metadataIndexDataPath = string.Empty;
        _metadataIndexReady = false;
    }

    private static AssetMetaIdentity? ReadSubAssetMeta(string metaPath)
    {
        if (!FileSystem.FileExists(metaPath)) return null;
        try { return YamlUtility.Deserialize<AssetMetaIdentity>(FileSystem.ReadAllText(metaPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or FormatException or
                                          YamlDotNet.Core.YamlException) { return null; }
    }

    private static bool TryParseSubAssetReference(string reference, out string path, out long localIdentifier)
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
        if (assetType == typeof(Texture))
        {
            if (!Texture.IsSupportedSourcePath(fullPath)) return null;
            var texture = new Texture { name = Paths.GetFileName(fullPath) };
            if (!TryReadImageSize(fullPath, out var width, out var height)) return null;
            texture.width = width;
            texture.height = height;
            ApplyTextureImportSettings(texture, ReadTextureImportSettings(fullPath));
            return texture;
        }
        if (assetType == typeof(Font)) return new Font { name = Paths.GetFileName(fullPath) };
        if (assetType == typeof(Script))
        {
            var script = new Script { name = Paths.GetFileName(fullPath) };
            script.SetImportedContents(FileSystem.ReadAllText(fullPath), null);
            return script;
        }
        if (assetType == typeof(Shader))
            return new Shader(Paths.GetFileNameWithoutExtension(fullPath), FileSystem.ReadAllText(fullPath));
        if (assetType == typeof(TextAsset)) return new TextAsset(FileSystem.ReadAllText(fullPath), fullPath);
        if (assetType == typeof(Scene)) return SceneAssetSerialization.Load(fullPath);
        if (assetType == typeof(PrefabAsset)) return PrefabAssetSerialization.Load(fullPath);
        return null;
    }

    private static BAsset? InvokeTypeLoader(string fullPath, Type assetType)
    {
        var load = assetType.GetMethod(nameof(Material.Load), BindingFlags.Public | BindingFlags.Static |
                                              BindingFlags.DeclaredOnly, binder: null,
            types: [typeof(string)], modifiers: null);
        if (load is null || !assetType.IsAssignableFrom(load.ReturnType)) return null;
        try { return load.Invoke(null, [fullPath]) as BAsset; }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }

    private static BAsset? LoadManagedOrYaml(string fullPath, Type assetType)
    {
        if (fullPath.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var document = YamlUtility.Deserialize<ManagedAssetData>(FileSystem.ReadAllText(fullPath));
            var storedType = ResolveType(document.TypeName) ?? throw new InvalidDataException(
                $"Managed asset type '{document.TypeName}' is not loaded.");
            if (!assetType.IsAssignableFrom(storedType))
                throw new InvalidDataException(
                    $"Managed asset contains {storedType.FullName}, expected {assetType.FullName}.");
            return document.Create(storedType) as BAsset;
        }
        return YamlUtility.Deserialize(FileSystem.ReadAllText(fullPath), assetType) as BAsset;
    }

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
        var stableReference = AssetReferencePath.ToReference(fullPath);
        var identity = Encoding.UTF8.GetBytes($"{stableReference}\n{assetType.AssemblyQualifiedName}");
        var hash = SHA256.HashData(identity);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static bool TryReadMeta(string fullPath, out AssetMetaIdentity meta)
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
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or
                                          YamlDotNet.Core.YamlException)
        {
            Debug.LogWarning($"Could not read asset metadata from {metaPath}: {exception.Message}");
            meta = null!;
            return false;
        }
    }

    private static Fix64 ReadPivot(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) &&
        Fix64.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? Mathf.Clamp01(parsed)
            : Fix64.Half;

    internal static (TextureFilterMode FilterMode, TextureWrapMode WrapMode) ReadTextureSamplingSettings(
        string fullPath)
    {
        EnsureServiceContext();
        var settings = ReadTextureImportSettings(fullPath);
        return (settings.FilterMode, settings.WrapMode);
    }

    private static TextureImportSettings ReadTextureImportSettings(string fullPath)
    {
        if (!TryReadMeta(fullPath, out var meta)) return TextureImportSettings.Default;
        var settings = meta.Settings;
        return new TextureImportSettings(
            ReadBoolean(settings, "sRGBTexture", true),
            ReadBoolean(settings, "alphaIsTransparency", true),
            ReadBoolean(settings, "isReadable", false),
            ReadEnum(settings, "compressionFormat", TextureCompressionFormat.Automatic),
            ReadEnum(settings, "filterMode", TextureFilterMode.Bilinear),
            ReadEnum(settings, "wrapMode", TextureWrapMode.Clamp),
            ReadBoolean(settings, "generateMipMaps", false),
            NormalizeMaxSize(ReadInteger(settings, "maxTextureSize", 2048)),
            Math.Clamp(ReadInteger(settings, "pixelsPerUnit", 100), 1, 10000));
    }

    private static void ApplyTextureImportSettings(Texture texture, TextureImportSettings settings)
    {
        texture.sRGB = settings.SRgbTexture;
        texture.alphaIsTransparency = settings.AlphaIsTransparency;
        texture.isReadable = settings.IsReadable;
        texture.compressionFormat = settings.CompressionFormat;
        texture.filterMode = settings.FilterMode;
        texture.wrapMode = settings.WrapMode;
        texture.mipMaps = settings.GenerateMipMaps;
        texture.maxSize = settings.MaxTextureSize;
        texture.pixelsPerUnit = settings.PixelsPerUnit;
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, string> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadInteger(IReadOnlyDictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static TEnum ReadEnum<TEnum>(
        IReadOnlyDictionary<string, string> settings,
        string key,
        TEnum fallback) where TEnum : struct, Enum =>
        settings.TryGetValue(key, out var value) &&
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static int NormalizeMaxSize(int value)
    {
        var clamped = Math.Clamp(value, 32, 16384);
        var lower = 32;
        while (lower <= clamped / 2) lower *= 2;
        var upper = Math.Min(16384, lower * 2);
        return clamped - lower < upper - clamped ? lower : upper;
    }

    private static string CanonicalPath(string path)
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

    private static bool TryReadImageSize(string path, out int width, out int height) =>
        Rendering.ImageCodec.TryGetInfo(FileSystem.ReadAllBytes(path), out width, out height);

    private readonly record struct CacheKey(string Path, Type AssetType);
    private readonly record struct SubAssetCacheKey(string Path, long LocalIdentifier, Type AssetType);
    private readonly record struct FileSubAssetKey(Guid OwnerGuid, long LocalIdentifier);

    private sealed class AssetCacheEntry
    {
        private WeakReference<BAsset>? _asset;
        private bool _loading;
        internal bool IsLoading => _loading;

        internal BAsset? GetOrLoad(Func<BAsset?> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);
            if (_asset?.TryGetTarget(out var cached) is true && !cached.isDestroyed) return cached;
            if (_loading) throw new InvalidOperationException("A cyclic asset load was detected.");
            _loading = true;

            BAsset? loaded = null;
            try
            {
                loaded = loader();
                return loaded;
            }
            finally
            {
                if (loaded is not null) _asset = new WeakReference<BAsset>(loaded);
                _loading = false;
            }
        }

        internal bool TryGetTarget(out BAsset asset)
        {
            if (_asset?.TryGetTarget(out asset!) is true && !asset.isDestroyed) return true;
            asset = null!;
            return false;
        }
    }

    private sealed class AssetMetaIdentity
    {
        public string Guid { get; set; } = string.Empty;
        public Dictionary<string, string> Settings { get; set; } = [];
        public string ParentGuid { get; set; } = string.Empty;
        public long LocalIdentifier { get; set; }
        public List<SubAssetIdentity> SubAssets { get; set; } = [];
    }

    private sealed class AssetMetaIndexIdentity
    {
        public string Guid { get; set; } = string.Empty;
        public string ParentGuid { get; set; } = string.Empty;
        public long LocalIdentifier { get; set; }
    }

    private sealed class SubAssetIdentity
    {
        public string Guid { get; set; } = string.Empty;
        public long LocalIdentifier { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    private readonly record struct TextureImportSettings(
        bool SRgbTexture,
        bool AlphaIsTransparency,
        bool IsReadable,
        TextureCompressionFormat CompressionFormat,
        TextureFilterMode FilterMode,
        TextureWrapMode WrapMode,
        bool GenerateMipMaps,
        int MaxTextureSize,
        int PixelsPerUnit)
    {
        internal static TextureImportSettings Default => new(
            true,
            true,
            false,
            TextureCompressionFormat.Automatic,
            TextureFilterMode.Bilinear,
            TextureWrapMode.Clamp,
            false,
            2048,
            100);
    }
}
