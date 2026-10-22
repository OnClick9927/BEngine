namespace BEngine.Reactive;

public sealed class BCompositeDisposable : IDisposable
{
    private List<IDisposable>? _items = [];

    public int Count => _items?.Count ?? 0;
    public bool IsDisposed => _items is null;

    public void Add(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        if (_items is null) disposable.Dispose();
        else _items.Add(disposable);
    }

    public bool Remove(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        if (_items is null || !_items.Remove(disposable)) return false;
        disposable.Dispose();
        return true;
    }

    public void Clear()
    {
        if (_items is not { Count: > 0 } items) return;
        _items = [];
        foreach (var item in items) item.Dispose();
    }

    public void Dispose()
    {
        var items = _items;
        _items = null;
        if (items is null) return;
        foreach (var item in items) item.Dispose();
    }
}
