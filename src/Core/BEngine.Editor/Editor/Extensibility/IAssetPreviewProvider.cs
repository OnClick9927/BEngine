namespace BEngine.Editor;
public interface IAssetPreviewProvider
{
    bool CanPreview(BObject asset);
    AssetPreviewImage? GetPreview(BObject asset);
    string GetIcon(BObject asset);
    string GetInfo(BObject asset);
    void Draw(BObject asset, Rect area);
}
internal static class AssetPreviewProviders
{
    internal static IAssetPreviewProvider? Find(BObject asset)
    {
        foreach (var type in TypeCache.GetTypesDerivedFrom<IAssetPreviewProvider>().Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) is not null))
            if (Activator.CreateInstance(type) is IAssetPreviewProvider provider && provider.CanPreview(asset)) return provider;
        return null;
    }
}
