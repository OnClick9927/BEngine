using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.App.State;

/// <summary>
/// Explicit, instance-owned state registry driven from the game thread.
/// Initialize does not enter a state automatically. Failed Exit or Enter leaves no active state;
/// failed StateChanged callbacks leave the successfully entered state active.
/// </summary>
public sealed class GameStateService : IGameService
{
    private enum Phase { Configuring, Initializing, Ready, Faulted, Disposed }
    private IServiceProvider? _services;
    private readonly List<Registration> _registrations = [];
    private readonly Dictionary<Type, Registration> _byType = [];
    private readonly HashSet<IGameState> _instances = new(ReferenceEqualityComparer.Instance);
    private Registration? _current;
    private GameStateContext? _context;
    private Phase _phase;
    private bool _transitioning;
    private bool _ticking;
    private bool _hasPending;
    private Type? _pendingType;
    private BCancellationToken _pendingToken;

    public GameStateService(IServiceProvider services) => _services = services ?? throw new ArgumentNullException(nameof(services));
    public IGameState? Current => _current?.Instance;
    public bool IsInitialized => _phase == Phase.Ready;
    public event Action<IGameState?, IGameState?>? StateChanged;

    void IGameService.Initialize(GameDomain domain) => Initialize(domain.Lifetime.Token);
    void IGameService.Update(Fix64 deltaTime) => Tick(deltaTime);
    void IGameService.FixedUpdate(Fix64 deltaTime) => FixedTick(deltaTime);
    void IGameService.LateUpdate(Fix64 deltaTime) => LateTick(deltaTime);

