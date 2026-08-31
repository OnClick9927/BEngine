using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BEngine.Documents;

namespace BEngine;

internal static class BAssetReferenceLoader
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<CacheKey, BAsset> Cache = [];
    private static readonly Dictionary<SubAssetCacheKey, BAsset> SubAssetCache = [];
    private static readonly Dictionary<Guid, string> GuidPaths = [];

    internal static BAsset? Load(string path, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException($"{assetType.FullName} is not a concrete BAsset type.", nameof(assetType));
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (TryParseSubAssetReference(path, out var mainPath, out var localIdentifier))
            return LoadSubAsset(mainPath, localIdentifier, assetType);
        var fullPath = AssetReferencePath.Resolve(path);
        if (!File.Exists(fullPath)) return null;
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        lock (Gate)
            if (Cache.TryGetValue(key, out var cached)) return cached;

        var asset = LoadUncached(fullPath, assetType);
        if (asset is null) return null;
        lock (Gate) Cache[key] = asset;
        return asset;
    }

    private static BAsset? LoadUncached(string fullPath, Type assetType)
    {
        var asset = assetType == typeof(Sprite)
            ? LoadSprite(fullPath)
            : assetType == typeof(Texture)
                ? LoadKnown(fullPath, assetType)
                : LoadKnown(fullPath, assetType) ?? InvokeTypeLoader(fullPath, assetType) ??
                  LoadManagedOrYaml(fullPath, assetType);
        if (asset is null) return null;
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Asset '{fullPath}' contains {asset.GetType().FullName}, expected {assetType.FullName}.");
        asset.BindAssetReference(AssetReferencePath.ToReference(fullPath), ResolveIdentity(fullPath, assetType));
        if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
        if (asset is FileAsset fileAsset)
        {
            fileAsset.sourcePath = fullPath;
            fileAsset.assetType = assetType.Name;
        }
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

    internal static BAsset? LoadSubAsset(string path, long localIdentifier, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract || localIdentifier <= 0 ||
            string.IsNullOrWhiteSpace(path)) return null;
        string mainFullPath;
        if (path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(path.AsSpan("guid:".Length), out var ownerGuid) ||
                ResolveGuidPath(ownerGuid) is not { } resolvedPath) return null;
            mainFullPath = resolvedPath;
        }
        else mainFullPath = AssetReferencePath.Resolve(path);
        if (!File.Exists(mainFullPath)) return null;
        var mainReference = AssetReferencePath.ToReference(mainFullPath);
        var mainMeta = ReadSubAssetMeta(mainFullPath + ".meta");
        if (mainMeta is null || !Guid.TryParse(mainMeta.Guid, out var parentGuid)) return null;
        var cacheKey = new SubAssetCacheKey(CanonicalPath(mainFullPath), localIdentifier, assetType);
        lock (Gate)
            if (SubAssetCache.TryGetValue(cacheKey, out var cached)) return cached;

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
        else if (IsKnownImportedRepresentation(mainMeta, localIdentifier, assetType))
        {
            // Imported representations such as a Texture's Sprite share the main source file
            // and have a stable importer-defined local identifier rather than a child meta file.
            asset = Load(mainFullPath, assetType);
            asset?.BindSubAssetReference(mainReference, parentGuid, localIdentifier, asset.Id);
        }
        if (asset is null) return null;
        lock (Gate) SubAssetCache[cacheKey] = asset;
        return asset;
    }

    private static bool IsKnownImportedRepresentation(
        AssetMetaIdentity mainMeta,
        long localIdentifier,
        Type assetType) =>
        localIdentifier == 21300000 && assetType == typeof(Sprite) &&
        mainMeta.Settings.TryGetValue("textureType", out var textureType) &&
        textureType.Equals("Sprite", StringComparison.OrdinalIgnoreCase);

    internal static void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = AssetReferencePath.Resolve(path);
        var affectedPaths = AssociatedCachePaths(fullPath);
        lock (Gate)
        {
            foreach (var key in Cache.Keys.Where(key => affectedPaths.Contains(key.Path)).ToArray())
                Cache.Remove(key);

            // A sub-asset cache key is rooted at the main asset, so invalidating either a
            // file-backed child or its owner must discard the resolved representation.
            SubAssetCache.Clear();
            GuidPaths.Clear();
        }
    }

    private static HashSet<string> AssociatedCachePaths(string fullPath)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal) { CanonicalPath(fullPath) };
        var meta = ReadSubAssetMeta(fullPath + ".meta");
        if (meta is null) return paths;

        Guid.TryParse(meta.Guid, out var assetGuid);
        Guid.TryParse(meta.ParentGuid, out var parentGuid);
        if (assetGuid == Guid.Empty && parentGuid == Guid.Empty) return paths;

        foreach (var metaPath in EnumerateMetadataPaths())
        {
            var candidate = ReadSubAssetMeta(metaPath);
            if (candidate is null) continue;
            if (parentGuid != Guid.Empty && Guid.TryParse(candidate.Guid, out var candidateGuid) &&
                candidateGuid == parentGuid)
            {
                paths.Add(CanonicalPath(metaPath[..^".meta".Length]));
            }
            if (assetGuid != Guid.Empty && Guid.TryParse(candidate.ParentGuid, out var candidateParent) &&
                candidateParent == assetGuid)
            {
                paths.Add(CanonicalPath(metaPath[..^".meta".Length]));
            }
        }
        return paths;
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Cache.Clear();
            SubAssetCache.Clear();
            GuidPaths.Clear();
        }
    }

    private static string? ResolveGuidPath(Guid guid)
    {
        lock (Gate)
            if (GuidPaths.TryGetValue(guid, out var cached)) return cached;

        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath)) return null;
        var assetsRoot = Path.GetFullPath(dataPath);
        var projectRoot = Path.GetFileName(assetsRoot).Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(assetsRoot)?.FullName
            : null;
        var roots = projectRoot is null
            ? [assetsRoot]
            : new[] { assetsRoot, Path.Combine(projectRoot, "Packages") };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> metadata;
            try { metadata = Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var metaPath in metadata)
            {
                try
                {
                    var meta = YamlUtility.Deserialize<AssetMetaIdentity>(File.ReadAllText(metaPath));
                    if (!Guid.TryParse(meta.Guid, out var candidate) || candidate != guid) continue;
                    var sourcePath = metaPath[..^".meta".Length];
                    lock (Gate) GuidPaths[guid] = sourcePath;
                    return sourcePath;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                  InvalidDataException or FormatException or
                                                  YamlDotNet.Core.YamlException) { }
            }
        }
        return null;
    }

    private static string? ResolveFileSubAsset(Guid parentGuid, long localIdentifier)
    {
        foreach (var metaPath in EnumerateMetadataPaths())
        {
            var meta = ReadSubAssetMeta(metaPath);
            if (meta is null || meta.LocalIdentifier != localIdentifier ||
                !Guid.TryParse(meta.ParentGuid, out var parent) || parent != parentGuid) continue;
            return metaPath[..^".meta".Length];
        }
        return null;
    }

    private static IEnumerable<string> EnumerateMetadataPaths()
    {
        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath)) yield break;
        var assetsRoot = Path.GetFullPath(dataPath);
        var projectRoot = Path.GetFileName(assetsRoot).Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(assetsRoot)?.FullName
            : null;
        var roots = projectRoot is null
            ? [assetsRoot]
            : new[] { assetsRoot, Path.Combine(projectRoot, "Packages") };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            string[] metadata;
            try { metadata = Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var path in metadata) yield return path;
        }
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
        if (assetType == typeof(Scene)) return Document.LoadBObject<SceneDocument, Scene>(fullPath);
        if (assetType == typeof(PrefabAsset)) return Document.LoadBObject<PrefabDocument, PrefabAsset>(fullPath);
        return null;
    }

    private static Sprite? LoadSprite(string fullPath)
    {
        if (fullPath.EndsWith(".sprite.yaml", StringComparison.OrdinalIgnoreCase))
            return Sprite.Load(fullPath);
        if (!Texture.IsSupportedSourcePath(fullPath) || !TryReadMeta(fullPath, out var meta) ||
            !meta.Settings.TryGetValue("textureType", out var textureType) ||
            !textureType.Equals("Sprite", StringComparison.OrdinalIgnoreCase))
            return null;

        var reference = AssetReferencePath.ToReference(fullPath);
        var pivot = new Vector2(
            (Fix64)(double)ReadPivot(meta.Settings, "spritePivotX"),
            (Fix64)(double)ReadPivot(meta.Settings, "spritePivotY"));
        return Sprite.FromTexture(reference, pivot, reference);
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

    private static BAsset? LoadManagedOrYaml(string fullPath, Type assetType)
    {
        if (fullPath.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var document = Document.Load<ManagedAssetDocument>(fullPath);
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
        var resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (resolved is not null) return resolved;
        var fullName = assemblyQualifiedName.Split(',')[0].Trim();
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
        if (stream.Read(header) != header.Length ||
            !header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return width > 0 && height > 0;
    }

    private readonly record struct CacheKey(string Path, Type AssetType);
    private readonly record struct SubAssetCacheKey(string Path, long LocalIdentifier, Type AssetType);

    private sealed class AssetMetaIdentity
    {
        public string Guid { get; set; } = string.Empty;
        public Dictionary<string, string> Settings { get; set; } = [];
        public string ParentGuid { get; set; } = string.Empty;
        public long LocalIdentifier { get; set; }
        public List<SubAssetIdentity> SubAssets { get; set; } = [];
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
