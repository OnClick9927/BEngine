using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.DependencyInjection;
using BEngine.Documents;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.Startup;
using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

internal sealed class PlayerStagedRuntimeCoordinator : IDisposable
{
    private readonly PlayerBuiltInResourceProvider? _playerResources;
    private readonly PlayerBuiltInResourceProvider _aotResources;
    private readonly AssetBundleManager _updateAssetBundles;
    private readonly AssetBundleResourceProvider _updateResourceProvider;
    private readonly PlayerAssetBundleBootstrap _updateBootstrap;
    private PlayerApplicationStage? _aotStage;
    private PlayerApplicationStage? _gameStage;
    private int _disposed;

    private PlayerStagedRuntimeCoordinator(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        AssetBundleSettingsDocument assetBundleSettings,
        PlayerBuiltInResourceProvider? playerResources,
        PlayerBuiltInResourceProvider aotResources,
        AssetBundleManager updateAssetBundles,
        AssetBundleResourceProvider updateResourceProvider,
        PlayerAssetBundleBootstrap updateBootstrap,
        PlayerAotStartupFlow flow,
        PlayerApplicationStage aotStage)
    {
        Workspace = workspace;
        ProjectSettings = projectSettings;
        AssetBundleSettings = assetBundleSettings;
        _playerResources = playerResources;
        _aotResources = aotResources;
        _updateAssetBundles = updateAssetBundles;
        _updateResourceProvider = updateResourceProvider;
        _updateBootstrap = updateBootstrap;
        Flow = flow;
        _aotStage = aotStage;
    }

    internal ProjectWorkspace Workspace { get; }
    internal ProjectSettingsData ProjectSettings { get; }
    internal AssetBundleSettingsDocument AssetBundleSettings { get; }
    internal PlayerAotStartupFlow Flow { get; }
    internal PlayerApplicationStage AotStage => _aotStage ??
        throw new InvalidOperationException("The AOT Player stage is no longer available.");
    internal PlayerAssetBundleBootstrap UpdateBootstrap => _updateBootstrap;

