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
    public bool IsDisposed => _release is null;

    public void Dispose()
    {
        var release = _release;
        _release = null;
        release?.Invoke();
    }

    public BValueTask DisposeAsync()
    {
        Dispose();
        return BValueTask.CompletedTask;
    }
}
