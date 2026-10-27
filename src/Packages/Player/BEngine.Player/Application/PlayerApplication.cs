using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Build;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

internal sealed class PlayerApplication : IDisposable
{
    private readonly IPlayerWindow _window;
    private readonly IPlayerRenderContextFactory _renderContextFactory;
    private readonly IPlayerInputContextFactory _inputContextFactory;
    private ISceneRuntimeFactory _sceneRuntimeFactory;
    private IRuntimeSceneManager _sceneManager;
    private readonly List<SceneRuntime> _runtimes = [];
    private Scene _scene = null!;
    private readonly bool _uiElementsEnabled;
    private readonly GraphicsBackend _preferredBackend;
    private readonly BuildTargetManifest _buildTarget;
    private PlayerAssetBundleBootstrap? _assetBundles;
    private PlayerStagedRuntimeCoordinator? _stagedRuntime;
    private PlayerHotUpdateSession? _hotUpdate;
    private readonly BCancellationSource _lifetime = new();
    private readonly PlayerSplashScreen? _startupSplash;
    private IPlayerRenderContext? _renderContext;
    private IPlayerInputContext? _inputContext;
    private bool _runtimeStarted;
    private bool _windowLoaded;
    private bool _isAotStage;
    private bool _transitioning;
    private BTask? _bootstrapOperation;
    private BTask? _transitionOperation;
    private ServiceProvider? _ownedServices;
    private IServiceProvider? _runtimeServices;
    private IDisposable? _runtimeServicesAttachment;
    private BTask? _disposeOperation;
    private Action? _windowReady;
    private bool _windowResourcesInitialized;
    private bool _startupSplashClosed;
    private bool _sceneManagerAttached;
    private bool _disposed;
    private bool _quitting;

    internal bool IsDisposalPending => _disposeOperation is { IsCompleted: false };

    public PlayerApplication(string projectPath) : this(CreateStandaloneServices(projectPath)) { }

    internal PlayerApplication(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        IServiceProvider services,
        ISceneRuntimeFactory sceneRuntimeFactory,
        IRuntimeSceneManager sceneManager,
        IPlayerWindowFactory windowFactory,
        IPlayerRenderContextFactory renderContextFactory,
        IPlayerInputContextFactory inputContextFactory,
        PlatformUtility platformService,
        PlayerAssetBundleBootstrap? assetBundles = null,
        PlayerHotUpdateSession? hotUpdate = null,
        bool initializeContent = true,
        string? startupScene = null,
        bool sceneAlreadyLoaded = false,
        bool isAotStage = false,
        PlayerStagedRuntimeCoordinator? stagedRuntime = null,
        PlayerBuiltInResourceProvider? packagedResources = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(services);
        _sceneRuntimeFactory = sceneRuntimeFactory ?? throw new ArgumentNullException(nameof(sceneRuntimeFactory));
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        ArgumentNullException.ThrowIfNull(windowFactory);
        _renderContextFactory = renderContextFactory ??
                                throw new ArgumentNullException(nameof(renderContextFactory));
        _inputContextFactory = inputContextFactory ??
                               throw new ArgumentNullException(nameof(inputContextFactory));
        ArgumentNullException.ThrowIfNull(platformService);
        _assetBundles = assetBundles;
        _stagedRuntime = stagedRuntime;
        _hotUpdate = hotUpdate;
        _isAotStage = isAotStage;
        ReplaceRuntimeServices(services);
        try
        {
            var fileSystem = services.GetRequiredService<FileUtility>();
            var pathService = services.GetRequiredService<PathUtility>();
            var applicationPaths = services.GetRequiredService<ApplicationPathUtility>();
            if (stagedRuntime is null)
                PlayerAssetEnvironment.Initialize(
                    workspace, packagedResources, fileSystem, pathService,
                    platformService, applicationPaths);
            platformService.CurrentDirectory = workspace.RootPath;
            _uiElementsEnabled = true;
            _preferredBackend = GraphicsBackendDefaults.Parse(projectSettings.GraphicsBackend);
            _buildTarget = BuildTargetManifestSerializer.LoadCurrent(
                baseDirectory: null, fileSystem, pathService, platformService);
            Application.companyName = projectSettings.CompanyName;
            Application.productName = projectSettings.ProductName;
            Application.isEditor = false;
            Application.isPlaying = true;
            GraphicsBackendSettings.PreferredBackend = _preferredBackend;
            Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
            try
            {
                if (initializeContent)
                {
                    if (assetBundles is not null)
                        PlayerMainThread.Complete(assetBundles.InitializeAsync());
                }
                if (hotUpdate is not null)
                    PlayerMainThread.Complete(hotUpdate.ActivateAsync(services));
                AttachSceneManager();
                var requestedScene = startupScene ?? workspace.Project.StartupScene;
                if (!sceneAlreadyLoaded)
                    _sceneManager.LoadScene(requestedScene, LoadSceneMode.Single);
                else
                    foreach (var loaded in _sceneManager.LoadedScenes.ToArray())
                        OnSceneLoaded(loaded, LoadSceneMode.Single);
                if (!_isAotStage)
                    PlayerStartupDiagnostics.Phase("06_SCENE_LOADED", $"scene={requestedScene}");
            }
            catch (Exception exception)
            {
                if (assetBundles is not null)
                    PlayerMainThread.Complete(assetBundles.RollbackAfterStartupFailureAsync(exception));
                throw;
            }

            _window = windowFactory.Create(CreateWindowOptions(workspace));
            AttachWindowEvents();
        }
        catch
        {
            DetachRuntimeServices();
            throw;
        }
    }

