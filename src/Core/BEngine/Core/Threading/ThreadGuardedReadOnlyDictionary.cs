using System.Collections;

namespace BEngine;

internal sealed class ThreadGuardedReadOnlyDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _items;

    internal ThreadGuardedReadOnlyDictionary(Dictionary<TKey, TValue> items) =>
        _items = items ?? throw new ArgumentNullException(nameof(items));

    public int Count
    {
        get { MainThreadGuard.Ensure(); return _items.Count; }
    }

    public TValue this[TKey key]
    {
        get { MainThreadGuard.Ensure(); return _items[key]; }
    }

    public IEnumerable<TKey> Keys
    {
        get { MainThreadGuard.Ensure(); return _items.Keys.ToArray(); }
    }

    public IEnumerable<TValue> Values
    {
        get { MainThreadGuard.Ensure(); return _items.Values.ToArray(); }
    }

    public bool ContainsKey(TKey key)
    {
        MainThreadGuard.Ensure();
        return _items.ContainsKey(key);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        MainThreadGuard.Ensure();
        return _items.TryGetValue(key, out value!);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        MainThreadGuard.Ensure();
        return new ThreadGuardedEnumerator<KeyValuePair<TKey, TValue>>(_items.GetEnumerator());
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
