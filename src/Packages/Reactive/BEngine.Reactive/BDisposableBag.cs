namespace BEngine.Reactive;

public struct BDisposableBag : IDisposable
{
    private List<IDisposable>? _items;
    private bool _disposed;

    public int Count => _items?.Count ?? 0;

    public void Add(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        if (_disposed) disposable.Dispose();
        else (_items ??= []).Add(disposable);
    }

    public void Clear()
    {
        if (_items is not { Count: > 0 } items) return;
        _items = [];
        foreach (var item in items) item.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
