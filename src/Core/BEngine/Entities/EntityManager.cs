namespace BEngine.Entities;

public sealed class EntityManager
{
    private readonly List<int> _versions = [0];
    private readonly List<bool> _alive = [false];
    private readonly List<long> _creationSequences = [0];
    private readonly Stack<int> _freeIndices = [];
    private readonly Dictionary<Type, IComponentStore> _componentStores = [];
    private readonly Dictionary<Type, EntityQuery> _managedQueries = [];
    private List<object>?[] _managedComponents = new List<object>?[16];
    private int _entityCount;
    private long _nextCreationSequence;

    internal long StructuralVersion { get; private set; }
    public int EntityCount
    {
        get
        {
            MainThreadGuard.Ensure();
            return _entityCount;
        }
    }

    public Entity CreateEntity()
    {
        MainThreadGuard.Ensure();
        int index;
        if (_freeIndices.TryPop(out var recycled))
        {
            index = recycled;
            _alive[index] = true;
            _creationSequences[index] = ++_nextCreationSequence;
        }
        else
        {
            index = _versions.Count;
            _versions.Add(1);
            _alive.Add(true);
            _creationSequences.Add(++_nextCreationSequence);
            EnsureManagedCapacity(index + 1);
        }

        _entityCount++;
        StructuralVersion++;
        return new Entity(index, _versions[index]);
    }

    public Entity CreateEntity<T>(T component) where T : struct, IComponentData
    {
        var entity = CreateEntity();
        AddComponentData(entity, component);
        return entity;
    }

    public bool DestroyEntity(Entity entity)
    {
        MainThreadGuard.Ensure();
        if (!ExistsUnchecked(entity)) return false;
        if (GetManagedComponent<GameObject>(entity) is { scene: not null } gameObject)
            return gameObject.scene.Destroy(gameObject);
        return DestroyEntityImmediate(entity);
    }

    public bool Exists(Entity entity)
    {
        MainThreadGuard.Ensure();
        return ExistsUnchecked(entity);
    }

