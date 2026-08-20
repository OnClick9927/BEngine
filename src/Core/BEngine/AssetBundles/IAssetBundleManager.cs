namespace BEngine.AssetBundles;

public interface IAssetBundleManager : IDisposable, IAsyncDisposable
{
    AssetBundleCatalog? ActiveCatalog { get; }
    AssetBundleVersion? ActiveVersion { get; }
    bool IsInitialized { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<AssetBundleUpdatePlan> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<AssetBundleUpdateResult> ApplyUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<bool> RollbackAsync(CancellationToken cancellationToken = default);
    Task<AssetBundleHandle<byte[]>> LoadBytesAsync(
        string address,
        CancellationToken cancellationToken = default);
    Task<AssetBundleHandle<string>> LoadTextAsync(
        string address,
        CancellationToken cancellationToken = default);
    bool TryLoadBytes(string address, out byte[] bytes);
    IReadOnlyList<string> EnumerateAddresses(string prefix = "");
    int UnloadUnused();
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}
