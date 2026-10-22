namespace BEngine;

/// <summary>Runs optional coroutine work after behaviour Update, before other scene systems.</summary>
public sealed class CoroutineRuntimeSystem : ISceneRuntimeSystem
{
    public string packageId => "com.bengine.coroutines";
    public int order => int.MinValue;
    public void Update(Scene scene, Fix64 deltaTime) => CoroutineScheduler.Tick(scene);
    public void Stop(Scene scene) => CoroutineScheduler.StopScene(scene);
}
