namespace BEngine.AssetBundles;

/// <summary>An immutable package update bound to the catalog against which it was checked.</summary>
public sealed class AssetBundlePackageUpdatePlan
{
    private readonly byte[] _targetVersionBytes;
    private readonly byte[] _sourceVersionBytes;
    private readonly byte[] _catalogBytes;
    private readonly AssetBundleDescriptor[] _downloads;
    private readonly string[] _packageNames;
    private readonly string[] _bundleNames;

    internal AssetBundlePackageUpdatePlan(
        AssetBundleVersion sourceVersion,
        AssetBundleVersion targetVersion,
        byte[] catalogBytes,
        IReadOnlyList<AssetBundleDescriptor> downloads,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<string> bundleNames,
        bool hasUpdates,
        AssetBundleResourceRevision baseRevision)
    {
        _sourceVersionBytes = AssetBundleCatalogSerializer.SerializeVersion(sourceVersion);
        _targetVersionBytes = AssetBundleCatalogSerializer.SerializeVersion(targetVersion);
        _catalogBytes = (byte[])catalogBytes.Clone();
        _ = AssetBundleCatalogSerializer.DeserializeCatalog(_catalogBytes);
        _downloads = downloads.Select(CloneDescriptor).ToArray();
        _packageNames = packageNames.ToArray();
        _bundleNames = bundleNames.ToArray();
        DownloadSize = _downloads.Aggregate(0L, static (total, bundle) => checked(total + bundle.Size));
        HasUpdates = hasUpdates;
        BaseRevision = baseRevision;
    }

    /// <summary>The immutable server release used to prepare this update.</summary>
    public AssetBundleVersion SourceVersion =>
        AssetBundleCatalogSerializer.DeserializeVersion(_sourceVersionBytes);
    /// <summary>The flattened local catalog retains all unselected installed packages.</summary>
    public AssetBundleCatalog TargetCatalog =>
        AssetBundleCatalogSerializer.DeserializeCatalog(_catalogBytes);
    public AssetBundleVersion TargetVersion =>
        AssetBundleCatalogSerializer.DeserializeVersion(_targetVersionBytes);
    public IReadOnlyList<AssetBundleDescriptor> Downloads =>
        Array.AsReadOnly(_downloads.Select(CloneDescriptor).ToArray());
    /// <summary>Requested packages plus all forward dependency packages.</summary>
    public IReadOnlyList<string> PackageNames => Array.AsReadOnly(_packageNames);
    public IReadOnlyList<string> BundleNames => Array.AsReadOnly(_bundleNames);
    public bool HasUpdates { get; }
    public long DownloadSize { get; }
    internal AssetBundleResourceRevision BaseRevision { get; }
    internal ReadOnlyMemory<byte> CatalogBytes => _catalogBytes;

    private static AssetBundleDescriptor CloneDescriptor(AssetBundleDescriptor source) => new()
    {
        Name = source.Name,
        FileName = source.FileName,
        Sha256 = source.Sha256,
        Size = source.Size,
        SourceVersion = source.SourceVersion,
        Dependencies = source.Dependencies.ToList()
    };
}
