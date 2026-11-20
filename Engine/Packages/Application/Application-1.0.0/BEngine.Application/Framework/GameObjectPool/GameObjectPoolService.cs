namespace BEngine.App.Pooling;

/// <summary>GameObject lifecycle policies around the Pooling package's single-loop ObjectPool.
/// Factories transfer ownership of fresh objects to this service. Parent transforms are borrowed.
/// A scene-owned idle parent must survive for as long as the pool (or unregister that pool on scene exit).
/// This service does not silently move objects to other scenes or make them persistent.</summary>
public sealed class GameObjectPoolService : GameService
{
    private readonly Dictionary<string, Pool> _pools = new(StringComparer.Ordinal);
    private readonly Dictionary<GameObject, OwnedObject> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly Transform? _defaultIdleParent;
    private bool _busy, _disposed, _disposePending;
    private int _callbackDepth;

    public int PoolCount => _pools.Count;
    public int ObjectCount => _objects.Count;
    public GameObjectPoolService(Transform? idleParent = null) => _defaultIdleParent = idleParent;

    /// <summary>onSpawn/onDespawn customize borrowed component state. Transform's original local pose
    /// is restored on return by default. Callbacks must not recursively mutate this service/pool.</summary>
    public void Register(string key, Func<GameObject> factory, Transform? idleParent = null,
        int maxInactive = 100, int maxActive = int.MaxValue, bool resetTransformOnReturn = true,
        Action<GameObject>? onSpawn = null, Action<GameObject>? onDespawn = null)
    {
        using var operation = BeginOperation();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        if (_pools.ContainsKey(key)) throw new InvalidOperationException($"Object pool '{key}' is already registered.");
        var parent = idleParent ?? _defaultIdleParent;
        ValidateParent(parent);
        var pool = new Pool(this, factory, parent, maxInactive, maxActive,
            resetTransformOnReturn, onSpawn, onDespawn);
        _pools.Add(key, pool);
    }

