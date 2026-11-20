namespace BEngine.AssetBundles;

public sealed class AssetBundleHandle<T> : IDisposable
{
    private Action? _release;

    internal AssetBundleHandle(string address, T value, Action release)
    {
        Address = address;
        Value = value;
        _release = release;
    }

    public string Address { get; }
    public T Value { get; }
    public T Asset => Value;
    public bool IsDisposed => Volatile.Read(ref _release) is null;

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        release?.Invoke();
    }

    public BValueTask DisposeAsync()
    {
        Dispose();
        return BValueTask.CompletedTask;
    }
}
