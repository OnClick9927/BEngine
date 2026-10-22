namespace BEngine;

public sealed class HashSetPool<T> : CollectionPool<HashSet<T>, T>
{
    public HashSetPool(IEqualityComparer<T>? comparer = null, int maxSize = 100,
        int maxActive = int.MaxValue)
        : base(() => new HashSet<T>(comparer), maxSize, maxActive) { }
}