    internal PlayerApplication(PlayerStagedRuntimeCoordinator stagedRuntime)
        : this(
            stagedRuntime.Workspace,
            stagedRuntime.ProjectSettings,
            stagedRuntime.AotStage.Services,
            stagedRuntime.AotStage.SceneRuntimeFactory,
            stagedRuntime.AotStage.SceneManager,
            stagedRuntime.AotStage.Services.GetRequiredService<IPlayerWindowFactory>(),
            stagedRuntime.AotStage.Services.GetRequiredService<IPlayerRenderContextFactory>(),
            stagedRuntime.AotStage.Services.GetRequiredService<IPlayerInputContextFactory>(),
            stagedRuntime.AotStage.Services.GetRequiredService<PlatformUtility>(),
            stagedRuntime.UpdateBootstrap,
            hotUpdate: null,
            initializeContent: false,
            startupScene: stagedRuntime.AotStage.ScenePath,
            sceneAlreadyLoaded: true,
            isAotStage: true,
            stagedRuntime: stagedRuntime) { }

    internal PlayerApplication(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        Func<BCancellationToken, BValueTask<PlayerStagedRuntimeCoordinator>> initialize,
        PlayerSplashScreen? splash,
        IPlayerWindowFactory windowFactory,
        IPlayerRenderContextFactory renderContextFactory,
        IPlayerInputContextFactory inputContextFactory,
        PlatformUtility platformService,
        IServiceProvider startupServices)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(projectSettings);
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(windowFactory);
        _renderContextFactory = renderContextFactory ??
                                throw new ArgumentNullException(nameof(renderContextFactory));
        _inputContextFactory = inputContextFactory ??
                               throw new ArgumentNullException(nameof(inputContextFactory));
        ArgumentNullException.ThrowIfNull(platformService);
        _sceneRuntimeFactory = null!;
        _sceneManager = null!;
        _startupSplash = splash;
        _isAotStage = true;
        ReplaceRuntimeServices(startupServices);
        try
        {
            var fileSystem = startupServices.GetRequiredService<FileUtility>();
            var pathService = startupServices.GetRequiredService<PathUtility>();
            platformService.CurrentDirectory = workspace.RootPath;
            _uiElementsEnabled = true;
            _preferredBackend = GraphicsBackendDefaults.Parse(projectSettings.GraphicsBackend);
            _buildTarget = BuildTargetManifestSerializer.LoadCurrent(
                baseDirectory: null, fileSystem, pathService, platformService);
            Application.companyName = projectSettings.CompanyName;
            Application.productName = projectSettings.ProductName;
            Application.isEditor = false;
            Application.isPlaying = true;
            GraphicsBackendSettings.PreferredBackend = _preferredBackend;
            Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
            _window = windowFactory.Create(CreateWindowOptions(workspace));
            AttachWindowEvents();
            _bootstrapOperation = InitializePackagedAsync(initialize, splash);
        }
        catch
        {
            DetachRuntimeServices();
            throw;
        }
    }

    private PlayerApplication((ProjectWorkspace Workspace, ServiceProvider Services) startup)
        : this(
            startup.Workspace,
            startup.Services.GetRequiredService<ProjectSettingsData>(),
            startup.Services,
            startup.Services.GetRequiredService<ISceneRuntimeFactory>(),
            startup.Services.GetRequiredService<IRuntimeSceneManager>(),
            startup.Services.GetRequiredService<IPlayerWindowFactory>(),
            startup.Services.GetRequiredService<IPlayerRenderContextFactory>(),
            startup.Services.GetRequiredService<IPlayerInputContextFactory>(),
            startup.Services.GetRequiredService<PlatformUtility>(),
            startup.Services.GetService<PlayerAssetBundleBootstrap>(),
            startup.Services.GetService<PlayerHotUpdateSession>()) =>
        _ownedServices = startup.Services;

    private static (ProjectWorkspace Workspace, ServiceProvider Services) CreateStandaloneServices(
        string projectPath)
    {
        var services = new ServiceCollection().AddBEnginePlayer(projectPath);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        return (provider.GetRequiredService<ProjectWorkspace>(), provider);
    }

    public void Run() => _window.Run();

    internal void Run(Action windowReady)
    {
        _windowReady = windowReady ?? throw new ArgumentNullException(nameof(windowReady));
        _window.Run();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lifetime.Cancel();
            if (_sceneManager is not null) DetachSceneManager();
            StopAllRuntimesSafely("PLAYER_RUNTIME_STOP");
            try { _renderContext?.Dispose(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_RENDER_STOP", exception); }
            _renderContext = null;
            try { _inputContext?.Dispose(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_INPUT_STOP", exception); }
            _inputContext = null;
            DetachWindowEvents();
            try { _window.Dispose(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_WINDOW_STOP", exception); }
            _disposeOperation = CompleteDisposalAsync(_transitionOperation, _bootstrapOperation);
            GC.SuppressFinalize(this);
        }

        if (_disposeOperation is not { } operation) return;
        if (PlayerRuntimeShutdownDrain.TryComplete(
                operation,
                OnUpdate,
                nameof(PlayerApplication),
                "PLAYER_APPLICATION_SHUTDOWN_DRAIN"))
            _disposeOperation = null;
    }

    private async BTask CompleteDisposalAsync(BTask? transition, BTask? bootstrap)
    {
        await ObserveShutdownOperationAsync(transition, "GAME_TRANSITION_STOP");
        await ObserveShutdownOperationAsync(bootstrap, "PLAYER_BOOTSTRAP_STOP");
        if (_sceneManager is not null) DetachSceneManager();
        StopAllRuntimesSafely("PLAYER_RUNTIME_STOP");
        if (_stagedRuntime is null && _sceneManager is not null)
        {
            try { DisposeLoadedScenes(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_SCENE_STOP", exception); }
        }
        DetachRuntimeServices();
        if (_stagedRuntime is not null)
        {
            try { await _stagedRuntime.DisposeAsync(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("STAGED_RUNTIME_STOP", exception); }
        }
        else if (_hotUpdate is not null)
        {
            try { await _hotUpdate.DisposeAsync(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure("HOTUPDATE_RUNTIME_STOP", exception); }
        }
        _stagedRuntime = null;
        _hotUpdate = null;
        var ownedServices = _ownedServices;
        _ownedServices = null;
        try { ownedServices?.Dispose(); }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_SERVICES_STOP", exception); }
        try { _lifetime.Dispose(); }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure("PLAYER_LIFETIME_STOP", exception); }
    }

    private static async BValueTask ObserveShutdownOperationAsync(BTask? operation, string phase)
    {
        if (operation is null) return;
        try { await operation; }
        catch (BOperationCanceledException) { }
        catch (Exception exception) { PlayerStartupDiagnostics.Failure(phase, exception); }
    }

    private void OnLoad()
    {
        Time.Reset();
        _windowLoaded = true;
        if (_bootstrapOperation is null) InitializeWindowResources();
        TryStartRuntime();
    }

    private void InitializeWindowResources()
    {
        if (_windowResourcesInitialized) return;
        _renderContext = _renderContextFactory.Create(_window, _buildTarget, _preferredBackend);
        Debug.Log($"Active graphics API: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
        _inputContext = _inputContextFactory.Create(_window);
        _windowResourcesInitialized = true;
        var windowReady = _windowReady;
        _windowReady = null;
        windowReady?.Invoke();
    }

    private void OnUpdate(double deltaSeconds) =>
        OnUpdate(TimeSpan.FromSeconds(Math.Max(0, deltaSeconds)));

    private void OnUpdate(TimeSpan elapsed)
    {
        try
        {
            if (!_disposed) _startupSplash?.Pump();
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            PlayerMainThread.Tick(elapsed);
            if (_disposed) return;
            TryStartRuntime();
            if (!_runtimeStarted) return;
            if (_isAotStage) _stagedRuntime!.Flow.Pump();
            _inputContext?.ApplyCursorState();
            _inputContext?.Poll();
            if (!Application.isFocused && !Application.runInBackground) return;
            SceneRuntime.TickAll(_runtimes, Fix64.FromTimeSpan(elapsed));
            if (_isAotStage)
            {
                _stagedRuntime!.Flow.Pump();
                TryEnterGame();
            }
        }
        finally { Input.EndFrame(); }
    }

    private void OnRender(double _)
    {
        if (!_runtimeStarted || _renderContext is null)
        {
            return;
        }

        var size = _window.FramebufferSize;
        DisplayUtility.Current.ReportDisplay(Math.Max(1, size.Width), Math.Max(1, size.Height),
            _window.IsFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        _renderContext.Render(
            _sceneManager.LoadedScenes,
            _scene,
            size.Width,
            size.Height,
            _uiElementsEnabled);
    }

    private void OnApplicationQuitting()
    {
        if (_quitting || _disposed) return;
        _quitting = true;
        _window.Close();
    }

    private void OnClosing()
    {
        if (!_quitting)
        {
            _quitting = true;
            Application.Quit();
        }
        StopAllRuntimes();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        _scene = scene;
        if (_runtimes.Any(item => ReferenceEquals(item.Scene, scene))) return;
        var runtime = _sceneRuntimeFactory.Create(scene);
        _runtimes.Add(runtime);
        if (_runtimeStarted) runtime.Start();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        _runtimes.RemoveAll(item => ReferenceEquals(item.Scene, scene));
    }

    private void OnActiveSceneChanged(Scene? _, Scene? current)
    {
        if (current is not null) _scene = current;
    }

    private void StopAllRuntimes()
    {
        for (var index = _runtimes.Count - 1; index >= 0; index--) _runtimes[index].Stop();
        _runtimeStarted = false;
    }

    private async BTask InitializePackagedAsync(
        Func<BCancellationToken, BValueTask<PlayerStagedRuntimeCoordinator>> initialize,
        PlayerSplashScreen? splash)
    {
        if (splash is not null)
        {
            await splash.WaitForMinimumDurationAsync(_lifetime.Token);
            PlayerStartupDiagnostics.Phase("01_SPLASH_PLAYBACK_COMPLETED");
        }

        var staged = await initialize(_lifetime.Token);
        if (_disposed)
        {
            await staged.DisposeAsync();
            _lifetime.Token.ThrowIfCancellationRequested();
            return;
        }
        _stagedRuntime = staged;
        ReplaceRuntimeServices(staged.AotStage.Services);
        _assetBundles = staged.UpdateBootstrap;
        _sceneRuntimeFactory = staged.AotStage.SceneRuntimeFactory;
        _sceneManager = staged.AotStage.SceneManager;
        AttachSceneManager();
        foreach (var loaded in _sceneManager.LoadedScenes.ToArray())
            OnSceneLoaded(loaded, LoadSceneMode.Single);
    }

    private void TryStartRuntime()
    {
        if (!_windowLoaded || _runtimeStarted) return;
        if (_bootstrapOperation is not null)
        {
            if (!_bootstrapOperation.IsCompleted) return;
            PlayerMainThread.Complete(_bootstrapOperation);
            _bootstrapOperation = null;
        }
        if (!_windowResourcesInitialized) InitializeWindowResources();
        if (_sceneManager is null) return;

        _runtimeStarted = true;
        try
        {
            foreach (var runtime in _runtimes.ToArray()) runtime.Start();
            if (_isAotStage)
            {
                PlayerStartupDiagnostics.Phase("03_AOT_SCENE_STARTED",
                    $"graphics={SystemInfo.graphicsDeviceType};scene={_scene.path}");
                _stagedRuntime!.Flow.Start();
                CloseStartupSplash();
            }
            else
            {
                if (_assetBundles is not null)
                    PlayerMainThread.Complete(_assetBundles.CommitStartupAsync());
                PlayerStartupDiagnostics.Phase("07_GAME_STARTED",
                    $"graphics={SystemInfo.graphicsDeviceType};scene={_scene.path}");
            }
        }
        catch (Exception exception)
        {
            try { StopAllRuntimes(); }
            catch (Exception stopFailure) { PlayerStartupDiagnostics.Failure("GAME_STOP", stopFailure); }
            if (_assetBundles is not null)
                PlayerMainThread.Complete(_assetBundles.RollbackAfterStartupFailureAsync(exception));
            throw;
        }
    }

    private void CloseStartupSplash()
    {
        if (_startupSplashClosed || _startupSplash is null) return;
        _startupSplashClosed = true;
        _startupSplash.Close();
        PlayerStartupDiagnostics.Phase("01_SPLASH_CLOSED");
    }

    private void ReplaceRuntimeServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        DetachRuntimeServices();
        _runtimeServicesAttachment = services.AttachBEngineRuntimeServices();
        _runtimeServices = services;
    }

    private void DetachRuntimeServices()
    {
        var attachment = _runtimeServicesAttachment;
        _runtimeServicesAttachment = null;
        attachment?.Dispose();
        _runtimeServices = null;
    }

    private PlayerWindowOptions CreateWindowOptions(ProjectWorkspace workspace) => new(
        workspace.Project.Window.Title,
        workspace.Project.Window.Width,
        workspace.Project.Window.Height,
        workspace.Project.Window.VSync)
    {
        GraphicsBackends = GraphicsBackendSelector.GetCandidates(_buildTarget, _preferredBackend)
    };

    private void AttachWindowEvents()
    {
        Application.quitting += OnApplicationQuitting;
        _window.Loaded += OnLoad;
        _window.Updating += OnUpdate;
        _window.Rendering += OnRender;
        _window.Closing += OnClosing;
        _window.FocusChanged += Application.SetFocus;
    }

    private void DetachWindowEvents()
    {
        Application.quitting -= OnApplicationQuitting;
        _window.Loaded -= OnLoad;
        _window.Updating -= OnUpdate;
        _window.Rendering -= OnRender;
        _window.Closing -= OnClosing;
        _window.FocusChanged -= Application.SetFocus;
    }

    private void TryEnterGame()
    {
        if (_transitioning || !_stagedRuntime!.Flow.EnterGameRequested) return;
        _transitioning = true;
        _transitionOperation = TransitionToGameAsync();
    }

    private async BTask TransitionToGameAsync()
    {
        try { await TransitionToGameCoreAsync(); }
        finally
        {
            _transitioning = false;
            _transitionOperation = null;
        }
    }

    private async BValueTask TransitionToGameCoreAsync()
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
            PlayerStartupDiagnostics.Phase("06_SCENE_LOADED", $"scene={next.ScenePath}");
            PlayerStartupDiagnostics.Phase("07_GAME_STARTED",
                $"graphics={SystemInfo.graphicsDeviceType};scene={_scene.path};" +
                $"runInBackground={Application.runInBackground}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Player startup diagnostics failed after the game stage committed: {exception}");
        }
        try { await stagedRuntime.ReleaseCommittedAotStageAsync(committedAotStage!); }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("AOT_STAGE_RELEASE", exception);
        }
    }

    private PlayerStageSnapshot CaptureStageSnapshot() => new(
        _runtimeServices ?? throw new InvalidOperationException("The AOT runtime services are unavailable."),
        _sceneRuntimeFactory,
        _sceneManager,
        _runtimes.ToArray(),
        _scene,
        _runtimeStarted,
        _isAotStage);

    private void ActivateGameStage(PlayerApplicationStage next)
    {
        StopAllRuntimes();
        DetachSceneManager();
        _runtimes.Clear();
        ReplaceRuntimeServices(next.Services);
        _sceneRuntimeFactory = next.SceneRuntimeFactory;
        _sceneManager = next.SceneManager;
        AttachSceneManager();
        foreach (var loaded in _sceneManager.LoadedScenes.ToArray())
            OnSceneLoaded(loaded, LoadSceneMode.Single);
        foreach (var runtime in _runtimes.ToArray()) runtime.Start();
        _runtimeStarted = true;
        _isAotStage = false;
    }

    private async BValueTask RollbackPreparedGameStageAsync(
        PlayerStagedRuntimeCoordinator stagedRuntime,
        PlayerApplicationStage next,
        Exception failure)
    {
        StopAllRuntimesSafely("FAILED_GAME_STAGE_STOP");
        if (_sceneManager is not null) DetachSceneManager();
        _runtimes.Clear();
        DetachRuntimeServices();
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

    private void RestoreStageSnapshot(PlayerStageSnapshot previous)
    {
        ReplaceRuntimeServices(previous.Services);
        _sceneRuntimeFactory = previous.SceneRuntimeFactory;
        _sceneManager = previous.SceneManager;
        _scene = previous.Scene;
        _runtimes.AddRange(previous.Runtimes);
        _runtimeStarted = false;
        _isAotStage = previous.IsAotStage;
        if (_disposed) return;
        AttachSceneManager();
        if (!previous.RuntimeStarted) return;
        foreach (var runtime in _runtimes.ToArray()) runtime.Start();
        _runtimeStarted = true;
    }

    private void StopAllRuntimesSafely(string diagnosticPhase)
    {
        for (var index = _runtimes.Count - 1; index >= 0; index--)
        {
            try { _runtimes[index].Stop(); }
            catch (Exception exception) { PlayerStartupDiagnostics.Failure(diagnosticPhase, exception); }
        }
        _runtimeStarted = false;
    }

    private void AttachSceneManager()
    {
        if (_sceneManagerAttached) return;
        _sceneManager.SceneLoaded += OnSceneLoaded;
        _sceneManager.SceneUnloaded += OnSceneUnloaded;
        _sceneManager.ActiveSceneChanged += OnActiveSceneChanged;
        _sceneManagerAttached = true;
    }

    private void DetachSceneManager()
    {
        if (!_sceneManagerAttached) return;
        _sceneManager.SceneLoaded -= OnSceneLoaded;
        _sceneManager.SceneUnloaded -= OnSceneUnloaded;
        _sceneManager.ActiveSceneChanged -= OnActiveSceneChanged;
        _sceneManagerAttached = false;
    }

    private void DisposeLoadedScenes()
    {
        foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            _sceneManager.UnregisterScene(scene, disposeScene: true);
    }

    private sealed record PlayerStageSnapshot(
        IServiceProvider Services,
        ISceneRuntimeFactory SceneRuntimeFactory,
        IRuntimeSceneManager SceneManager,
        SceneRuntime[] Runtimes,
        Scene Scene,
        bool RuntimeStarted,
        bool IsAotStage);
}
