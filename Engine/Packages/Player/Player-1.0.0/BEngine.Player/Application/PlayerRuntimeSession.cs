using BEngine.DependencyInjection;
using BEngine.Diagnostics;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.Serialization;
using BEngine.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.SceneManagement;
using BEngine.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

public static class BEnginePlayer
{
    public const string PersistentDataPathEnvironmentVariable = "BENGINE_PLAYER_PERSISTENT_DATA_PATH";
    public const string NativeDiagnosticsEnvironmentVariable = "BENGINE_PLAYER_NATIVE_DIAGNOSTICS";

    /// <summary>
    /// Removes diagnostics-listener options before resolving the Player workspace. Optional
    /// diagnostics packages still read the original process command line when they are loaded.
    /// </summary>
    public static string[] FilterHostArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] names = ["--diagnostics-host", "--diagnostics-port", "--diagnostics-token"];
        var result = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var exact = names.Any(name => string.Equals(argument, name,
                StringComparison.OrdinalIgnoreCase));
            if (exact)
            {
                if (index + 1 >= arguments.Count)
                    throw new ArgumentException($"A value is required after '{argument}'.");
                index++;
                continue;
            }
            if (names.Any(name => argument.StartsWith(name + "=",
                    StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(argument);
        }
        return result.ToArray();
    }

    public static int Run(params string[] args) =>
        Run(new ServiceCollection(), null, args);

    public static int Run(IPlayerSplashHost? splashHost, params string[] args) =>
        Run(new ServiceCollection(), splashHost, args);

    public static int Run(IServiceCollection services, params string[] args) =>
        Run(services, null, args);

    public static int Run(
        IServiceCollection services,
        IPlayerSplashHost? splashHost,
        params string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.AddBEnginePlayerPlatformServices();
        services.AddBEnginePlayerManagedCodeServices();
        using var startupServices = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        services.ShareBEnginePlatformState(startupServices);
        var hostServices = services.ToArray();
        using var runtimeServicesAttachment = startupServices.AttachBEngineRuntimeServices();
        var startupResolver = startupServices.GetRequiredService<IPlayerStartupResolver>();
        var nativeLibraryLoader = startupServices.GetRequiredService<IPlayerNativeLibraryLoader>();
        var hostOutput = startupServices.GetRequiredService<IPlayerHostOutput>();
        var fileSystem = startupServices.GetRequiredService<FileUtility>();
        var pathService = startupServices.GetRequiredService<PathUtility>();
        var platformService = startupServices.GetRequiredService<PlatformUtility>();
        ServiceProvider? persistentServices = null;
        IDisposable? persistentServicesAttachment = null;
        var activeStartupServices = startupServices;
        PlayerStartupDiagnostics? diagnostics = null;
        PlayerSplashScreen? splash = null;
        PlayerBuiltInResourceProvider? startupResources = null;
        var failurePhase = "BOOTSTRAP";
        try
        {
            var startup = startupResolver.Resolve(args);
            var validation = args.Length != 0 &&
                             args[0].Equals("--validate", StringComparison.OrdinalIgnoreCase);
            var persistentDataPath = startupResolver.PersistentDataPath;
            if (!string.IsNullOrWhiteSpace(persistentDataPath))
            {
                persistentDataPath = PlayerPersistentApplicationPaths.Configure(
                    services, persistentDataPath, startupServices);
                persistentServices = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
                activeStartupServices = persistentServices;
                persistentServicesAttachment = persistentServices.AttachBEngineRuntimeServices();
                hostServices = services.ToArray();
            }
            if ((startup.Manifest as BEngine.Build.PlayerBootstrapManifest) is { Version: >= 4 } packagedManifest)
            {
                startupResources = new PlayerBuiltInResourceProvider(
                    packagedManifest.ResolvePlayerResourceArchive(startupResolver.BaseDirectory),
                    fileSystem,
                    pathService);
                startupResources.Activate();
            }
            diagnostics = PlayerStartupDiagnostics.Start(
                startup.ProjectPath,
                (startup.Manifest as BEngine.Build.PlayerBootstrapManifest),
                persistentDataPath,
                (startup.Manifest as BEngine.Build.PlayerBootstrapManifest)?.WritePlayerLog ?? true);
            hostOutput.WriteErrorLine($"BENGINE_PLAYER_LOG|{diagnostics.LogPath}");
            RemoteDiagnosticsHost.StartFromEnvironment();
            PlayerStartupDiagnostics.Phase("01_BOOTSTRAP_STARTED",
                $"project={startup.ProjectPath};build={(startup.Manifest as BEngine.Build.PlayerBootstrapManifest)?.BuildVersion ?? "development"}");
            if (!validation && startup.Manifest is BEngine.Build.PlayerBootstrapManifest splashManifest)
            {
                failurePhase = "SPLASH_SCREEN";
                splash = PlayerSplashScreen.Show(
                    (startup.Manifest as BEngine.Build.PlayerBootstrapManifest),
                    startupResolver.BaseDirectory,
                    splashHost,
                    fileSystem,
                    pathService);
                PlayerStartupDiagnostics.Phase("01_SPLASH_SHOWN",
                    $"visible={splash.IsVisible};image=" +
                    (splashManifest.Version >= 4
                        ? splashManifest.SplashImageResource
                        : splashManifest.SplashImage));
            }
            nativeLibraryLoader.InitializeDesktopLibraries(startup.NativeLibraryDirectory);
            if (validation)
            {
                failurePhase = "CORE_AOT_BOOTSTRAP";
                RunCoreAotBootstrap(fileSystem, pathService, platformService);
                failurePhase = "VALIDATION";
                Validate(
                    services,
                    startup.ProjectPath,
                    persistentDataPath: persistentDataPath);
                hostOutput.WriteLine($"BENGINE_PLAYER_VALIDATION_OK|{startup.ProjectPath}");
                return 0;
            }
            failurePhase = "PLAYER_COMPOSITION";
            if ((startup.Manifest as BEngine.Build.PlayerBootstrapManifest) is not null)
            {
                var workspace = PlayerWorkspace.OpenRuntime(
                    startup.ProjectPath, fileSystem, pathService);
                var projectSettings = ProjectRuntimeSettings.LoadAndApply(
                    workspace, fileSystem, pathService);
                using var stagedApplication = new PlayerApplication(
                    workspace,
                    projectSettings,
                    cancellationToken => InitializePackagedCoordinatorAsync(
                        startup.ProjectPath,
                        persistentDataPath,
                        activeStartupServices.GetRequiredService<IAssetBundleManagerFactory>(),
                        hostServices,
                        fileSystem,
                        pathService,
                        platformService,
                        activeStartupServices.GetRequiredService<ApplicationPathUtility>(),
                        activeStartupServices.GetRequiredService<IManagedCodeRuntimeFactory>(),
                        cancellationToken),
                    splash,
                    activeStartupServices.GetRequiredService<IPlayerWindowFactory>(),
                    activeStartupServices.GetRequiredService<IPlayerRenderContextFactory>(),
                    activeStartupServices.GetRequiredService<IPlayerInputContextFactory>(),
                    platformService,
                    activeStartupServices);
                failurePhase = "AOT_GAME_LOOP";
                stagedApplication.Run();
                return 0;
            }
            failurePhase = "CORE_AOT_BOOTSTRAP";
            RunCoreAotBootstrap(fileSystem, pathService, platformService);
            using var gameServices = services
                .AddBEnginePlayer(startup.ProjectPath, persistentDataPath)
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var application = gameServices.GetRequiredService<PlayerApplication>();
            failurePhase = "GAME_LOOP";
            application.Run();
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                diagnostics ??= PlayerStartupDiagnostics.StartForPlayerRoot(
                    startupResolver.BaseDirectory);
                PlayerStartupDiagnostics.Failure(failurePhase, exception);
                hostOutput.WriteErrorLine($"BENGINE_PLAYER_LOG|{diagnostics.LogPath}");
            }
            catch { }
            hostOutput.WriteErrorLine(exception.ToString());
            return 1;
        }
        finally
        {
            splash?.Dispose();
            startupResources?.Dispose();
            diagnostics?.Dispose();
            persistentServicesAttachment?.Dispose();
            persistentServices?.Dispose();
        }
    }

    private static async BValueTask<PlayerStagedRuntimeCoordinator> InitializePackagedCoordinatorAsync(
        string projectPath,
        string? persistentDataPath,
        IAssetBundleManagerFactory assetBundleManagerFactory,
        IReadOnlyList<ServiceDescriptor> hostServices,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths,
        IManagedCodeRuntimeFactory managedCodeRuntimeFactory,
        BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RunCoreAotBootstrap(fileSystem, pathService, platformService);
        return await PlayerStagedRuntimeCoordinator.CreateAsync(
            projectPath,
            persistentDataPath,
            assetBundleManagerFactory,
            hostServices,
            fileSystem,
            pathService,
            platformService,
            applicationPaths,
            managedCodeRuntimeFactory,
            cancellationToken);
    }

    private static void RunCoreAotBootstrap(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        PlayerStartupDiagnostics.Phase("02_CORE_AOT_BOOTSTRAP_STARTED");
        var target = BuildTargetManifestSerializer.LoadCurrent(
            baseDirectory: null, fileSystem, pathService, platformService);
        RuntimeTypeCache.Warmup();
        PlayerStartupDiagnostics.Phase("02_CORE_AOT_BOOTSTRAP_COMPLETED",
            $"target={target.TargetId};runtime={target.ManagedCodeRuntime}");
    }

    public static string ResolveDefaultProjectPath(string? playerDirectory = null) =>
        ResolveDefaultProjectPath(new ServiceCollection(), playerDirectory);

    public static string ResolveDefaultProjectPath(
        IServiceCollection services,
        string? playerDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBEnginePlatformServices();
        services.AddBEnginePlayerPlatformServices();
        services.AddBEnginePlayerManagedCodeServices();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IPlayerStartupResolver>()
            .ResolveDefaultProjectPath(playerDirectory);
    }

    public static PlayerRuntimeSession CreateRuntimeSession(
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null) =>
        CreateRuntimeSession(
            new ServiceCollection(), projectPath, declareAotInterpreter, persistentDataPath);

    public static PlayerRuntimeSession CreateRuntimeSession(
        IServiceCollection services,
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (declareAotInterpreter) AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        services.AddBEnginePlatformServices();
        services.AddBEnginePlayerPlatformServices();
        services.AddBEnginePlayerManagedCodeServices();
        var platformServices = services.BuildServiceProvider();
        ServiceProvider? originalPlatformServices = null;
        try
        {
            persistentDataPath = PlayerPersistentApplicationPaths.Configure(
                services, persistentDataPath, platformServices);
            if (persistentDataPath is not null)
            {
                originalPlatformServices = platformServices;
                platformServices = services.BuildServiceProvider();
            }
            // AOT and game stages must observe the same input, focus and display state
            // configured by the embedding host before asynchronous startup completes.
            services.ShareBEnginePlatformState(platformServices);
            RuntimeTypeCache.Warmup();
            return new PlayerRuntimeSession(
                projectPath,
                persistentDataPath,
                platformServices.GetRequiredService<IAssetBundleManagerFactory>(),
                platformServices.GetRequiredService<FileUtility>(),
                platformServices.GetRequiredService<PlatformUtility>(),
                platformServices.GetRequiredService<PathUtility>(),
                services,
                platformServices,
                originalPlatformServices);
        }
        catch
        {
            try { platformServices.Dispose(); }
            finally
            {
                if (!ReferenceEquals(platformServices, originalPlatformServices))
                    originalPlatformServices?.Dispose();
            }
            throw;
        }
    }

    public static void Validate(
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null) =>
        Validate(
            new ServiceCollection(), projectPath, declareAotInterpreter, persistentDataPath);

    public static void Validate(
        IServiceCollection services,
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null)
    {
        using var session = CreateRuntimeSession(
            services, projectPath, declareAotInterpreter, persistentDataPath);
        session.Start();
        try { session.Validate(TimeSpan.FromMinutes(5)); }
        finally { session.Stop(); }
    }

}

