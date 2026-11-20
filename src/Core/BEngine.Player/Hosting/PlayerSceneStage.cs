using BEngine.DependencyInjection;
using BEngine.SceneManagement;

namespace BEngine.Player;

/// <summary>Owns a prepared scene set, its content lease and an explicitly transferred service owner.</summary>
public sealed class PlayerSceneStage : IDisposable
{
    private readonly Func<BValueTask>? _releaseContent;
    private readonly IDisposable? _serviceOwner;
    private readonly PlayerRuntimeDiagnostics _diagnostics;
    private BTask? _disposal;

    public PlayerSceneStage(IServiceProvider services, ISceneRuntimeFactory sceneRuntimeFactory,
        IRuntimeSceneManager sceneManager, string scenePath, Func<BValueTask>? releaseContent = null,
        IDisposable? serviceOwner = null, PlayerRuntimeDiagnostics? diagnostics = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        SceneRuntimeFactory = sceneRuntimeFactory ?? throw new ArgumentNullException(nameof(sceneRuntimeFactory));
        SceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        ArgumentException.ThrowIfNullOrWhiteSpace(scenePath);
        ScenePath = scenePath;
        _releaseContent = releaseContent;
        _serviceOwner = serviceOwner;
        _diagnostics = diagnostics ?? PlayerRuntimeDiagnostics.Default;
    }

    public IServiceProvider Services { get; }
    public ISceneRuntimeFactory SceneRuntimeFactory { get; }
    public IRuntimeSceneManager SceneManager { get; }
    public string ScenePath { get; }

    public BValueTask DisposeAsync()
    {
        if (_disposal is not null) return new(_disposal);
        // Publish the operation before invoking user teardown, including synchronous callbacks.
        var completion = new BTaskCompletionSource();
        _disposal = completion.Task;
        _ = DisposeCoreAsync(completion);
        return new(_disposal);
    }

    public void Dispose()
    {
        var operation = DisposeAsync().AsBTask();
        if (operation.IsCompleted) PlayerExecution.Complete(operation);
        else _ = ObserveDisposalAsync(operation);
    }

    private async BTask DisposeCoreAsync(BTaskCompletionSource completion)
    {
        try
        {
            List<Exception>? failures = null;
            Scene[] scenes = [];
            try { scenes = SceneManager.LoadedScenes.ToArray(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            foreach (var scene in scenes)
            {
                try { SceneManager.UnregisterScene(scene, disposeScene: true); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            try { if (_releaseContent is not null) await _releaseContent(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            try { _serviceOwner?.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            if (failures is { Count: > 0 })
                throw new AggregateException("Player scene stage shutdown failed.", failures);
            completion.SetResult();
        }
        catch (Exception exception) { completion.SetException(exception); }
    }

    private async BValueTask ObserveDisposalAsync(BTask operation)
    {
        try { await operation; }
        catch (Exception exception) { _diagnostics.Failure("PLAYER_STAGE_STOP", exception); }
    }
}
