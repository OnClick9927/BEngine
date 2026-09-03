using BEngine.DependencyInjection;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

internal sealed class PlayerApplicationStage(
    ServiceProvider services,
    ISceneRuntimeFactory sceneRuntimeFactory,
    IRuntimeSceneManager sceneManager,
    PlayerHotUpdateSession code,
    string scenePath,
    bool isAot) : IDisposable
{
    private int _disposed;

    internal ServiceProvider Services { get; } = services;
    internal ISceneRuntimeFactory SceneRuntimeFactory { get; } = sceneRuntimeFactory;
    internal IRuntimeSceneManager SceneManager { get; } = sceneManager;
    internal PlayerHotUpdateSession Code { get; } = code;
    internal string ScenePath { get; } = scenePath;
    internal bool IsAot { get; } = isAot;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var scene in SceneManager.LoadedScenes.ToArray())
            SceneManager.UnregisterScene(scene, disposeScene: true);
        Code.Dispose();
        Services.Dispose();
    }
}
