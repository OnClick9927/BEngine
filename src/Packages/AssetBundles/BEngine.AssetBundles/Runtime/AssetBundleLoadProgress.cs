namespace BEngine.AssetBundles;

public enum AssetBundleLoadKind { Initialization, Version, Catalog, Bundle, Asset }
public enum AssetBundleLoadPhase { Waiting, Receiving, Verifying, Opening, Reading, Completed, Retrying, Failed, Canceled }

/// <summary>Actual work for one logical resource, not an estimated overall percentage.
/// Byte counts belong to the current attempt; a null total means its length is not known yet.
/// Completed is emitted only after the corresponding integrity/format checks succeed.</summary>
public readonly record struct AssetBundleLoadProgress(
    AssetBundleLoadKind Kind,
    AssetBundleLoadPhase Phase,
    string Resource,
    long ProcessedBytes,
    long? TotalBytes,
    int Attempt = 1);

/// <summary>Read-only progress for an asynchronous AB operation. Poll progress/loadProgress
/// from the owning thread. Shared bundle state is exposed without copying a collection.</summary>
public abstract class AssetBundleLoadOperation : AsyncOperation
{
    internal AssetBundleLoadTracker Tracker { get; } = new();
    public AssetBundleLoadProgress loadProgress => exception is null ? Tracker.Snapshot : Tracker.Snapshot with
    {
        Phase = status == AsyncOperationStatus.Canceled ? AssetBundleLoadPhase.Canceled : AssetBundleLoadPhase.Failed
    };
    public int bundleCount => Tracker.Bundles.Count;
    public AssetBundleLoadProgress GetBundleProgress(int index) => Tracker.Bundles[index].Snapshot;
}
