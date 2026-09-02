namespace BEngine.AssetBundles;

public sealed class AssetBundleRequest<T> : AsyncOperation, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private AssetBundleHandle<T>? _handle;
    private bool _disposed;

    public AssetBundleHandle<T>? handle
    {
        get { lock (_gate) return _handle; }
    }

    public T? asset
    {
        get { lock (_gate) return _handle is null ? default : _handle.Asset; }
    }

    internal AssetBundleRequest(Func<CancellationToken, Task<AssetBundleHandle<T>>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            var loaded = await loader(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed) loaded.Dispose();
                else _handle = loaded;
            }
            cancellationToken.ThrowIfCancellationRequested();
            SetProgress(0.99f);
        });
    }

    public void Dispose()
    {
        AssetBundleHandle<T>? release;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            release = _handle;
            _handle = null;
        }
        Cancel();
        release?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
