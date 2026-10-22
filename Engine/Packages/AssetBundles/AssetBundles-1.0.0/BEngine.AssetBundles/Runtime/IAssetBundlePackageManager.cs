namespace BEngine.AssetBundles;

/// <summary>Optional package maintenance. Ordinary asset loading uses the global address space.</summary>
public interface IAssetBundlePackageManager
{
    IReadOnlyList<AssetBundlePackageDescriptor> GetPackages();
    BValueTask<AssetBundlePackageUpdatePlan> CheckPackageForUpdatesAsync(
        string packageName, BCancellationToken cancellationToken = default);
    BValueTask<AssetBundlePackageUpdatePlan> CheckPackagesForUpdatesAsync(
        IEnumerable<string> packageNames, BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleUpdateResult> ApplyPackageUpdateAsync(
        AssetBundlePackageUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default);
    BValueTask<long> GetPackageDownloadSizeAsync(
        string packageName, BCancellationToken cancellationToken = default);
    BValueTask<AssetBundleUpdateResult> RepairPackageAsync(
        string packageName,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default);
}
