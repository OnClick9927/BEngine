using System;

namespace BEngine.App.State;

/// <summary>A state owned by one GameStateService. Callbacks run on the caller's game thread.</summary>
public interface IGameState : IDisposable
{
    void Initialize(IServiceProvider services);
    void Enter(GameStateContext context);
    void Exit(IGameState? next);
    void Update(Fix64 deltaTime) { }
    void FixedUpdate(Fix64 deltaTime) { }
    void LateUpdate(Fix64 deltaTime) { }
}

/// <summary>Resources and cancellation owned by one activation, including a partially failed Enter.</summary>
public sealed class GameStateContext
{
    private readonly LifetimeScope _scope;
    private IServiceProvider? _services;
    private IGameState? _previous;

    internal GameStateContext(IServiceProvider services, IGameState? previous, BCancellationToken cancellationToken)
    {
        _services = services; _previous = previous;
        _scope = new LifetimeScope(cancellationToken);
    }

    public IServiceProvider Services => _services ?? throw new ObjectDisposedException(nameof(GameStateContext));
    public IGameState? Previous => _previous;
    public BCancellationToken CancellationToken => _scope.Token;
    public bool IsDisposed => _scope.IsDisposed;
    public T Track<T>(T resource) where T : IDisposable => _scope.Track(resource);

    internal void Dispose()
    {
        try { _scope.Dispose(); }
        finally { _services = null; _previous = null; }
    }
}
