using System.Collections.ObjectModel;

namespace BEngine.AssetBundles;

public sealed class AssetBundleCatalogIndex
{
    // These private tables are populated only during construction. Dictionary
    // avoids FrozenDictionary's setup passes on short-lived runtime catalogs;
    // no mutable table or its values collection is exposed to callers.
    private readonly Dictionary<string, AssetBundleAsset> _assetsByAddress;
    private readonly Dictionary<string, IndexedBundle> _bundlesByName;
    private readonly Dictionary<string, IndexedBundle[]> _closuresByBundle;
    private readonly Dictionary<string, IReadOnlyList<string>> _closureNamesByBundle;
    private readonly Dictionary<Guid, AssetBundleAsset> _mainAssetsByOwnerGuid;
    private readonly Dictionary<string, IReadOnlyList<string>> _bundlesByPackage;

    public AssetBundleCatalogIndex(AssetBundleCatalog catalog)
        : this(catalog, cloneCatalog: true)
    {
    }

    internal AssetBundleCatalogIndex(AssetBundleCatalog catalog, bool cloneCatalog, bool catalogValidated = false,
        ValidatedSeeds? validatedSeeds = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        using var measurement = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.CatalogIndex);
        if (validatedSeeds is not null &&
            (cloneCatalog || !catalogValidated || !validatedSeeds.TryConsume(catalog)))
            throw new ArgumentException("Validated index seeds require their privately owned catalog.", nameof(validatedSeeds));
        if (!catalogValidated) catalog.Validate();
        if (cloneCatalog) catalog = AssetBundleCatalogSerializer.CloneCatalog(catalog);
        _bundlesByPackage = AssetBundlePackageUtility.GetPackages(catalog).ToDictionary(
            package => package.Name,
            package => (IReadOnlyList<string>)new ReadOnlyCollection<string>(
                package.Bundles.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
            StringComparer.OrdinalIgnoreCase);
        PackageNames = new ReadOnlyCollection<string>(
            _bundlesByPackage.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray());

        _assetsByAddress = validatedSeeds?.AssetsByAddress ?? catalog.Assets.ToDictionary(
            asset => asset.Address, StringComparer.OrdinalIgnoreCase);
        var assetsByBundle = validatedSeeds is null
            ? catalog.Assets.GroupBy(asset => asset.Bundle, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase)
            : null;
        _bundlesByName = catalog.Bundles.ToDictionary(
            descriptor => descriptor.Name,
            descriptor => new IndexedBundle(
                descriptor,
                validatedSeeds is null
                    ? assetsByBundle!.GetValueOrDefault(descriptor.Name) ?? []
                    : validatedSeeds.AssetsByBundle.GetValueOrDefault(descriptor.Name) ?? []),
            StringComparer.OrdinalIgnoreCase);
        _mainAssetsByOwnerGuid = validatedSeeds?.MainAssetsByOwnerGuid ?? catalog.Assets
            .Where(static asset => asset.LocalIdentifier == 0)
            .GroupBy(static asset => asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid)
            .Where(static group => group.Key != Guid.Empty)
            .ToDictionary(static group => group.Key, static group => group.First());

        var closures = new Dictionary<string, IndexedBundle[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in _bundlesByName.Values)
        {
            var ordered = new List<IndexedBundle>();
            CollectClosure(bundle.Descriptor.Name, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ordered);
            closures.Add(bundle.Descriptor.Name, ordered.ToArray());
        }
        _closuresByBundle = closures;
        _closureNamesByBundle = closures.ToDictionary(
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
    public IReadOnlyList<string> PackageNames { get; }

    public IReadOnlyList<string> GetPackageBundleNames(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return _bundlesByPackage.TryGetValue(packageName, out var bundles) ? bundles :
            throw new KeyNotFoundException($"Assetbundle package '{packageName}' was not found.");
    }

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

    internal sealed record IndexedBundle(
        AssetBundleDescriptor Descriptor,
        IReadOnlyList<AssetBundleAsset> Assets);

    internal sealed class ValidatedSeeds(
        AssetBundleCatalog catalog,
        Dictionary<string, AssetBundleAsset> assetsByAddress,
        Dictionary<Guid, AssetBundleAsset> mainAssetsByOwnerGuid,
        Dictionary<string, List<AssetBundleAsset>> assetsByBundle, bool canonical = false)
    {
        internal bool Canonical { get; } = canonical;
        private int _consumed;
        private AssetBundleCatalog Catalog { get; } = catalog;
        internal bool TryConsume(AssetBundleCatalog ownedCatalog) =>
            ReferenceEquals(Catalog, ownedCatalog) && Interlocked.Exchange(ref _consumed, 1) == 0;
        internal Dictionary<string, AssetBundleAsset> AssetsByAddress { get; } = assetsByAddress;
        internal Dictionary<Guid, AssetBundleAsset> MainAssetsByOwnerGuid { get; } = mainAssetsByOwnerGuid;
        internal Dictionary<string, List<AssetBundleAsset>> AssetsByBundle { get; } = assetsByBundle;
    }
}
