using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Entities;

public sealed class SimulationSystemGroup : IDisposable
{
    private readonly World _world;
    private readonly List<ISystem> _systems = [];
    private readonly HashSet<ISystem> _created = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ISystem> _running = new(ReferenceEqualityComparer.Instance);
    private ISystem[] _snapshot = [];
    private bool _snapshotDirty = true;
    private bool _started;
    private bool _disposed;

    internal SimulationSystemGroup(World world) => _world = world;

    public IReadOnlyList<ISystem> Systems
    {
        get
        {
            MainThreadGuard.Ensure();
            return _systems;
        }
    }

    public void AddSystem(ISystem system)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(system);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_systems.Contains(system)) return;
        _systems.Add(system);
        _systems.Sort(static (left, right) =>
        {
            var order = left.order.CompareTo(right.order);
            return order != 0 ? order : string.Compare(left.GetType().FullName, right.GetType().FullName,
                StringComparison.Ordinal);
        });
        _snapshotDirty = true;
        EnsureCreated(system);
        if (_started) StartSystem(system);
    }

    public bool RemoveSystem(ISystem system)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(system);
        if (!_systems.Remove(system)) return false;
        _snapshotDirty = true;
        StopSystem(system);
        DestroySystem(system);
        return true;
    }

    public T GetOrCreateSystem<T>() where T : class, ISystem
    {
        MainThreadGuard.Ensure();
        if (_systems.OfType<T>().FirstOrDefault() is { } existing) return existing;
        var created = _world.Services.GetService<T>() ??
                      ActivatorUtilities.CreateInstance<T>(_world.Services);
        AddSystem(created);
        return created;
    }

    public ISystem GetOrCreateSystem(Type systemType)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(systemType);
        if (!typeof(ISystem).IsAssignableFrom(systemType) || systemType.IsAbstract)
            throw new ArgumentException($"{systemType.FullName} is not a concrete ECS system.", nameof(systemType));
        if (_systems.FirstOrDefault(system => system.GetType() == systemType) is { } existing) return existing;
        var created = (ISystem)(_world.Services.GetService(systemType) ??
                                ActivatorUtilities.CreateInstance(_world.Services, systemType));
        AddSystem(created);
        return created;
    }

    internal void Start()
    {
        MainThreadGuard.Ensure();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        foreach (var system in _systems) StartSystem(system);
    }

    internal void FixedUpdate(Fix64 deltaTime)
    {
        MainThreadGuard.Ensure();
        if (!_started) return;
        var state = new SystemState(_world, deltaTime, isFixedStep: true);
        foreach (var system in Snapshot())
        {
            if (!system.enabled || !_running.Contains(system)) continue;
            try { system.OnFixedUpdate(ref state); }
            catch (Exception exception) { LogFailure(system, "OnFixedUpdate", exception); }
        }
        PlaybackCommands();
    }

    internal void Update(Fix64 deltaTime)
    {
        MainThreadGuard.Ensure();
        if (!_started) return;
        var state = new SystemState(_world, deltaTime, isFixedStep: false);
        foreach (var system in Snapshot())
        {
            if (!system.enabled || !_running.Contains(system)) continue;
            try { system.OnUpdate(ref state); }
            catch (Exception exception) { LogFailure(system, "OnUpdate", exception); }
        }
        PlaybackCommands();
    }

    internal void Stop()
    {
        MainThreadGuard.Ensure();
        if (!_started) return;
        for (var index = _systems.Count - 1; index >= 0; index--) StopSystem(_systems[index]);
        _started = false;
    }

    public void Dispose()
    {
        MainThreadGuard.Ensure();
        if (_disposed) return;
        Stop();
        for (var index = _systems.Count - 1; index >= 0; index--) DestroySystem(_systems[index]);
        _systems.Clear();
        _snapshot = [];
        _snapshotDirty = false;
        _disposed = true;
    }

    private void EnsureCreated(ISystem system)
    {
        if (!_created.Add(system)) return;
        var state = new SystemState(_world, Fix64.Zero, isFixedStep: false);
        try { system.OnCreate(ref state); }
        catch (Exception exception) { LogFailure(system, "OnCreate", exception); }
    }

    private void StartSystem(ISystem system)
    {
        EnsureCreated(system);
        if (!_running.Add(system)) return;
        var state = new SystemState(_world, Fix64.Zero, isFixedStep: false);
        try { system.OnStartRunning(ref state); }
        catch (Exception exception) { LogFailure(system, "OnStartRunning", exception); }
    }

    private void StopSystem(ISystem system)
    {
        if (!_running.Remove(system)) return;
        var state = new SystemState(_world, Fix64.Zero, isFixedStep: false);
        try { system.OnStopRunning(ref state); }
        catch (Exception exception) { LogFailure(system, "OnStopRunning", exception); }
    }

    private void DestroySystem(ISystem system)
    {
        if (!_created.Remove(system)) return;
        var state = new SystemState(_world, Fix64.Zero, isFixedStep: false);
        try { system.OnDestroy(ref state); }
        catch (Exception exception) { LogFailure(system, "OnDestroy", exception); }
    }

    private static void LogFailure(ISystem system, string callback, Exception exception) =>
        Debug.LogError($"{system.GetType().FullName}.{callback} failed: {exception.Message}");

    private ISystem[] Snapshot()
    {
        if (!_snapshotDirty) return _snapshot;
        _snapshot = [.. _systems];
        _snapshotDirty = false;
        return _snapshot;
    }

    private void PlaybackCommands()
    {
        try { _world.EndSimulationEntityCommandBuffer.Playback(_world.EntityManager); }
        catch (Exception exception)
        {
            Debug.LogError($"ECS command buffer playback failed: {exception.Message}");
        }
    }
}
