namespace BEngine.AssetBundles;

public interface IAssetBundleManagerFactory
{
    ITransactionalAssetBundleManager Create(AssetBundleRuntimeOptions options);
}
