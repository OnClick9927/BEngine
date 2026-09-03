using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BEngine.Documents;

namespace BEngine;

internal static class BAssetReferenceLoader
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Lock Gate = new();
    private static readonly Dictionary<CacheKey, AssetCacheEntry> Cache = [];
    private static readonly Dictionary<SubAssetCacheKey, WeakReference<BAsset>> SubAssetCache = [];
    private static readonly Dictionary<SubAssetCacheKey, WeakReference<Sprite>> SpriteSubAssetCache = [];
    private static readonly Dictionary<Guid, string> GuidPaths = [];
    private static readonly Dictionary<FileSubAssetKey, string> FileSubAssetPaths = [];
    private static string _metadataIndexDataPath = string.Empty;
    private static bool _metadataIndexReady;

    internal static BAsset? Load(string path, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException($"{assetType.FullName} is not a concrete BAsset type.", nameof(assetType));
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (TryParseSubAssetReference(path, out var mainPath, out var localIdentifier))
            return LoadSubAsset(mainPath, localIdentifier, assetType);
        var fullPath = AssetReferencePath.Resolve(path);
        var fileBacked = File.Exists(fullPath);
        if (!fileBacked) return ResourceLoader.LoadAsset(path, assetType, "Resources");
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        AssetCacheEntry pending;
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out pending!))
            {
                pending = new AssetCacheEntry();
                Cache.Add(key, pending);
            }
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

    private static BAsset? LoadUncached(string fullPath, Type assetType)
    {
        var asset = assetType == typeof(Texture)
            ? LoadKnown(fullPath, assetType)
            : LoadKnown(fullPath, assetType) ?? InvokeTypeLoader(fullPath, assetType) ??
              LoadManagedOrYaml(fullPath, assetType);
        if (asset is null) return null;
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Asset '{fullPath}' contains {asset.GetType().FullName}, expected {assetType.FullName}.");
        asset.BindAssetReference(AssetReferencePath.ToReference(fullPath), ResolveIdentity(fullPath, assetType));
        if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
        asset.sourcePath = fullPath;
        asset.artifactPath = fullPath;
        asset.assetType = assetType.Name;
        if (asset is Scene scene) scene.path = asset.assetPath;
        if (asset is PrefabAsset prefab) prefab.assetPath = asset.assetPath;
        return asset;
    }

    internal static BAsset? LoadByGuid(string guid, Type assetType)
    {
        if (!Guid.TryParse(guid, out var id)) return null;
        var path = ResolveGuidPath(id);
        return path is null ? null : Load(path, assetType);
    }

    internal static BObject? LoadObjectReference(string path, Type objectType)
    {
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
        if (!File.Exists(mainFullPath))
            return path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                   Guid.TryParse(path.AsSpan("guid:".Length), out var missingOwnerGuid)
                ? LoadBundledSprite(missingOwnerGuid, localIdentifier)
                : null;

        var mainMeta = ReadSubAssetMeta(mainFullPath + ".meta");
        if (mainMeta is null || !Guid.TryParse(mainMeta.Guid, out var parentGuid) ||
            !IsKnownImportedRepresentation(mainMeta, localIdentifier, typeof(Sprite))) return null;
        var cacheKey = new SubAssetCacheKey(CanonicalPath(mainFullPath), localIdentifier, typeof(Sprite));
        lock (Gate)
            if (TryGetTarget(SpriteSubAssetCache, cacheKey, out var cached)) return cached;

        if (Load(mainFullPath, typeof(Texture)) is not Texture texture) return null;
        var pivot = new Vector2(
            (Fix64)(double)ReadPivot(mainMeta.Settings, "spritePivotX"),
            (Fix64)(double)ReadPivot(mainMeta.Settings, "spritePivotY"));
        var sprite = texture.CreateSprite(pivot, localIdentifier);
        sprite.BindSourceIdentity(parentGuid.ToString("N"), localIdentifier,
            AssetReferencePath.ToReference(mainFullPath));
        lock (Gate)
        {
            if (TryGetTarget(SpriteSubAssetCache, cacheKey, out var existing)) return existing;
            SpriteSubAssetCache[cacheKey] = new WeakReference<Sprite>(sprite);
            return sprite;
        }
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
        if (!File.Exists(mainFullPath))
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
        lock (Gate)
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
        lock (Gate)
        {
            if (TryGetTarget(SubAssetCache, cacheKey, out var existing)) return existing;
            SubAssetCache[cacheKey] = new WeakReference<BAsset>(asset);
            return asset;
        }
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
        lock (Gate)
        {
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
            foreach (var key in staleKeys ?? [])
                Cache.Remove(key);

            // A sub-asset cache key is rooted at the main asset, so invalidating either a
            // file-backed child or its owner must discard the resolved representation.
            SubAssetCache.Clear();
            SpriteSubAssetCache.Clear();
            ResetMetadataIndex();
        }
    }

    internal static void Unload(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (Gate)
        {
            RemoveCachedValue(Cache, target, static pending =>
                pending.TryGetTarget(out var asset) ? asset : null);
            RemoveCachedValue(SubAssetCache, target, static reference =>
                reference.TryGetTarget(out var asset) ? asset : null);
            RemoveCachedValue(SpriteSubAssetCache, target, static reference =>
                reference.TryGetTarget(out var asset) ? asset : null);
        }
    }

    internal static void PruneMissingFiles()
    {
        lock (Gate)
        {
            RemoveMissingPaths(Cache, static key => key.Path);
            RemoveMissingPaths(SubAssetCache, static key => key.Path);
            RemoveMissingPaths(SpriteSubAssetCache, static key => key.Path);
            RemoveDeadReferences(Cache, static entry => entry.TryGetTarget(out _));
            RemoveDeadReferences(SubAssetCache, static reference => reference.TryGetTarget(out _));
            RemoveDeadReferences(SpriteSubAssetCache, static reference => reference.TryGetTarget(out _));
            ResetMetadataIndex();
        }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Cache.Clear();
            SubAssetCache.Clear();
            SpriteSubAssetCache.Clear();
            ResetMetadataIndex();
        }
    }

    private static void RemovePendingLoad(CacheKey key, AssetCacheEntry pending)
    {
        lock (Gate)
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
            if (!File.Exists(pathSelector(key))) (removals ??= []).Add(key);
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
        if (cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out asset!)) return true;
        asset = null!;
        return false;
    }

    private static string? ResolveGuidPath(Guid guid)
    {
        EnsureMetadataIndex();
        lock (Gate)
            return GuidPaths.GetValueOrDefault(guid);
    }

    private static string? ResolveFileSubAsset(Guid parentGuid, long localIdentifier)
    {
        EnsureMetadataIndex();
        lock (Gate)
            return FileSubAssetPaths.GetValueOrDefault(new FileSubAssetKey(parentGuid, localIdentifier));
    }

    private static void EnsureMetadataIndex()
    {
        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath)) return;
        var assetsRoot = Path.GetFullPath(dataPath);
        var canonicalDataPath = CanonicalPath(assetsRoot);
        lock (Gate)
        {
            if (_metadataIndexReady && _metadataIndexDataPath == canonicalDataPath) return;
            ResetMetadataIndex();
            _metadataIndexDataPath = canonicalDataPath;

            IndexMetadataRoot(assetsRoot);
            var projectRoot = Path.GetFileName(assetsRoot).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(assetsRoot)?.FullName
                : null;
            if (projectRoot is not null)
            {
                IndexMetadataRoot(Path.Combine(projectRoot, "Packages"));
                IndexMetadataRoot(Path.Combine(projectRoot, "Library", "Artifacts"));
            }
            _metadataIndexReady = true;
        }
    }

    private static void IndexMetadataRoot(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var metaPath in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
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
        try { return YamlUtility.Deserialize<AssetMetaIndexIdentity>(File.ReadAllText(metaPath)); }
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
        if (!File.Exists(metaPath)) return null;
        try { return YamlUtility.Deserialize<AssetMetaIdentity>(File.ReadAllText(metaPath)); }
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
            var texture = new Texture { name = Path.GetFileName(fullPath) };
            if (!TryReadPngSize(fullPath, out var width, out var height)) return null;
            texture.width = width;
            texture.height = height;
            ApplyTextureImportSettings(texture, ReadTextureImportSettings(fullPath));
            return texture;
        }
        if (assetType == typeof(Font)) return new Font { name = Path.GetFileName(fullPath) };
        if (assetType == typeof(Script))
        {
            var script = new Script { name = Path.GetFileName(fullPath) };
            script.SetImportedContents(File.ReadAllText(fullPath), null);
            return script;
        }
        if (assetType == typeof(Shader))
            return new Shader(Path.GetFileNameWithoutExtension(fullPath), File.ReadAllText(fullPath));
        if (assetType == typeof(TextAsset)) return new TextAsset(File.ReadAllText(fullPath), fullPath);
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
            var document = YamlUtility.Load<ManagedAssetData>(fullPath);
            var storedType = ResolveType(document.TypeName) ?? throw new InvalidDataException(
                $"Managed asset type '{document.TypeName}' is not loaded.");
            if (!assetType.IsAssignableFrom(storedType))
                throw new InvalidDataException(
                    $"Managed asset contains {storedType.FullName}, expected {assetType.FullName}.");
            return YamlUtility.Deserialize(document.Data, storedType) as BAsset;
        }
        return YamlUtility.Deserialize(File.ReadAllText(fullPath), assetType) as BAsset;
    }

    internal static Type? ResolveType(string assemblyQualifiedName)
    {
        var fullName = assemblyQualifiedName.Split(',')[0].Trim();
        var preferred = RuntimeTypeCache.FindType(assemblyQualifiedName) ?? RuntimeTypeCache.FindType(fullName);
        if (preferred is not null) return preferred;
        var resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (resolved is not null) return resolved;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (assembly.GetType(fullName, throwOnError: false, ignoreCase: false) is { } type) return type;
            }
            catch (Exception exception) when (exception is NotSupportedException or FileNotFoundException) { }
        }
        return null;
    }

    private static Guid ResolveIdentity(string fullPath, Type assetType)
    {
        var metaPath = fullPath + ".meta";
        if (File.Exists(metaPath))
        {
            try
            {
                var meta = YamlUtility.Deserialize<AssetMetaIdentity>(File.ReadAllText(metaPath));
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
        if (!File.Exists(metaPath))
        {
            meta = null!;
            return false;
        }
        try
        {
            meta = YamlUtility.Deserialize<AssetMetaIdentity>(File.ReadAllText(metaPath));
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

    private static float ReadPivot(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        float.IsFinite(parsed)
            ? Math.Clamp(parsed, 0, 1)
            : 0.5f;

    internal static (TextureFilterMode FilterMode, TextureWrapMode WrapMode) ReadTextureSamplingSettings(
        string fullPath)
    {
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
        var canonical = Path.GetFullPath(path).Replace('\\', '/');
        return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
    }

    private static string AssetName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    private static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = height = 0;
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) return false;
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == header.Length && TryReadPngSize(header, out width, out height);
    }

    private static bool TryReadPngSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 24 ||
            !bytes[..8].SequenceEqual(PngSignature)) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        return width > 0 && height > 0;
    }

    private readonly record struct CacheKey(string Path, Type AssetType);
    private readonly record struct SubAssetCacheKey(string Path, long LocalIdentifier, Type AssetType);
    private readonly record struct FileSubAssetKey(Guid OwnerGuid, long LocalIdentifier);

    private sealed class AssetCacheEntry
    {
        private readonly object _gate = new();
        private WeakReference<BAsset>? _asset;
        private bool _loading;
        private int _loadingThreadId;

        internal BAsset? GetOrLoad(Func<BAsset?> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);
            lock (_gate)
            {
                if (_asset?.TryGetTarget(out var cached) is true) return cached;
                while (_loading)
                {
                    if (_loadingThreadId == Environment.CurrentManagedThreadId)
                        throw new InvalidOperationException("A cyclic asset load was detected.");
                    Monitor.Wait(_gate);
                    if (_asset?.TryGetTarget(out cached) is true) return cached;
                }
                _loading = true;
                _loadingThreadId = Environment.CurrentManagedThreadId;
            }

            BAsset? loaded = null;
            try
            {
                loaded = loader();
                return loaded;
            }
            finally
            {
                lock (_gate)
                {
                    if (loaded is not null) _asset = new WeakReference<BAsset>(loaded);
                    _loading = false;
                    _loadingThreadId = 0;
                    Monitor.PulseAll(_gate);
                }
            }
        }

        internal bool TryGetTarget(out BAsset asset)
        {
            lock (_gate)
            {
                if (_asset?.TryGetTarget(out asset!) is true) return true;
                asset = null!;
                return false;
            }
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
