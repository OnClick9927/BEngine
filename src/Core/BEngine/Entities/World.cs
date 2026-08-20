using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Entities;

public sealed class World : IDisposable
{
    [ThreadStatic]
    private static World? _current;
    private readonly IServiceScope? _serviceScope;
    private bool _disposed;

    public World(string name = "World") : this(name, null, null) { }

    public World(string name, IServiceProvider services) : this(name, null, services) { }

    internal World(string name, Scene? scene, IServiceProvider? services = null)
    {
        MainThreadGuard.Ensure("Create World");
        Name = string.IsNullOrWhiteSpace(name) ? "World" : name;
        Scene = scene;
        if (services?.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory scopeFactory)
        {
            _serviceScope = scopeFactory.CreateScope();
            Services = _serviceScope.ServiceProvider;
        }
        else
            Services = services ?? EmptyServiceProvider.Instance;
        EntityManager = new EntityManager();
        EndSimulationEntityCommandBuffer = new EntityCommandBuffer();
        SimulationSystemGroup = new SimulationSystemGroup(this);
    }

    public string Name { get; }
    public static World? Current => _current;
    public bool IsCreated => !_disposed;
    public IServiceProvider Services { get; }
    public EntityManager EntityManager { get; }
    public EntityCommandBuffer EndSimulationEntityCommandBuffer { get; }
    public SimulationSystemGroup SimulationSystemGroup { get; }
    public Scene? Scene { get; }

    internal WorldContextScope EnterContext()
    {
        MainThreadGuard.Ensure();
        return new WorldContextScope(this);
    }
    internal static World? SetCurrent(World? world)
    {
        var previous = _current;
        _current = world;
        return previous;
    }

    public void Dispose()
    {
        MainThreadGuard.Ensure();
        if (_disposed) return;
        SimulationSystemGroup.Dispose();
        EndSimulationEntityCommandBuffer.Clear();
        EntityManager.Clear();
        _serviceScope?.Dispose();
        _disposed = true;
    }
}
