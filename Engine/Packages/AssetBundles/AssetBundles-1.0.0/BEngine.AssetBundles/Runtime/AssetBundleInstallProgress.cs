namespace BEngine.AssetBundles;

public enum AssetBundleInstallPhase
{
    Verifying,
    Copying,
    Activating,
    Completed
}

public sealed record AssetBundleInstallProgress(
    AssetBundleInstallPhase Phase,
    int CompletedBundles,
    int TotalBundles,
    long CompletedBytes,
    long TotalBytes,
    string BundleName = "");
