namespace BEngine.Entities;

public readonly struct SystemState
{
    internal SystemState(World world, Fix64 deltaTime, bool isFixedStep)
    {
        World = world;
        DeltaTime = deltaTime;
        IsFixedStep = isFixedStep;
    }

    public World World { get; }
    public EntityManager EntityManager => World.EntityManager;
    public IServiceProvider Services => World.Services;
    public EntityCommandBuffer CommandBuffer => World.EndSimulationEntityCommandBuffer;
    public Scene? Scene => World.Scene;
    public Fix64 DeltaTime { get; }
    public bool IsFixedStep { get; }
}
