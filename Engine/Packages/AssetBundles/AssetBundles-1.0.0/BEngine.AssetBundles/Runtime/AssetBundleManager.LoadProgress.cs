namespace BEngine.AssetBundles;

public sealed partial class AssetBundleManager
{
    private readonly Dictionary<string, AssetBundleLoadTracker> _bundleLoadTrackers = new(StringComparer.OrdinalIgnoreCase);
    private AssetBundleLoadTracker? _initializationProgress;

    private AssetBundleLoadTracker GetBundleLoadTracker(AssetBundleDescriptor descriptor)
    {
        if (_bundleLoadTrackers.TryGetValue(descriptor.Sha256, out var tracker)) return tracker;
        tracker = new();
        var loaded = _loadedBundles.TryGetValue(descriptor.Sha256, out var task) && task.IsCompletedSuccessfully;
        tracker.Report(new(AssetBundleLoadKind.Bundle, loaded ? AssetBundleLoadPhase.Completed : AssetBundleLoadPhase.Waiting,
            descriptor.Name, loaded ? descriptor.Size : 0, descriptor.Size));
        _bundleLoadTrackers.Add(descriptor.Sha256, tracker);
        return tracker;
    }

    private async BValueTask<byte[]> ReadAssetWithProgressAsync(
        LoadedAssetBundle bundle, AssetBundleAsset asset, BCancellationToken cancellationToken, AssetBundleLoadTracker? progress)
    {
        progress?.Report(new(AssetBundleLoadKind.Asset, AssetBundleLoadPhase.Reading, asset.Address, 0, asset.Size));
        try
        {
            var bytes = await bundle.ReadAssetAsync(asset, _options.MaximumAssetSize, cancellationToken,
                progress is null ? null : count => progress.Report(new(
                    AssetBundleLoadKind.Asset, AssetBundleLoadPhase.Reading, asset.Address, count, asset.Size)));
            progress?.Report(new(AssetBundleLoadKind.Asset, AssetBundleLoadPhase.Completed, asset.Address, asset.Size, asset.Size));
            return bytes;
        }
        catch
        {
            progress?.Report(new(AssetBundleLoadKind.Asset, AssetBundleLoadPhase.Failed, asset.Address, 0, asset.Size));
            throw;
        }
    }
}
