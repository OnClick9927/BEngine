namespace BEngine;

/// <summary>An instance-owned collection pool. Returning a collection clears its contents.</summary>
public class CollectionPool<TCollection, TItem> : ObjectPool<TCollection>
    where TCollection : class, ICollection<TItem>, new()
{
    public CollectionPool(Func<TCollection>? create = null, int maxSize = 100,
        int maxActive = int.MaxValue)
        : base(create ?? (() => new TCollection()), onRelease: collection => collection.Clear(),
            maxSize: maxSize, maxActive: maxActive) { }
}
