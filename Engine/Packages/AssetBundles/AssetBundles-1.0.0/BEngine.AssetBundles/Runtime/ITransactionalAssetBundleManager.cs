namespace BEngine.AssetBundles;

public interface ITransactionalAssetBundleManager : IAssetBundleManager
{
    bool HasPendingActivation { get; }

    BValueTask CommitPendingActivationAsync(
        BCancellationToken cancellationToken = default);
}