    public GameObject Get(string key, Transform? parent = null)
    {
        using var operation = BeginOperation();
        var pool = FindPool(key);
        ValidateParent(parent);
        var item = GetCore(pool);
        try { SetParent(item, parent); }
        catch (Exception error)
        {
            try { pool.Objects.Release(item); }
            catch (Exception cleanup) { throw new AggregateException("Object positioning and return failed.", error, cleanup); }
            throw;
        }
        return item;
    }
    /// <summary>A generation-checked rental backed by the Pooling package. Disposing a stale rental cannot
    /// return a newer acquisition of the same GameObject. No wrapper singleton or global registry is used.</summary>
    public GameObjectRental Rent(string key, Transform? parent = null)
    {
        using var operation = BeginOperation();
        var pool = FindPool(key);
        ValidateParent(parent);
        // Discard cached objects destroyed by their scene before acquiring a generation-checked rental.
        PooledObject<GameObject> rental;
        while (true)
        {
            try { rental = pool.Objects.Rent(); break; }
            catch (DestroyedCachedObjectException) { }
        }
        try { SetParent(rental.Value, parent); }
        catch (Exception error)
        {
            try { rental.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Object positioning and return failed.", error, cleanup); }
            throw;
        }
        return new GameObjectRental(this, rental);
    }

    internal void ReturnRental(PooledObject<GameObject> rental)
    {
        if (rental.IsReturned) { rental.Dispose(); return; }
        using var operation = BeginOperation();
        rental.Dispose();
    }

    public void Return(GameObject item)
    {
        using var operation = BeginOperation();
        ArgumentNullException.ThrowIfNull(item);
        if (!_objects.TryGetValue(item, out var owned)) throw new InvalidOperationException("This service does not own the GameObject.");
        owned.Pool.Objects.Release(item);
    }
    public bool TryReturn(GameObject item)
    {
        using var operation = BeginOperation();
        return item is not null && _objects.TryGetValue(item, out var owned) && owned.Pool.Objects.TryRelease(item);
    }
    public bool Owns(GameObject item) => item is not null && _objects.ContainsKey(item);
    public bool IsActive(GameObject item) => item is not null && _objects.TryGetValue(item, out var owned) && owned.Pool.Objects.IsActive(item);
    public PoolStatistics GetStatistics(string key) { CheckAvailable(); return FindPool(key).Objects.Statistics; }
    public int Prewarm(string key, int count)
    {
        using var operation = BeginOperation();
        return FindPool(key).Objects.Prewarm(count);
    }
    public int Trim(string key, int inactiveCount = 0)
    {
        using var operation = BeginOperation();
        return FindPool(key).Objects.Trim(inactiveCount);
    }
    /// <summary>Destroys all objects belonging to this pool, including outstanding rentals.
    /// Borrowed scene parents and prefab assets captured by the factory are never destroyed.</summary>
    public bool Unregister(string key)
    {
        using var operation = BeginOperation();
        if (!_pools.Remove(key, out var pool)) return false;
        DestroyPool(pool);
        return true;
    }
    public void Clear()
    {
        using var operation = BeginOperation();
        ClearCore();
    }
    protected override void OnDispose()
    {
        if (_disposed) return;
        // Domain changes requested by factory/spawn/despawn callbacks cannot recursively dispose
        // ObjectPool while its operation guard is active. Drain at our outer operation boundary.
        if (_busy || _callbackDepth != 0) { _disposePending = true; return; }
        DisposeCore();
    }
    private void DisposeCore()
    {
        _disposed = true;
        _busy = true;
        try { ClearCore(); }
        finally { _busy = false; }
    }

    private GameObject GetCore(Pool pool)
    {
        while (true)
        {
            try { return pool.Objects.Get(); }
            catch (DestroyedCachedObjectException) { }
        }
    }
    private Pool FindPool(string key) => _pools.TryGetValue(key, out var pool)
        ? pool : throw new KeyNotFoundException($"Object pool '{key}' is not registered.");
    private void ClearCore()
    {
        List<Exception>? failures = null;
        foreach (var pool in _pools.Values)
            try { DestroyPool(pool); } catch (Exception error) { (failures ??= []).Add(error); }
        _pools.Clear();
        if (failures is not null) throw new AggregateException("Object pools could not all be released.", failures);
    }
    private void DestroyPool(Pool pool)
    {
        List<Exception>? failures = null;
        // ObjectPool.Dispose releases inactive objects. Explicitly return remaining active objects to
        // the disposed pool so their rentals are invalidated and their destroy policy runs exactly once.
        try { pool.Objects.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        var count = pool.Objects.CountActive;
        if (count != 0)
        {
            // Idle-only pools need no ownership scan. Snapshot exactly the outstanding rentals before
            // callbacks remove them from the index, without a growing List for each individual pool.
            var active = new GameObject[count];
            var index = 0;
            foreach (var pair in _objects)
                if (ReferenceEquals(pair.Value.Pool, pool)) active[index++] = pair.Key;
            for (var i = 0; i < index; i++)
                try { pool.Objects.TryRelease(active[i]); } catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException("A GameObject pool could not be fully released.", failures);
    }
    private GameObject Create(Pool pool)
    {
        ValidateParent(pool.Parent);
        var item = pool.Factory() ?? throw new InvalidOperationException("GameObject factory returned null.");
        if (_objects.ContainsKey(item)) throw new InvalidOperationException("A pool factory returned an already owned GameObject.");
        if (pool.Parent is { } parent && (ReferenceEquals(item, parent.gameObject) || parent.IsChildOf(item.transform)))
            throw new InvalidOperationException("A pool factory cannot transfer ownership of its borrowed parent hierarchy.");
        if (!IsAlive(item)) throw new InvalidOperationException("GameObject factory returned a destroyed object or an object in a disposed scene.");
        if (IsDisposed)
        {
            // The factory may have requested a domain replacement. Ownership has been validated,
            // but ObjectPool has not accepted this item yet, so release the fresh result here.
            BObject.Destroy(item);
            throw new ObjectDisposedException(nameof(GameObjectPoolService));
        }
        var transform = item.transform;
        _objects.Add(item, new OwnedObject(pool, transform.localPosition, transform.localRotation, transform.localScale));
        return item;
    }
    private void Spawn(Pool pool, GameObject item)
    {
        ValidateParent(pool.Parent);
        CheckAvailable();
        if (!IsAlive(item)) throw new DestroyedCachedObjectException();
        // Remove the object from its idle hierarchy before activation. The caller may parent it only
        // to another transform in the same scene; Core owns and validates scene transfers.
        item.transform.SetParent(null, false);
        item.SetActive(true);
        InvokeCallback(pool.OnSpawn, item);
        CheckAvailable();
    }
    private void Despawn(Pool pool, GameObject item)
    {
        if (!IsAlive(item)) return;
        item.SetActive(false);
        InvokeCallback(pool.OnDespawn, item);
        // A destroyed idle parent makes this return fail and ObjectPool destroys the object instead
        // of retaining a reference into an invalid scene.
        ValidateParent(pool.Parent);
        SetParent(item, pool.Parent);
        if (pool.ResetTransform && _objects.TryGetValue(item, out var owned))
        {
            item.transform.localPosition = owned.LocalPosition;
            item.transform.localRotation = owned.LocalRotation;
            item.transform.localScale = owned.LocalScale;
        }
    }
    private void Destroy(GameObject item)
    {
        _objects.Remove(item);
        if (!item.isDestroyed) BObject.Destroy(item);
    }
    private static bool IsAlive(GameObject item) => !item.isDestroyed && item.scene is not { isCreated: false };
    private void InvokeCallback(Action<GameObject>? callback, GameObject item)
    {
        if (callback is null) return;
        _callbackDepth++;
        try { callback(item); }
        finally { _callbackDepth--; }
    }
    private static void ValidateParent(Transform? parent)
    {
        if (parent is not null && (parent.isDestroyed || !IsAlive(parent.gameObject)))
            throw new InvalidOperationException("The GameObject pool parent no longer exists. Unregister its scene-owned pool.");
    }
    private static void SetParent(GameObject item, Transform? parent)
    {
        if (ReferenceEquals(item.transform.parent, parent)) return;
        item.transform.SetParent(parent, false);
    }
    private void CheckAvailable() => ObjectDisposedException.ThrowIf(_disposed || IsDisposed, this);
    private Operation BeginOperation()
    {
        CheckAvailable();
        if (_busy || _callbackDepth != 0) throw new InvalidOperationException("Pool callbacks cannot recursively mutate this service.");
        _busy = true;
        return new Operation(this);
    }
    private sealed class Pool
    {
        public readonly Func<GameObject> Factory;
        public readonly Transform? Parent;
        public readonly bool ResetTransform;
        public readonly Action<GameObject>? OnSpawn, OnDespawn;
        public readonly ObjectPool<GameObject> Objects;
        public Pool(GameObjectPoolService service, Func<GameObject> factory, Transform? parent,
            int maxInactive, int maxActive, bool resetTransform, Action<GameObject>? onSpawn, Action<GameObject>? onDespawn)
        {
            Factory = factory; Parent = parent; ResetTransform = resetTransform;
            OnSpawn = onSpawn; OnDespawn = onDespawn;
            Objects = new ObjectPool<GameObject>(() => service.Create(this), item => service.Spawn(this, item),
                item => service.Despawn(this, item), service.Destroy, maxInactive, maxActive: maxActive);
        }
    }
    private readonly record struct OwnedObject(Pool Pool, Vector2 LocalPosition, Fix64 LocalRotation, Vector2 LocalScale);
    private sealed class DestroyedCachedObjectException : InvalidOperationException;
    private readonly struct Operation(GameObjectPoolService service) : IDisposable
    {
        public void Dispose()
        {
            service._busy = false;
            if (service._disposePending) { service._disposePending = false; service.DisposeCore(); }
        }
    }
}
