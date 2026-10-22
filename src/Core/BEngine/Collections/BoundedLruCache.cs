namespace BEngine;

/// <summary>Single-threaded weighted cache. Trim explicitly at a safe resource lifetime boundary.</summary>
public sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _items;
    private readonly LinkedList<Entry> _recency = new();
    private readonly Action<TValue>? _release;
    private readonly record struct Entry(TKey Key, TValue Value, long Bytes);
    public BoundedLruCache(IEqualityComparer<TKey>? comparer = null, Action<TValue>? release = null)
    { _items = new(comparer); _release = release; }
    public int Count => _items.Count;
    public long RetainedBytes { get; private set; }
    public IEnumerable<TValue> Values { get { foreach (var entry in _recency) yield return entry.Value; } }
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!_items.TryGetValue(key, out var node)) { value = default!; return false; }
        _recency.Remove(node); _recency.AddLast(node); value = node.Value.Value; return true;
    }
    public void Add(TKey key, TValue value, long bytes = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (_items.ContainsKey(key)) throw new ArgumentException("Duplicate cache key.", nameof(key));
        var total = checked(RetainedBytes + bytes);
        var node = _recency.AddLast(new Entry(key, value, bytes));
        _items.Add(key, node); RetainedBytes = total;
    }
    public bool Remove(TKey key, out TValue value)
    {
        if (!_items.Remove(key, out var node)) { value = default!; return false; }
        _recency.Remove(node); RetainedBytes -= node.Value.Bytes; value = node.Value.Value; return true;
    }
    public int Trim(int maximumCount, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        var removed = 0;
        while (_recency.First is { } oldest && (Count > maximumCount || RetainedBytes > maximumBytes))
        {
            Remove(oldest.Value.Key, out var value); _release?.Invoke(value); removed++;
        }
        return removed;
    }
    public void Clear()
    {
        if (_release is not null) foreach (var entry in _recency) _release(entry.Value);
        _items.Clear(); _recency.Clear(); RetainedBytes = 0;
    }
}
