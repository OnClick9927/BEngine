using BEngine.Hosting;
using BEngine.Platform;
using BEngine.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

/// <summary>Runs prepared content in one engine window, including its startup presentation.</summary>
public sealed class PlayerWindowApplication : IDisposable
{
    private readonly WindowScenePresentation _presentation;
    private readonly PlayerRuntimeSession _session;
    private readonly bool _autoAdvanceStartup = Environment.GetEnvironmentVariable("BENGINE_PLAYER_AUTO_ADVANCE_STARTUP") == "1";
    private bool _disposed;

    public PlayerWindowApplication(RuntimeWorkspace workspace, HostPresentationOptions presentationOptions,
        IServiceProvider services, Func<BCancellationToken, BValueTask<IPlayerRuntimeContent>> prepare,
        HostRuntimeLifetime? runtimeLifetime = null, bool ownsRuntimeLifetime = true,
        PlayerRuntimeDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(presentationOptions);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(prepare);
        _presentation = new WindowScenePresentation(presentationOptions, services, static () => null);
        try
        {
            services.GetRequiredService<PlatformUtility>().CurrentDirectory = workspace.RootPath;
            _session = new PlayerRuntimeSession(workspace, services, prepare, null, null,
                runtimeLifetime, ownsRuntimeLifetime, diagnostics, _presentation);
            _presentation.SetFrameSource(CaptureFrame);
            _presentation.Loaded += _session.Start;
            _presentation.Updating += UpdateScenes;
            _presentation.Closing += _session.Stop;
            if (_presentation.IsLoaded) _session.Start();
        }
        catch
        {
            if (_session is not null)
            {
                DetachEvents();
                _session.Dispose();
            }
            _presentation.Dispose();
            throw;
        }
    }

    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _presentation.Run();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DetachEvents();
            _presentation.SetFrameSource(static () => null);
            try { _session.Dispose(); }
            finally { _presentation.Dispose(); }
        }
        else if (_session.IsDisposalPending) _session.Dispose();
    }

    private HostSceneFrame? CaptureFrame() => _session.IsStarted && _session.CanRender && _session.ActiveScene is { } scene
        ? new HostSceneFrame(_session.LoadedScenes, scene) : null;

    private void UpdateScenes(TimeSpan elapsed)
    {
        if (_autoAdvanceStartup && _session.StartupControl is { Current.CanAdvance: true } flow)
            flow.Advance();
        _session.UpdateScenes(elapsed);
    }

    private void DetachEvents()
    {
        _presentation.Loaded -= _session.Start;
        _presentation.Updating -= UpdateScenes;
        _presentation.Closing -= _session.Stop;
    }
}
