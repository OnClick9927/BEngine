using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace BEngine.App.State;

/// <summary>Optional base for data-only states; no Scene, rendering or MonoBehaviour dependency.</summary>
public abstract class GameStateBase : IGameState
{
    private IServiceProvider? _services;
    private GameStateContext? _context;
    private LifetimeScope? _lifetime;
    private bool _initialized;
    private bool _initializationAttempted;
    private bool _disposed;

    protected IServiceProvider Services => _services ?? throw new InvalidOperationException("The state has not been initialized or was disposed.");
    protected GameStateContext Context => _context ?? throw new InvalidOperationException("The state is not active.");
    protected BCancellationToken ActivationToken => Context.CancellationToken;
    protected T Track<T>(T resource) where T : IDisposable => Context.Track(resource);
    protected T TrackLifetime<T>(T resource) where T : IDisposable
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_lifetime ??= new LifetimeScope()).Track(resource);
    }

    public void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initializationAttempted) throw new InvalidOperationException("A state can only be initialized once.");
        _initializationAttempted = true; _services = services;
        OnInitialize(); _initialized = true;
    }

    public void Enter(GameStateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized || _context is not null) throw new InvalidOperationException("The state must be initialized and inactive before Enter.");
        _context = context;
        OnEnter(context.Previous);
    }

    public void Exit(IGameState? next)
    {
        if (_context is null) return;
        try { OnExit(next); }
        finally { _context = null; }
    }

    public void Update(Fix64 deltaTime) { EnsureActive(); OnUpdate(deltaTime); }
    public void FixedUpdate(Fix64 deltaTime) { EnsureActive(); OnFixedUpdate(deltaTime); }
    public void LateUpdate(Fix64 deltaTime) { EnsureActive(); OnLateUpdate(deltaTime); }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context is null) throw new InvalidOperationException("Only an active state may tick.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        var context = _context;
        if (context is not null)
        {
            try { Exit(null); } catch (Exception error) { StateErrors.Add(ref errors, error); }
            try { context.Dispose(); } catch (Exception error) { StateErrors.Add(ref errors, error); }
        }
        try { OnDispose(); } catch (Exception error) { StateErrors.Add(ref errors, error); }
        try { _lifetime?.Dispose(); } catch (Exception error) { StateErrors.Add(ref errors, error); }
        _lifetime = null; _services = null; _context = null;
        GC.SuppressFinalize(this);
        StateErrors.Throw(errors);
    }

    protected virtual void OnInitialize() { }
    protected virtual void OnEnter(IGameState? previous) { }
    protected virtual void OnExit(IGameState? next) { }
    protected virtual void OnUpdate(Fix64 deltaTime) { }
    protected virtual void OnFixedUpdate(Fix64 deltaTime) { }
    protected virtual void OnLateUpdate(Fix64 deltaTime) { }
    protected virtual void OnDispose() { }
}

internal static class StateErrors
{
    internal static void Add(ref List<Exception>? errors, Exception error) => (errors ??= []).Add(error);
    internal static void Throw(List<Exception>? errors)
    {
        if (errors is null) return;
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("State lifecycle cleanup failed.", errors);
    }
}
