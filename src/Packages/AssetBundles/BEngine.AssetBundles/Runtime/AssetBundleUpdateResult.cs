namespace BEngine.AssetBundles;

public sealed record AssetBundleUpdateResult(
    bool Updated,
    string PreviousVersion,
    string ActiveVersion,
    int DownloadedBundleCount,
    long DownloadedBytes);
