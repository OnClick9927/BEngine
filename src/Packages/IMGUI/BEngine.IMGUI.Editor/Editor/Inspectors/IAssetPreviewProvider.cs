namespace BEngine.Editor;
[RuntimeTypeAlias("BEngine.Editor.IAssetPreviewProvider, BEngine.Editor")]
[RuntimeTypeAlias("BEngine.Editor.IAssetPreviewProvider, BEngine.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
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
    private static int _generation = -1;
    private static IAssetPreviewProvider[] _providers = [];
    internal static void Invalidate() { _providers = []; _generation = -1; }
    internal static IAssetPreviewProvider? Find(BObject asset)
    {
        var generation=RuntimeTypeCache.stats.Generation;
        if (_generation!=generation)
        {
            _providers=TypeCache.GetTypesDerivedFrom<IAssetPreviewProvider>()
                .Where(t=>!t.IsAbstract&&!t.IsInterface&&t.GetConstructor(Type.EmptyTypes)!=null)
                .Select(t=>(IAssetPreviewProvider)Activator.CreateInstance(t)!).ToArray();
            _generation=generation;
        }
        foreach(var provider in _providers) if(provider.CanPreview(asset)) return provider;
        return null;
    }
}
