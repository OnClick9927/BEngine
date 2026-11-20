using BEngine.Instrumentation;
using BEngine.Hosting;
using BEngine.DependencyInjection;
using BEngine.Build;
using BEngine.Serialization;
using BEngine.Player.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>Engine-owned Player composition and lifetime; deployment content is supplied through a neutral workflow.</summary>
public sealed class PlayerHost(IPlayerContentWorkflow content) : IPlayerHost
{
    private readonly IPlayerContentWorkflow _content = content ?? throw new ArgumentNullException(nameof(content));

    public const string PersistentDataPathEnvironmentVariable = "BENGINE_PLAYER_PERSISTENT_DATA_PATH";
    public const string NativeDiagnosticsEnvironmentVariable = "BENGINE_PLAYER_NATIVE_DIAGNOSTICS";

    /// <summary>
    /// Removes diagnostics-listener options before resolving the Player workspace. Optional
    /// diagnostics packages still read the original process command line when they are loaded.
    /// </summary>
    public string[] FilterHostArguments(IReadOnlyList<string> arguments) => BEnginePlayer.FilterHostArguments(arguments);

    public int Run(params string[] args) =>
        Run(new ServiceCollection(), args);

    public int Run(
        IServiceCollection services,
        params string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureServices(services);
        using var startupServices = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        services.ShareBEnginePlatformState(startupServices);
        var hostServices = services.ToArray();
        using var runtimeServicesAttachment = startupServices.AttachBEngineRuntimeServices();
        var validation = args.Length != 0 &&
                         args[0].Equals("--validate", StringComparison.OrdinalIgnoreCase);
        using var initializationSession = validation ? null : HostRuntimeLifetime.Acquire();
        var startupResolver = startupServices.GetRequiredService<IPlayerStartupResolver>();
        var nativeLibraryLoader = startupServices.GetRequiredService<INativeLibraryLoader>();
        var hostOutput = startupServices.GetRequiredService<IHostOutput>();
        var fileSystem = startupServices.GetRequiredService<FileUtility>();
        var pathService = startupServices.GetRequiredService<PathUtility>();
        var platformService = startupServices.GetRequiredService<PlatformUtility>();
        ServiceProvider? persistentServices = null;
        IDisposable? persistentServicesAttachment = null;
        var activeStartupServices = startupServices;
        IPlayerStartupLog? diagnostics = null;
        IDisposable? startupResources = null;
        var failurePhase = "BOOTSTRAP";
        try
        {
            var startup = startupResolver.Resolve(args);
            var persistentDataPath = startupResolver.PersistentDataPath;
            if (!string.IsNullOrWhiteSpace(persistentDataPath))
            {
                persistentDataPath = PlayerHostServices.ConfigurePersistentDataDirectory(
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
            startupResources = _content.ActivateStartupResources(startup, activeStartupServices);
            diagnostics = _content.StartDiagnostics(startup, persistentDataPath);
            hostOutput.WriteErrorLine($"BENGINE_PLAYER_LOG|{diagnostics.LogPath}");
            _content.Diagnostics.Phase("01_BOOTSTRAP_STARTED",
                $"project={startup.ProjectPath};build={(startup.Manifest as HostDeploymentManifest)?.BuildVersion ?? "development"}");
            nativeLibraryLoader.Initialize(startup.NativeLibraryDirectory,
                platformService.GetEnvironmentVariable("BENGINE_PLAYER_NATIVE_DIAGNOSTICS") == "1");
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
            var workspace = _content.OpenWorkspace(startup.ProjectPath, activeStartupServices);
            var projectSettings = RuntimeConfiguration.Apply(workspace);
            using var application = new PlayerWindowApplication(workspace,
                HostPresentationConfiguration.Create(workspace.Project, projectSettings, activeStartupServices), activeStartupServices,
                async cancellationToken =>
                {
                    RunCoreAotBootstrap(fileSystem, pathService, platformService);
                    return await _content.PrepareAsync(workspace, persistentDataPath,
                        hostServices, activeStartupServices, cancellationToken);
                }, initializationSession, ownsRuntimeLifetime: false, _content.Diagnostics);
            failurePhase = "GAME_LOOP";
            application.Run();
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                diagnostics ??= _content.StartFailureDiagnostics(
                    startupResolver.BaseDirectory);
                _content.Diagnostics.Failure(failurePhase, exception);
                hostOutput.WriteErrorLine($"BENGINE_PLAYER_LOG|{diagnostics.LogPath}");
            }
            catch { }
            hostOutput.WriteErrorLine(exception.ToString());
            return 1;
        }
        finally
        {
            List<Exception>? failures = null;
            Release(startupResources, ref failures);
            Release(diagnostics, ref failures);
            Release(persistentServicesAttachment, ref failures);
            Release(persistentServices, ref failures);
            if (failures is not null)
                throw new AggregateException("Player host shutdown failed.", failures);
        }
    }

    private static void Release(IDisposable? resource, ref List<Exception>? failures)
    {
        try { resource?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private void RunCoreAotBootstrap(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _content.Diagnostics.Phase("02_CORE_AOT_BOOTSTRAP_STARTED");
        var target = BuildTargetManifestSerializer.LoadCurrent(
            baseDirectory: null, fileSystem, pathService, platformService);
        RuntimeTypeCache.Warmup();
        _content.Diagnostics.Phase("02_CORE_AOT_BOOTSTRAP_COMPLETED",
            $"target={target.TargetId};runtime={target.ManagedCodeRuntime}");
    }

    public string ResolveDefaultProjectPath(string? playerDirectory = null) =>
        ResolveDefaultProjectPath(new ServiceCollection(), playerDirectory);

    public string ResolveDefaultProjectPath(
        IServiceCollection services,
        string? playerDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var resolveTiming = PlayerStartupPerformanceDiagnostics.Begin("resolve.total");
        using (PlayerStartupPerformanceDiagnostics.Begin("resolve.registration"))
        {
            ConfigureServices(services);
        }
        ServiceProvider provider;
        using (PlayerStartupPerformanceDiagnostics.Begin("resolve.provider"))
            provider = services.BuildServiceProvider();
        using var providerLifetime = provider;
        using var resolverTiming = PlayerStartupPerformanceDiagnostics.Begin("resolve.resolverAndMetadata");
        return provider.GetRequiredService<IPlayerStartupResolver>()
            .ResolveDefaultProjectPath(playerDirectory);
    }

    public PlayerRuntimeSession CreateRuntimeSession(
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null) =>
        CreateRuntimeSession(
            new ServiceCollection(), projectPath, declareAotInterpreter, persistentDataPath);

    public PlayerRuntimeSession CreateRuntimeSession(
        IServiceCollection services,
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var createTiming = PlayerStartupPerformanceDiagnostics.Begin("create.total");
        if (declareAotInterpreter) AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        using (PlayerStartupPerformanceDiagnostics.Begin("create.registration"))
        {
            ConfigureServices(services);
        }
        ServiceProvider platformServices;
        using (PlayerStartupPerformanceDiagnostics.Begin("create.provider"))
            platformServices = services.BuildServiceProvider();
        ServiceProvider? originalPlatformServices = null;
        HostRuntimeLifetime? initializationSession = null;
        try
        {
            using (PlayerStartupPerformanceDiagnostics.Begin("create.persistentPaths"))
            {
                persistentDataPath = PlayerHostServices.ConfigurePersistentDataDirectory(
                    services, persistentDataPath, platformServices);
                if (persistentDataPath is not null)
                {
                    originalPlatformServices = platformServices;
                    platformServices = services.BuildServiceProvider();
                }
            }
            // AOT and game stages must observe the same input, focus and display state
            // configured by the embedding host before asynchronous startup completes.
            using (PlayerStartupPerformanceDiagnostics.Begin("create.sharePlatformState"))
                services.ShareBEnginePlatformState(platformServices);
            using (PlayerStartupPerformanceDiagnostics.Begin("create.runtimeTypeCache"))
                RuntimeTypeCache.Warmup();
            using var constructionTiming = PlayerStartupPerformanceDiagnostics.Begin("create.sessionAndArguments");
            initializationSession = HostRuntimeLifetime.Acquire();
            var workspace = _content.OpenWorkspace(projectPath, platformServices);
            var hostServices = services.ToArray();
            return new PlayerRuntimeSession(workspace, platformServices,
                token => _content.PrepareAsync(workspace, persistentDataPath,
                    hostServices, platformServices, token), platformServices, originalPlatformServices,
                initializationSession, diagnostics: _content.Diagnostics);
        }
        catch
        {
            initializationSession?.Dispose();
            try { platformServices.Dispose(); }
            finally
            {
                if (!ReferenceEquals(platformServices, originalPlatformServices))
                    originalPlatformServices?.Dispose();
            }
            throw;
        }
    }

    public void Validate(
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null) =>
        Validate(
            new ServiceCollection(), projectPath, declareAotInterpreter, persistentDataPath);

    public void Validate(
        IServiceCollection services,
        string projectPath,
        bool declareAotInterpreter = false,
        string? persistentDataPath = null)
    {
        using var session = CreateRuntimeSession(
            services, projectPath, declareAotInterpreter, persistentDataPath);
        session.Start();
        try { ValidateSession(session, RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>(), TimeSpan.FromMinutes(5)); }
        finally { session.Stop(); }
    }

    private void ValidateSession(PlayerRuntimeSession session, PlatformUtility platform, TimeSpan timeout)
    {
        if (!session.IsStarted) throw new InvalidOperationException("The Player runtime session has not been started.");
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var started = platform.MonotonicTimestamp;
        var previous = TimeSpan.Zero;
        while (platform.GetElapsedTime(started) < timeout)
        {
            var now = platform.GetElapsedTime(started);
            session.Tick(now - previous);
            previous = now;
            if (!session.CanRender) continue;
            if (session.TransitionFlow is null) return;
            if (session.StartupControl is not { } control) continue;
            var state = control.Current;
            if (state.IsFailure)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(state.Error) ? state.Status : state.Error);
            if (state.CanAdvance) control.Advance();
        }
        throw new TimeoutException($"The Player runtime did not start within {timeout}.");
    }



    private void ConfigureServices(IServiceCollection services)
    {
        services.AddBEngineRenderingServices();
        services.AddBEnginePlayerRuntimeServices();
        _content.ConfigureServices(services);
    }

    IPlayerRuntimeSession IPlayerHost.CreateRuntimeSession(IServiceCollection services, string projectPath,
        bool declareAotInterpreter, string? persistentDataPath) =>
        CreateRuntimeSession(services, projectPath, declareAotInterpreter, persistentDataPath);
}