    public void AddComponentData<T>(Entity entity, T component) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        GetOrCreateStore<T>().Add(entity.Index, component);
        StructuralVersion++;
    }

    public void AddComponent<T>(Entity entity, T component) where T : struct, IComponentData =>
        AddComponentData(entity, component);

    public void SetComponentData<T>(Entity entity, T component) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        GetStore<T>().Set(entity.Index, component);
    }

    public void SetComponent<T>(Entity entity, T component) where T : struct, IComponentData =>
        SetComponentData(entity, component);

    public T GetComponentData<T>(Entity entity) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        return GetStore<T>().Get(entity.Index);
    }

    public T GetComponent<T>(Entity entity) where T : struct, IComponentData => GetComponentData<T>(entity);

    public ref T GetComponentDataRW<T>(Entity entity) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        return ref GetStore<T>().GetReference(entity.Index);
    }

    public bool TryGetComponentData<T>(Entity entity, out T component) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        if (ExistsUnchecked(entity) && _componentStores.TryGetValue(typeof(T), out var candidate) &&
            candidate is ComponentStore<T> store && store.Contains(entity.Index))
        {
            component = store.Get(entity.Index);
            return true;
        }

        component = default;
        return false;
    }

    public bool HasComponent<T>(Entity entity) => HasComponent(entity, typeof(T));
    public bool HasComponent(Entity entity, Type componentType)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(componentType);
        return HasComponentUnchecked(entity, componentType);
    }

    public bool RemoveComponent<T>(Entity entity) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        if (!ExistsUnchecked(entity) || !_componentStores.TryGetValue(typeof(T), out var store) ||
            !store.Remove(entity.Index)) return false;
        StructuralVersion++;
        return true;
    }

    public EntityQuery CreateEntityQuery(params ComponentType[] components)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(components);
        if (components.Any(component => component.Type is null))
            throw new ArgumentException("A query component type cannot be null.", nameof(components));
        return new EntityQuery(this, [.. components.Distinct()]);
    }

    public ManagedComponentQuery<T> QueryManagedComponents<T>() where T : class
    {
        MainThreadGuard.Ensure();
        if (!_managedQueries.TryGetValue(typeof(T), out var query))
        {
            query = CreateEntityQuery(ComponentType.ReadOnly<T>());
            _managedQueries.Add(typeof(T), query);
        }
        return new ManagedComponentQuery<T>(this, query);
    }

    public T? GetManagedComponent<T>(Entity entity) where T : class
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        var components = _managedComponents[entity.Index];
        if (components is null) return null;
        foreach (var component in components)
            if (component is T typed) return typed;
        return null;
    }

    public IReadOnlyList<T> GetManagedComponents<T>(Entity entity) where T : class
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        var components = _managedComponents[entity.Index];
        return components is null ? [] : [.. components.OfType<T>()];
    }

    internal void AddManagedComponent(Entity entity, object component)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(component);
        Validate(entity);
        var components = _managedComponents[entity.Index] ??= [];
        if (components.Contains(component)) return;
        components.Add(component);
        StructuralVersion++;
    }

    internal bool RemoveManagedComponent(Entity entity, object component)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(component);
        if (!ExistsUnchecked(entity) || _managedComponents[entity.Index] is not { } components ||
            !components.Remove(component)) return false;
        StructuralVersion++;
        return true;
    }

    internal void ReplaceManagedComponent(Entity entity, object previous, object replacement)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        Validate(entity);
        var components = _managedComponents[entity.Index] ??
            throw new InvalidOperationException($"{entity} has no managed components.");
        var index = components.IndexOf(previous);
        if (index < 0) throw new InvalidOperationException($"{entity} does not contain {previous.GetType().FullName}.");
        components[index] = replacement;
        StructuralVersion++;
    }

    internal IReadOnlyList<object> GetManagedComponentsRaw(Entity entity)
    {
        MainThreadGuard.Ensure();
        Validate(entity);
        return _managedComponents[entity.Index] ?? [];
    }

    internal Entity[] BuildQuerySnapshot(ComponentType[] components)
    {
        MainThreadGuard.Ensure();
        var result = new Entity[_entityCount];
        var count = 0;
        for (var index = 1; index < _alive.Count; index++)
        {
            if (!_alive[index]) continue;
            var entity = new Entity(index, _versions[index]);
            var matches = true;
            foreach (var component in components)
            {
                var hasComponent = HasComponentUnchecked(entity, component.Type);
                if (component.AccessMode == ComponentAccessMode.Exclude ? hasComponent : !hasComponent)
                {
                    matches = false;
                    break;
                }
            }
            if (matches) result[count++] = entity;
        }

        if (count != result.Length) Array.Resize(ref result, count);
        Array.Sort(result, (left, right) =>
            _creationSequences[left.Index].CompareTo(_creationSequences[right.Index]));
        return result;
    }

    internal bool DestroyEntityImmediate(Entity entity)
    {
        MainThreadGuard.Ensure();
        if (!ExistsUnchecked(entity)) return false;
        foreach (var store in _componentStores.Values) store.Remove(entity.Index);
        _managedComponents[entity.Index]?.Clear();
        _managedComponents[entity.Index] = null;
        _alive[entity.Index] = false;
        _versions[entity.Index] = unchecked(_versions[entity.Index] + 1);
        if (_versions[entity.Index] == 0) _versions[entity.Index] = 1;
        _freeIndices.Push(entity.Index);
        _entityCount--;
        StructuralVersion++;
        return true;
    }

    internal void Clear()
    {
        MainThreadGuard.Ensure();
        foreach (var store in _componentStores.Values) store.Clear();
        Array.Clear(_managedComponents);
        _versions.Clear();
        _versions.Add(0);
        _alive.Clear();
        _alive.Add(false);
        _creationSequences.Clear();
        _creationSequences.Add(0);
        _freeIndices.Clear();
        _entityCount = 0;
        _nextCreationSequence = 0;
        StructuralVersion++;
    }

    private ComponentStore<T> GetOrCreateStore<T>() where T : struct, IComponentData
    {
        if (_componentStores.TryGetValue(typeof(T), out var existing)) return (ComponentStore<T>)existing;
        var created = new ComponentStore<T>();
        _componentStores.Add(typeof(T), created);
        return created;
    }

    private ComponentStore<T> GetStore<T>() where T : struct, IComponentData =>
        _componentStores.TryGetValue(typeof(T), out var store) && store is ComponentStore<T> typed
            ? typed
            : throw new InvalidOperationException($"No {typeof(T).FullName} component store exists.");

    private void Validate(Entity entity)
    {
        if (!ExistsUnchecked(entity))
            throw new InvalidOperationException($"{entity} is not alive in this EntityManager.");
    }

    private bool ExistsUnchecked(Entity entity) => entity.Index > 0 && entity.Index < _versions.Count &&
        _alive[entity.Index] && _versions[entity.Index] == entity.Version;

    private bool HasComponentUnchecked(Entity entity, Type componentType)
    {
        if (!ExistsUnchecked(entity)) return false;
        if (_componentStores.TryGetValue(componentType, out var store) && store.Contains(entity.Index)) return true;
        var managed = _managedComponents[entity.Index];
        return managed is not null && managed.Exists(componentType.IsInstanceOfType);
    }

    private void EnsureManagedCapacity(int capacity)
    {
        if (capacity <= _managedComponents.Length) return;
        var next = _managedComponents.Length;
        while (next < capacity) next *= 2;
        Array.Resize(ref _managedComponents, next);
    }
}
