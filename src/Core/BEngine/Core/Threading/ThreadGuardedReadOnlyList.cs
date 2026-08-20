using System.Collections;

namespace BEngine;

internal sealed class ThreadGuardedReadOnlyList<T> : IReadOnlyList<T>
{
    private readonly List<T> _items;

    internal ThreadGuardedReadOnlyList(List<T> items) =>
        _items = items ?? throw new ArgumentNullException(nameof(items));

    public int Count
    {
        get { MainThreadGuard.Ensure(); return _items.Count; }
    }

    public T this[int index]
    {
        get { MainThreadGuard.Ensure(); return _items[index]; }
    }

    public IEnumerator<T> GetEnumerator()
    {
        MainThreadGuard.Ensure();
        return new ThreadGuardedEnumerator<T>(_items.GetEnumerator());
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
