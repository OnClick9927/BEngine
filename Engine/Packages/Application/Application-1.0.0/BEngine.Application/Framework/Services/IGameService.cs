namespace BEngine.App;

/// <summary>An instance-owned, single-threaded GameDomain service. Registration order defines
/// initialization and frame order; shutdown and disposal run in reverse order.</summary>
public interface IGameService : IDisposable
{
    void Initialize(GameDomain domain) { }
    void Update(Fix64 deltaTime) { }
    void FixedUpdate(Fix64 deltaTime) { }
    void LateUpdate(Fix64 deltaTime) { }
    void Focus(bool focused) { }
    void Pause(bool paused) { }
    void Disable() { }
    /// <summary>Called after a successfully started domain requests shutdown, before services are disposed.</summary>
    void OnDomainStopping() { }
}
