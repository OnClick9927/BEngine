using BEngine.Serialization;

namespace BEngine;

/// <summary>Identity and path services for storage adapters; cache implementation remains private.</summary>
public static class RuntimeAssetReferences
{
    public static string ResolvePath(string path) => AssetReferencePath.Resolve(path);
    public static string ToReferencePath(string path) => AssetReferencePath.ToReference(path);
    public static Guid ResolveIdentity(string path, Type assetType) => BAssetReferenceLoader.ResolveIdentity(path, assetType);
    public static string? ResolveGuidPath(Guid guid) => BAssetReferenceLoader.ResolveGuidPath(guid);
    public static string CanonicalPath(string path) => BAssetReferenceLoader.CanonicalPath(path);
    public static bool TryParseSubAssetReference(string reference, out string path, out long localIdentifier) =>
        BAssetReferenceLoader.TryParseSubAssetReference(reference, out path, out localIdentifier);

    /// <summary>Queries the source resolver's child-metadata extension point. The address and
    /// format belong to the caller and resolver; Core does not read or interpret the payload.</summary>
    public static AssetReferenceMetadata? ReadSubAssetMetadata(string metadataPath) =>
        BAssetReferenceLoader.ReadSubAssetMeta(metadataPath);

    /// <summary>Queries source metadata through the active reference resolver, without reading source files in Core.</summary>
    public static bool TryReadMetadata(string path, out AssetReferenceMetadata metadata) =>
        BAssetReferenceLoader.TryReadMeta(path, out metadata);

    public static bool TryGetSubAssetIdentity(BAsset asset, out Guid ownerId, out long localId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ownerId = asset.parentAssetGuid.GetValueOrDefault();
        localId = asset.localIdentifier;
        return asset.parentAssetGuid.HasValue;
    }

    public static void EnsureAssetType(BAsset asset, string assetType)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(assetType);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        if (string.IsNullOrWhiteSpace(asset.assetType)) asset.assetType = assetType;
    }

    /// <summary>Binds an address without importing the asset or assigning a persistent identity.
    /// A supplied source path replaces the physical source; null preserves it. No file is read,
    /// decoded or cached. Object identity, GUID text and artifact information remain unchanged;
    /// any prior sub-asset association is cleared. Use BindSubAsset for child references.</summary>
    public static void BindReference(BAsset asset, string address, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(address);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        asset.BindAssetReference(address);
        if (sourcePath is not null) asset.sourcePath = sourcePath;
    }

    public static void BindSource(BAsset asset, string address, string sourcePath, string artifactPath,
        Guid id, string assetType)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(artifactPath);
        ArgumentNullException.ThrowIfNull(assetType);
        asset.BindAssetFile(address, sourcePath, artifactPath, id, assetType);
    }

    public static void BindSubAsset(BAsset asset, string ownerAddress, Guid ownerId, long localId, Guid objectId,
        string? defaultAssetType = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        ArgumentNullException.ThrowIfNull(ownerAddress);
        asset.BindSubAssetReference(ownerAddress, ownerId, localId, objectId);
        if (string.IsNullOrWhiteSpace(asset.assetType) && defaultAssetType is not null) asset.assetType = defaultAssetType;
    }

    public static bool IsTransient(BObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IsRuntimeOnly || value is Scene scene && SceneRuntime.IsRunningScene(scene) ||
            value is GameObject gameObject && gameObject.SceneUnchecked is { } owner &&
            (owner.IsRuntimeOnly || SceneRuntime.IsRunningScene(owner));
    }
    public static void BindScenePath(Scene scene, string path)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(path);
        ObjectDisposedException.ThrowIf(scene.isDestroyed, scene);
        scene.path = path;
    }

    public static void Bind(BAsset asset, in RuntimeResourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Address);
        if (metadata.Guid == Guid.Empty || metadata.LocalIdentifier < 0 ||
            metadata.LocalIdentifier > 0 &&
            (metadata.OwnerGuid == Guid.Empty || string.IsNullOrWhiteSpace(metadata.OwnerAddress)))
            throw new ArgumentException("Resource identity must contain a valid GUID and owner for a sub-asset.", nameof(metadata));
        asset.BindAssetFile(metadata.LocalIdentifier > 0 ? metadata.OwnerAddress : metadata.Address,
            string.Empty, string.Empty, metadata.Guid, metadata.AssetType);
        if (metadata.LocalIdentifier > 0)
            asset.BindSubAssetReference(metadata.OwnerAddress, metadata.OwnerGuid, metadata.LocalIdentifier, metadata.Guid);
        if (asset is Scene scene) scene.path = metadata.Address;
    }

    public static void ValidateOptionalRelativePath(string path, string fieldName) =>
        AssetDataValidation.ValidateOptionalRelativePath(path, fieldName);
}
