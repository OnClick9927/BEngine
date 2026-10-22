namespace BEngine.Reactive;

public static class BDisposable
{
    public static IDisposable Empty { get; } = new EmptyDisposable();

    public static IDisposable Create(Action dispose) =>
        new ActionDisposable(dispose ?? throw new ArgumentNullException(nameof(dispose)));

    public static IDisposable Combine(params IDisposable[] disposables)
    {
        ArgumentNullException.ThrowIfNull(disposables);
        return disposables.Length switch
        {
            0 => Empty,
            1 => disposables[0] ?? throw new ArgumentException("A disposable is null.", nameof(disposables)),
            _ => new CombinedDisposable(disposables)
        };
    }

    public static void Dispose(ref IDisposable? disposable)
    {
        var current = disposable;
        disposable = null;
        current?.Dispose();
    }

    private sealed class EmptyDisposable : IDisposable { public void Dispose() { } }

    private sealed class ActionDisposable(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose()
        {
            var current = _action;
            _action = null;
            current?.Invoke();
        }
    }

    private sealed class CombinedDisposable : IDisposable
    {
        private IDisposable[]? _items;

        public CombinedDisposable(IDisposable[] items)
        {
            if (items.Any(static item => item is null))
                throw new ArgumentException("A disposable is null.", nameof(items));
            _items = items;
        }

        public void Dispose()
        {
            var items = _items;
            _items = null;
            if (items is null) return;
            foreach (var item in items) item.Dispose();
        }
    }
}
