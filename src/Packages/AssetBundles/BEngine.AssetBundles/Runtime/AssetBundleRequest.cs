namespace BEngine.AssetBundles;

public sealed class AssetBundleRequest<T> : AssetBundleLoadOperation, IDisposable
{
    private AssetBundleHandle<T>? _handle;
    private bool _disposed;

    public AssetBundleHandle<T>? handle
    {
        get => _handle;
    }

    public T? asset
    {
        get => _handle is null ? default : _handle.Asset;
    }

    internal AssetBundleRequest(Func<BCancellationToken, BValueTask<AssetBundleHandle<T>>> loader)
        : this((token, _) => loader(token)) { }

    internal AssetBundleRequest(Func<BCancellationToken, AssetBundleLoadTracker, BValueTask<AssetBundleHandle<T>>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Start(async cancellationToken =>
        {
            var loading = loader(cancellationToken, Tracker);
            while (!loading.IsCompleted)
            {
                SetProgress(Tracker.Progress);
                await BTask.Yield();
            }
            var loaded = await loading;
            if (_disposed) loaded.Dispose();
            else _handle = loaded;
            cancellationToken.ThrowIfCancellationRequested();
            var detail = Tracker.Snapshot;
            long? size = detail.Kind == AssetBundleLoadKind.Asset ? detail.TotalBytes : null;
            if (loaded.Value is byte[] bytes) size = bytes.LongLength;
            Tracker.Report(new(AssetBundleLoadKind.Asset, AssetBundleLoadPhase.Completed,
                loaded.Address, size ?? 0, size));
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var release = _handle;
        _handle = null;
        Cancel();
        release?.Dispose();
    }

    public BValueTask DisposeAsync()
    {
        Dispose();
        return BValueTask.CompletedTask;
    }
}
