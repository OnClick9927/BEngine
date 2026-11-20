namespace BEngine;

/// <summary>Optional source/reference format adapter; Core does not inspect metadata files.</summary>
public interface IAssetReferenceResolver
{
    long Generation { get; }
    BAsset? Load(string path, Type type);
    bool CanPrepareAsync(Type type);
    BTask<BAsset?> LoadPreparedAsync(string path, Type type, AsyncLoadContext context);
    BAsset? LoadByGuid(string guid, Type type);
    BAsset? LoadSubAsset(string path, long localIdentifier, Type type);
    void Invalidate(string path);
    void Unload(BObject target);
    void PruneMissingFiles();
    void Clear();
    string? ResolveGuidPath(Guid guid);
    Guid ResolveIdentity(string path, Type type);
    bool TryReadMetadata(string path, out AssetReferenceMetadata metadata);
    AssetReferenceMetadata? ReadSubAssetMetadata(string metadataPath);
}
