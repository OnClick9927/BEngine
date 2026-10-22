namespace BEngine.AssetBundles;

/// <summary>Opt-in installation of an immutable, complete local release into a persistent cache.</summary>
public interface IAssetBundleLocalInstaller
{
    /// <summary>
    /// Verifies version.yaml, its catalog and bundles in <paramref name="releaseDirectory"/>,
    /// then copies missing or corrupt objects before atomically activating the release.
    /// Call before loading any bundles. This also initializes an empty manager.
    /// A valid newer vN cache is retained; matching bundled objects can repair that cache.
    /// </summary>
    BValueTask<AssetBundleInstallResult> InstallBuiltInReleaseAsync(
        string releaseDirectory,
        IProgress<AssetBundleInstallProgress>? progress = null,
        BCancellationToken cancellationToken = default);
}
