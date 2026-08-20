namespace BEngine;

public interface ISceneRuntimeSystem
{
    string packageId => string.Empty;
    int order => 0;
    void Start(Scene scene) { }
    void FixedUpdate(Scene scene, Fix64 fixedDeltaTime) { }
    void Update(Scene scene, Fix64 deltaTime) { }
    void Stop(Scene scene) { }
}
