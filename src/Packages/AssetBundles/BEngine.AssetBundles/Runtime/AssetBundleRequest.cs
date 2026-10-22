namespace BEngine.AssetBundles;

public sealed class AssetBundleRequest<T> : AsyncOperation, IDisposable
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
    {
        ArgumentNullException.ThrowIfNull(loader);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            var loaded = await loader(cancellationToken);
            if (_disposed) loaded.Dispose();
            else _handle = loaded;
            cancellationToken.ThrowIfCancellationRequested();
            SetProgress(0.99f);
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
