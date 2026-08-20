namespace BEngine.Entities;

public sealed class EntityCommandBuffer
{
    private readonly List<Action<EntityManager>> _commands = [];

    public int Count
    {
        get
        {
            MainThreadGuard.Ensure();
            return _commands.Count;
        }
    }
    public bool IsEmpty
    {
        get
        {
            MainThreadGuard.Ensure();
            return _commands.Count == 0;
        }
    }

    public void AddComponentData<T>(Entity entity, T component) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        _commands.Add(manager => manager.AddComponentData(entity, component));
    }

    public void SetComponentData<T>(Entity entity, T component) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        _commands.Add(manager => manager.SetComponentData(entity, component));
    }

    public void RemoveComponent<T>(Entity entity) where T : struct, IComponentData
    {
        MainThreadGuard.Ensure();
        _commands.Add(manager => manager.RemoveComponent<T>(entity));
    }

    public void DestroyEntity(Entity entity)
    {
        MainThreadGuard.Ensure();
        _commands.Add(manager => manager.DestroyEntity(entity));
    }

    public void Playback(EntityManager manager)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(manager);
        if (_commands.Count == 0) return;
        var commands = _commands.ToArray();
        _commands.Clear();
        foreach (var command in commands) command(manager);
    }

    public void Clear()
    {
        MainThreadGuard.Ensure();
        _commands.Clear();
    }
}
