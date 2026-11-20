using BEngine.Serialization;

namespace BEngine.AssetBundles;

/// <summary>Supplies catalog identity and verified bytes to registered object-format codecs.</summary>
public static class AssetBundleAssetLoader
{
    public static BAsset? LoadAsset(IAssetBundleManager manager, string reference, Type assetType)
    {
        ValidateType(assetType, typeof(BAsset));
        return LoadObject(manager, reference, assetType) as BAsset;
    }

    public static BObject? LoadObject(IAssetBundleManager manager, string reference, Type objectType)
    {
        ValidateType(objectType, typeof(BObject));
        var codec = RuntimeResourceCodecs.Find(objectType);
        if (!TryDescribe(manager, reference, out var metadata, codec)) return null;
        if (!CanDecode(codec, metadata, objectType) || !manager.TryLoadBytes(metadata.Address, out var bytes)) return null;
        var value = codec is null ? DecodeAsset(bytes, metadata.SourcePath, metadata.Address, objectType)
            : codec.PreferAssetDecoder && RuntimeAssetDecoding.TryDecodeRegistered(bytes, metadata.SourcePath, objectType, out var overridden)
                ? overridden : codec.Decode(bytes, metadata);
        return Bind(value, metadata, objectType);
    }

    public static async BTask<BAsset?> LoadAssetAsync(IAssetBundleManager manager, string reference,
        Type assetType, AsyncLoadContext context)
    {
        ValidateType(assetType, typeof(BAsset));
        return await LoadObjectAsync(manager, reference, assetType, context) as BAsset;
    }

