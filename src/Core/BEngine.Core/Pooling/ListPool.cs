namespace BEngine;

/// <summary>A list pool that also releases oversized backing arrays when returning a list.</summary>
public sealed class ListPool<T> : ObjectPool<List<T>>
{
    public ListPool(int maxSize = 100, int maxActive = int.MaxValue,
        int initialCapacity = 0, int maxRetainedCapacity = 4096)
        : base(() => new List<T>(initialCapacity), onRelease: list =>
        {
            list.Clear();
            if (list.Capacity > maxRetainedCapacity) list.Capacity = initialCapacity;
        }, maxSize: maxSize, maxActive: maxActive)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedCapacity, initialCapacity);
    }
}
