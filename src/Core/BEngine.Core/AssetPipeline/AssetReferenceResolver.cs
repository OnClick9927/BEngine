using BEngine.DependencyInjection;

namespace BEngine;

internal sealed class AssetReferenceResolver : IAssetReferenceResolver
{
    internal static void Register()
    {
        RuntimeServiceContributions.RegisterDefault<IAssetReferenceResolver>(new AssetReferenceResolver());
        RuntimeServiceContributions.RegisterDefault<ISceneDocumentCodec>(new SceneDocumentCodec());
        RuntimeServiceContributions.RegisterDefault<IPrefabDocumentCodec>(new PrefabDocumentCodec());
        RuntimeServiceContributions.RegisterDefault<IManagedAssetDocumentCodec>(new ManagedAssetDocumentCodec());
        RuntimeAssemblyCleanup.Register(static _ => SourceAssetReferenceLoader.Clear());
    }
    public long Generation => SourceAssetReferenceLoader.Generation;
    public BAsset? Load(string path, Type type) => SourceAssetReferenceLoader.Load(path, type);
    public bool CanPrepareAsync(Type type) => SourceAssetReferenceLoader.CanPrepareAsync(type);
    public BTask<BAsset?> LoadPreparedAsync(string path, Type type, AsyncLoadContext context) => SourceAssetReferenceLoader.LoadPreparedAsync(path, type, context);
    public BAsset? LoadByGuid(string guid, Type type) => SourceAssetReferenceLoader.LoadByGuid(guid, type);
    public BAsset? LoadSubAsset(string path, long localIdentifier, Type type) => SourceAssetReferenceLoader.LoadSubAsset(path, localIdentifier, type);
    public void Invalidate(string path) => SourceAssetReferenceLoader.Invalidate(path);
    public void Unload(BObject target) => SourceAssetReferenceLoader.Unload(target);
    public void PruneMissingFiles() => SourceAssetReferenceLoader.PruneMissingFiles();
    public void Clear() => SourceAssetReferenceLoader.Clear();
    public string? ResolveGuidPath(Guid guid) => SourceAssetReferenceLoader.ResolveGuidPath(guid);
    public Guid ResolveIdentity(string path, Type type) => SourceAssetReferenceLoader.ResolveIdentity(path, type);
    public bool TryReadMetadata(string path, out AssetReferenceMetadata metadata) => SourceAssetReferenceLoader.TryReadMeta(path, out metadata);
    public AssetReferenceMetadata? ReadSubAssetMetadata(string metadataPath) => SourceAssetReferenceLoader.ReadSubAssetMeta(metadataPath);
}
