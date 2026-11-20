namespace BEngine.Editor;

/// <summary>Commits imported and embedded object identities without exposing identity setters.
/// Import selection, files, caches, history and notifications belong to the caller.</summary>
public static class EditorAssetBinding
{
    public static void BindImported(BObject value, in EditorAssetInfo asset)
    {
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(value.isDestroyed, value);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.AssetPath);
        ArgumentNullException.ThrowIfNull(asset.SourcePath);
        ArgumentNullException.ThrowIfNull(asset.ArtifactPath);
        ArgumentNullException.ThrowIfNull(asset.AssetType);
        if (asset.Guid == Guid.Empty)
            throw new ArgumentException("An imported object requires a valid identity.", nameof(asset));

        if (value is BAsset persistent)
            RuntimeAssetReferences.BindSource(persistent, asset.AssetPath, asset.SourcePath,
                asset.ArtifactPath, asset.Guid, asset.AssetType);
        else value.Id = asset.Guid;
    }

    /// <summary>Commits an embedded object's identity and owner address. Existing physical origins
    /// are retained; the metadata container and its save/import policy remain outside Core.</summary>
    public static void BindEmbedded(BObject value, in EditorAssetInfo owner, Guid objectId, long localId)
    {
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(value.isDestroyed, value);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.AssetPath);
        if (owner.Guid == Guid.Empty || owner.ParentGuid.HasValue || objectId == Guid.Empty || localId <= 0)
            throw new ArgumentException("An embedded object requires a main owner and valid object/local identities.");

        if (value is BAsset persistent)
            RuntimeAssetReferences.BindSubAsset(persistent, owner.AssetPath, owner.Guid, localId, objectId);
        else value.Id = objectId;
    }
}
