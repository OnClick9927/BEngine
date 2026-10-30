using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.DependencyInjection;
using BEngine.Serialization;
using BEngine.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

internal sealed class PlayerStagedRuntimeCoordinator : IDisposable
{
    private readonly PlayerBuiltInResourceProvider? _playerResources;
    private readonly PlayerBuiltInResourceProvider _aotResources;
    private readonly ITransactionalAssetBundleManager _updateAssetBundles;
    private readonly AssetBundleResourceProvider _updateResourceProvider;
    private readonly PlayerAssetBundleBootstrap _updateBootstrap;
    private readonly ServiceDescriptor[] _hostServices;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly ApplicationPathUtility _applicationPaths;
    private readonly IManagedCodeRuntimeFactory _managedCodeRuntimeFactory;
    private PlayerApplicationStage? _aotStage;
    private PlayerApplicationStage? _gameStage;
    private PlayerBuiltInResourceProvider? _gameResources;
    private BTask? _disposeOperation;
    private bool _aotResourcesReleased;
    private bool _disposed;

    private PlayerStagedRuntimeCoordinator(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        AssetBundleSettingsDocument assetBundleSettings,
        PlayerBuiltInResourceProvider? playerResources,
        PlayerBuiltInResourceProvider aotResources,
        ITransactionalAssetBundleManager updateAssetBundles,
        AssetBundleResourceProvider updateResourceProvider,
        PlayerAssetBundleBootstrap updateBootstrap,
        PlayerAotStartupFlow flow,
        PlayerApplicationStage aotStage,
        ServiceDescriptor[] hostServices,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths,
        IManagedCodeRuntimeFactory managedCodeRuntimeFactory)
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
        _hostServices = hostServices;
        _fileSystem = fileSystem;
        _pathService = pathService;
        _platformService = platformService;
        _applicationPaths = applicationPaths;
        _managedCodeRuntimeFactory = managedCodeRuntimeFactory;
    }

    internal ProjectWorkspace Workspace { get; }
    internal ProjectSettingsData ProjectSettings { get; }
    internal AssetBundleSettingsDocument AssetBundleSettings { get; }
    internal PlayerAotStartupFlow Flow { get; }
    internal PlayerApplicationStage AotStage => _aotStage ??
        throw new InvalidOperationException("The AOT Player stage is no longer available.");
    internal PlayerAssetBundleBootstrap UpdateBootstrap => _updateBootstrap;

    internal static async BValueTask<PlayerStagedRuntimeCoordinator> CreateAsync(
        string projectPath,
        string? persistentDataPath,
        IAssetBundleManagerFactory assetBundleManagerFactory,
        IReadOnlyList<ServiceDescriptor> hostServices,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths,
        IManagedCodeRuntimeFactory managedCodeRuntimeFactory,
        BCancellationToken cancellationToken = default,
        ProjectWorkspace? initialWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(assetBundleManagerFactory);
        ArgumentNullException.ThrowIfNull(hostServices);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(managedCodeRuntimeFactory);
        using var workspaceTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.workspaceIdentity");
        using var loadingBudget = FrameWorkBudget.EnterLoading(cancellationToken);
        PlayerStartupEnvironment.Current?.Report(.05f, "project");
        var capturedHostServices = hostServices.ToArray();
        var workspace = PlayerWorkspace.ReuseOrOpenRuntime(
            initialWorkspace, pathService.GetFullPath(projectPath), fileSystem, pathService);
        if (workspace.GetRuntimeMetadata()?.PlayerBootstrap is not { } manifest)
            throw new InvalidDataException("Staged Player startup requires packaged runtime metadata.");
        var playerRoot = PlayerPersistentDataPaths.ResolvePlayerRoot(workspace.RootPath, manifest);
        workspaceTiming.Dispose();
        PlayerBuiltInResourceProvider? playerResources = null;
        try
        {
            using var environmentTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.playerResourcesAndEnvironment");
            if (manifest.Version >= 4)
            {
                playerResources = workspace.GetPlayerResourceArchive() is { } archive
                    ? new PlayerBuiltInResourceProvider(archive, pathService)
                    : new PlayerBuiltInResourceProvider(
                        manifest.ResolvePlayerResourceArchive(playerRoot), fileSystem, pathService);
                PlayerAssetEnvironment.Initialize(
                    workspace, playerResources, fileSystem, pathService,
                    platformService, applicationPaths);
                BuildTargetManifestSerializer.SetCurrent(
                    playerResources.ReadBytes(manifest.BuildTargetResource));
            }
            else
            {
                PlayerAssetEnvironment.Initialize(
                    workspace, fileSystem: fileSystem, pathService: pathService,
                    platformService: platformService, applicationPaths: applicationPaths);
                BuildTargetManifestSerializer.SetCurrentPath(
                    manifest.ResolveBuildTargetManifest(playerRoot), fileSystem, pathService);
            }
            environmentTiming.Dispose();
            PlayerStartupEnvironment.Current?.Report(.12f, "settings");
            using var settingsTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.projectAndAssetBundleSettings");
            var projectSettings = ProjectRuntimeSettings.LoadAndApply(workspace, fileSystem, pathService);
            Application.companyName = projectSettings.CompanyName;
            Application.productName = projectSettings.ProductName;
            var settings = LoadAssetBundleSettings(workspace, fileSystem, pathService);
            PlayerServiceCollectionExtensions.ApplyAssetBundleRemoteOverride(settings, platformService);
            BEngine.AssetBundles.AssetBundleSettingsValidation.Validate(settings);
            if (!settings.Enabled)
                throw new InvalidDataException(
                    "Staged Player startup requires remote AssetBundles for HotUpdate game content.");
            settingsTiming.Dispose();
            PlayerStartupEnvironment.Current?.Report(.2f, "resources");

            using var optionsTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.assetBundleOptions");
            var sourceOptions = PlayerServiceCollectionExtensions.CreateAssetBundleOptions(
                workspace, settings, persistentDataPath, usePersistentRootAsCache: true,
                fileSystem: fileSystem,
                pathService: pathService,
                platformService: platformService,
                applicationPaths: applicationPaths);
            var direct = settings.LoadMode == AssetBundleLoadMode.StreamingAssets;
            if (!direct)
                PlayerServiceCollectionExtensions.RemoveLegacyStagedCache(
                    workspace, settings, persistentDataPath, sourceOptions.CacheDirectory,
                    fileSystem, pathService, platformService, applicationPaths);
            var updateOptions = direct ? sourceOptions : CreateRemoteOptions(sourceOptions);
            optionsTiming.Dispose();
            using var archiveTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.aotArchiveOpenAndIndex");
            var aotArchivePath = pathService.Combine(
                workspace.RootPath,
                PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                BuiltInResourceArchive.FileName);
            var aotResources = new PlayerBuiltInResourceProvider(
                aotArchivePath, fileSystem, pathService, workspace.GetContentEncryptionKey());
            archiveTiming.Dispose();
            PlayerStartupEnvironment.Current?.Report(.35f, "managedCode");
            using var managerTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.updateManager");
            var updateManager = assetBundleManagerFactory.Create(updateOptions);
            var updateProvider = new AssetBundleResourceProvider(updateManager);
            var updateBootstrap = new PlayerAssetBundleBootstrap(
                updateManager, updateProvider, settings, registerResourceProviderOnPrepare: false);
            managerTiming.Dispose();
            PlayerAotStartupFlow? flow = null;
            PlayerHotUpdateSession? aotCode = null;
            PlayerApplicationStage? aotStage = null;
            try
            {
                using var activationTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.activateBuiltInResources");
                aotResources.Activate();
                PlayerStartupDiagnostics.Phase("03_BUILTIN_CONTENT_READY",
                    $"source=archive;entries={aotResources.Entries.Count};path={aotArchivePath}");
                activationTiming.Dispose();
                using var codeTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.aotReleaseSynchronousPrefix");
                var codeOperation = PlayerPackageLoader.LoadAsync(
                                  workspace, managedCodeRuntimeFactory, null,
                                  PlayerManagedCodeStage.Aot, aotResources,
                                  cancellationToken, fileSystem, pathService, platformService,
                                  applicationPaths);
                codeTiming.Dispose();
                aotCode = await codeOperation ??
                              throw new InvalidDataException(
                                  "The built-in AOT resource archive does not contain an AOT managed-code release.");
                PlayerStartupEnvironment.Current?.Report(.75f, "services");
                flow = new PlayerAotStartupFlow(
                    async cancellationToken =>
                    {
                        var plan = await updateBootstrap.CheckForStartupUpdateAsync(cancellationToken);
                        if (!plan.HasUpdates)
                            await updateBootstrap.CompleteWithoutUpdateAsync(cancellationToken);
                        return plan;
                    },
                    (plan, progress, cancellationToken) =>
                        updateBootstrap.ApplyCheckedUpdateAsync(plan, progress, cancellationToken),
                    cancellationToken => ValidateGameContentAsync(
                        updateManager, manifest, aotResources, cancellationToken),
                    settings.FailStartupWhenUpdateFails,
                    platformService);
                using var stageTiming = PlayerStartupPerformanceDiagnostics.Begin("coordinator.aotStageSynchronousPrefix");
                var stageOperation = CreateStageAsync(
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
                    builtInResources: aotResources,
                    hostServices: capturedHostServices,
                    cancellationToken: cancellationToken);
                stageTiming.Dispose();
                aotStage = await stageOperation;
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
                    aotStage,
                    capturedHostServices,
                    fileSystem,
                    pathService,
                    platformService,
                    applicationPaths,
                    managedCodeRuntimeFactory);
            }
            catch
            {
                if (aotStage is not null) await aotStage.DisposeAsync();
                else if (aotCode is not null) await aotCode.DisposeAsync();
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

    internal async BValueTask<PlayerApplicationStage> CreateGameStageAsync(
        BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_gameStage is not null) return _gameStage;
        var manifest = Workspace.GetRuntimeMetadata()?.PlayerBootstrap ??
                       throw new InvalidDataException("Player bootstrap metadata is missing.");
        var validation = await ValidateGameContentAsync(
            _updateAssetBundles, manifest, _aotResources, cancellationToken);
        if (!validation.IsValid)
            throw new InvalidDataException(validation.Error);
        if (string.IsNullOrWhiteSpace(manifest.HotUpdateStartupScene))
            throw new InvalidDataException("Player bootstrap metadata has no HotUpdate startup scene.");
        var hotUpdate = await PlayerPackageLoader.LoadAsync(
                            Workspace, _managedCodeRuntimeFactory, _updateAssetBundles,
                            PlayerManagedCodeStage.HotUpdate,
                            cancellationToken: cancellationToken,
                            fileSystem: _fileSystem,
                            pathService: _pathService,
                            platformService: _platformService,
                            applicationPaths: _applicationPaths) ??
                        throw new InvalidDataException(
                            "The active remote content release has no HotUpdate managed-code release.");
        PlayerBuiltInResourceProvider? gameResources = null;
        try
        {
            // Keep main-package Resources available after the startup domain is
            // released, with a separate decoded-object cache for the game domain.
            gameResources = _aotResources.CreateView();
            _aotResources.Deactivate();
            gameResources.Activate();
            _updateBootstrap.ActivateResourceProvider();
            _gameStage = await CreateStageAsync(
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
                builtInResources: gameResources,
                hostServices: _hostServices,
                cancellationToken: cancellationToken);
            _gameResources = gameResources;
            return _gameStage;
        }
        catch
        {
            _updateBootstrap.DeactivateResourceProvider();
            try { await hotUpdate.DisposeAsync(); }
            finally
            {
                try { gameResources?.Dispose(); }
                finally { RestoreAotResources(); }
            }
            throw;
        }
    }

    internal PlayerApplicationStage CommitTransition(PlayerApplicationStage gameStage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(_gameStage, gameStage))
            throw new InvalidOperationException("The supplied game stage is not the prepared transition stage.");
        var stage = _aotStage ??
                    throw new InvalidOperationException("The AOT Player stage was already released.");
        _aotStage = null;
        return stage;
    }

    internal async BValueTask ReleaseCommittedAotStageAsync(PlayerApplicationStage aotStage)
    {
        ArgumentNullException.ThrowIfNull(aotStage);
        PlayerStartupDiagnostics.Phase("07_AOT_STAGE_RELEASE_STARTED");
        try { await aotStage.DisposeAsync(); }
        finally { ReleaseAotResources(); }
        PlayerStartupDiagnostics.Phase("07_AOT_STAGE_RELEASE_COMPLETED");
    }

    internal void CompleteTransition()
    {
        var gameStage = _gameStage ??
                        throw new InvalidOperationException("The HotUpdate game stage is not prepared.");
        var aotStage = CommitTransition(gameStage);
        var operation = ReleaseCommittedAotStageAsync(aotStage).AsBTask();
        if (operation.IsCompleted) PlayerMainThread.Complete(operation);
        else _ = ObserveDisposalAsync(operation);
    }

    internal async BValueTask AbortGameTransitionAsync(PlayerApplicationStage gameStage)
    {
        ArgumentNullException.ThrowIfNull(gameStage);
        if (ReferenceEquals(_gameStage, gameStage)) _gameStage = null;
        _updateBootstrap.DeactivateResourceProvider();
        try { await gameStage.DisposeAsync(); }
        finally
        {
            try { ReleaseGameResources(); }
            finally { RestoreAotResources(); }
        }
    }

    private static async BValueTask<(bool IsValid, string Error)> ValidateGameContentAsync(
        IAssetBundleManager manager,
        BEngine.Build.PlayerBootstrapManifest manifest,
        PlayerBuiltInResourceProvider? builtInResources,
        BCancellationToken cancellationToken)
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
        var sceneInMainPackage = builtInResources?.ContainsAddress(manifest.HotUpdateStartupScene) == true;
        if (!sceneInMainPackage && !addresses.Contains(manifest.HotUpdateStartupScene))
            return Invalid($"The sandbox game release has no startup scene '{manifest.HotUpdateStartupScene}'.");

        try
        {
            using var releaseHandle = await manager.LoadBytesAsync(
                ManagedCodeReleaseManifest.DefaultAddress, cancellationToken);
            var release = ManagedCodeReleaseManifestSerializer.Deserialize(releaseHandle.Value);
            if (release.Modules.Count == 0)
                return Invalid("The sandbox HotUpdate release contains no C# modules.");
            if (release.Modules.Any(module => module.Name.Equals("AOT", StringComparison.OrdinalIgnoreCase)))
                return Invalid("The sandbox HotUpdate release incorrectly contains the AOT assembly.");

            foreach (var module in release.Modules)
            {
                if (!addresses.Contains(module.AssemblyAddress))
                    return Invalid($"The sandbox is missing C# module '{module.Name}'.");
                using var assembly = await manager.LoadBytesAsync(
                    module.AssemblyAddress, cancellationToken);
                if (assembly.Value.Length == 0)
                    return Invalid($"The sandbox C# module '{module.Name}' is empty.");
                if (string.IsNullOrWhiteSpace(module.SymbolsAddress)) continue;
                if (!addresses.Contains(module.SymbolsAddress))
                    return Invalid($"The sandbox is missing symbols for C# module '{module.Name}'.");
                using var symbols = await manager.LoadBytesAsync(
                    module.SymbolsAddress, cancellationToken);
            }

            if (sceneInMainPackage)
            {
                var scene = await builtInResources!.ReadBytesAsync(
                    manifest.HotUpdateStartupScene, cancellationToken);
                if (scene.Length == 0)
                    return Invalid("The main-package HotUpdate startup scene is empty.");
            }
            else
            {
                using var scene = await manager.LoadBytesAsync(
                    manifest.HotUpdateStartupScene, cancellationToken);
                if (scene.Value.Length == 0)
                    return Invalid("The sandbox HotUpdate startup scene is empty.");
            }
            PlayerStartupDiagnostics.Phase("04_GAME_CONTENT_READY",
                $"version={manager.ActiveVersion.Version};scene={manifest.HotUpdateStartupScene};" +
                $"modules={release.Modules.Count}");
            return (true, string.Empty);
        }
        catch (Exception exception) when (exception is not BOperationCanceledException)
        {
            return Invalid($"The sandbox game release is invalid: {exception.Message}");
        }

        static (bool IsValid, string Error) Invalid(string error)
        {
            PlayerStartupDiagnostics.Phase("04_GAME_CONTENT_INVALID", $"reason={error}");
            return (false, error);
        }
    }

    private static async BValueTask<PlayerApplicationStage> CreateStageAsync(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        AssetBundleSettingsDocument assetBundleSettings,
        ITransactionalAssetBundleManager assetBundles,
        AssetBundleResourceProvider resourceProvider,
        PlayerAssetBundleBootstrap bootstrap,
        PlayerHotUpdateSession code,
        string scenePath,
        PlayerAotStartupFlow? flow,
        bool isAot,
        PlayerBuiltInResourceProvider? builtInResources,
        IReadOnlyList<ServiceDescriptor> hostServices,
        BCancellationToken cancellationToken)
    {
        using var registrationTiming = PlayerStartupPerformanceDiagnostics.Begin("stage.registration");
        var services = new ServiceCollection();
        foreach (var descriptor in hostServices)
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        var context = new EngineServiceContext(
            EngineHostKind.Player,
            workspace.RootPath,
            isAot ? $"Player:AOT:{workspace.Project.Name}" : $"Player:HotUpdate:{workspace.Project.Name}");
        services.AddBEngine(context, code.Assemblies);
        services.AddBEnginePlayerPlatformServices();
        services.AddBEnginePlayerManagedCodeServices();
        services.AddSingleton(workspace);
        services.AddSingleton(projectSettings);
        services.AddSingleton(assetBundleSettings);
        services.AddSingleton<ITransactionalAssetBundleManager>(assetBundles);
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
            workspace,
            provider.GetRequiredService<IAssetBundleManager>(),
            builtInResources,
            provider.GetRequiredService<FileUtility>(),
            provider.GetRequiredService<PathUtility>()));
        registrationTiming.Dispose();
        if (isAot) PlayerStartupEnvironment.Current?.Report(.8f, "services");
        using var providerTiming = PlayerStartupPerformanceDiagnostics.Begin("stage.providerAndAttach");
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var runtimeServicesAttachment = provider.AttachBEngineRuntimeServices();
        providerTiming.Dispose();
        if (isAot) PlayerStartupEnvironment.Current?.Report(.85f, "activation");
        try
        {
            using var activationTiming = PlayerStartupPerformanceDiagnostics.Begin("stage.activationSynchronousPrefix");
            var activationOperation = code.ActivateAsync(provider, cancellationToken);
            activationTiming.Dispose();
            await activationOperation;
            if (isAot) PlayerStartupEnvironment.Current?.Report(.93f, "scene");
            var sceneManager = provider.GetRequiredService<IRuntimeSceneManager>();
            sceneManager.LoadScene(scenePath, LoadSceneMode.Single);
            if (isAot) PlayerStartupEnvironment.Current?.Report(.99f, "scene");
            return new PlayerApplicationStage(
                provider,
                provider.GetRequiredService<ISceneRuntimeFactory>(),
                sceneManager,
                code,
                scenePath,
                isAot);
        }
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            IRuntimeSceneManager? sceneManager = null;
            try { sceneManager = provider.GetService<IRuntimeSceneManager>(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (sceneManager is not null)
            {
                foreach (var scene in sceneManager.LoadedScenes.ToArray())
                {
                    try { sceneManager.UnregisterScene(scene, disposeScene: true); }
                    catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
                }
            }
            // HotUpdate Stop callbacks may resolve stage services, so the code domain owns
            // the earlier teardown position and the provider is always released last.
            try { await code.DisposeAsync(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            try { provider.Dispose(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (cleanupFailures is { Count: > 0 })
                throw new AggregateException(
                    "Player stage creation and rollback both failed.",
                    [failure, .. cleanupFailures]);
            throw;
        }
    }

    private static AssetBundleSettingsDocument LoadAssetBundleSettings(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        var path = pathService.Combine(
            workspace.ProjectSettingsPath, AssetBundleSettingsDocument.FileName);
        return workspace.GetRuntimeMetadata()?.AssetBundles ??
               (fileSystem.FileExists(path)
                   ? YamlUtility.Load<AssetBundleSettingsDocument>(path, fileSystem, pathService)
                   : new AssetBundleSettingsDocument());
    }

    private static AssetBundleRuntimeOptions CreateRemoteOptions(AssetBundleRuntimeOptions source) => new()
    {
        EncryptionKey = source.EncryptionKey,
        RequireEncryptedContent = source.RequireEncryptedContent,
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
        HttpTransport = source.HttpTransport,
        HttpTransferObserver = source.HttpTransferObserver
    };

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
        if (_disposed) return;
        _disposed = true;
        var gameStage = _gameStage;
        _gameStage = null;
        var aotStage = _aotStage;
        _aotStage = null;
        List<Exception>? failures = null;
        if (gameStage is not null)
        {
            try { await gameStage.DisposeAsync(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { ReleaseGameResources(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (aotStage is not null)
        {
            try { await aotStage.DisposeAsync(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        Flow.Dispose();
        _updateBootstrap.Dispose();
        _updateAssetBundles.Dispose();
        ReleaseAotResources();
        _playerResources?.Dispose();
        if (failures is { Count: > 0 })
            throw new AggregateException("Staged Player runtime shutdown failed.", failures);
    }

    private void ReleaseAotResources()
    {
        if (_aotResourcesReleased) return;
        _aotResourcesReleased = true;
        _aotResources.Dispose();
    }

    private void ReleaseGameResources()
    {
        var resources = _gameResources;
        _gameResources = null;
        resources?.Dispose();
    }

    private void RestoreAotResources()
    {
        if (!_disposed && !_aotResourcesReleased && _aotStage is not null)
            _aotResources.Activate();
    }

    private static async BValueTask ObserveDisposalAsync(BTask operation)
    {
        try { await operation; }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("STAGED_RUNTIME_STOP", exception);
        }
    }
}
