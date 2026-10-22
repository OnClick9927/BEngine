namespace BEngine;

/// <summary>An idempotent, generation-checked lease. Dispose returns only this rental.</summary>
public sealed class PooledObject<T> : IDisposable where T : class
{
    private ObjectPool<T>? _pool;
    private readonly T _value;
    private readonly long _generation;

    internal PooledObject(ObjectPool<T> pool, T value, long generation)
    {
        _pool = pool;
        _value = value;
        _generation = generation;
    }

    public bool IsReturned => _pool is null || !_pool.IsRentalActive(_value, _generation);

    public T Value => !IsReturned ? _value : throw new ObjectDisposedException(nameof(PooledObject<T>));

    public void Dispose()
    {
        var pool = _pool;
        if (pool is null) return;
        // Keep the lease usable if a callback attempts a reentrant return before ownership changes.
        try { pool.ReleaseRental(_value, _generation); }
        finally { if (!pool.IsRentalActive(_value, _generation)) _pool = null; }
    }
}
