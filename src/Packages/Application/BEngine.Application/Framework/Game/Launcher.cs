using System.Runtime.CompilerServices;
using BEngine.SceneManagement;

namespace BEngine.App;

/// <summary>Drives one GameDomain per scene-manager domain. It uses Core's scene persistence and frame callbacks;
/// it never controls the early splash, platform, renderer or assembly loader.</summary>
[AddComponentMenu("Application/Launcher")]
public sealed class Launcher : MonoBehaviour
{
    private static readonly ConditionalWeakTable<object, Session> Sessions = new();
    private object? _domain;
    private GameDomain? _game;
    private GameDomain? _pending;
    private bool _switching;
    private bool _destroying;
    private int _replacementVersion;

    public GameDomain? RunningDomain => _game is { IsRunning: true } game ? game : null;
    public GameDomain? ActiveDomain => _game is { Status: GameDomainStatus.Starting or GameDomainStatus.Running } game ? game : null;
    public static Launcher? Current
    {
        get
        {
            if (SceneRuntime.currentScene is not { } scene) return null;
            var domain = GetDomain(scene);
            return Sessions.TryGetValue(domain, out var session) && session.Owner.TryGetTarget(out var owner)
                ? owner : null;
        }
    }

    /// <summary>Call from the AOT or hot-update stage scene's Awake, once that stage's assemblies are ready.
    /// Replacing a domain requires an explicit opt-in. The scene manager owns the Launcher object.</summary>
    public static Launcher Run(GameDomain game, bool replaceExisting = false)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Status != GameDomainStatus.Created) throw new InvalidOperationException("Launch a fresh GameDomain instance.");
        var scene = SceneRuntime.currentScene ?? throw new InvalidOperationException("Launcher.Run requires an active scene runtime callback.");
        var domain = GetDomain(scene);
        RuntimeLifecycle.sceneStopping -= OnSceneStopping;
        RuntimeLifecycle.sceneStopping += OnSceneStopping;
        Launcher owner;
        if (Sessions.TryGetValue(domain, out var session) && session.Owner.TryGetTarget(out var existing))
        {
            owner = existing;
            if (owner._game is not null && !replaceExisting)
                throw new InvalidOperationException("This runtime already has a domain. Explicitly request replacement or reuse Launcher.Current.");
        }
        else
        {
            owner = scene.CreateGameObject("Application Launcher").AddComponent<Launcher>();
            owner._domain = domain;
            Sessions.Remove(domain);
            Sessions.Add(domain, new Session(owner));
            BObject.DontDestroyOnLoad(owner.gameObject);
        }
        owner.Replace(game);
        return owner;
    }

    /// <summary>Stops the old domain completely before starting the replacement. A request from
    /// an active callback is completed when that callback has safely returned.</summary>
    public void Replace(GameDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (_destroying) throw new ObjectDisposedException(nameof(Launcher));
        if (domain.Status != GameDomainStatus.Created) throw new InvalidOperationException("Replace with a fresh GameDomain.");
        if (_pending is not null || _switching) throw new InvalidOperationException("A domain replacement is already pending.");
        _pending = domain;
        _replacementVersion++;
        _switching = true;
        try { _game?.Quit(); }
        catch (Exception stopError)
        {
            _pending = null;
            try { domain.Dispose(); }
            catch (Exception discardError) { throw new AggregateException("Domain replacement failed.", stopError, discardError); }
            throw;
        }
        finally { _switching = false; }
        StartPending();
    }

    public void StopDomain()
    {
        var pending = _pending; _pending = null;
        List<Exception>? failures = null;
        try { pending?.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        try { _game?.Quit(); } catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is { Count: 1 }) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException("Domain shutdown failed.", failures);
    }
    private void StartPending()
    {
        if (_game is not null || _pending is null || _switching || _destroying) return;
        var next = _pending; _pending = null;
        var version = _replacementVersion;
        _game = next;
        next.SetStopCallback(OnGameStopped);
        try { next.Start(new SceneServices(this)); }
        catch (Exception error) when (error is OperationCanceledException or BOperationCanceledException &&
            next.Status == GameDomainStatus.Stopped && _replacementVersion != version)
        {
            // An explicit replacement requested inside Startup completed after the callback returned.
        }
        catch
        {
            if (ReferenceEquals(_game, next)) _game = null;
            throw;
        }
    }

    private void OnGameStopped(GameDomain game)
    {
        if (ReferenceEquals(_game, game)) _game = null;
        if (game.Status == GameDomainStatus.Faulted)
        {
            var discarded = _pending; _pending = null;
            discarded?.Dispose();
            return;
        }
        StartPending();
    }

    public override void Update() => _game?.Tick(Time.deltaTime);
    public override void FixedUpdate() => _game?.FixedTick(Time.fixedDeltaTime);
    public override void LateUpdate() => _game?.LateTick(Time.deltaTime);
    public override void OnApplicationFocus(bool hasFocus) => _game?.NotifyFocus(hasFocus);
    public override void OnApplicationPause(bool pauseStatus) => _game?.NotifyPause(pauseStatus);
    public override void OnDisable() => _game?.NotifyDisable();
    public override void OnApplicationQuit() => StopDomain();
    public override void OnDestroy()
    {
        if (_domain is { } domain && Sessions.TryGetValue(domain, out var session) &&
            session.Owner.TryGetTarget(out var owner) && ReferenceEquals(owner, this)) Sessions.Remove(domain);
        _domain = null;
        _destroying = true;
        StopDomain();
    }

    private static object GetDomain(Scene scene) => (object?)scene.GetService<IRuntimeSceneManager>() ?? scene;
    private static void OnSceneStopping(Scene scene)
    {
        // Persistent objects have already moved to the next scene before the old one stops.
        // Stopping Play Mode or the final owning scene must release the game even if the
        // host retains that scene for inspection instead of destroying it immediately.
        if (Sessions.TryGetValue(GetDomain(scene), out var session) && session.Owner.TryGetTarget(out var owner) &&
            ReferenceEquals(owner.gameObject.scene, scene)) owner.StopDomain();
    }
    private sealed class Session(Launcher owner)
    {
        internal readonly WeakReference<Launcher> Owner = new(owner);
    }
    private sealed class SceneServices(Launcher owner) : IServiceProvider
    {
        // Resolve against the object's current scene after DontDestroyOnLoad transfers it.
        public object? GetService(Type serviceType) => owner.gameObject.scene?.GetService(serviceType);
    }
}
