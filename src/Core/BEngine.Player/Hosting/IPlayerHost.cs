using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
namespace BEngine.Player;
public interface IPlayerHost
{
    int Run(IServiceCollection services, string[] arguments);
    string ResolveDefaultProjectPath(IServiceCollection services, string? playerDirectory);
    IPlayerRuntimeSession CreateRuntimeSession(IServiceCollection services, string projectPath,
        bool declareAotInterpreter, string? persistentDataPath);
    void Validate(IServiceCollection services, string projectPath, bool declareAotInterpreter, string? persistentDataPath);
}
public interface IPlayerRuntimeSession : IDisposable
{
    RuntimeWorkspace Workspace { get; }
    IServiceProvider Services { get; }
    IReadOnlyList<Scene> LoadedScenes { get; }
    Scene? ActiveScene { get; }
    bool IsStarted { get; }
    bool CanRender { get; }
    IPlayerStartupControl? StartupControl { get; }
    bool IsStartupPrepared { get; }
    bool IsDisposalPending { get; }
    SceneRenderStatistics LastRenderStatistics { get; }
    void AttachGraphicsDevice(IGraphicsPresentationDevice device, bool ownsDevice = true);
    void DetachGraphicsDevice();
    bool Render(int width, int height, bool drawUi = true);
    void Start();
    void Tick(TimeSpan elapsed);
    void SetFocused(bool focused);
    void SetPaused(bool paused);
    void RaiseLowMemory();
    void Stop();
}
public interface IPlayerContentKey { string Fingerprint { get; } }
