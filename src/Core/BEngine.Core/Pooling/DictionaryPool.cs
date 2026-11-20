namespace BEngine;

public sealed class DictionaryPool<TKey, TValue> : CollectionPool<Dictionary<TKey, TValue>, KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    public DictionaryPool(IEqualityComparer<TKey>? comparer = null, int maxSize = 100,
        int maxActive = int.MaxValue)
        : base(() => new Dictionary<TKey, TValue>(comparer), maxSize, maxActive) { }
}
