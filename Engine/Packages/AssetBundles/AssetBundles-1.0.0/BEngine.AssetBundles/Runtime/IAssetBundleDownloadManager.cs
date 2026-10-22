namespace BEngine.AssetBundles;

public interface IAssetBundleDownloadManager
{
    BValueTask<long> GetDownloadSizeAsync(
        IEnumerable<string> bundleNames, BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleUpdateResult> DownloadDependenciesAsync(
        IEnumerable<string> bundleNames,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default);
}
