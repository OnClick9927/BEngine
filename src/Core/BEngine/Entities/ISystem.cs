namespace BEngine.Entities;

public interface ISystem
{
    int order => 0;
    bool enabled => true;
    void OnCreate(ref SystemState state) { }
    void OnStartRunning(ref SystemState state) { }
    void OnFixedUpdate(ref SystemState state) { }
    void OnUpdate(ref SystemState state) { }
    void OnStopRunning(ref SystemState state) { }
    void OnDestroy(ref SystemState state) { }
}
