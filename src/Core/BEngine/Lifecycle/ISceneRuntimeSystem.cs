namespace BEngine;

public interface ISceneRuntimeSystem
{
    string packageId => string.Empty;
    int order => 0;
    void Start(Scene scene) { }
    /// <summary>Runs once per frame before behaviour FixedUpdate and Update, after Start.</summary>
    void BeforeUpdate(Scene scene, Fix64 deltaTime) { }
    void FixedUpdate(Scene scene, Fix64 fixedDeltaTime) { }
    void Update(Scene scene, Fix64 deltaTime) { }
    void Stop(Scene scene) { }
}
