using BEngine.Hosting;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>Owns scene execution and presentation for content prepared by any registered provider.</summary>
public sealed class PlayerRuntimeSession : IPlayerRuntimeSession
{
    private readonly HostRuntimeLifetime _initializationSession;
    private readonly bool _ownsInitializationSession;
    private readonly BCancellationSource _lifetime = new();
    private readonly PlatformUtility _platformService;
    private readonly IDisposable? _startupServices;
    private readonly IDisposable? _originalStartupServices;
    private IDisposable? _runtimeServicesAttachment;
    private IServiceProvider? _services;
    private readonly PlayerSceneRuntimeGroup _scenes = new();
    private IPlayerRuntimeContent? _contentLifecycle;
    private IPlayerRuntimeContent? _stagedRuntime;
    private BTask<IPlayerRuntimeContent>? _bootstrapOperation;
    private bool _started;
    private bool _isInitialStage;
    private bool _transitioning;
    private BTask? _transitionOperation;
    private BTask? _startupCommitOperation;
    private BTask? _disposeOperation;
    private readonly EmbeddedScenePresentation _presentation = new();
    private readonly WindowScenePresentation? _windowPresentation;
    private readonly Action<TimeSpan> _advanceWorkflow;
    private readonly PlayerRuntimeDiagnostics _diagnostics;
    private bool _disposed;

    /// <summary>Transfers explicitly supplied owners after successful construction. Services and workspace are borrowed.</summary>
    public PlayerRuntimeSession(RuntimeWorkspace workspace, IServiceProvider startupServices,
        Func<BCancellationToken, BValueTask<IPlayerRuntimeContent>> prepare,
        IDisposable? startupServiceOwner = null, IDisposable? originalStartupServiceOwner = null,
        HostRuntimeLifetime? runtimeLifetime = null, bool ownsRuntimeLifetime = true,
        PlayerRuntimeDiagnostics? diagnostics = null)
        : this(workspace, startupServices, prepare, startupServiceOwner, originalStartupServiceOwner,
            runtimeLifetime, ownsRuntimeLifetime, diagnostics, null) { }

