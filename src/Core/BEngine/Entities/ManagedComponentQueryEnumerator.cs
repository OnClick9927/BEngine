namespace BEngine.Entities;

public struct ManagedComponentQueryEnumerator<T> where T : class
{
    private readonly EntityManager _manager;
    private readonly Entity[] _entities;
    private int _entityIndex;
    private int _componentIndex;
    private T? _current;

    internal ManagedComponentQueryEnumerator(EntityManager manager, Entity[] entities)
    {
        _manager = manager;
        _entities = entities;
        _entityIndex = 0;
        _componentIndex = -1;
        _current = null;
    }

    public T Current => _current!;

    public bool MoveNext()
    {
        while (_entityIndex < _entities.Length)
        {
            var components = _manager.GetManagedComponentsRaw(_entities[_entityIndex]);
            while (++_componentIndex < components.Count)
            {
                if (components[_componentIndex] is not T component) continue;
                _current = component;
                return true;
            }

            _entityIndex++;
            _componentIndex = -1;
        }

        _current = null;
        return false;
    }
}
