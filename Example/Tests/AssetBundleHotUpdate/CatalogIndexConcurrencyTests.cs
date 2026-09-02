using System.Diagnostics;
using BEngine.AssetBundles;
using BEngine.Editor;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class CatalogIndexConcurrencyTests
{
    internal static async Task RunAsync(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult build)
    {
        IndexIsImmutableAndPrecomputesDependencyClosures(build);
        LargeIndexProvidesConstantTimeAddressLookup(build);
        await ConcurrentInitializationAndLoadsShareBundleState(workspace, build).ConfigureAwait(false);
    }

    private static void IndexIsImmutableAndPrecomputesDependencyClosures(
        AssetBundleBuildResult build)
    {
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(build.Catalog));
        var originalAddress = catalog.Assets[0].Address;
        var index = new AssetBundleCatalogIndex(catalog);
        catalog.Assets[0].Address = "Assets/Mutated/after-index.txt";

        TestAssert.That(index.AddressCount == build.Catalog.Assets.Count &&
                        index.BundleCount == build.Catalog.Bundles.Count &&
                        index.ContainsAddress(originalAddress) &&
                        !index.ContainsAddress(catalog.Assets[0].Address),
            "The catalog index did not retain an immutable address snapshot.");

        foreach (var descriptor in build.Catalog.Bundles)
        {
            var closure = index.GetDependencyClosure(descriptor.Name);
            TestAssert.That(closure.Count > 0 &&
                            closure[^1].Equals(descriptor.Name, StringComparison.OrdinalIgnoreCase) &&
                            closure.Distinct(StringComparer.OrdinalIgnoreCase).Count() == closure.Count &&
                            descriptor.Dependencies.All(dependency =>
                                closure.Contains(dependency, StringComparer.OrdinalIgnoreCase)),
                $"The catalog index did not precompute the dependency closure for '{descriptor.Name}'.");
        }
    }

    private static void LargeIndexProvidesConstantTimeAddressLookup(AssetBundleBuildResult build)
    {
        const int assetCount = 20_000;
        const int lookupCount = 200_000;
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(build.Catalog));
        var template = catalog.Assets.First(asset => asset.LocalIdentifier == 0);
        var bundleName = catalog.Bundles[0].Name;
        catalog.Assets = Enumerable.Range(0, assetCount)
            .Select(index => new AssetBundleAsset
            {
                Address = $"Assets/Performance/item-{index:D5}.bytes",
                Guid = Guid.NewGuid(),
                Bundle = bundleName,
                Entry = $"Assets/Performance/item-{index:D5}.bytes",
                AssetType = template.AssetType,
                Sha256 = template.Sha256,
                Size = template.Size,
                Importer = template.Importer,
                ImporterSettings = new Dictionary<string, string>(
                    template.ImporterSettings, StringComparer.Ordinal)
            })
            .ToList();
        var catalogIndex = new AssetBundleCatalogIndex(catalog);
        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < lookupCount; iteration++)
        {
            var address = $"Assets/Performance/item-{iteration % assetCount:D5}.bytes";
            if (!catalogIndex.ContainsAddress(address))
                throw new InvalidOperationException($"Indexed address '{address}' was not found.");
        }
        stopwatch.Stop();
        TestAssert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Indexed address lookup regressed: {lookupCount} lookups took {stopwatch.Elapsed}.");
    }

    private static async Task ConcurrentInitializationAndLoadsShareBundleState(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult build)
    {
        var cache = Path.Combine(workspace.Root, "Caches", "concurrent-runtime");
        await using var manager = RuntimeAssetBundleTests.CreateManager(cache, build.PackageDirectory);
        await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => manager.InitializeAsync())).ConfigureAwait(false);
        TestAssert.That(manager.IsInitialized && manager.ActiveVersion?.Version == build.Version.Version,
            "Concurrent initialization did not publish one complete runtime state.");

        var loadTasks = Enumerable.Range(0, 64)
            .Select(_ => manager.LoadTextAsync(AssetBundleTestWorkspace.SharedAddress))
            .ToArray();
        var allLoads = Task.WhenAll(loadTasks);
        while (!allLoads.IsCompleted)
        {
            _ = manager.UnloadUnused();
            await Task.Yield();
        }
        var handles = await allLoads.ConfigureAwait(false);
        TestAssert.That(handles.All(handle => handle.Value == "shared-v1") &&
                        manager.UnloadUnused() == 0,
            "Concurrent loads did not share a stable referenced bundle.");
        await Task.WhenAll(handles.Select(handle => Task.Run(handle.Dispose))).ConfigureAwait(false);
        TestAssert.That(manager.UnloadUnused() == 1,
            "The concurrently loaded bundle was not released exactly once.");
    }
}
