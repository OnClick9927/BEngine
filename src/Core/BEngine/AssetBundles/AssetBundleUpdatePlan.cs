namespace BEngine.AssetBundles;

public sealed class AssetBundleUpdatePlan
{
    private readonly byte[] _targetVersionBytes;
    private readonly byte[] _catalogBytes;
    private readonly AssetBundleDescriptor[] _downloads;
    private readonly long _downloadSize;

    internal AssetBundleUpdatePlan(
        AssetBundleVersion targetVersion,
        AssetBundleCatalog targetCatalog,
        byte[] catalogBytes,
        IReadOnlyList<AssetBundleDescriptor> downloads,
        bool hasUpdates)
    {
        ArgumentNullException.ThrowIfNull(targetVersion);
        ArgumentNullException.ThrowIfNull(targetCatalog);
        ArgumentNullException.ThrowIfNull(catalogBytes);
        ArgumentNullException.ThrowIfNull(downloads);
        _targetVersionBytes = AssetBundleCatalogSerializer.SerializeVersion(targetVersion);
        _catalogBytes = (byte[])catalogBytes.Clone();
        _ = AssetBundleCatalogSerializer.DeserializeCatalog(_catalogBytes);
        _downloads = downloads.Select(CloneDescriptor).ToArray();
        _downloadSize = _downloads.Aggregate(0L, (total, item) => checked(total + item.Size));
        HasUpdates = hasUpdates;
    }

    public AssetBundleVersion TargetVersion => CreateTargetVersionSnapshot();
    public AssetBundleCatalog TargetCatalog => CreateTargetCatalogSnapshot();
    public IReadOnlyList<AssetBundleDescriptor> Downloads =>
        Array.AsReadOnly(_downloads.Select(CloneDescriptor).ToArray());
    public bool HasUpdates { get; }
    public long DownloadSize => _downloadSize;
    internal ReadOnlyMemory<byte> CatalogBytes => _catalogBytes;

    internal AssetBundleVersion CreateTargetVersionSnapshot() =>
        AssetBundleCatalogSerializer.DeserializeVersion(_targetVersionBytes);

    internal AssetBundleCatalog CreateTargetCatalogSnapshot() =>
        AssetBundleCatalogSerializer.DeserializeCatalog(_catalogBytes);

    private static AssetBundleDescriptor CloneDescriptor(AssetBundleDescriptor source) => new()
    {
        Name = source.Name,
        FileName = source.FileName,
        Sha256 = source.Sha256,
        Size = source.Size,
        Dependencies = source.Dependencies.ToList()
    };
}