    internal PlayerRuntimeSession(RuntimeWorkspace workspace, IServiceProvider startupServices,
        Func<BCancellationToken, BValueTask<IPlayerRuntimeContent>> prepare,
        IDisposable? startupServiceOwner, IDisposable? originalStartupServiceOwner,
        HostRuntimeLifetime? runtimeLifetime, bool ownsRuntimeLifetime,
        PlayerRuntimeDiagnostics? diagnostics, WindowScenePresentation? windowPresentation)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(startupServices);
        ArgumentNullException.ThrowIfNull(prepare);
        _advanceWorkflow = AdvanceWorkflow;
        _diagnostics = diagnostics ?? PlayerRuntimeDiagnostics.Default;
        _platformService = startupServices.GetRequiredService<PlatformUtility>();
        _startupServices = startupServiceOwner;
        _originalStartupServices = originalStartupServiceOwner;
        _windowPresentation = windowPresentation;
        Workspace = workspace;
        _initializationSession = runtimeLifetime ?? HostRuntimeLifetime.Acquire();
        _ownsInitializationSession = runtimeLifetime is null || ownsRuntimeLifetime;
        try
        {
            using var sessionTiming = PlayerStartupPerformanceDiagnostics.Begin("session.constructor");
            ReplaceRuntimeServices(startupServices);
            RuntimeConfiguration.Apply(workspace);
            Application.isEditor = false;
            Application.isPlaying = false;
            Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
            _bootstrapOperation = (windowPresentation is null
                ? _presentation.PrepareAsync(prepare, _lifetime.Token, static content => content.DisposeAsync())
                : windowPresentation.PrepareAsync(prepare, _lifetime.Token, static content => content.DisposeAsync())).AsBTask();
        }
        catch
        {
            DetachRuntimeServices();
            _lifetime.Dispose();
            if (runtimeLifetime is null) _initializationSession.Dispose();
            throw;
        }
    }

    public RuntimeWorkspace Workspace { get; }
    public IServiceProvider Services => _services ??
        throw new InvalidOperationException("The Player runtime session is still initializing.");
    public IReadOnlyList<Scene> LoadedScenes => _scenes.LoadedScenes;
    public Scene? ActiveScene => _scenes.ActiveScene;
    public bool IsStarted => _started;
    public bool CanRender => _scenes.IsRunning && ActiveScene is not null;
    public IPlayerStageTransitionFlow? TransitionFlow => _isInitialStage ? _stagedRuntime?.TransitionFlow : null;
    public IPlayerStartupControl? StartupControl => _stagedRuntime?.TransitionFlow?.StartupControl;
    public bool IsStartupPrepared => _stagedRuntime is not null;
    public bool IsDisposalPending => _disposeOperation is { IsCompleted: false };

    /// <summary>Attaches the presentation device owned by an embedded mobile or browser host.</summary>
    public void AttachGraphicsDevice(IGraphicsPresentationDevice device, bool ownsDevice = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        _presentation.Attach(device, ownsDevice);
    }

    /// <summary>Releases the surface through the engine presentation host.</summary>
    public void DetachGraphicsDevice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _presentation.Detach();
    }

    public SceneRenderStatistics LastRenderStatistics => _presentation.LastRenderStatistics;

    /// <summary>Delegates scene presentation to the engine-owned embedded host.</summary>
    public bool Render(int width, int height, bool drawUi = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("The Player runtime session has not been started.");
        HostSceneFrame? frame = _scenes.IsRunning && ActiveScene is { } scene
            ? new HostSceneFrame(LoadedScenes, scene, drawUi) : null;
        return _presentation.Render(_services, frame, width, height);
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
            if (_contentLifecycle is not null && _startupCommitOperation is null)
                PlayerExecution.Complete(_contentLifecycle.RollbackStartupAsync(exception));
            throw;
        }
    }

    public void Tick(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed && !IsDisposalPending, this);
        if (!_started && !_disposed)
            throw new InvalidOperationException("The Player runtime session has not been started.");
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        HostRuntimeFrame.Advance(elapsed, _advanceWorkflow);
    }

    /// <summary>Updates a session inside a window which already advances the scheduler and input.</summary>
    public void UpdateScenes(TimeSpan elapsed)
    {
        if (_disposed) return;
        if (!_started) throw new InvalidOperationException("Start the Player runtime session before updating it.");
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        AdvanceWorkflow(elapsed);
    }

    private void AdvanceWorkflow(TimeSpan elapsed)
    {
        if (_disposed) return;
        TryCompleteBootstrap();
        TryStartRuntimeStage();
        ObserveStartupCommit();
        if (_disposed || !_started) return;
        ObserveTransition();
        if (!_scenes.IsRunning) return;
        if (_isInitialStage) _stagedRuntime!.TransitionFlow!.Pump();
        if (!_scenes.Tick(elapsed)) return;
        if (_isInitialStage)
        {
            _stagedRuntime!.TransitionFlow!.Pump();
            TryEnterNextStage();
        }
        ObserveTransition();
    }

    public void SetFocused(bool focused) => ApplicationHostEvents.NotifyFocusChanged(focused);
    public void SetPaused(bool paused) => ApplicationHostEvents.NotifyPauseChanged(paused);
    public void RaiseLowMemory() => ApplicationHostEvents.NotifyLowMemory();

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
            _disposed = true;
            StopRuntimeStageSafely("PLAYER_RUNTIME_STOP");
            _started = false;
            Application.isPlaying = false;
            _lifetime.Cancel();
            _disposeOperation = CompleteDisposalAsync(_transitionOperation, _bootstrapOperation);
            GC.SuppressFinalize(this);
        }

        if (_disposeOperation is not { } operation) return;
        if (PlayerExecution.TryCompleteShutdown(
                operation,
                Tick,
                nameof(PlayerRuntimeSession),
                "PLAYER_RUNTIME_SESSION_SHUTDOWN_DRAIN"))
            _disposeOperation = null;
    }

    private async BTask CompleteDisposalAsync(
        BTask? transition,
        BTask<IPlayerRuntimeContent>? bootstrap)
    {
        await ObserveShutdownOperationAsync(transition, "STAGE_TRANSITION_STOP");
        await ObserveShutdownOperationAsync(_startupCommitOperation, "STARTUP_COMMIT_STOP");

        var stagedRuntime = _stagedRuntime;
        if (bootstrap is not null)
        {
            try { stagedRuntime ??= await bootstrap; }
            catch (BOperationCanceledException) { }
            catch (Exception exception) { _diagnostics.Failure("PLAYER_BOOTSTRAP_STOP", exception); }
        }

        StopRuntimeStageSafely("PLAYER_RUNTIME_STOP");
        if (_scenes.Manager is not null)
        {
            try { _scenes.Detach(); }
            catch (Exception exception) { _diagnostics.Failure("PLAYER_SCENE_STOP", exception); }
        }
        _scenes.Clear();
        try { _presentation.Dispose(); }
        catch (Exception exception) { _diagnostics.Failure("PLAYER_PRESENTATION_STOP", exception); }
        DetachRuntimeServices();

        if (stagedRuntime is not null)
        {
            try { await stagedRuntime.DisposeAsync(); }
            catch (Exception exception) { _diagnostics.Failure("STAGED_RUNTIME_STOP", exception); }
        }
        _stagedRuntime = null;
        _services = null;

        try { _lifetime.Dispose(); }
        catch (Exception exception) { _diagnostics.Failure("PLAYER_LIFETIME_STOP", exception); }
        try { _startupServices?.Dispose(); }
        catch (Exception exception) { _diagnostics.Failure("PLAYER_STARTUP_SERVICES_STOP", exception); }
        try { if (!ReferenceEquals(_startupServices, _originalStartupServices)) _originalStartupServices?.Dispose(); }
        catch (Exception exception) { _diagnostics.Failure("PLAYER_ORIGINAL_STARTUP_SERVICES_STOP", exception); }
        if (_ownsInitializationSession) _initializationSession.Dispose();
    }

    private async BValueTask ObserveShutdownOperationAsync(BTask? operation, string phase)
    {
        if (operation is null) return;
        try { await operation; }
        catch (BOperationCanceledException) { }
        catch (Exception exception) { _diagnostics.Failure(phase, exception); }
    }

    private void TryEnterNextStage()
    {
        if (_transitioning || !_stagedRuntime!.TransitionFlow!.TransitionRequested) return;
        _transitioning = true;
        _transitionOperation = TransitionToNextStageAsync();
    }

    private void TryCompleteBootstrap()
    {
        if (_bootstrapOperation is not { IsCompleted: true } operation) return;
        _bootstrapOperation = null;
        var staged = PlayerExecution.Complete(operation);
        if (_disposed)
        {
            _ = staged.DisposeAsync();
            return;
        }

        _stagedRuntime = staged;
        _contentLifecycle = staged;
        _services = staged.InitialStage.Services;
        ReplaceRuntimeServices(_services);
        _scenes.Bind(staged.InitialStage.SceneManager, staged.InitialStage.SceneRuntimeFactory);
        _scenes.TrackLoadedScenes();
        _isInitialStage = staged.TransitionFlow is not null;
    }

    private void TryStartRuntimeStage()
    {
        if (!_started || _scenes.IsRunning || _scenes.Manager is null) return;
        if (!(_windowPresentation?.CanStartScenes ?? _presentation.CanStartScenes)) return;
        _scenes.Start();
        try
        {
            if (_isInitialStage)
            {
                _diagnostics.Phase("INITIAL_STAGE_STARTED", "mode=runtime-session");
                _stagedRuntime!.TransitionFlow!.Start();
            }
            else
            {
                _startupCommitOperation = CommitInitialStageAsync();
                ObserveStartupCommit();
            }
        }
        catch
        {
            StopRuntimeStageSafely("PLAYER_STARTUP_STOP");
            throw;
        }
    }

    private void StopRuntimeStage()
    {
        if (_scenes.IsRunning) _scenes.Stop();
    }

    private async BTask CommitInitialStageAsync()
    {
        try
        {
            if (_contentLifecycle is not null) await _contentLifecycle.CommitStartupAsync(_lifetime.Token);
            _diagnostics.Phase("RUNTIME_STAGE_STARTED", "mode=runtime-session");
        }
        catch (Exception failure)
        {
            StopRuntimeStageSafely("PLAYER_STARTUP_STOP");
            _started = false;
            Application.isPlaying = false;
            if (_contentLifecycle is not null)
            {
                try { await _contentLifecycle.RollbackStartupAsync(failure); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("Initial content commit and rollback failed.", failure, rollbackFailure);
                }
            }
            throw;
        }
    }

    private void ObserveStartupCommit()
    {
        if (_startupCommitOperation is not { IsCompleted: true } operation) return;
        PlayerExecution.Complete(operation);
        _startupCommitOperation = null;
    }

    private void ObserveTransition()
    {
        if (_transitionOperation is not { IsCompleted: true } operation) return;
        _transitionOperation = null;
        PlayerExecution.Complete(operation);
    }

    private async BTask TransitionToNextStageAsync()
    {
        try
        {
            var stagedRuntime = _stagedRuntime!;
            PlayerSceneStage next;
            try
            {
                next = await stagedRuntime.PrepareTransitionAsync(_lifetime.Token);
                _lifetime.Token.ThrowIfCancellationRequested();
            }
            catch (BOperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _diagnostics.Failure("STAGE_TRANSITION", exception);
                if (!_disposed) stagedRuntime.TransitionFlow!.ReportTransitionFailure(exception);
                return;
            }

            var previous = CaptureStageSnapshot();
            PlayerSceneStage? committedPreviousStage = null;
            var transitionFailure = await PlayerExecution.RunTransitionAsync(
                () => ActivateNextStage(next),
                () => _contentLifecycle?.CommitStartupAsync(_lifetime.Token) ?? BValueTask.CompletedTask,
                () => committedPreviousStage = stagedRuntime.CommitTransition(next),
                failure => RollbackPreparedStageAsync(stagedRuntime, next, failure),
                () => RestoreStageSnapshot(previous));
            if (transitionFailure is not null)
            {
                if (transitionFailure is not BOperationCanceledException || !_lifetime.IsCancellationRequested)
                    _diagnostics.Failure("STAGE_TRANSITION", transitionFailure);
                if (!_disposed) stagedRuntime.TransitionFlow!.ReportTransitionFailure(transitionFailure);
                return;
            }

            try
            {
                _diagnostics.Phase("SCENE_LOADED",
                    $"scene={next.ScenePath};mode=runtime-session");
                _diagnostics.Phase("RUNTIME_STAGE_STARTED", "mode=runtime-session");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Player startup diagnostics failed after the runtime stage committed: {exception}");
            }
            try { await stagedRuntime.ReleasePreviousStageAsync(committedPreviousStage!); }
            catch (Exception exception) { _diagnostics.Failure("PREVIOUS_STAGE_RELEASE", exception); }
        }
        finally
        {
            _transitioning = false;
        }
    }

    private PlayerRuntimeStageSnapshot CaptureStageSnapshot() => new(
        _services ?? throw new InvalidOperationException("The initial runtime services are unavailable."),
        _scenes.Capture(),
        _isInitialStage);

    private void ActivateNextStage(PlayerSceneStage next)
    {
        StopRuntimeStage();
        _scenes.Clear();
        _services = next.Services;
        ReplaceRuntimeServices(_services);
        _scenes.Bind(next.SceneManager, next.SceneRuntimeFactory);
        _scenes.TrackLoadedScenes();
        _scenes.Start();
        _isInitialStage = false;
    }

    private async BValueTask RollbackPreparedStageAsync(
        IPlayerRuntimeContent stagedRuntime,
        PlayerSceneStage next,
        Exception failure)
    {
        StopRuntimeStageSafely("FAILED_GAME_STAGE_STOP");
        _scenes.Clear();
        DetachRuntimeServices();
        _services = null;
        List<Exception>? failures = null;
        if (_contentLifecycle is not null)
        {
            try { await _contentLifecycle.RollbackStartupAsync(failure, BCancellationToken.None); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { await stagedRuntime.AbortTransitionAsync(next); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 })
            throw new AggregateException("The failed runtime stage could not be completely rolled back.", failures);
    }

    private void RestoreStageSnapshot(PlayerRuntimeStageSnapshot previous)
    {
        _services = previous.Services;
        ReplaceRuntimeServices(_services);
        _scenes.Restore(previous.Scenes, subscribe: !_disposed);
        _isInitialStage = previous.IsInitialStage;
        if (!_disposed && previous.Scenes.WasRunning) _scenes.Start();
    }

    private void StopRuntimeStageSafely(string diagnosticPhase) =>
        _scenes.Stop(exception => _diagnostics.Failure(diagnosticPhase, exception));

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
        IServiceProvider Services,
        PlayerSceneRuntimeGroup.Snapshot Scenes,
        bool IsInitialStage);
}