    public GameStateService Register<TState>(TState state, bool ownsInstance = true) where TState : class, IGameState
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureConfiguring();
        if (_instances.Contains(state)) throw new InvalidOperationException("The same state instance cannot be registered twice.");
        Add(new Registration(typeof(TState), null, state, ownsInstance));
        _instances.Add(state); return this;
    }

    public GameStateService Register<TState>(Func<IServiceProvider, TState> factory, bool ownsInstance = true) where TState : class, IGameState
    {
        ArgumentNullException.ThrowIfNull(factory); EnsureConfiguring();
        Add(new Registration(typeof(TState), provider => factory(provider), null, ownsInstance)); return this;
    }

    public GameStateService Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TState>() where TState : class, IGameState =>
        Register<TState>(static provider => ActivatorUtilities.CreateInstance<TState>(provider));

    private void Add(Registration registration)
    {
        if (!_byType.TryAdd(registration.Type, registration)) throw new InvalidOperationException($"State '{registration.Type}' is already registered.");
        _registrations.Add(registration);
    }

    public TState Get<TState>() where TState : class, IGameState => (TState)Get(typeof(TState));
    public IGameState Get(Type stateType)
    {
        ArgumentNullException.ThrowIfNull(stateType); EnsureUsable();
        if (!_byType.TryGetValue(stateType, out var registration)) throw new KeyNotFoundException($"State '{stateType}' is not registered.");
        return registration.Instance ?? throw new InvalidOperationException("Initialize states before resolving factory-created instances.");
    }

    public void Initialize(BCancellationToken cancellationToken = default)
    {
        EnsureConfiguring(); cancellationToken.ThrowIfCancellationRequested(); _phase = Phase.Initializing;
        try
        {
            foreach (var registration in _registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (registration.Instance is not null) continue;
                var instance = registration.Factory!(_services!) ?? throw new InvalidOperationException($"State factory '{registration.Type}' returned null.");
                if (!_instances.Add(instance)) throw new InvalidOperationException("State factories must return distinct instances.");
                registration.Instance = instance; registration.Factory = null;
            }
            foreach (var registration in _registrations)
            { cancellationToken.ThrowIfCancellationRequested(); registration.Instance!.Initialize(_services!); }
            cancellationToken.ThrowIfCancellationRequested(); _phase = Phase.Ready;
        }
        catch (Exception error)
        {
            List<Exception>? errors = [error];
            Cleanup(ref errors); _phase = Phase.Faulted;
            StateErrors.Throw(errors); throw;
        }
    }

    /// <summary>Switches immediately, or after the current tick callback returns. At most one distinct request per tick.</summary>
    public bool Switch<TState>(BCancellationToken cancellationToken = default) where TState : class, IGameState => Switch(typeof(TState), cancellationToken);
    public bool Switch(Type stateType, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stateType); EnsureReady(); cancellationToken.ThrowIfCancellationRequested();
        if (!_byType.ContainsKey(stateType)) throw new KeyNotFoundException($"State '{stateType}' is not registered.");
        return RequestTransition(stateType, cancellationToken);
    }

    public bool Exit() { EnsureReady(); return RequestTransition(null, default); }

    private bool RequestTransition(Type? stateType, BCancellationToken token)
    {
        if (_transitioning) throw new InvalidOperationException("State transitions cannot be reentered from Enter, Exit or StateChanged.");
        if (_ticking)
        {
            if (_hasPending && _pendingType != stateType) throw new InvalidOperationException("Only one distinct state transition can be requested in a tick.");
            _pendingType = stateType; _pendingToken = token; _hasPending = true;
            return _current?.Type != stateType;
        }
        return Transition(stateType, token);
    }

    private bool Transition(Type? stateType, BCancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var next = stateType is null ? null : _byType[stateType];
        if (ReferenceEquals(next, _current)) return false;
        _transitioning = true;
        try
        {
            var previous = Current;
            List<Exception>? errors = null;
            ExitCurrent(next?.Instance, ref errors);
            StateErrors.Throw(errors);
            token.ThrowIfCancellationRequested();
            if (next is not null)
            {
                var context = new GameStateContext(_services!, previous, token);
                try
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    next.Instance!.Enter(context);
                    context.CancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception error)
                {
                    errors = [error];
                    try { next.Instance!.Exit(null); } catch (Exception cleanup) { StateErrors.Add(ref errors, cleanup); }
                    try { context.Dispose(); } catch (Exception cleanup) { StateErrors.Add(ref errors, cleanup); }
                    StateErrors.Throw(errors); throw;
                }
                _context = context; _current = next;
            }
            StateChanged?.Invoke(previous, Current);
            return true;
        }
        finally { _transitioning = false; }
    }

    public void Tick(Fix64 deltaTime) => TickCore(deltaTime, 0);
    public void FixedTick(Fix64 deltaTime) => TickCore(deltaTime, 1);
    public void LateTick(Fix64 deltaTime) => TickCore(deltaTime, 2);

    private void TickCore(Fix64 deltaTime, int kind)
    {
        EnsureReady();
        if (_ticking || _transitioning) throw new InvalidOperationException("State ticks cannot be reentered.");
        if (_context?.CancellationToken.IsCancellationRequested == true) { Exit(); return; }
        var state = Current; if (state is null) return;
        _ticking = true;
        var succeeded = false;
        try
        {
            if (kind == 0) state.Update(deltaTime);
            else if (kind == 1) state.FixedUpdate(deltaTime);
            else state.LateUpdate(deltaTime);
            succeeded = true;
        }
        finally
        {
            _ticking = false;
            if (!succeeded) { _hasPending = false; _pendingType = null; _pendingToken = default; }
        }
        if (!_hasPending) return;
        var pending = _pendingType; var token = _pendingToken;
        _hasPending = false; _pendingType = null; _pendingToken = default;
        Transition(pending, token);
    }

    private void ExitCurrent(IGameState? next, ref List<Exception>? errors)
    {
        var previous = Current; var context = _context;
        _current = null; _context = null;
        if (previous is not null)
            try { previous.Exit(next); } catch (Exception error) { StateErrors.Add(ref errors, error); }
        if (context is not null)
            try { context.Dispose(); } catch (Exception error) { StateErrors.Add(ref errors, error); }
    }

    public void Dispose()
    {
        if (_phase == Phase.Disposed) return;
        if (_transitioning || _ticking || _phase == Phase.Initializing) throw new InvalidOperationException("A state service cannot be disposed from its lifecycle callback.");
        _phase = Phase.Disposed; _transitioning = true;
        List<Exception>? errors = null;
        try { Cleanup(ref errors); }
        finally { _transitioning = false; }
        StateErrors.Throw(errors);
    }

    private void Cleanup(ref List<Exception>? errors)
    {
        ExitCurrent(null, ref errors);
        for (var index = _registrations.Count - 1; index >= 0; index--)
        {
            var registration = _registrations[index];
            if (registration.OwnsInstance && registration.Instance is { } instance)
                try { instance.Dispose(); } catch (Exception error) { StateErrors.Add(ref errors, error); }
            registration.Instance = null; registration.Factory = null;
        }
        _registrations.Clear(); _byType.Clear(); _instances.Clear();
        _hasPending = false; _pendingType = null; _pendingToken = default;
        _services = null; StateChanged = null;
    }

    private void EnsureConfiguring()
    {
        EnsureUsable();
        if (_phase != Phase.Configuring) throw new InvalidOperationException("Register and initialize states only once, before the service starts.");
    }
    private void EnsureReady() { EnsureUsable(); if (_phase != Phase.Ready) throw new InvalidOperationException("Initialize the state service first."); }
    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_phase == Phase.Disposed, this);
        if (_phase == Phase.Faulted) throw new InvalidOperationException("State initialization failed; create a new service to retry.");
    }

    private sealed class Registration(Type type, Func<IServiceProvider, IGameState>? factory, IGameState? instance, bool ownsInstance)
    {
        internal readonly Type Type = type;
        internal Func<IServiceProvider, IGameState>? Factory = factory;
        internal IGameState? Instance = instance;
        internal readonly bool OwnsInstance = ownsInstance;
    }
}