    internal static PlayerStagedRuntimeCoordinator Create(
        string projectPath,
        string? persistentDataPath = null)
    {
        var workspace = ProjectWorkspace.OpenRuntime(Path.GetFullPath(projectPath));
        if (workspace.RuntimeMetadata?.PlayerBootstrap is not { } manifest)
            throw new InvalidDataException("Staged Player startup requires packaged runtime metadata.");
        var playerRoot = PlayerPersistentDataPaths.ResolvePlayerRoot(workspace.RootPath, manifest);
        PlayerBuiltInResourceProvider? playerResources = null;
        try
        {
            if (manifest.Version >= 4)
            {
                playerResources = new PlayerBuiltInResourceProvider(
                    manifest.ResolvePlayerResourceArchive(playerRoot));
                PlayerAssetEnvironment.Initialize(workspace, playerResources);
                BuildTargetManifestSerializer.SetCurrent(
                    playerResources.ReadBytes(manifest.BuildTargetResource));
            }
            else
            {
                PlayerAssetEnvironment.Initialize(workspace);
                BuildTargetManifestSerializer.SetCurrentPath(
                    manifest.ResolveBuildTargetManifest(playerRoot));
            }
            var projectSettings = ProjectRuntimeSettings.LoadAndApply(workspace);
        Application.companyName = projectSettings.CompanyName;
        Application.productName = projectSettings.ProductName;
        var settings = LoadAssetBundleSettings(workspace);
        ApplyRemoteOverride(settings);
        AssetDataValidation.ValidateAssetBundleSettings(settings);
        if (!settings.Enabled)
            throw new InvalidDataException(
                "Staged Player startup requires remote AssetBundles for HotUpdate game content.");

        var sourceOptions = PlayerServiceCollectionExtensions.CreateAssetBundleOptions(
            workspace, settings, persistentDataPath, usePersistentRootAsCache: true);
        PlayerServiceCollectionExtensions.RemoveLegacyStagedCache(
            workspace, settings, persistentDataPath, sourceOptions.CacheDirectory);
        var updateOptions = CreateRemoteOptions(sourceOptions);
        var aotArchivePath = Path.Combine(
            workspace.RootPath,
            PlayerPackagedResourceAddresses.ResourcesDirectoryName,
            BuiltInResourceArchive.FileName);
        var aotResources = new PlayerBuiltInResourceProvider(aotArchivePath);
        var updateManager = new AssetBundleManager(updateOptions);
        var updateProvider = new AssetBundleResourceProvider(updateManager);
        var updateBootstrap = new PlayerAssetBundleBootstrap(
            updateManager, updateProvider, settings, registerResourceProviderOnPrepare: false);
        PlayerAotStartupFlow? flow = null;
        PlayerApplicationStage? aotStage = null;
        try
        {
            aotResources.Activate();
            PlayerStartupDiagnostics.Phase("03_BUILTIN_CONTENT_READY",
                $"source=archive;entries={aotResources.Entries.Count};path={aotArchivePath}");
            var aotCode = PlayerPackageLoader.Load(
                              workspace, assetBundles: null, PlayerManagedCodeStage.Aot, aotResources) ??
                          throw new InvalidDataException(
                              "The built-in AOT resource archive does not contain an AOT managed-code release.");
            flow = new PlayerAotStartupFlow(
                async cancellationToken =>
                {
                    var plan = await updateBootstrap.CheckForStartupUpdateAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!plan.HasUpdates)
                        await updateBootstrap.CompleteWithoutUpdateAsync(cancellationToken)
                            .ConfigureAwait(false);
                    return plan;
                },
                (plan, progress, cancellationToken) =>
                    updateBootstrap.ApplyCheckedUpdateAsync(plan, progress, cancellationToken),
                cancellationToken => ValidateGameContentAsync(
                    updateManager, manifest, cancellationToken),
                settings.FailStartupWhenUpdateFails);
            aotStage = CreateStage(
                workspace,
                projectSettings,
                settings,
                updateManager,
                updateProvider,
                updateBootstrap,
                aotCode,
                workspace.Project.StartupScene,
                flow,
                isAot: true,
                builtInResources: aotResources);
            PlayerStartupDiagnostics.Phase("03_AOT_SCENE_PREPARED",
                $"scene={workspace.Project.StartupScene}");
            return new PlayerStagedRuntimeCoordinator(
                workspace,
                projectSettings,
                settings,
                playerResources,
                aotResources,
                updateManager,
                updateProvider,
                updateBootstrap,
                flow,
                aotStage);
        }
        catch
        {
            aotStage?.Dispose();
            flow?.Dispose();
            updateBootstrap.Dispose();
            updateManager.Dispose();
            aotResources.Dispose();
            throw;
        }
        }
        catch
        {
            playerResources?.Dispose();
            throw;
        }
    }

    internal PlayerApplicationStage CreateGameStage()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_gameStage is not null) return _gameStage;
        var manifest = Workspace.RuntimeMetadata?.PlayerBootstrap ??
                       throw new InvalidDataException("Player bootstrap metadata is missing.");
        var validation = ValidateGameContentAsync(
                _updateAssetBundles, manifest, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();
        if (!validation.IsValid)
            throw new InvalidDataException(validation.Error);
        if (string.IsNullOrWhiteSpace(manifest.HotUpdateStartupScene))
            throw new InvalidDataException("Player bootstrap metadata has no HotUpdate startup scene.");
        var hotUpdate = PlayerPackageLoader.Load(
                            Workspace, _updateAssetBundles, PlayerManagedCodeStage.HotUpdate) ??
                        throw new InvalidDataException(
                            "The active remote content release has no HotUpdate managed-code release.");
        _updateBootstrap.ActivateResourceProvider();
        try
        {
            _gameStage = CreateStage(
                Workspace,
                ProjectSettings,
                AssetBundleSettings,
                _updateAssetBundles,
                _updateResourceProvider,
                _updateBootstrap,
                hotUpdate,
                manifest.HotUpdateStartupScene,
                flow: null,
                isAot: false,
                builtInResources: null);
            return _gameStage;
        }
        catch
        {
            _updateBootstrap.DeactivateResourceProvider();
            hotUpdate.Dispose();
            throw;
        }
    }

    internal void CompleteTransition()
    {
        var stage = Interlocked.Exchange(ref _aotStage, null);
        try { stage?.Dispose(); }
        finally { _aotResources.Dispose(); }
    }

    internal void Validate(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var aotRuntimes = StartStage(AotStage);
        try
        {
            PlayerStartupDiagnostics.Phase("03_AOT_SCENE_STARTED", "mode=validation");
            Flow.Start();
            Flow.CheckForUpdates();
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                Flow.Pump();
                var state = Flow.Current;
                if (state.RequiresUpdateConfirmation)
                {
                    if (state.CanDeclineUpdate && Environment.GetEnvironmentVariable(
                            PlayerAotStartupFlow.AutoDeclineUpdateEnvironmentVariable) == "1")
                        Flow.DeclineUpdate();
                    else
                        Flow.ConfirmUpdate();
                    continue;
                }
                if (state.CanEnterGame) break;
                if (state.CanRetry)
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(state.Error) ? state.Status : state.Error);
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"The AOT update flow did not complete within {timeout}.");
                Thread.Sleep(10);
            }
            Flow.EnterGame();
            var game = CreateGameStage();
            StopStage(aotRuntimes);
            aotRuntimes.Clear();
            CompleteTransition();

            var gameRuntimes = StartStage(game);
            try
            {
                foreach (var runtime in gameRuntimes) runtime.Tick(Fix64.Zero);
                _updateBootstrap.CommitStartup();
                PlayerStartupDiagnostics.Phase("06_SCENE_LOADED",
                    $"scene={game.ScenePath};mode=validation");
                PlayerStartupDiagnostics.Phase("07_GAME_STARTED", "mode=validation");
            }
            catch (Exception exception)
            {
                _updateBootstrap.RollbackAfterStartupFailure(exception);
                throw;
            }
            finally { StopStage(gameRuntimes); }
        }
        finally { StopStage(aotRuntimes); }
    }

    private static List<SceneRuntime> StartStage(PlayerApplicationStage stage)
    {
        var runtimes = stage.SceneManager.LoadedScenes
            .Select(stage.SceneRuntimeFactory.Create)
            .ToList();
        try
        {
            foreach (var runtime in runtimes) runtime.Start();
            return runtimes;
        }
        catch
        {
            StopStage(runtimes);
            throw;
        }
    }

    private static void StopStage(IReadOnlyList<SceneRuntime> runtimes)
    {
        for (var index = runtimes.Count - 1; index >= 0; index--) runtimes[index].Stop();
    }

    private static async Task<(bool IsValid, string Error)> ValidateGameContentAsync(
        AssetBundleManager manager,
        BEngine.Build.PlayerBootstrapManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!manager.IsInitialized || manager.ActiveVersion is null || manager.ActiveCatalog is not { } catalog)
            return Invalid("The sandbox has no installed game content release.");
        if (string.IsNullOrWhiteSpace(manifest.HotUpdateStartupScene))
            return Invalid("The Player bootstrap has no HotUpdate startup scene.");
        if (catalog.Assets.Any(asset => asset.Address.Equals("Assets/Aot", StringComparison.OrdinalIgnoreCase) ||
                                        asset.Address.StartsWith("Assets/Aot/", StringComparison.OrdinalIgnoreCase)))
            return Invalid("The sandbox catalog contains AOT content that belongs in the main package.");

        var addresses = catalog.Assets.Select(static asset => asset.Address)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!addresses.Contains(ManagedCodeReleaseManifest.DefaultAddress))
            return Invalid("The sandbox game release has no HotUpdate managed-code manifest.");
        if (!addresses.Contains(manifest.HotUpdateStartupScene))
            return Invalid($"The sandbox game release has no startup scene '{manifest.HotUpdateStartupScene}'.");

        try
        {
            await using var releaseHandle = await manager.LoadBytesAsync(
                ManagedCodeReleaseManifest.DefaultAddress, cancellationToken).ConfigureAwait(false);
            var release = ManagedCodeReleaseManifestSerializer.Deserialize(releaseHandle.Value);
            if (release.Modules.Count == 0)
                return Invalid("The sandbox HotUpdate release contains no C# modules.");
            if (release.Modules.Any(module => module.Name.Equals("AOT", StringComparison.OrdinalIgnoreCase)))
                return Invalid("The sandbox HotUpdate release incorrectly contains the AOT assembly.");

            foreach (var module in release.Modules)
            {
                if (!addresses.Contains(module.AssemblyAddress))
                    return Invalid($"The sandbox is missing C# module '{module.Name}'.");
                await using var assembly = await manager.LoadBytesAsync(
                    module.AssemblyAddress, cancellationToken).ConfigureAwait(false);
                if (assembly.Value.Length == 0)
                    return Invalid($"The sandbox C# module '{module.Name}' is empty.");
                if (string.IsNullOrWhiteSpace(module.SymbolsAddress)) continue;
                if (!addresses.Contains(module.SymbolsAddress))
                    return Invalid($"The sandbox is missing symbols for C# module '{module.Name}'.");
                await using var symbols = await manager.LoadBytesAsync(
                    module.SymbolsAddress, cancellationToken).ConfigureAwait(false);
            }

            await using var scene = await manager.LoadBytesAsync(
                manifest.HotUpdateStartupScene, cancellationToken).ConfigureAwait(false);
            if (scene.Value.Length == 0)
                return Invalid("The sandbox HotUpdate startup scene is empty.");
            PlayerStartupDiagnostics.Phase("04_GAME_CONTENT_READY",
                $"version={manager.ActiveVersion.Version};scene={manifest.HotUpdateStartupScene};" +
                $"modules={release.Modules.Count}");
            return (true, string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Invalid($"The sandbox game release is invalid: {exception.Message}");
        }

        static (bool IsValid, string Error) Invalid(string error)
        {
            PlayerStartupDiagnostics.Phase("04_GAME_CONTENT_INVALID", $"reason={error}");
            return (false, error);
        }
    }

    private static PlayerApplicationStage CreateStage(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        AssetBundleSettingsDocument assetBundleSettings,
        AssetBundleManager assetBundles,
        AssetBundleResourceProvider resourceProvider,
        PlayerAssetBundleBootstrap bootstrap,
        PlayerHotUpdateSession code,
        string scenePath,
        PlayerAotStartupFlow? flow,
        bool isAot,
        PlayerBuiltInResourceProvider? builtInResources)
    {
        var services = new ServiceCollection();
        var context = new EngineServiceContext(
            EngineHostKind.Player,
            workspace.RootPath,
            isAot ? $"Player:AOT:{workspace.Project.Name}" : $"Player:HotUpdate:{workspace.Project.Name}");
        services.AddBEngine(context, code.Assemblies);
        services.AddSingleton(workspace);
        services.AddSingleton(projectSettings);
        services.AddSingleton(assetBundleSettings);
        services.AddSingleton(assetBundles);
        services.AddSingleton<IAssetBundleManager>(assetBundles);
        services.AddSingleton(resourceProvider);
        services.AddSingleton(bootstrap);
        services.AddSingleton<IContentBootstrapper>(bootstrap);
        if (flow is not null)
        {
            services.AddSingleton(flow);
            services.AddSingleton<IAotStartupFlow>(flow);
        }
        if (builtInResources is not null) services.AddSingleton(builtInResources);
        services.AddSingleton<ISceneLoader>(provider => new PlayerProjectSceneLoader(
            workspace, provider.GetRequiredService<IAssetBundleManager>(), builtInResources));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        try
        {
            code.Activate(provider);
            var sceneManager = provider.GetRequiredService<IRuntimeSceneManager>();
            sceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            return new PlayerApplicationStage(
                provider,
                provider.GetRequiredService<ISceneRuntimeFactory>(),
                sceneManager,
                code,
                scenePath,
                isAot);
        }
        catch
        {
            foreach (var scene in provider.GetService<IRuntimeSceneManager>()?.LoadedScenes.ToArray() ?? [])
                provider.GetRequiredService<IRuntimeSceneManager>().UnregisterScene(scene, disposeScene: true);
            provider.Dispose();
            code.Dispose();
            throw;
        }
    }

    private static AssetBundleSettingsDocument LoadAssetBundleSettings(ProjectWorkspace workspace)
    {
        var path = Path.Combine(workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        return workspace.RuntimeMetadata?.AssetBundles ??
               (File.Exists(path)
                   ? YamlUtility.Load<AssetBundleSettingsDocument>(path)
                   : new AssetBundleSettingsDocument());
    }

    private static void ApplyRemoteOverride(AssetBundleSettingsDocument settings)
    {
        var remote = Environment.GetEnvironmentVariable(
            PlayerServiceCollectionExtensions.AssetBundleRemoteUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(remote)) return;
        settings.Enabled = true;
        settings.RemoteBaseUrl = remote.Trim();
        settings.CheckForUpdatesOnStartup = true;
        settings.ApplyUpdatesOnStartup = true;
    }

    private static AssetBundleRuntimeOptions CreateRemoteOptions(AssetBundleRuntimeOptions source) => new()
    {
        PackageName = source.PackageName,
        BuiltInDirectory = null,
        CacheDirectory = source.CacheDirectory,
        UsePersistentCache = true,
        RequireHttps = source.RequireHttps,
        MaxRetries = source.MaxRetries,
        MaximumBundleCount = source.MaximumBundleCount,
        MaximumAssetCount = source.MaximumAssetCount,
        MaximumBundleSize = source.MaximumBundleSize,
        MaximumAssetSize = source.MaximumAssetSize,
        MaximumCatalogSize = source.MaximumCatalogSize,
        RemoteBaseUri = source.RemoteBaseUri,
        HttpClient = source.HttpClient,
        HttpTransferObserver = source.HttpTransferObserver
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gameStage?.Dispose();
        _gameStage = null;
        _aotStage?.Dispose();
        _aotStage = null;
        Flow.Dispose();
        _updateBootstrap.Dispose();
        _updateAssetBundles.Dispose();
        _aotResources.Dispose();
        _playerResources?.Dispose();
    }
}
