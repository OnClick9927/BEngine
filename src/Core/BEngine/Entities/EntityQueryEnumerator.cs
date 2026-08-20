namespace BEngine.Entities;

public struct EntityQueryEnumerator
{
    private readonly Entity[] _entities;
    private int _index;

    internal EntityQueryEnumerator(Entity[] entities)
    {
        _entities = entities;
        _index = -1;
    }

    public Entity Current => _entities[_index];
    public bool MoveNext() => ++_index < _entities.Length;
}
