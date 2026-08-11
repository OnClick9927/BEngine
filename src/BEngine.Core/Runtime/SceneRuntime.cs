namespace BEngine;

public sealed class SceneRuntime
{
    private readonly HashSet<MonoBehaviour> _awakened = [];
    private readonly HashSet<MonoBehaviour> _started = [];
    private ISceneRuntimeSystem[] _systems = [];
    private Fix64 _fixedAccumulator;

    public Scene Scene { get; }
    public bool IsRunning { get; private set; }

    public SceneRuntime(Scene scene) => Scene = scene ?? throw new ArgumentNullException(nameof(scene));

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        Time.Reset();
        _fixedAccumulator = Fix64.Zero;
        _systems = RuntimeSystemRegistry.CreateSystems();
        IsRunning = true;
        foreach (var system in _systems) system.Start(Scene);
        InvokeNewBehaviours();
    }

    public void Tick(Fix64 deltaTime)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The scene runtime has not been started.");
        }

        Time.deltaTime = Fix64.Clamp(deltaTime, Fix64.Zero, Fix64.Parse("0.25"));
        Time.time += Time.deltaTime;
        Time.frameCount++;
        _fixedAccumulator += Time.deltaTime;

        InvokeNewBehaviours();
        while (_fixedAccumulator >= Time.fixedDeltaTime)
        {
            foreach (var behaviour in ActiveBehaviours())
            {
                behaviour.FixedUpdate();
            }

            foreach (var system in _systems) system.FixedUpdate(Scene, Time.fixedDeltaTime);

            _fixedAccumulator -= Time.fixedDeltaTime;
        }

        foreach (var behaviour in ActiveBehaviours())
        {
            behaviour.Update();
        }

        foreach (var system in _systems) system.Update(Scene, Time.deltaTime);

        foreach (var behaviour in ActiveBehaviours())
        {
            behaviour.LateUpdate();
        }

        Input.BeginFrame();
    }

    public void Stop()
    {
        if (IsRunning)
        {
            foreach (var system in _systems.Reverse()) system.Stop(Scene);
        }
        IsRunning = false;
        _awakened.Clear();
        _started.Clear();
        _fixedAccumulator = Fix64.Zero;
        _systems = [];
    }

    private void InvokeNewBehaviours()
    {
        foreach (var behaviour in ActiveBehaviours())
        {
            if (_awakened.Add(behaviour))
            {
                behaviour.Awake();
            }

            if (_started.Add(behaviour))
            {
                behaviour.Start();
            }
        }
    }

    private MonoBehaviour[] ActiveBehaviours() => Scene.gameObjects
        .Where(item => item.activeInHierarchy)
        .SelectMany(item => item.GetComponents<MonoBehaviour>())
        .Where(item => item.enabled)
        .ToArray();
}
