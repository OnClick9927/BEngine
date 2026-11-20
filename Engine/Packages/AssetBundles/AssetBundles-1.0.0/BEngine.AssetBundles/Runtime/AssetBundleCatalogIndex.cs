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
        ValidatedSeeds? validatedSeeds = null, ClosureWorkspacePool? workspaces = null)
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

        _assetsByAddress = validatedSeeds?.AssetsByAddress ?? BuildIndex2294(catalog.Assets, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset> BuildIndex2294(global::System.Collections.Generic.IEnumerable<global::BEngine.AssetBundles.AssetBundleAsset> indexSource2294, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2294)
        {
            ArgumentNullException.ThrowIfNull(indexSource2294, "source");
            var knownCount = indexSource2294.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset>(indexComparer2294);
            global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundleAsset> values;
            if (knownCount && indexSource2294 is global::BEngine.AssetBundles.AssetBundleAsset[] array)
                values = array;
            else if (knownCount && indexSource2294 is global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleAsset> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset>(capacity, indexComparer2294);
                foreach (var item in indexSource2294)
                    result.Add(IndexKey2294(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset>(values.Length, indexComparer2294);
                foreach (var item in values)
                    result.Add(IndexKey2294(item), item);
                return result;
            }
        
            string IndexKey2294(global::BEngine.AssetBundles.AssetBundleAsset asset) => asset.Address;
        }
        var assetsByBundle = validatedSeeds is null ? BuildIndex2460(catalog.Assets.GroupBy(asset => asset.Bundle, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) : null;
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset[]> BuildIndex2460(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.AssetBundles.AssetBundleAsset>> indexSource2460, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2460)
        {
            ArgumentNullException.ThrowIfNull(indexSource2460, "source");
            var knownCount = indexSource2460.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset[]>(indexComparer2460);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.AssetBundles.AssetBundleAsset>> values;
            if (knownCount && indexSource2460 is global::System.Linq.IGrouping<string, global::BEngine.AssetBundles.AssetBundleAsset>[] array)
                values = array;
            else if (knownCount && indexSource2460 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.AssetBundles.AssetBundleAsset>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset[]>(capacity, indexComparer2460);
                foreach (var item in indexSource2460)
                    result.Add(IndexKey2460(item), IndexValue2460(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleAsset[]>(values.Length, indexComparer2460);
                foreach (var item in values)
                    result.Add(IndexKey2460(item), IndexValue2460(item));
                return result;
            }
        
            string IndexKey2460(global::System.Linq.IGrouping<string, global::BEngine.AssetBundles.AssetBundleAsset> group) => group.Key;
            global::BEngine.AssetBundles.AssetBundleAsset[] IndexValue2460(global::System.Linq.IGrouping<string, global::BEngine.AssetBundles.AssetBundleAsset> group) => group.ToArray();
        }
        _bundlesByName = BuildIndex2695(catalog.Bundles, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleCatalogIndex.IndexedBundle> BuildIndex2695(global::System.Collections.Generic.IEnumerable<global::BEngine.AssetBundles.AssetBundleDescriptor> indexSource2695, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2695)
        {
            ArgumentNullException.ThrowIfNull(indexSource2695, "source");
            var knownCount = indexSource2695.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleCatalogIndex.IndexedBundle>(indexComparer2695);
            global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundleDescriptor> values;
            if (knownCount && indexSource2695 is global::BEngine.AssetBundles.AssetBundleDescriptor[] array)
                values = array;
            else if (knownCount && indexSource2695 is global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleDescriptor> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleCatalogIndex.IndexedBundle>(capacity, indexComparer2695);
                foreach (var item in indexSource2695)
                    result.Add(IndexKey2695(item), IndexValue2695(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleCatalogIndex.IndexedBundle>(values.Length, indexComparer2695);
                foreach (var item in values)
                    result.Add(IndexKey2695(item), IndexValue2695(item));
                return result;
            }
        
            string IndexKey2695(global::BEngine.AssetBundles.AssetBundleDescriptor descriptor) => descriptor.Name;
            global::BEngine.AssetBundles.AssetBundleCatalogIndex.IndexedBundle IndexValue2695(global::BEngine.AssetBundles.AssetBundleDescriptor descriptor) => new IndexedBundle(descriptor, validatedSeeds is null ? assetsByBundle!.GetValueOrDefault(descriptor.Name) ?? [] : validatedSeeds.AssetsByBundle.GetValueOrDefault(descriptor.Name) ?? []);
        }
        _mainAssetsByOwnerGuid = validatedSeeds?.MainAssetsByOwnerGuid ?? BuildIndex3175(catalog.Assets.Where(static asset => asset.LocalIdentifier == 0).GroupBy(static asset => asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid).Where(static group => group.Key != Guid.Empty), null);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset> BuildIndex3175(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>> indexSource3175, global::System.Collections.Generic.IEqualityComparer<global::System.Guid>? indexComparer3175)
        {
            ArgumentNullException.ThrowIfNull(indexSource3175, "source");
            var knownCount = indexSource3175.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>(indexComparer3175);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>> values;
            if (knownCount && indexSource3175 is global::System.Linq.IGrouping<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>[] array)
                values = array;
            else if (knownCount && indexSource3175 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>(capacity, indexComparer3175);
                foreach (var item in indexSource3175)
                    result.Add(IndexKey3175(item), IndexValue3175(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset>(values.Length, indexComparer3175);
                foreach (var item in values)
                    result.Add(IndexKey3175(item), IndexValue3175(item));
                return result;
            }
        
            global::System.Guid IndexKey3175(global::System.Linq.IGrouping<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset> group) => group.Key;
            global::BEngine.AssetBundles.AssetBundleAsset IndexValue3175(global::System.Linq.IGrouping<global::System.Guid, global::BEngine.AssetBundles.AssetBundleAsset> group) => group.First();
        }

        var closures = new Dictionary<string, IndexedBundle[]>(StringComparer.OrdinalIgnoreCase);
        using var ownedWorkspaces = workspaces is null ? new ClosureWorkspacePool() : null;
        workspaces ??= ownedWorkspaces!;
        var workspace = workspaces.Get();
        try
        {
            foreach (var bundle in _bundlesByName.Values)
            {
                workspace.Ordered.Clear(); workspace.Visited.Clear();
                CollectClosure(bundle.Descriptor.Name, workspace.Visited, workspace.Ordered);
                closures.Add(bundle.Descriptor.Name, workspace.Ordered.ToArray());
            }
        }
        finally { workspaces.Release(workspace); }
        _closuresByBundle = closures;
        _closureNamesByBundle = closures.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)new ReadOnlyCollection<string>(
                (item.Value is { } projectionSource4401 ? projectionSource4401.Length == 0 ? global::System.Array.Empty<string>() : global::System.Array.ConvertAll<global::BEngine.AssetBundles.AssetBundleCatalogIndex.IndexedBundle, string>(projectionSource4401, bundle => bundle.Descriptor.Name) : throw new global::System.ArgumentNullException("source"))),
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

    internal sealed class ClosureWorkspacePool : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly ObjectPool<ClosureWorkspace> _pool = new(
            static () => new(), onRelease: static workspace => workspace.Reset(), maxSize: 1);
        internal ClosureWorkspace Get() { lock (_gate) return _pool.Get(); }
        internal void Release(ClosureWorkspace workspace) { lock (_gate) _pool.Release(workspace); }
        public void Dispose() { lock (_gate) _pool.Dispose(); }
    }

    internal sealed class ClosureWorkspace
    {
        internal readonly List<IndexedBundle> Ordered = [];
        internal readonly HashSet<string> Visited = new(StringComparer.OrdinalIgnoreCase);
        internal void Reset()
        {
            Ordered.Clear(); Visited.Clear();
            if (Ordered.Capacity > 4096) Ordered.Capacity = 0;
            if (Visited.EnsureCapacity(0) > 4096) Visited.TrimExcess();
        }
    }

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
