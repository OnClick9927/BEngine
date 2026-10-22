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
    private BTask? _disposeOperation;

    internal ServiceProvider Services { get; } = services;
    internal ISceneRuntimeFactory SceneRuntimeFactory { get; } = sceneRuntimeFactory;
    internal IRuntimeSceneManager SceneManager { get; } = sceneManager;
    internal PlayerHotUpdateSession Code { get; } = code;
    internal string ScenePath { get; } = scenePath;
    internal bool IsAot { get; } = isAot;

    internal BValueTask DisposeAsync()
    {
        _disposeOperation ??= DisposeCoreAsync();
        return new BValueTask(_disposeOperation);
    }

    public void Dispose()
    {
        var operation = DisposeAsync().AsBTask();
        if (operation.IsCompleted)
        {
            PlayerMainThread.Complete(operation);
            return;
        }
        _ = ObserveDisposalAsync(operation);
    }

    private async BTask DisposeCoreAsync()
    {
        List<Exception>? failures = null;
        foreach (var scene in SceneManager.LoadedScenes.ToArray())
        {
            try { SceneManager.UnregisterScene(scene, disposeScene: true); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { await Code.DisposeAsync(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { Services.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 })
            throw new AggregateException("Player application stage shutdown failed.", failures);
    }

    private static async BValueTask ObserveDisposalAsync(BTask operation)
    {
        try { await operation; }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("PLAYER_STAGE_STOP", exception);
        }
    }
}
