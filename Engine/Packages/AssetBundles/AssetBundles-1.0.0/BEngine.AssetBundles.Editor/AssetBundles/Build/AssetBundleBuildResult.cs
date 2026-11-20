using BEngine.AssetBundles;

namespace BEngine.Editor;

public sealed class AssetBundleBuildResult
{
    internal AssetBundleBuildResult(
        AssetBundleCatalog catalog,
        AssetBundleVersion version,
        string packageDirectory,
        string versionDirectory,
        bool reusedExistingVersion)
    {
        Catalog = catalog;
        Version = version;
        PackageDirectory = packageDirectory;
        VersionDirectory = versionDirectory;
        ReusedExistingVersion = reusedExistingVersion;
    }

    public AssetBundleCatalog Catalog { get; }
    public AssetBundleVersion Version { get; }
    public string PackageDirectory { get; }
    public string VersionDirectory { get; }
    public bool ReusedExistingVersion { get; }
    public int BuiltBundleCount { get; internal init; }
    public int ReusedBundleCount { get; internal init; }
    public IReadOnlyList<AssetBundlePackageBuildResult> Packages { get; internal init; } = [];
}
