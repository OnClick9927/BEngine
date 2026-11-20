namespace BEngine.App;

/// <summary>Optional base for a service owned by one domain. Use only on the game loop.</summary>
public abstract class GameService : IGameService
{
    private GameDomain? _domain;
    private bool _initializationAttempted;
    public bool IsInitialized { get; private set; }
    public bool IsDisposed { get; private set; }
    public GameDomain Domain => _domain ?? throw new InvalidOperationException("The service is not attached to a GameDomain.");
    protected LifetimeScope Lifetime { get; } = new();
    protected T Track<T>(T resource) where T : IDisposable => Lifetime.Track(resource);

    public void Initialize(GameDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_initializationAttempted) throw new InvalidOperationException("A service instance can only be initialized once.");
        _initializationAttempted = true;
        _domain = domain;
        OnInitialize();
        IsInitialized = true;
    }
    public void Update(Fix64 deltaTime) { EnsureReady(); OnUpdate(deltaTime); }
    public void FixedUpdate(Fix64 deltaTime) { EnsureReady(); OnFixedUpdate(deltaTime); }
    public void LateUpdate(Fix64 deltaTime) { EnsureReady(); OnLateUpdate(deltaTime); }
    public void Focus(bool focused) { EnsureReady(); OnFocus(focused); }
    public void Pause(bool paused) { EnsureReady(); OnPause(paused); }
    public void Disable() { EnsureReady(); OnDisable(); }
    public void OnDomainStopping() { EnsureReady(); OnStopping(); }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        List<Exception>? failures = null;
        try { Lifetime.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        try { OnDispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        _domain = null;
        IsInitialized = false;
        if (failures is { Count: 1 }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException("Service cleanup failed.", failures);
    }
    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!IsInitialized) throw new InvalidOperationException("Initialize the service before dispatching callbacks.");
    }
    protected virtual void OnInitialize() { }
    protected virtual void OnUpdate(Fix64 deltaTime) { }
    protected virtual void OnFixedUpdate(Fix64 deltaTime) { }
    protected virtual void OnLateUpdate(Fix64 deltaTime) { }
    protected virtual void OnFocus(bool focused) { }
    protected virtual void OnPause(bool paused) { }
    protected virtual void OnDisable() { }
    protected virtual void OnStopping() { }
    protected virtual void OnDispose() { }
}
