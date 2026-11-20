using BEngine.DependencyInjection;
using BEngine.Hosting;
using BEngine.SceneManagement;

namespace BEngine.Player;

/// <summary>Drives the scenes of one Player stage independently of how they were loaded.</summary>
public sealed class PlayerSceneRuntimeGroup
{
    private readonly List<SceneRuntime> _runtimes = [];
    private ISceneRuntimeFactory? _factory;
    private bool _subscribed;

    public IRuntimeSceneManager? Manager { get; private set; }
    public IReadOnlyList<Scene> LoadedScenes => Manager?.LoadedScenes ?? [];
    public Scene? ActiveScene => Manager?.ActiveScene;
    public Scene? PresentationScene { get; private set; }
    public bool IsRunning { get; private set; }

    public void Bind(IRuntimeSceneManager manager, ISceneRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(factory);
        Clear();
        Manager = manager;
        _factory = factory;
        Attach();
    }

    public void TrackLoadedScenes()
    {
        if (Manager is not { } manager) return;
        foreach (var scene in manager.LoadedScenes.ToArray())
            OnSceneLoaded(scene, LoadSceneMode.Single);
    }

    public void Start()
    {
        if (IsRunning) return;
        if (Manager is null) throw new InvalidOperationException("Bind a scene manager before starting the Player scenes.");
        IsRunning = true;
        try
        {
            foreach (var runtime in _runtimes.ToArray())
            {
                if (!IsRunning) break;
                // A preceding scene's startup can unload another scene in the snapshot.
                if (runtime.Scene.isCreated) runtime.Start();
            }
        }
        catch
        {
            Stop(static exception => Debug.LogError($"Failed to stop Player scenes after startup failed: {exception}"));
            throw;
        }
    }

    public bool Tick(TimeSpan elapsed) =>
        IsRunning && HostRuntimeFrame.TickScenes(_runtimes, elapsed);

    public void Stop(Action<Exception>? reportFailure = null)
    {
        IsRunning = false;
        // Stop callbacks can load or unload scenes. Iterate the original group.
        var snapshot = _runtimes.ToArray();
        for (var index = snapshot.Length - 1; index >= 0; index--)
        {
            try { snapshot[index].Stop(); }
            catch (Exception exception) when (reportFailure is not null) { reportFailure(exception); }
        }
    }

    public void Detach()
    {
        if (!_subscribed || Manager is not { } manager) return;
        manager.SceneLoaded -= OnSceneLoaded;
        manager.SceneUnloaded -= OnSceneUnloaded;
        manager.ActiveSceneChanged -= OnActiveSceneChanged;
        _subscribed = false;
    }

    public void Clear()
    {
        if (IsRunning) throw new InvalidOperationException("Stop the Player scenes before replacing them.");
        Detach();
        _runtimes.Clear();
        Manager = null;
        _factory = null;
        PresentationScene = null;
    }

    /// <summary>Captures the current stage's scene runtimes for a possible rollback after switching stages.</summary>
    public Snapshot Capture() => new(
        Manager ?? throw new InvalidOperationException("The Player scene manager is unavailable."),
        _factory!,
        _runtimes.ToArray(),
        PresentationScene,
        IsRunning);

    public void Restore(Snapshot snapshot, bool subscribe)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Clear();
        Manager = snapshot.Manager;
        _factory = snapshot.Factory;
        _runtimes.AddRange(snapshot.Runtimes);
        PresentationScene = snapshot.PresentationScene;
        if (subscribe) Attach();
    }

    private void Attach()
    {
        if (_subscribed || Manager is not { } manager) return;
        manager.SceneLoaded += OnSceneLoaded;
        manager.SceneUnloaded += OnSceneUnloaded;
        manager.ActiveSceneChanged += OnActiveSceneChanged;
        _subscribed = true;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        PresentationScene = scene;
        foreach (var runtime in _runtimes)
            if (ReferenceEquals(runtime.Scene, scene)) return;

        var next = _factory!.Create(scene);
        _runtimes.Add(next);
        if (IsRunning) next.Start();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        for (var index = _runtimes.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(_runtimes[index].Scene, scene)) continue;
            _runtimes.RemoveAt(index);
            break;
        }
    }

    private void OnActiveSceneChanged(Scene? _, Scene? active)
    {
        if (active is not null) PresentationScene = active;
    }

    public sealed class Snapshot
    {
        internal Snapshot(
            IRuntimeSceneManager manager,
            ISceneRuntimeFactory factory,
            SceneRuntime[] runtimes,
            Scene? presentationScene,
            bool wasRunning)
        {
            Manager = manager;
            Factory = factory;
            Runtimes = runtimes;
            PresentationScene = presentationScene;
            WasRunning = wasRunning;
        }

        internal IRuntimeSceneManager Manager { get; }
        internal ISceneRuntimeFactory Factory { get; }
        internal SceneRuntime[] Runtimes { get; }
        internal Scene? PresentationScene { get; }
        public bool WasRunning { get; }
    }
}
