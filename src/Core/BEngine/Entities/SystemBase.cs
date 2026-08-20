namespace BEngine.Entities;

public abstract class SystemBase : ISystem
{
    private World? _world;

    public virtual int order => 0;
    public bool enabled { get; set; } = true;
    protected World World => _world ?? throw new InvalidOperationException("The system has not been added to a World.");
    protected EntityManager EntityManager => World.EntityManager;

    public virtual void OnCreate(ref SystemState state) => _world = state.World;
    public virtual void OnStartRunning(ref SystemState state) { }
    public abstract void OnUpdate(ref SystemState state);
    public virtual void OnFixedUpdate(ref SystemState state) { }
    public virtual void OnStopRunning(ref SystemState state) { }
    public virtual void OnDestroy(ref SystemState state) => _world = null;
}
