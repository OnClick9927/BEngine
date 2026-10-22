namespace BEngine.Reactive;

public sealed class BSerialDisposable : IDisposable
{
    private IDisposable? _current;
    private bool _disposed;

    public IDisposable? Disposable
    {
        get => _current;
        set
        {
            if (ReferenceEquals(_current, value)) return;
            var previous = _current;
            _current = _disposed ? null : value;
            previous?.Dispose();
            if (_disposed) value?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BDisposable.Dispose(ref _current);
    }
}