public sealed class PlayerRuntimeSession : IDisposable
{
    private readonly BCancellationSource _lifetime = new();
    private readonly FileUtility _fileSystem;
    private readonly PlatformUtility _platformService;
    private readonly PathUtility _pathService;
    private readonly IDisposable _startupServices;
    private readonly IDisposable? _originalStartupServices;
    private IDisposable? _runtimeServicesAttachment;
    private ServiceProvider? _services;
    private ISceneRuntimeFactory? _runtimeFactory;
    private IRuntimeSceneManager? _sceneManager;
    private PlayerAssetBundleBootstrap? _assetBundles;
    private PlayerStagedRuntimeCoordinator? _stagedRuntime;
    private readonly List<SceneRuntime> _runtimes = [];
    private BTask<PlayerStagedRuntimeCoordinator>? _bootstrapOperation;
    private bool _started;
    private bool _runtimeStageStarted;
    private bool _isAotStage;
    private bool _transitioning;
    private BTask? _transitionOperation;
    private BTask? _disposeOperation;
    private IGraphicsPresentationDevice? _presentationDevice;
    private ISceneRenderer? _embeddedRenderer;
    private readonly List<Camera2D> _embeddedCameras = [];
    private bool _ownsPresentationDevice;
    private bool _disposed;

    internal PlayerRuntimeSession(
        string projectPath,
        string? persistentDataPath,
        IAssetBundleManagerFactory assetBundleManagerFactory,
        FileUtility fileSystem,
        PlatformUtility platformService,
        PathUtility pathService,
        IServiceCollection runtimeServices,
        ServiceProvider startupServices,
        IDisposable? originalStartupServices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(assetBundleManagerFactory);
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _startupServices = startupServices ?? throw new ArgumentNullException(nameof(startupServices));
        _originalStartupServices = originalStartupServices;
        ArgumentNullException.ThrowIfNull(runtimeServices);
        ReplaceRuntimeServices(startupServices);
        string fullProjectPath;
        ProjectWorkspace runtimeWorkspace;
        try
        {
            fullProjectPath = _pathService.GetFullPath(projectPath);
            runtimeWorkspace = PlayerWorkspace.OpenRuntime(
                fullProjectPath, _fileSystem, _pathService);
        }
        catch
        {
            DetachRuntimeServices();
            throw;
        }
        Workspace = runtimeWorkspace;
        if (runtimeWorkspace.GetRuntimeMetadata()?.PlayerBootstrap is not null)
        {
            try
            {
                var stagedSettings = ProjectRuntimeSettings.LoadAndApply(
                    runtimeWorkspace, _fileSystem, _pathService);
                Application.companyName = stagedSettings.CompanyName;
                Application.productName = stagedSettings.ProductName;
                Application.isEditor = false;
                Application.isPlaying = false;
                Time.fixedDeltaTime = Fix64.Parse(Workspace.Project.FixedDeltaTime);
                _isAotStage = true;
                _bootstrapOperation = PlayerStagedRuntimeCoordinator.CreateAsync(
                    fullProjectPath,
                    persistentDataPath,
                    assetBundleManagerFactory,
                    runtimeServices.ToArray(),
                    fileSystem,
                    pathService,
                    platformService,
                    startupServices.GetRequiredService<ApplicationPathUtility>(),
                    startupServices.GetRequiredService<IManagedCodeRuntimeFactory>(),
                    _lifetime.Token).AsBTask();
            }
            catch
            {
                DetachRuntimeServices();
                throw;
            }
            return;
        }
        try
        {
            _services = runtimeServices
                .AddBEnginePlayer(fullProjectPath, persistentDataPath)
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
        }
        catch
        {
            DetachRuntimeServices();
            throw;
        }
        ReplaceRuntimeServices(_services);
        try
        {
            Workspace = _services.GetRequiredService<ProjectWorkspace>();
            var projectSettings = _services.GetRequiredService<ProjectSettingsData>();
            Application.companyName = projectSettings.CompanyName;
            Application.productName = projectSettings.ProductName;
            Application.isEditor = false;
            Application.isPlaying = false;
            Time.fixedDeltaTime = Fix64.Parse(Workspace.Project.FixedDeltaTime);
            _runtimeFactory = _services.GetRequiredService<ISceneRuntimeFactory>();
            _sceneManager = _services.GetRequiredService<IRuntimeSceneManager>();
            _assetBundles = _services.GetService<PlayerAssetBundleBootstrap>();
            if (_assetBundles is not null)
                PlayerMainThread.Complete(_assetBundles.InitializeAsync());
            try
            {
                var hotUpdate = _services.GetService<PlayerHotUpdateSession>();
                if (hotUpdate is not null)
                    PlayerMainThread.Complete(hotUpdate.ActivateAsync(_services));
                AttachSceneManager();
                _sceneManager.LoadScene(Workspace.StartupScenePath, LoadSceneMode.Single);
                PlayerStartupDiagnostics.Phase("06_SCENE_LOADED",
                    $"scene={Workspace.Project.StartupScene};mode=validation");
            }
            catch (Exception exception)
            {
                if (_assetBundles is not null)
                    PlayerMainThread.Complete(_assetBundles.RollbackAfterStartupFailureAsync(exception));
                throw;
            }
        }
        catch
        {
            DetachRuntimeServices();
            _services.Dispose();
            throw;
        }
    }

