namespace BEngine.AssetBundles;

public sealed class AssetBundleUpdatePlan
{
    internal AssetBundleUpdatePlan(
        AssetBundleVersion targetVersion,
        AssetBundleCatalog targetCatalog,
        byte[] catalogBytes,
        IReadOnlyList<AssetBundleDescriptor> downloads,
        bool hasUpdates)
    {
        TargetVersion = targetVersion;
        TargetCatalog = targetCatalog;
        CatalogBytes = catalogBytes;
        Downloads = downloads;
        HasUpdates = hasUpdates;
    }

    public AssetBundleVersion TargetVersion { get; }
    public AssetBundleCatalog TargetCatalog { get; }
    public IReadOnlyList<AssetBundleDescriptor> Downloads { get; }
    public bool HasUpdates { get; }
    public long DownloadSize => Downloads.Sum(item => item.Size);
    internal byte[] CatalogBytes { get; }
}
