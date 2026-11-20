using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Player.ProjectSystem;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>Adapts deployment content to the engine-owned runtime session.</summary>
internal static class AssetBundlePlayerContent
{
    internal static PlayerRuntimeDiagnostics Diagnostics { get; } = new(
        (phase, detail) => PlayerStartupDiagnostics.Phase(phase switch
        {
            "INITIAL_STAGE_STARTED" => "03_AOT_SCENE_STARTED",
            "SCENE_LOADED" => "06_SCENE_LOADED",
            "RUNTIME_STAGE_STARTED" => "07_GAME_STARTED",
            _ => phase
        }, detail),
        (phase, failure) => PlayerStartupDiagnostics.Failure(phase switch
        {
            "STAGE_TRANSITION" => "HOTUPDATE_STAGE",
            "PREVIOUS_STAGE_RELEASE" => "AOT_STAGE_RELEASE",
            "STAGE_TRANSITION_STOP" => "GAME_TRANSITION_STOP",
            _ => phase
        }, failure));

    internal static async BValueTask<IPlayerRuntimeContent> PrepareAsync(
        RuntimeWorkspace workspace, string? persistentDataPath,
        IReadOnlyList<ServiceDescriptor> hostServices, IServiceProvider startupServices,
        BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (workspace.GetRuntimeMetadata()?.PlayerBootstrap is not null)
            return await PlayerStagedRuntimeCoordinator.CreateAsync(
                workspace.RootPath, persistentDataPath,
                startupServices.GetRequiredService<BEngine.AssetBundles.IAssetBundleManagerFactory>(),
                hostServices, startupServices.GetRequiredService<FileUtility>(),
                startupServices.GetRequiredService<PathUtility>(), startupServices.GetRequiredService<PlatformUtility>(),
                startupServices.GetRequiredService<ApplicationPathUtility>(),
                startupServices.GetRequiredService<IManagedCodeRuntimeFactory>(), cancellationToken, workspace);

        var services = new ServiceCollection();
        foreach (var descriptor in hostServices) ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        var provider = services.AddBEnginePlayer(workspace.RootPath, persistentDataPath)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        PlayerAssetBundleBootstrap? bootstrap = null;
        PlayerSceneStage? stage = null;
        try
        {
            using var attachment = provider.AttachBEngineRuntimeServices();
            bootstrap = provider.GetService<PlayerAssetBundleBootstrap>();
            var code = provider.GetService<ManagedCodePlayerSession>();
            var sceneManager = provider.GetRequiredService<IRuntimeSceneManager>();
            stage = new PlayerSceneStage(provider, provider.GetRequiredService<ISceneRuntimeFactory>(),
                sceneManager, workspace.StartupScenePath, code is null ? null : code.DisposeAsync,
                provider, Diagnostics);
            if (bootstrap is not null) await bootstrap.InitializeAsync(cancellationToken);
            if (code is not null) await code.ActivateAsync(provider, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            sceneManager.LoadScene(workspace.StartupScenePath, LoadSceneMode.Single);
            PlayerStartupDiagnostics.Phase("06_SCENE_LOADED", $"scene={workspace.StartupScenePath};mode=workspace");
            return new WorkspaceContent(stage, bootstrap);
        }
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            try { if (bootstrap is not null) await bootstrap.RollbackAfterStartupFailureAsync(failure); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            try
            {
                if (stage is not null) await stage.DisposeAsync();
                else provider.Dispose();
            }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (cleanupFailures is not null)
                throw new AggregateException("Player content preparation and cleanup failed.", [failure, .. cleanupFailures]);
            throw;
        }
    }

    private sealed class WorkspaceContent(PlayerSceneStage stage, PlayerAssetBundleBootstrap? bootstrap) : IPlayerRuntimeContent
    {
        public PlayerSceneStage InitialStage => stage;
        public BValueTask CommitStartupAsync(BCancellationToken cancellationToken = default) =>
            bootstrap?.CommitStartupAsync(cancellationToken) ?? BValueTask.CompletedTask;
        public BValueTask RollbackStartupAsync(Exception failure, BCancellationToken cancellationToken = default) =>
            bootstrap?.RollbackAfterStartupFailureAsync(failure, cancellationToken) ?? BValueTask.CompletedTask;
        public BValueTask DisposeAsync() => stage.DisposeAsync();
    }
}
