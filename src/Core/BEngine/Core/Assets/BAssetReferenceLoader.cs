using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BEngine.Documents;
using BEngine.Serialization;

namespace BEngine;

internal static class BAssetReferenceLoader
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<CacheKey, BAsset> Cache = [];

    internal static BAsset? Load(string path, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException($"{assetType.FullName} is not a concrete BAsset type.", nameof(assetType));
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fullPath = AssetReferencePath.Resolve(path);
        if (!File.Exists(fullPath)) return null;
        var key = new CacheKey(CanonicalPath(fullPath), assetType);
        lock (Gate)
            if (Cache.TryGetValue(key, out var cached)) return cached;

        var asset = LoadKnown(fullPath, assetType) ?? InvokeTypeLoader(fullPath, assetType) ??
                    LoadManagedOrYaml(fullPath, assetType);
        if (asset is null) return null;
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Asset '{path}' contains {asset.GetType().FullName}, expected {assetType.FullName}.");
        asset.BindAssetReference(AssetReferencePath.ToReference(fullPath), ResolveIdentity(fullPath, assetType));
        if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(fullPath);
        if (asset is FileAsset fileAsset)
        {
            fileAsset.sourcePath = fullPath;
            fileAsset.assetType = assetType.Name;
        }
        if (asset is Scene scene) scene.path = asset.assetPath;
        if (asset is PrefabAsset prefab) prefab.assetPath = asset.assetPath;
        lock (Gate) Cache[key] = asset;
        return asset;
    }

    internal static void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var canonical = CanonicalPath(AssetReferencePath.Resolve(path));
        lock (Gate)
            foreach (var key in Cache.Keys.Where(key => key.Path == canonical).ToArray()) Cache.Remove(key);
    }

    internal static void Clear()
    {
        lock (Gate) Cache.Clear();
    }

    private static BAsset? LoadKnown(string fullPath, Type assetType)
    {
        if (assetType == typeof(Texture))
        {
            var texture = new Texture { name = Path.GetFileName(fullPath) };
            if (TryReadPngSize(fullPath, out var width, out var height))
            {
                texture.width = width;
                texture.height = height;
            }
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

    private sealed class AssetMetaIdentity
    {
        public string Guid { get; set; } = string.Empty;
    }
}
