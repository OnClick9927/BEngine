using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace BEngine.AssetBundles;

public sealed class AssetBundleCatalogIndex
{
    private readonly FrozenDictionary<string, AssetBundleAsset> _assetsByAddress;
    private readonly FrozenDictionary<string, IndexedBundle> _bundlesByName;
    private readonly FrozenDictionary<string, IndexedBundle[]> _closuresByBundle;
    private readonly FrozenDictionary<string, IReadOnlyList<string>> _closureNamesByBundle;
    private readonly FrozenDictionary<Guid, AssetBundleAsset> _mainAssetsByOwnerGuid;

    public AssetBundleCatalogIndex(AssetBundleCatalog catalog)
        : this(catalog, cloneCatalog: true)
    {
    }

    internal AssetBundleCatalogIndex(AssetBundleCatalog catalog, bool cloneCatalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (cloneCatalog) catalog = CloneCatalog(catalog);
        catalog.Validate();

        _assetsByAddress = catalog.Assets.ToFrozenDictionary(
            asset => asset.Address, StringComparer.OrdinalIgnoreCase);
        var assetsByBundle = catalog.Assets
            .GroupBy(asset => asset.Bundle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        _bundlesByName = catalog.Bundles.ToFrozenDictionary(
            descriptor => descriptor.Name,
            descriptor => new IndexedBundle(
                descriptor,
                assetsByBundle.GetValueOrDefault(descriptor.Name) ?? []),
            StringComparer.OrdinalIgnoreCase);
        _mainAssetsByOwnerGuid = catalog.Assets
            .Where(static asset => asset.LocalIdentifier == 0)
            .GroupBy(static asset => asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid)
            .Where(static group => group.Key != Guid.Empty)
            .ToFrozenDictionary(static group => group.Key, static group => group.First());

        var closures = new Dictionary<string, IndexedBundle[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in _bundlesByName.Values)
        {
            var ordered = new List<IndexedBundle>();
            CollectClosure(bundle.Descriptor.Name, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ordered);
            closures.Add(bundle.Descriptor.Name, ordered.ToArray());
        }
        _closuresByBundle = closures.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _closureNamesByBundle = closures.ToFrozenDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)new ReadOnlyCollection<string>(
                item.Value.Select(bundle => bundle.Descriptor.Name).ToArray()),
            StringComparer.OrdinalIgnoreCase);
        Addresses = new ReadOnlyCollection<string>(
            catalog.Assets.Select(asset => asset.Address)
                .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public int AddressCount => _assetsByAddress.Count;
    public int BundleCount => _bundlesByName.Count;
    public IReadOnlyList<string> Addresses { get; }

    public bool ContainsAddress(string address)
    {
        try
        {
            return _assetsByAddress.ContainsKey(AssetBundleValidation.NormalizeAddress(address));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetDependencyClosure(string bundleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleName);
        return _closureNamesByBundle.TryGetValue(bundleName, out var closure)
            ? closure
            : throw new KeyNotFoundException($"Asset bundle '{bundleName}' was not found.");
    }

    internal bool TryGetAsset(string canonicalAddress, out AssetBundleAsset asset) =>
        _assetsByAddress.TryGetValue(canonicalAddress, out asset!);

    internal bool TryGetMainAsset(Guid ownerGuid, out AssetBundleAsset asset) =>
        _mainAssetsByOwnerGuid.TryGetValue(ownerGuid, out asset!);

    internal IReadOnlyList<IndexedBundle> GetBundleClosure(string bundleName) =>
        _closuresByBundle.TryGetValue(bundleName, out var closure)
            ? closure
            : throw new KeyNotFoundException($"Asset bundle '{bundleName}' was not found.");

    private void CollectClosure(
        string bundleName,
        ISet<string> visited,
        ICollection<IndexedBundle> ordered)
    {
        if (!visited.Add(bundleName)) return;
        if (!_bundlesByName.TryGetValue(bundleName, out var bundle))
            throw new InvalidDataException($"Bundle '{bundleName}' is missing from the catalog index.");
        foreach (var dependency in bundle.Descriptor.Dependencies)
            CollectClosure(dependency, visited, ordered);
        ordered.Add(bundle);
    }

    private static AssetBundleCatalog CloneCatalog(AssetBundleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(catalog));
    }

    internal sealed record IndexedBundle(
        AssetBundleDescriptor Descriptor,
        IReadOnlyList<AssetBundleAsset> Assets);
}
