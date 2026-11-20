using Microsoft.Extensions.DependencyInjection;

namespace BEngine.App;

public enum GameDomainStatus { Created, Starting, Running, Stopping, Stopped, Faulted }

/// <summary>A single-threaded game workflow and an extensible collection of service instances.
/// AOT and hot-update domains use the same contract. No particular service is required or installed implicitly.</summary>
public abstract class GameDomain : IDisposable
{
    private ServiceProvider? _rootProvider;
    private IServiceScope? _scope;
    private IServiceProvider? _hostServices;
    private bool _startupCompleted;
    private Action<GameDomain>? _stopped;
    private int _dispatchDepth;
    private bool _stopRequested;
    private List<Exception>? _stopFailures;

    protected GameDomain() => Services = new GameServiceCollection(this);
    public static GameDomain? Current => Launcher.Current?.ActiveDomain;
    public GameDomainStatus Status { get; private set; }
    public bool IsRunning => Status == GameDomainStatus.Running && !_stopRequested;
    public LifetimeScope Lifetime { get; } = new();
    public GameServiceCollection Services { get; }
    public IServiceProvider HostServices => _hostServices ?? EmptyHostServices.Instance;

    /// <summary>Optional ordinary DI registrations. Domain services are explicitly registered in Configure.</summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }
    protected virtual void Configure() { }
    protected abstract void Startup();
    protected virtual void OnQuit() { }
    protected virtual void OnUpdate(Fix64 deltaTime) { }
    protected virtual void OnFixedUpdate(Fix64 deltaTime) { }
    protected virtual void OnLateUpdate(Fix64 deltaTime) { }
    protected virtual void OnApplicationFocus(bool focused) { }
    protected virtual void OnApplicationPause(bool paused) { }
    protected virtual void OnDisable() { }

    public void Start(IServiceProvider? hostServices = null)
    {
        if (Status != GameDomainStatus.Created) throw new InvalidOperationException("A GameDomain instance can only be started once.");
        Status = GameDomainStatus.Starting;
        _hostServices = hostServices;
        try
        {
            EnsureStarting();
            var services = new ServiceCollection();
            services.AddSingleton(this);
            services.AddSingleton(Lifetime);
            services.AddSingleton(Services);
            ConfigureServices(services);
            EnsureStarting();
            _rootProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _rootProvider.CreateScope();
            Services.SetFallback(_scope.ServiceProvider);
            Configure();
            EnsureStarting();
            Services.Initialize();
            EnsureStarting();
            Startup();
            EnsureStarting();
            _startupCompleted = true;
            Status = GameDomainStatus.Running;
        }
        catch (Exception error)
        {
            List<Exception>? failures = [error];
            CollectStopFailures(ref failures);
            Cleanup(ref failures);
            Status = _stopRequested && failures is { Count: 1 } && error is OperationCanceledException or BOperationCanceledException
                ? GameDomainStatus.Stopped : GameDomainStatus.Faulted;
            NotifyStopped(ref failures);
            ThrowFailures(failures);
            throw;
        }
    }
    public T Track<T>(T resource) where T : IDisposable => Lifetime.Track(resource);
    public void Tick(Fix64 deltaTime) => Dispatch(deltaTime, 0);
    public void FixedTick(Fix64 deltaTime) => Dispatch(deltaTime, 1);
    public void LateTick(Fix64 deltaTime) => Dispatch(deltaTime, 2);
    private void Dispatch(Fix64 deltaTime, int phase)
    {
        if (deltaTime < Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (IsRunning && Lifetime.IsDisposed) Dispose();
        if (!IsRunning) return;
        List<Exception>? failures = null;
        _dispatchDepth++;
        try
        {
            Services.Dispatch(deltaTime, phase);
            if (IsRunning)
            {
                if (phase == 0) OnUpdate(deltaTime);
                else if (phase == 1) OnFixedUpdate(deltaTime);
                else OnLateUpdate(deltaTime);
            }
        }
        catch (Exception error) { failures = [error]; }
        finally { EndDispatch(ref failures); }
        ThrowFailures(failures);
    }
    public void NotifyFocus(bool focused) => Notify(0, focused);
    public void NotifyPause(bool paused) => Notify(1, paused);
    public void NotifyDisable() => Notify(2, false);
    private void Notify(int kind, bool value)
    {
        if (!IsRunning) return;
        List<Exception>? failures = null;
        _dispatchDepth++;
        try
        {
            Services.Notify(kind, value);
            if (IsRunning)
            {
                if (kind == 0) OnApplicationFocus(value);
                else if (kind == 1) OnApplicationPause(value);
                else OnDisable();
            }
        }
        catch (Exception error) { failures = [error]; }
        finally { EndDispatch(ref failures); }
        ThrowFailures(failures);
    }
    public void Quit() => Dispose();
    public void Dispose()
    {
        if (Status is GameDomainStatus.Stopped or GameDomainStatus.Stopping or GameDomainStatus.Faulted || _stopRequested) return;
        _stopRequested = true;
        Try(Lifetime.Dispose, ref _stopFailures);
        if (Status == GameDomainStatus.Starting || _dispatchDepth > 0) return;
        List<Exception>? failures = null;
        CompleteStop(ref failures);
        ThrowFailures(failures);
    }
    private void EndDispatch(ref List<Exception>? failures)
    {
        _dispatchDepth--;
        if (_dispatchDepth == 0 && _stopRequested) CompleteStop(ref failures);
    }
    private void CompleteStop(ref List<Exception>? failures)
    {
        CollectStopFailures(ref failures);
        Cleanup(ref failures);
        Status = failures is null ? GameDomainStatus.Stopped : GameDomainStatus.Faulted;
        NotifyStopped(ref failures);
    }
    private void CollectStopFailures(ref List<Exception>? failures)
    {
        if (_stopFailures is null) return;
        if (failures is null) failures = _stopFailures;
        else failures.AddRange(_stopFailures);
        _stopFailures = null;
    }
    internal void SetStopCallback(Action<GameDomain> callback) => _stopped = callback;
    private void NotifyStopped(ref List<Exception>? failures)
    {
        var callback = _stopped; _stopped = null;
        if (callback is not null) Try(() => callback(this), ref failures);
    }
    private void Cleanup(ref List<Exception>? failures)
    {
        Status = GameDomainStatus.Stopping;
        Try(Lifetime.Dispose, ref failures);
        if (_scope is not null) Try(OnQuit, ref failures);
        Services.Cleanup(_startupCompleted, ref failures);
        if (_scope is not null) Try(_scope.Dispose, ref failures);
        if (_rootProvider is not null) Try(_rootProvider.Dispose, ref failures);
        _scope = null; _rootProvider = null; _hostServices = null;
    }
    private void EnsureStarting()
    {
        if (Status != GameDomainStatus.Starting || _stopRequested || Lifetime.IsDisposed)
            throw new OperationCanceledException("GameDomain startup was stopped.");
    }
    private static void Try(Action callback, ref List<Exception>? failures)
    {
        try { callback(); } catch (Exception error) { (failures ??= []).Add(error); }
    }
    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures is { Count: 1 }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException("GameDomain lifecycle failed.", failures);
    }
    private sealed class EmptyHostServices : IServiceProvider
    {
        internal static readonly EmptyHostServices Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
