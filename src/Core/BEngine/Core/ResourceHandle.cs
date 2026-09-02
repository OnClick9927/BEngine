namespace BEngine;

public sealed class ResourceHandle<T> : IDisposable where T : class
{
    private Action? _release;

    internal ResourceHandle(T asset, Action release)
    {
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public T asset { get; }
    public bool isValid => Volatile.Read(ref _release) is not null;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