    public ProjectWorkspace Workspace { get; }
    public IServiceProvider Services => _services ??
        throw new InvalidOperationException("The Player runtime session is still initializing.");
    public IReadOnlyList<Scene> LoadedScenes => _sceneManager?.LoadedScenes ?? [];
    public Scene? ActiveScene => _sceneManager?.ActiveScene;
    public bool IsStarted => _started;
    public bool CanRender => _runtimeStageStarted && ActiveScene is not null;
    public IAotStartupFlow? StartupFlow => _stagedRuntime?.Flow;
    internal bool IsDisposalPending => _disposeOperation is { IsCompleted: false };

    /// <summary>Attaches the presentation device owned by an embedded mobile or browser host.</summary>
    public void AttachGraphicsDevice(IGraphicsPresentationDevice device, bool ownsDevice = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        if (_presentationDevice is not null)
            throw new InvalidOperationException("A graphics presentation device is already attached.");
        _presentationDevice = device;
        _ownsPresentationDevice = ownsDevice;
    }

    /// <summary>Releases the renderer and detaches a surface-bound presentation device.</summary>
    public void DetachGraphicsDevice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _embeddedRenderer?.Dispose();
        _embeddedRenderer = null;
        if (_ownsPresentationDevice) _presentationDevice?.Dispose();
        _presentationDevice = null;
        _ownsPresentationDevice = false;
    }

    /// <summary>Statistics from the most recent embedded scene render.</summary>
    public SceneRenderStatistics LastRenderStatistics => _embeddedRenderer?.LastRenderStatistics ?? default;

    /// <summary>Renders the currently loaded Player scenes. Returns false while staged startup is incomplete.</summary>
    public bool Render(int width, int height, bool drawUi = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
            throw new InvalidOperationException("The Player runtime session has not been started.");
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var device = _presentationDevice ?? throw new InvalidOperationException(
            "AttachGraphicsDevice must be called before rendering an embedded Player.");
        var scene = ActiveScene;
        if (!_runtimeStageStarted || scene is null) return false;
        _embeddedRenderer ??= Services.GetRequiredService<ISceneRendererFactory>().Create(device);
        device.BeginFrame(width, height);
        EngineRenderer.FillGameCameras(LoadedScenes, _embeddedCameras);
        _embeddedRenderer.RenderCameras(
            LoadedScenes, scene, _embeddedCameras,
            new GraphicsRect(0, 0, width, height), width, height, drawUi);
        device.Present();
        return true;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        Time.Reset();
        _started = true;
        Application.isEditor = false;
        Application.isPlaying = true;
        try
        {
            TryCompleteBootstrap();
            TryStartRuntimeStage();
        }
        catch (Exception exception)
        {
            _started = false;
            Application.isPlaying = false;
            if (_assetBundles is not null)
                PlayerMainThread.Complete(_assetBundles.RollbackAfterStartupFailureAsync(exception));
            throw;
        }
    }

    public void Tick(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed && !IsDisposalPending, this);
        if (!_started && !_disposed)
            throw new InvalidOperationException("The Player runtime session has not been started.");
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        try
        {
            PlayerMainThread.Tick(elapsed);
            if (_disposed) return;
            TryCompleteBootstrap();
            TryStartRuntimeStage();
            ObserveTransition();
            if (!_runtimeStageStarted) return;
            if (_isAotStage) _stagedRuntime!.Flow.Pump();
            if (!Application.isFocused && !Application.runInBackground) return;
            SceneRuntime.TickAll(_runtimes, Fix64.FromTimeSpan(elapsed));
            if (_isAotStage)
            {
                _stagedRuntime!.Flow.Pump();
                TryEnterGame();
            }
            ObserveTransition();
        }
        finally { Input.EndFrame(); }
    }

    public void SetFocused(bool focused) => Application.SetFocus(focused);
    public void SetPaused(bool paused) => Application.SetPaused(paused);
    public void RaiseLowMemory() => Application.RaiseLowMemory();

    public void Stop()
    {
        if (_disposed || !_started) return;
        StopRuntimeStage();
        _started = false;
        Application.isPlaying = false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopRuntimeStageSafely("PLAYER_RUNTIME_STOP");
            _started = false;
            Application.isPlaying = false;
            _disposed = true;
            _lifetime.Cancel();
            _disposeOperation = CompleteDisposalAsync(_transitionOperation, _bootstrapOperation);
            GC.SuppressFinalize(this);
        }

        if (_disposeOperation is not { } operation) return;
        if (PlayerRuntimeShutdownDrain.TryComplete(
                operation,
                Tick,
                nameof(PlayerRuntimeSession),
                "PLAYER_RUNTIME_SESSION_SHUTDOWN_DRAIN"))
            _disposeOperation = null;
    }

    private async BTask CompleteDisposalAsync(
        BTask? transition,
        BTask<PlayerStagedRuntimeCoordinator>? bootstrap)
    {
        await ObserveShutdownOperationAsync(transition, "GAME_TRANSITION_STOP");

        var stagedRuntime = _stagedRuntime;
        if (bootstrap is not null)
        {
            try { stagedRuntime ??= await bootstrap; }
            catch (BOperationCanceledException) { }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_BOOTSTRAP_STOP", exception); }
        }

        StopRuntimeStageSafely("PLAYER_RUNTIME_STOP");
        if (_sceneManager is not null)
        {
            try { DetachSceneManager(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_SCENE_STOP", exception); }
            foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            {
                try { _sceneManager.UnregisterScene(scene, disposeScene: true); }
                catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_SCENE_STOP", exception); }
            }
        }
        _runtimes.Clear();
        try { _embeddedRenderer?.Dispose(); }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_RENDERER_STOP", exception); }
        _embeddedRenderer = null;
        if (_ownsPresentationDevice)
        {
            try { _presentationDevice?.Dispose(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_GRAPHICS_STOP", exception); }
        }
        _presentationDevice = null;
        DetachRuntimeServices();

        if (stagedRuntime is null)
        {
            try
            {
                if (_services?.GetService<PlayerHotUpdateSession>() is { } hotUpdate)
                    await hotUpdate.DisposeAsync();
            }
            catch (Exception exception)
            {
                PlayerStartupDiagnostics.Failure("HOTUPDATE_RUNTIME_STOP", exception);
            }
            try { _services?.Dispose(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_SERVICES_STOP", exception); }
        }
        else
        {
            try { await stagedRuntime.DisposeAsync(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("STAGED_RUNTIME_STOP", exception); }
        }
        _stagedRuntime = null;
        _services = null;
        _sceneManager = null;
        _runtimeFactory = null;

        try { _lifetime.Dispose(); }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_LIFETIME_STOP", exception); }
        try { _startupServices.Dispose(); }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_STARTUP_SERVICES_STOP", exception); }
        try { _originalStartupServices?.Dispose(); }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_ORIGINAL_STARTUP_SERVICES_STOP", exception); }
    }

    private static async BValueTask ObserveShutdownOperationAsync(BTask? operation, string phase)
    {
        if (operation is null) return;
        try { await operation; }
        catch (BOperationCanceledException) { }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure(phase, exception); }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (_runtimes.Any(runtime => ReferenceEquals(runtime.Scene, scene))) return;
        var runtime = _runtimeFactory!.Create(scene);
        _runtimes.Add(runtime);
        if (_runtimeStageStarted) runtime.Start();
    }

    private void OnSceneUnloaded(Scene scene) =>
        _runtimes.RemoveAll(runtime => ReferenceEquals(runtime.Scene, scene));

    private void TryEnterGame()
    {
        if (_transitioning || !_stagedRuntime!.Flow.EnterGameRequested) return;
        _transitioning = true;
        _transitionOperation = TransitionToGameAsync();
    }

    internal void Validate(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("The Player runtime session has not been started.");
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var started = _platformService.MonotonicTimestamp;
        var previous = TimeSpan.Zero;
        var checkRequested = false;
        while (_platformService.GetElapsedTime(started) < timeout)
        {
            var now = _platformService.GetElapsedTime(started);
            Tick(now - previous);
            previous = now;
            if (!_runtimeStageStarted) continue;
            if (!_isAotStage) return;

            var flow = _stagedRuntime!.Flow;
            if (!checkRequested)
            {
                flow.CheckForUpdates();
                checkRequested = true;
                continue;
            }
            var state = flow.Current;
            if (state.RequiresUpdateConfirmation)
            {
                if (state.CanDeclineUpdate && _platformService.GetEnvironmentVariable(
                        PlayerAotStartupFlow.AutoDeclineUpdateEnvironmentVariable) == "1")
                    flow.DeclineUpdate();
                else
                    flow.ConfirmUpdate();
                continue;
            }
            if (state.CanRetry)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(state.Error) ? state.Status : state.Error);
            if (state.CanEnterGame) flow.EnterGame();
        }
        throw new TimeoutException($"The Player runtime did not start within {timeout}.");
    }

    private void TryCompleteBootstrap()
    {
        if (_bootstrapOperation is not { IsCompleted: true } operation) return;
        _bootstrapOperation = null;
        var staged = PlayerMainThread.Complete(operation);
        if (_disposed)
        {
            staged.Dispose();
            return;
        }

        _stagedRuntime = staged;
        _assetBundles = staged.UpdateBootstrap;
        _services = staged.AotStage.Services;
        ReplaceRuntimeServices(_services);
        _runtimeFactory = staged.AotStage.SceneRuntimeFactory;
        _sceneManager = staged.AotStage.SceneManager;
        AttachSceneManager();
        foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            OnSceneLoaded(scene, LoadSceneMode.Single);
    }

    private void TryStartRuntimeStage()
    {
        if (!_started || _runtimeStageStarted || _sceneManager is null) return;
        var started = 0;
        try
        {
            foreach (var runtime in _runtimes.ToArray())
            {
                runtime.Start();
                started++;
            }
            _runtimeStageStarted = true;
            if (_isAotStage)
            {
                PlayerStartupDiagnostics.Phase("03_AOT_SCENE_STARTED", "mode=runtime-session");
                _stagedRuntime!.Flow.Start();
            }
            else
            {
                if (_assetBundles is not null)
                    PlayerMainThread.Complete(_assetBundles.CommitStartupAsync());
                PlayerStartupDiagnostics.Phase("07_GAME_STARTED", "mode=runtime-session");
            }
        }
        catch
        {
            for (var index = started - 1; index >= 0; index--) _runtimes[index].Stop();
            _runtimeStageStarted = false;
            throw;
        }
    }

    private void StopRuntimeStage()
    {
        if (!_runtimeStageStarted) return;
        for (var index = _runtimes.Count - 1; index >= 0; index--) _runtimes[index].Stop();
        _runtimeStageStarted = false;
    }

    private void ObserveTransition()
    {
        if (_transitionOperation is not { IsCompleted: true } operation) return;
        _transitionOperation = null;
        PlayerMainThread.Complete(operation);
    }

    private async BTask TransitionToGameAsync()
    {
        try
        {
            var stagedRuntime = _stagedRuntime!;
            PlayerApplicationStage next;
            try
            {
                next = await stagedRuntime.CreateGameStageAsync(_lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
            }
            catch (BOperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                PlayerStartupDiagnostics.Failure("HOTUPDATE_STAGE", exception);
                if (!_disposed) stagedRuntime.Flow.ReportTransitionFailure(exception);
                return;
            }

            var previous = CaptureStageSnapshot();
            PlayerApplicationStage? committedAotStage = null;
            var transitionFailure = await PlayerStageTransitionTransaction.ExecuteAsync(
                () => ActivateGameStage(next),
                () => _assetBundles?.CommitStartupAsync(_lifetime.Token) ?? BValueTask.CompletedTask,
                () => committedAotStage = stagedRuntime.CommitTransition(next),
                failure => RollbackPreparedGameStageAsync(stagedRuntime, next, failure),
                () => RestoreStageSnapshot(previous));
            if (transitionFailure is not null)
            {
                if (transitionFailure is not BOperationCanceledException || !_lifetime.IsCancellationRequested)
                    PlayerStartupDiagnostics.Failure("HOTUPDATE_STAGE", transitionFailure);
                if (!_disposed) stagedRuntime.Flow.ReportTransitionFailure(transitionFailure);
                return;
            }

            try
            {
                PlayerStartupDiagnostics.Phase("06_SCENE_LOADED",
                    $"scene={next.ScenePath};mode=runtime-session");
                PlayerStartupDiagnostics.Phase("07_GAME_STARTED", "mode=runtime-session");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Player startup diagnostics failed after the game stage committed: {exception}");
            }
            try { await stagedRuntime.ReleaseCommittedAotStageAsync(committedAotStage!); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("AOT_STAGE_RELEASE", exception); }
        }
        finally
        {
            _transitioning = false;
        }
    }

    private PlayerRuntimeStageSnapshot CaptureStageSnapshot() => new(
        _services ?? throw new InvalidOperationException("The AOT runtime services are unavailable."),
        _runtimeFactory ?? throw new InvalidOperationException("The AOT runtime factory is unavailable."),
        _sceneManager ?? throw new InvalidOperationException("The AOT scene manager is unavailable."),
        _runtimes.ToArray(),
        _runtimeStageStarted,
        _isAotStage);

    private void ActivateGameStage(PlayerApplicationStage next)
    {
        StopRuntimeStage();
        DetachSceneManager();
        _runtimes.Clear();
        _services = next.Services;
        ReplaceRuntimeServices(_services);
        _runtimeFactory = next.SceneRuntimeFactory;
        _sceneManager = next.SceneManager;
        AttachSceneManager();
        foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            OnSceneLoaded(scene, LoadSceneMode.Single);
        foreach (var runtime in _runtimes.ToArray()) runtime.Start();
        _runtimeStageStarted = true;
        _isAotStage = false;
    }

    private async BValueTask RollbackPreparedGameStageAsync(
        PlayerStagedRuntimeCoordinator stagedRuntime,
        PlayerApplicationStage next,
        Exception failure)
    {
        StopRuntimeStageSafely("FAILED_GAME_STAGE_STOP");
        if (_sceneManager is not null) DetachSceneManager();
        _runtimes.Clear();
        DetachRuntimeServices();
        _services = null;
        List<Exception>? failures = null;
        if (_assetBundles is not null)
        {
            try { await _assetBundles.RollbackAfterStartupFailureAsync(failure, BCancellationToken.None); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { await stagedRuntime.AbortGameTransitionAsync(next); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 })
            throw new AggregateException("The failed game stage could not be completely rolled back.", failures);
    }

    private void RestoreStageSnapshot(PlayerRuntimeStageSnapshot previous)
    {
        _services = previous.Services;
        ReplaceRuntimeServices(_services);
        _runtimeFactory = previous.RuntimeFactory;
        _sceneManager = previous.SceneManager;
        _runtimes.Clear();
        _runtimes.AddRange(previous.Runtimes);
        _runtimeStageStarted = false;
        _isAotStage = previous.IsAotStage;
        if (_disposed) return;
        AttachSceneManager();
        if (!previous.RuntimeStageStarted) return;
        var started = 0;
        try
        {
            foreach (var runtime in _runtimes.ToArray())
            {
                runtime.Start();
                started++;
            }
            _runtimeStageStarted = true;
        }
        catch
        {
            for (var index = started - 1; index >= 0; index--)
            {
                try { _runtimes[index].Stop(); }
                catch (Exception exception) { PlayerStartupDiagnostics.Failure("AOT_STAGE_RESTORE_STOP", exception); }
            }
            throw;
        }
    }

    private void StopRuntimeStageSafely(string diagnosticPhase)
    {
        for (var index = _runtimes.Count - 1; index >= 0; index--)
        {
            try { _runtimes[index].Stop(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure(diagnosticPhase, exception); }
        }
        _runtimeStageStarted = false;
    }

    private void AttachSceneManager()
    {
        _sceneManager!.SceneLoaded += OnSceneLoaded;
        _sceneManager.SceneUnloaded += OnSceneUnloaded;
    }

    private void DetachSceneManager()
    {
        _sceneManager!.SceneLoaded -= OnSceneLoaded;
        _sceneManager.SceneUnloaded -= OnSceneUnloaded;
    }

    private void ReplaceRuntimeServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        DetachRuntimeServices();
        _runtimeServicesAttachment = services.AttachBEngineRuntimeServices();
    }

    private void DetachRuntimeServices()
    {
        var attachment = _runtimeServicesAttachment;
        _runtimeServicesAttachment = null;
        attachment?.Dispose();
    }

    private sealed record PlayerRuntimeStageSnapshot(
        ServiceProvider Services,
        ISceneRuntimeFactory RuntimeFactory,
        IRuntimeSceneManager SceneManager,
        SceneRuntime[] Runtimes,
        bool RuntimeStageStarted,
        bool IsAotStage);
}
