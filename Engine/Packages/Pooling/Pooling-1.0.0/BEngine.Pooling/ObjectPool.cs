using System.Diagnostics.CodeAnalysis;

namespace BEngine;

/// <summary>
/// A single-threaded pool with ownership checks, bounded caching and optional active limits.
/// Callbacks may inspect the pool but must not mutate it recursively.
/// </summary>
public class ObjectPool<T> : IObjectPool<T> where T : class
{
    private readonly Func<T> _create;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onRelease;
    private readonly Action<T>? _onDestroy;
    private readonly Stack<T> _inactive;
    private readonly Dictionary<T, long> _active = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<T> _cached = new(ReferenceEqualityComparer.Instance);
    private bool _busy;
    private long _generation;
    private int _peakActive;
    private long _created, _gets, _reused, _released, _destroyed;

    public int CountActive => _active.Count;
    public int CountInactive => _inactive.Count;
    public int CountAll => CountActive + CountInactive;
    /// <summary>Maximum number of idle objects retained, not the active rental limit.</summary>
    public int MaxSize { get; }
    public int MaxActive { get; }
    public bool IsDisposed { get; private set; }
    public PoolStatistics Statistics => new(CountActive, CountInactive, _peakActive,
        _created, _gets, _reused, _released, _destroyed);

    public ObjectPool(Func<T> create, Action<T>? onGet = null, Action<T>? onRelease = null,
        Action<T>? onDestroy = null, int maxSize = 100, int defaultCapacity = 0,
        int maxActive = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(defaultCapacity, maxSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxActive, 1);
        PoolMethodPreparation<T>.Prepare();
        _create = create;
        _onGet = onGet;
        _onRelease = onRelease;
        _onDestroy = onDestroy;
        MaxSize = maxSize;
        MaxActive = maxActive;
        _inactive = new Stack<T>(defaultCapacity);
    }

    public T Get()
    {
        using var operation = BeginOperation();
        return TryGetCore(out var item) ? item : throw new InvalidOperationException("The pool's active rental limit was reached.");
    }

    /// <summary>Returns false only when MaxActive is reached. Factory and callback errors propagate.</summary>
    public bool TryGet([MaybeNullWhen(false)] out T item)
    {
        using var operation = BeginOperation();
        return TryGetCore(out item);
    }

    public PooledObject<T> Rent()
    {
        var item = Get();
        return new PooledObject<T>(this, item, _active[item]);
    }

    public void Release(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var operation = BeginOperation();
        if (!_active.Remove(item))
            throw new InvalidOperationException(_cached.Contains(item)
                ? "Object was already released." : "Object does not belong to this pool or is no longer active.");
        ReturnCore(item);
    }

    /// <summary>Returns false for null, foreign or already returned objects. Callback errors propagate.</summary>
    public bool TryRelease(T item)
    {
        using var operation = BeginOperation();
        if (item is null || !_active.Remove(item)) return false;
        ReturnCore(item);
        return true;
    }

    public bool Owns(T item) => item is not null && (_active.ContainsKey(item) || _cached.Contains(item));
    public bool IsActive(T item) => item is not null && _active.ContainsKey(item);

    /// <summary>
    /// Ensures at least targetInactiveCount cached objects, up to MaxSize. New objects receive
    /// onRelease to initialize their idle state; onGet is not called. Returns the number created.
    /// </summary>
    public int Prewarm(int targetInactiveCount)
    {
        using var operation = BeginOperation();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ValidateInactiveCount(targetInactiveCount);
        var count = Math.Max(0, targetInactiveCount - CountInactive);
        using var budget = LoopExecutionGuard.AllowIterations(count);
        for (var i = 0; i < count; i++)
        {
            var item = CreateItem();
            try { _onRelease?.Invoke(item); }
            catch (Exception failure) { DestroyAfterFailure(item, failure); throw; }
            Cache(item);
        }
        return count;
    }

    /// <summary>Destroys excess cached objects. Active rentals are untouched.</summary>
    public int Trim(int targetInactiveCount = 0)
    {
        using var operation = BeginOperation();
        ValidateInactiveCount(targetInactiveCount);
        return TrimCore(targetInactiveCount);
    }

    public void Clear() => Trim();

    /// <summary>Clears the cache and stops acquisition. Outstanding objects are destroyed on return.</summary>
    public void Dispose()
    {
        using var operation = BeginOperation();
        if (IsDisposed) return;
        IsDisposed = true;
        TrimCore(0);
    }

    internal bool IsRentalActive(T item, long generation) =>
        _active.TryGetValue(item, out var current) && current == generation;

    internal void ReleaseRental(T item, long generation)
    {
        using var operation = BeginOperation();
        if (!IsRentalActive(item, generation)) return;
        _active.Remove(item);
        ReturnCore(item);
    }

    private bool TryGetCore([MaybeNullWhen(false)] out T item)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (CountActive >= MaxActive) { item = null; return false; }
        var reused = _inactive.TryPop(out var cached);
        item = reused ? cached! : CreateItem();
        if (reused) _cached.Remove(item);
        _active.Add(item, ++_generation);
        try { _onGet?.Invoke(item); }
        catch (Exception failure)
        {
            _active.Remove(item);
            DestroyAfterFailure(item, failure);
            throw;
        }
        _gets++;
        if (reused) _reused++;
        _peakActive = Math.Max(_peakActive, CountActive);
        return true;
    }

    private T CreateItem()
    {
        var item = _create() ?? throw new InvalidOperationException("The pool factory returned null.");
        if (Owns(item)) throw new InvalidOperationException("The pool factory returned an object already owned by this pool.");
        _created++;
        return item;
    }

    private void ReturnCore(T item)
    {
        try { _onRelease?.Invoke(item); }
        catch (Exception failure) { DestroyAfterFailure(item, failure); throw; }
        _released++;
        if (!IsDisposed && CountInactive < MaxSize) Cache(item);
        else DestroyItem(item);
    }

    private void Cache(T item)
    {
        _inactive.Push(item);
        _cached.Add(item);
    }

    private void DestroyItem(T item)
    {
        _destroyed++;
        _onDestroy?.Invoke(item);
    }

    private void DestroyAfterFailure(T item, Exception failure)
    {
        try { DestroyItem(item); }
        catch (Exception cleanup) { throw new AggregateException("Pool callback and cleanup both failed.", failure, cleanup); }
    }

    private int TrimCore(int target)
    {
        var count = Math.Max(0, CountInactive - target);
        using var budget = LoopExecutionGuard.AllowIterations(count);
        List<Exception>? failures = null;
        for (var i = 0; i < count; i++)
        {
            var item = _inactive.Pop();
            _cached.Remove(item);
            try { DestroyItem(item); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        if (failures is not null) throw new AggregateException("One or more pooled objects could not be destroyed.", failures);
        return count;
    }

    private void ValidateInactiveCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxSize);
    }

    private Operation BeginOperation()
    {
        if (_busy) throw new InvalidOperationException("Pool callbacks cannot recursively mutate the same pool.");
        _busy = true;
        return new Operation(this);
    }

    private readonly struct Operation(ObjectPool<T> pool) : IDisposable
    {
        public void Dispose() => pool._busy = false;
    }
}
