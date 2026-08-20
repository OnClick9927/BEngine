namespace BEngine.AssetBundles;

public sealed record AssetBundleUpdateProgress(
    AssetBundleUpdatePhase Phase,
    int CompletedBundles,
    int TotalBundles,
    long CompletedBytes,
    long TotalBytes,
    string BundleName = "");
