namespace BEngine.Entities;

public readonly struct ManagedComponentQuery<T> where T : class
{
    private readonly EntityManager _manager;
    private readonly EntityQuery _entities;

    internal ManagedComponentQuery(EntityManager manager, EntityQuery entities)
    {
        _manager = manager;
        _entities = entities;
    }

    public int Count
    {
        get
        {
            var count = 0;
            var enumerator = GetEnumerator();
            while (enumerator.MoveNext()) count++;
            return count;
        }
    }

    public ManagedComponentQueryEnumerator<T> GetEnumerator() => new(_manager, _entities.Snapshot());

    public T[] ToArray()
    {
        var result = new List<T>();
        foreach (var component in this) result.Add(component);
        return [.. result];
    }
}
