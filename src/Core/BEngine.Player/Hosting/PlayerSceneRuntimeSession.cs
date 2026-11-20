using BEngine.DependencyInjection;
using BEngine.Hosting;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>
/// Runs scenes prepared by any content loader. The caller owns the service provider and content;
/// this session owns scene execution, startup gating and optional surface presentation.
/// Use Tick for an external surface, or UpdateScenes when WindowScenePresentation pumps the scheduler.
/// </summary>
public sealed class PlayerSceneRuntimeSession : IPlayerRuntimeSession
{
    private readonly PlayerSceneRuntimeGroup _scenes = new();
    private readonly EmbeddedScenePresentation _presentation = new();
    private readonly HostRuntimeLifetime _lifetime;
    private readonly IDisposable _serviceAttachment;
    private readonly Action<TimeSpan> _update;
    private bool _stopping;
    private bool _disposing;
    private bool _disposed;

    public PlayerSceneRuntimeSession(RuntimeWorkspace workspace, IServiceProvider services)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        var manager = services.GetRequiredService<IRuntimeSceneManager>();
        var factory = services.GetRequiredService<ISceneRuntimeFactory>();
        _update = AdvanceScenes;
        _serviceAttachment = RuntimeServices.Attach(services);
        try
        {
            _lifetime = HostRuntimeLifetime.Acquire();
            try
            {
                RuntimeConfiguration.Apply(workspace);
                Application.isEditor = false;
                Application.isPlaying = false;
                Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
                _scenes.Bind(manager, factory);
                _scenes.TrackLoadedScenes();
                HostStartupSession.Current?.CompletePreparation();
            }
            catch { _scenes.Clear(); _lifetime.Dispose(); throw; }
        }
        catch { _serviceAttachment.Dispose(); throw; }
    }

    public RuntimeWorkspace Workspace { get; }
    public IServiceProvider Services { get; }
    public IReadOnlyList<Scene> LoadedScenes => _scenes.LoadedScenes;
    public Scene? ActiveScene => _scenes.ActiveScene;
    public bool IsStarted { get; private set; }
    public bool CanRender => IsStarted && _scenes.IsRunning && ActiveScene is not null;
    public IPlayerStartupControl? StartupControl => null;
    public bool IsStartupPrepared => !_disposed;
    public bool IsDisposalPending => false;
    public SceneRenderStatistics LastRenderStatistics => _presentation.LastRenderStatistics;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping || _disposing) throw new InvalidOperationException("The Player runtime session is stopping.");
        if (IsStarted) return;
        Time.Reset();
        IsStarted = true;
        Application.isPlaying = true;
        TryStartScenes();
    }

    public void Tick(TimeSpan elapsed)
    {
        CheckUpdate(elapsed);
        HostRuntimeFrame.Advance(elapsed, _update);
    }

    /// <summary>Advances only scene execution; the native window already advances BTask and input.</summary>
    public void UpdateScenes(TimeSpan elapsed)
    {
        CheckUpdate(elapsed);
        AdvanceScenes(elapsed);
    }

    private void AdvanceScenes(TimeSpan elapsed)
    {
        // Scheduled work may stop or dispose this session before the frame callback runs.
        if (_disposed || !IsStarted) return;
        TryStartScenes();
        _scenes.Tick(elapsed);
    }

    private void CheckUpdate(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsStarted) throw new InvalidOperationException("Start the Player runtime session before updating it.");
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
    }

    private void TryStartScenes()
    {
        if (_scenes.IsRunning || !_presentation.CanStartScenes) return;
        try
        {
            _lifetime.InitializeThrough(RuntimeInitializeLoadType.BeforeSceneLoad);
            _scenes.Start();
            if (!_disposed) _lifetime.InitializeThrough(RuntimeInitializeLoadType.AfterSceneLoad);
        }
        catch { IsStarted = false; Application.isPlaying = false; throw; }
    }

    public void AttachGraphicsDevice(IGraphicsPresentationDevice device, bool ownsDevice = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _presentation.Attach(device, ownsDevice);
    }

    public void DetachGraphicsDevice() => _presentation.Detach();

    public bool Render(int width, int height, bool drawUi = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsStarted) throw new InvalidOperationException("Start the Player runtime session before rendering it.");
        HostSceneFrame? frame = CanRender ? new(LoadedScenes, ActiveScene!, drawUi) : null;
        return _presentation.Render(Services, frame, width, height);
    }

    public void SetFocused(bool focused) => ApplicationHostEvents.NotifyFocusChanged(focused);
    public void SetPaused(bool paused) => ApplicationHostEvents.NotifyPauseChanged(paused);
    public void RaiseLowMemory() => ApplicationHostEvents.NotifyLowMemory();

    public void Stop()
    {
        if (_disposed || !IsStarted) return;
        IsStarted = false;
        Application.isPlaying = false;
        List<Exception>? errors = null;
        _stopping = true;
        try { _scenes.Stop(exception => (errors ??= []).Add(exception)); }
        finally { _stopping = false; }
        if (errors is not null) throw new AggregateException("Failed to stop Player scenes.", errors);
    }

    public void Dispose()
    {
        if (_disposed || _disposing) return;
        _disposing = true;
        List<Exception>? errors = null;
        try { Stop(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        _disposed = true;
        var manager = _scenes.Manager;
        var scenes = LoadedScenes.ToArray();
        _scenes.Clear();
        foreach (var scene in scenes)
        {
            try { manager!.UnregisterScene(scene, disposeScene: true); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        }
        try { _presentation.Dispose(); } catch (Exception exception) { (errors ??= []).Add(exception); }
        try { _serviceAttachment.Dispose(); } finally { _lifetime.Dispose(); }
        if (errors is not null) throw new AggregateException("Failed to dispose the Player runtime session.", errors);
    }
}
