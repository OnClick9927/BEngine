namespace BEngine.AssetBundles;

/// <summary>RepairedBundleCount is the subset of copied objects that replaced an existing invalid file.</summary>
public sealed record AssetBundleInstallResult(
    bool Updated,
    string PreviousVersion,
    string ActiveVersion,
    bool PreservedNewerRelease,
    int CopiedBundleCount,
    int ReusedBundleCount,
    int RepairedBundleCount,
    long CopiedBytes);
