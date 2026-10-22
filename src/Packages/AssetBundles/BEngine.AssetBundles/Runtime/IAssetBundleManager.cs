namespace BEngine.AssetBundles;

public interface IAssetBundleManager : IDisposable
{
    AssetBundleCatalog? ActiveCatalog { get; }
    AssetBundleVersion? ActiveVersion { get; }
    bool IsInitialized { get; }

    BValueTask InitializeAsync(BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleUpdatePlan> CheckForUpdatesAsync(BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleUpdateResult> ApplyUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default);
    BValueTask<bool> RollbackAsync(BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleHandle<byte[]>> LoadBytesAsync(
        string address,
        BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleHandle<string>> LoadTextAsync(
        string address,
        BCancellationToken cancellationToken = default);
    bool TryLoadBytes(string address, out byte[] bytes);
    IReadOnlyList<string> EnumerateAddresses(string prefix = "");
    int UnloadUnused();
    BValueTask<int> CleanupAsync(BCancellationToken cancellationToken = default);
    BValueTask DisposeAsync();
}
