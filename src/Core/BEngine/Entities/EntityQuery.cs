using System.Collections;

namespace BEngine.Entities;

public sealed class EntityQuery : IReadOnlyCollection<Entity>
{
    private readonly EntityManager _manager;
    private readonly ComponentType[] _components;
    private Entity[] _snapshot = [];
    private long _snapshotVersion = -1;

    internal EntityQuery(EntityManager manager, ComponentType[] components)
    {
        _manager = manager;
        _components = components;
    }

    public int Count => Snapshot().Length;
    public EntityQueryEnumerator GetEnumerator() => new(Snapshot());
    public Entity[] ToEntityArray() => [.. Snapshot()];

    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => ((IEnumerable<Entity>)Snapshot()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Entity>)this).GetEnumerator();

    internal Entity[] Snapshot()
    {
        MainThreadGuard.Ensure();
        if (_snapshotVersion == _manager.StructuralVersion) return _snapshot;
        _snapshot = _manager.BuildQuerySnapshot(_components);
        _snapshotVersion = _manager.StructuralVersion;
        return _snapshot;
    }
}
