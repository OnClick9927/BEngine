namespace BEngine.AssetBundles;

public sealed class AssetBundleUpdatePlan
{
    private readonly byte[] _targetVersionBytes;
    private readonly byte[] _catalogBytes;
    private readonly AssetBundleDescriptor[] _downloads;
    private readonly long _downloadSize;
    private readonly string[] _bundleNames;

    internal AssetBundleUpdatePlan(
        AssetBundleVersion targetVersion,
        AssetBundleCatalog targetCatalog,
        byte[] catalogBytes,
        IReadOnlyList<AssetBundleDescriptor> downloads,
        bool hasUpdates,
        IReadOnlyList<string>? bundleNames = null)
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
        _bundleNames = ((((global::System.Collections.Generic.IEnumerable<string>)((bundleNames ?? ProjectItems1281()))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems1265, distinctComparer1265) => distinctItems1265.ToHashSet<string>(distinctComparer1265).ToArray()});
        [global::BEngine.SkipLoopGuard]
        string[] ProjectItems1281()
        {
            var projectionSource1281 = targetCatalog.Bundles;
            ArgumentNullException.ThrowIfNull(projectionSource1281, "source");
            global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundleDescriptor> projectionValues1281 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(projectionSource1281);
            var projectionOutput1281 = projectionValues1281.Length == 0 ? global::System.Array.Empty<string>() : new string[projectionValues1281.Length];
            var projectionIndex1281 = 0;
            foreach (var bundle in projectionValues1281)
            {
                projectionOutput1281[projectionIndex1281++] = bundle.Name;
            }
        
            return projectionOutput1281;
        }
    }

    public AssetBundleVersion TargetVersion => CreateTargetVersionSnapshot();
    public AssetBundleCatalog TargetCatalog => CreateTargetCatalogSnapshot();
    public IReadOnlyList<AssetBundleDescriptor> Downloads =>
        Array.AsReadOnly((_downloads is { } projectionSource1667 ? projectionSource1667.Length == 0 ? global::System.Array.Empty<global::BEngine.AssetBundles.AssetBundleDescriptor>() : global::System.Array.ConvertAll<global::BEngine.AssetBundles.AssetBundleDescriptor, global::BEngine.AssetBundles.AssetBundleDescriptor>(projectionSource1667, CloneDescriptor) : throw new global::System.ArgumentNullException("source")));
    public bool HasUpdates { get; }
    public long DownloadSize => _downloadSize;
    public IReadOnlyList<string> BundleNames => Array.AsReadOnly(_bundleNames);
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
        SourceVersion = source.SourceVersion,
        Dependencies = source.Dependencies.ToList()
    };
}
