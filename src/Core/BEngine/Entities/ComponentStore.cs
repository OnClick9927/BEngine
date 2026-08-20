namespace BEngine.Entities;

internal sealed class ComponentStore<T> : IComponentStore where T : struct, IComponentData
{
    private int[] _sparse = new int[16];
    private int[] _entities = new int[16];
    private T[] _values = new T[16];
    private int _count;

    public Type ComponentType => typeof(T);

    public bool Contains(int entityIndex) => entityIndex < _sparse.Length && _sparse[entityIndex] != 0;

    internal void Add(int entityIndex, T value)
    {
        EnsureSparseCapacity(entityIndex + 1);
        if (_sparse[entityIndex] != 0)
            throw new InvalidOperationException($"Entity {entityIndex} already has component {typeof(T).FullName}.");

        EnsureDenseCapacity(_count + 1);
        _entities[_count] = entityIndex;
        _values[_count] = value;
        _sparse[entityIndex] = ++_count;
    }

    internal void Set(int entityIndex, T value)
    {
        var denseIndex = DenseIndex(entityIndex);
        _values[denseIndex] = value;
    }

    internal T Get(int entityIndex) => _values[DenseIndex(entityIndex)];
    internal ref T GetReference(int entityIndex) => ref _values[DenseIndex(entityIndex)];

    public bool Remove(int entityIndex)
    {
        if (!Contains(entityIndex)) return false;

        var denseIndex = _sparse[entityIndex] - 1;
        var lastIndex = --_count;
        if (denseIndex != lastIndex)
        {
            var movedEntity = _entities[lastIndex];
            _entities[denseIndex] = movedEntity;
            _values[denseIndex] = _values[lastIndex];
            _sparse[movedEntity] = denseIndex + 1;
        }

        _entities[lastIndex] = 0;
        _values[lastIndex] = default;
        _sparse[entityIndex] = 0;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_sparse);
        Array.Clear(_entities);
        Array.Clear(_values);
        _count = 0;
    }

    private int DenseIndex(int entityIndex)
    {
        if (!Contains(entityIndex))
            throw new InvalidOperationException($"Entity {entityIndex} does not have component {typeof(T).FullName}.");
        return _sparse[entityIndex] - 1;
    }

    private void EnsureSparseCapacity(int capacity)
    {
        if (capacity <= _sparse.Length) return;
        Array.Resize(ref _sparse, NextCapacity(_sparse.Length, capacity));
    }

    private void EnsureDenseCapacity(int capacity)
    {
        if (capacity <= _entities.Length) return;
        var next = NextCapacity(_entities.Length, capacity);
        Array.Resize(ref _entities, next);
        Array.Resize(ref _values, next);
    }

    private static int NextCapacity(int current, int required)
    {
        var next = Math.Max(16, current);
        while (next < required) next *= 2;
        return next;
    }
}
