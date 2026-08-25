namespace BEngine.ExampleTests.SceneRuntimeArchitecture;

internal sealed class SceneProbeSystem : ISceneRuntimeSystem
{
    public List<string> Calls { get; } = [];
    public bool AllCallbacksUsedSceneContext { get; private set; } = true;
    public Action<Scene>? StartProbe { get; set; }

    public void Start(Scene scene)
    {
        Observe(scene);
        StartProbe?.Invoke(scene);
        Calls.Add("Start");
    }

    public void FixedUpdate(Scene scene, Fix64 fixedDeltaTime)
    {
        Observe(scene);
        Calls.Add("FixedUpdate");
    }

    public void Update(Scene scene, Fix64 deltaTime)
    {
        Observe(scene);
        Calls.Add("Update");
    }

    public void Stop(Scene scene)
    {
        Observe(scene);
        Calls.Add("Stop");
    }

    private void Observe(Scene scene) =>
        AllCallbacksUsedSceneContext &= ReferenceEquals(SceneRuntime.currentScene, scene);
}
