using BEngine.Serialization;

namespace BEngine;

/// <summary>Storage-independent decoding used by resource providers on the engine owner thread.</summary>
public static class RuntimeAssetDecoding
{
    public static bool HasPreparedDecoder(Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return RuntimeAssetCodecRegistry.HasPreparedDecoder(assetType);
    }
    public static bool HasFileLoader(Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return RuntimeAssetCodecRegistry.HasFileLoader(assetType);
    }
    public static BAsset? LoadFile(string path, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(assetType);
        return RuntimeAssetCodecRegistry.LoadFile(path, assetType);
    }
    public static string? ResolveArtifactPath(string path, Type assetType) =>
        DependencyInjection.RuntimeServiceContributions.GetDefault<IAssetArtifactResolver>()?.Resolve(path, assetType);

    public static bool HasDecoder(Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return RuntimeAssetCodecRegistry.HasDecoder(assetType);
    }

    public static bool TryDecodeRegistered(ReadOnlyMemory<byte> bytes, string sourcePath, Type assetType,
        out BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(sourcePath);
        return RuntimeAssetCodecRegistry.TryDecode(bytes, sourcePath, assetType, out asset);
    }

    public static BTask<BAsset?> DecodeRegisteredAsync(ReadOnlyMemory<byte> bytes, string sourcePath,
        Type assetType, AsyncLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(context);
        return RuntimeAssetCodecRegistry.DecodeAsync(bytes, sourcePath, assetType, context);
    }

    /// <summary>The gate validates the source revision and invokes the supplied commit once on the owner thread.</summary>
    public static BTask<BAsset?> DecodeRegisteredAsync(ReadOnlyMemory<byte> bytes, string sourcePath,
        Type assetType, AsyncLoadContext context, Func<Func<BAsset>, BAsset> commitGate)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commitGate);
        return RuntimeAssetCodecRegistry.DecodeAsync(bytes, sourcePath, assetType, context, commitGate);
    }

    /// <summary>Decodes text with the same BOM/replacement rules as source assets.</summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes) => TextAsset.Decode(bytes);
    public static string DecodeText(byte[] bytes) => TextAsset.Decode(bytes);

    /// <summary>Uses an installed decoder first, then the engine's fundamental asset formats.
    /// The caller owns the returned object and binds its storage identity separately.</summary>
    public static BAsset? Decode(ReadOnlyMemory<byte> bytes, string sourcePath, Type assetType,
        string? logicalAddress = null)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(sourcePath);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException("A concrete BAsset type is required.", nameof(assetType));
        if (TryDecodeRegistered(bytes, sourcePath, assetType, out var asset)) return asset;
        return DecodeFundamental(DecodeText(bytes.Span), sourcePath, assetType, logicalAddress);
    }

    /// <summary>Reads fundamental formats after provider-specific codecs and file loaders have been tried.</summary>
    public static BAsset? DecodeFundamental(string text, string sourcePath, Type assetType,
        string? logicalAddress = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException("A concrete BAsset type is required.", nameof(assetType));
        if (assetType == typeof(PrefabAsset)) return PrefabAssetSerialization.Deserialize(text, sourcePath);
        if (assetType == typeof(Scene)) return SceneAssetSerialization.Deserialize(text);
        if (assetType == typeof(TextAsset)) return new TextAsset(text, sourcePath);
        if ((logicalAddress ?? sourcePath).EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var document = YamlUtility.Deserialize<ManagedAssetData>(text);
            var storedType = RuntimeTypeCache.ResolveSerializedType(document.TypeName) ??
                throw new InvalidDataException($"Managed asset type '{document.TypeName}' is not loaded.");
            if (!assetType.IsAssignableFrom(storedType))
                throw new InvalidDataException($"Managed asset contains {storedType.FullName}, expected {assetType.FullName}.");
            return ManagedAssetDataOperations.Create(document, storedType) as BAsset;
        }
        return YamlUtility.Deserialize(text, assetType) as BAsset;
    }
}
