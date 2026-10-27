namespace BEngine.AssetBundles;

// Internal mutable state only. No progress events or external callbacks.
internal sealed class AssetBundleLoadTracker
{
    private AssetBundleLoadProgress _current = new(AssetBundleLoadKind.Initialization, AssetBundleLoadPhase.Waiting, "", 0, null);
    private bool _asset;
    private float _initializationProgress;
    internal List<AssetBundleLoadTracker> Bundles { get; } = [];
    internal AssetBundleLoadProgress Snapshot
    {
        get
        {
            if (!_asset || _current.Phase is AssetBundleLoadPhase.Reading or AssetBundleLoadPhase.Completed or AssetBundleLoadPhase.Failed)
                return _current;
            foreach (var bundle in Bundles)
                if (bundle.Snapshot.Phase != AssetBundleLoadPhase.Completed) return bundle.Snapshot;
            return _current;
        }
    }

    internal float Progress
    {
        get
        {
            if (_asset)
            {
                if (_current.Phase == AssetBundleLoadPhase.Completed) return 1;
                double total = 0, done = 0;
                foreach (var bundle in Bundles)
                {
                    var size = Math.Max(1, bundle._current.TotalBytes ?? 0);
                    total += size; done += size * bundle.Progress;
                }
                return .75f * (total == 0 ? 1 : (float)(done / total)) + .24f * Fraction(_current);
            }
            if (_current.Kind != AssetBundleLoadKind.Bundle) return _initializationProgress;
            return _current.Phase switch
            {
                AssetBundleLoadPhase.Completed => 1,
                AssetBundleLoadPhase.Opening when _current.ProcessedBytes > 0 => .99f,
                AssetBundleLoadPhase.Verifying => .9f + .08f * Fraction(_current),
                _ => .9f * Fraction(_current)
            };
        }
    }

    internal void ConfigureAsset(AssetBundleAsset asset, IEnumerable<AssetBundleLoadTracker> bundles)
    {
        _asset = true;
        _current = new(AssetBundleLoadKind.Asset, AssetBundleLoadPhase.Waiting, asset.Address, 0, asset.Size);
        Bundles.AddRange(bundles.Distinct());
    }

    internal void Report(AssetBundleLoadProgress value)
    {
        _current = value;
        if (_asset || value.Kind == AssetBundleLoadKind.Bundle) return;
        var fraction = value.Phase == AssetBundleLoadPhase.Completed ? 1 : Fraction(value);
        var progress = value.Kind switch
        {
            AssetBundleLoadKind.Version => .1f * fraction,
            AssetBundleLoadKind.Catalog => value.Phase switch
            {
                AssetBundleLoadPhase.Completed => .98f,
                AssetBundleLoadPhase.Opening => .95f,
                AssetBundleLoadPhase.Verifying => .9f,
                _ => .1f + .8f * fraction
            },
            AssetBundleLoadKind.Initialization when value.Phase == AssetBundleLoadPhase.Completed => 1,
            _ => 0
        };
        _initializationProgress = Math.Max(_initializationProgress, progress);
    }

    private static float Fraction(AssetBundleLoadProgress value) => value.TotalBytes is > 0
        ? Math.Clamp((float)((double)value.ProcessedBytes / value.TotalBytes.Value), 0, 1)
        : value.Phase == AssetBundleLoadPhase.Completed ? 1 : 0;
}