    public static async BTask<BObject?> LoadObjectAsync(IAssetBundleManager manager, string reference,
        Type objectType, AsyncLoadContext context)
    {
        ValidateType(objectType, typeof(BObject));
        ArgumentNullException.ThrowIfNull(context);
        context.cancellationToken.ThrowIfCancellationRequested();
        var codec = RuntimeResourceCodecs.Find(objectType);
        if (!TryDescribe(manager, reference, out var metadata, codec)) return null;
        if (!CanDecode(codec, metadata, objectType)) return null;
        using var payload = await manager.LoadBytesAsync(metadata.Address, context.cancellationToken);
        BObject? value = null;
        try
        {
            if ((codec is null || codec.PreferAssetDecoder) && RuntimeAssetDecoding.HasDecoder(objectType))
                value = await RuntimeAssetDecoding.DecodeRegisteredAsync(payload.Asset, metadata.SourcePath, objectType, context);
            else
                await context.RunSteps(() =>
                {
                    if (!ReferenceEquals(RuntimeResourceCodecs.Find(objectType), codec))
                        throw new InvalidOperationException("The resource codec changed while data was loading.");
                    value = codec is null ? DecodeAsset(payload.Asset, metadata.SourcePath, metadata.Address, objectType)
                        : codec.Decode(payload.Asset, metadata);
                    return CooperativeWorkStatus.Complete;
                });
            context.cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(RuntimeResourceCodecs.Find(objectType), codec))
                throw new InvalidOperationException("The resource codec changed while data was loading.");
            return Bind(value, metadata, objectType);
        }
        catch
        {
            if (value is not null) BObject.DestroyImmediate(value);
            throw;
        }
    }

    // Canonical object identity is independent of the address of the backing payload.
    internal static string? ResolveObjectAddress(IAssetBundleManager manager, string reference, Type? objectType = null)
    {
        var codec = objectType is null ? null : RuntimeResourceCodecs.Find(objectType);
        if (!TryDescribe(manager, reference, out var metadata, codec)) return null;
        if (objectType is not null && !CanDecode(codec, metadata, objectType)) return null;
        if (metadata.IsSynthetic)
            return (objectType is not null || RuntimeResourceCodecs.CanDecodeSynthetic(metadata))
                ? AssetBundleValidation.CreateSubAssetAddress(metadata.OwnerGuid, metadata.LocalIdentifier) : null;
        if (codec is { SyntheticLocalIdentifier: > 0 } && metadata.LocalIdentifier == 0)
            return AssetBundleValidation.CreateSubAssetAddress(metadata.OwnerGuid, codec.SyntheticLocalIdentifier);
        return metadata.Address;
    }

    internal static string? ResolveReferencePayloadAddress(IAssetBundleManager manager, string reference) =>
        TryDescribe(manager, reference, out var metadata) && (!metadata.IsSynthetic || RuntimeResourceCodecs.CanDecodeSynthetic(metadata))
            ? metadata.Address : null;

    private static bool CanDecode(RuntimeResourceCodec? codec, in RuntimeResourceMetadata metadata, Type objectType) =>
        codec is null ? !metadata.IsSynthetic && metadata.LocalIdentifier == 0 && typeof(BAsset).IsAssignableFrom(objectType)
            : (!metadata.IsSynthetic || codec.SyntheticLocalIdentifier == metadata.LocalIdentifier) && codec.CanDecode(metadata);

    private static bool TryDescribe(IAssetBundleManager manager, string reference, out RuntimeResourceMetadata metadata,
        RuntimeResourceCodec? codec = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        metadata = default;
        if (!manager.IsInitialized) return false;
        var canonical = AssetBundleValidation.NormalizeAddress(reference);
        var fallback = manager is AssetBundleManager or VirtualAssetBundleManager ? null : manager.ActiveCatalog;
        var entry = FindAsset(manager, fallback, canonical);
        var synthetic = false;
        var localIdentifier = entry?.LocalIdentifier ?? 0;
        if (entry is null && AssetBundleValidation.TryParseSubAssetAddress(canonical, out var requestedOwner, out localIdentifier))
        {
            entry = FindMainAsset(manager, fallback, requestedOwner);
            synthetic = true;
        }
        if (entry is null) return false;
        var owner = entry;
        if (entry.LocalIdentifier > 0)
        {
            if (entry.OwnerGuid == Guid.Empty || !entry.Address.Equals(
                AssetBundleValidation.CreateSubAssetAddress(entry.OwnerGuid, entry.LocalIdentifier), StringComparison.Ordinal)) return false;
            owner = FindMainAsset(manager, fallback, entry.OwnerGuid);
            if (owner is null) return false;
            // A module may describe an owner-backed child even when a legacy
            // catalog also contains a child record. Its format policy is identical.
            if (codec?.SyntheticLocalIdentifier == localIdentifier)
            { entry = owner; synthetic = true; }
        }
        var ownerGuid = owner.OwnerGuid == Guid.Empty ? owner.Guid : owner.OwnerGuid;
        metadata = new(entry.Address, AssetBundleResourceProvider.VirtualPathPrefix, owner.Address,
            entry.Guid, ownerGuid, localIdentifier, entry.AssetType, entry.Importer, entry.ImporterSettings, synthetic);
        return true;
    }

    private static BObject? Bind(BObject? value, in RuntimeResourceMetadata metadata, Type objectType)
    {
        if (value is null) return null;
        if (!objectType.IsInstanceOfType(value))
        {
            var actualType = value.GetType();
            BObject.DestroyImmediate(value);
            throw new InvalidDataException($"Resource '{metadata.Address}' contains {actualType.FullName}, expected {objectType.FullName}.");
        }
        if (value is BAsset asset)
        {
            RuntimeAssetReferences.Bind(asset, metadata);
            if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(metadata.Address);
        }
        return value;
    }

    private static void ValidateType(Type type, Type baseType)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!baseType.IsAssignableFrom(type) || type.IsAbstract)
            throw new ArgumentException($"A concrete {baseType.Name} type is required.", nameof(type));
    }

    private static BAsset? DecodeAsset(
        byte[] bytes,
        string virtualPath,
        string address,
        Type assetType)
        => RuntimeAssetDecoding.Decode(bytes, virtualPath, assetType, address);

    private static AssetBundleAsset? FindAsset(
        IAssetBundleManager manager,
        AssetBundleCatalog? fallbackCatalog,
        string canonicalAddress)
    {
        if (manager is AssetBundleManager runtime)
            return runtime.TryGetActiveAsset(canonicalAddress, out var indexed) ? indexed : null;
        if (manager is VirtualAssetBundleManager snapshot)
            return snapshot.TryGetActiveAsset(canonicalAddress, out var indexedSnapshot) ? indexedSnapshot : null;
        return fallbackCatalog?.Assets.FirstOrDefault(candidate =>
            candidate.Address.Equals(canonicalAddress, StringComparison.OrdinalIgnoreCase));
    }

    private static AssetBundleAsset? FindMainAsset(
        IAssetBundleManager manager,
        AssetBundleCatalog? fallbackCatalog,
        Guid ownerGuid)
    {
        if (manager is AssetBundleManager runtime)
            return runtime.TryGetActiveMainAsset(ownerGuid, out var indexed) ? indexed : null;
        if (manager is VirtualAssetBundleManager snapshot)
            return snapshot.TryGetActiveMainAsset(ownerGuid, out var indexedSnapshot) ? indexedSnapshot : null;
        return fallbackCatalog?.Assets.FirstOrDefault(candidate =>
            candidate.LocalIdentifier == 0 &&
            (candidate.OwnerGuid == Guid.Empty ? candidate.Guid : candidate.OwnerGuid) == ownerGuid);
    }

    private static string AssetName(string path)
    {
        var name = GetAssetFileNameWithoutExtension(path);
        return name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".material", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)
            ? GetAssetFileNameWithoutExtension(name)
            : name;
    }

    private static string GetAssetFileNameWithoutExtension(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        var extension = fileName.LastIndexOf('.');
        return extension <= 0 ? fileName : fileName[..extension];
    }

}
