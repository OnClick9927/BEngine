using BEngine.Build;
using BEngine.Platform;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Hosting;

/// <summary>The scenes selected by a workflow for the engine to present.</summary>
public readonly record struct HostSceneFrame(IReadOnlyList<Scene> Scenes, Scene ActiveScene, bool DrawUi = true);

/// <summary>Owns a native window, input and presentation. Workflows supply scene state and update callbacks.</summary>
public sealed class WindowScenePresentation : IDisposable
{
    private readonly IHostWindow _window;
    private readonly IHostRenderContextFactory _renderFactory;
    private readonly IHostInputContextFactory _inputFactory;
    private readonly BuildTargetDescriptor _target;
    private readonly GraphicsBackend _backend;
    private Func<HostSceneFrame?> _frameSource;
    private readonly BTaskCompletionSource _firstStartupFrame = new();
    private readonly Action<IGraphicsDevice, GraphicsRect> _drawStartup;
    private IHostRenderContext? _render;
    private IHostInputContext? _input;
    private IHostStartupRenderer? _startupRenderer;
    private HostStartupProgress? _drawingProgress;
    private bool _initialized;
    private bool _quitting;
    private bool _disposed;

    public WindowScenePresentation(
        HostPresentationOptions options,
        IServiceProvider services,
        Func<HostSceneFrame?> frameSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _renderFactory = services.GetRequiredService<IHostRenderContextFactory>();
        _inputFactory = services.GetRequiredService<IHostInputContextFactory>();
        _backend = options.PreferredBackend;
        _target = options.Target;
        GraphicsBackendSettings.PreferredBackend = _backend;
        _window = services.GetRequiredService<IHostWindowFactory>().Create(options.Window with
        {
            GraphicsBackends = GraphicsBackendSelector.GetCandidates(_target, _backend)
        });
        _drawStartup = DrawStartup;
        Application.quitting += OnApplicationQuitting;
        _window.Loaded += OnLoaded;
        _window.Updating += OnUpdating;
        _window.Rendering += OnRendering;
        _window.Closing += OnClosing;
        _window.FocusChanged += ApplicationHostEvents.NotifyFocusChanged;
    }

    public event Action? Loaded;
    public event Action<TimeSpan>? Updating;
    public event Action? Closing;
    public bool IsLoaded => _initialized;
    public bool CanStartScenes => HostStartupSession.Current?.CanContinue != false;
    public void Run() => _window.Run();
    public void SetFrameSource(Func<HostSceneFrame?> frameSource) =>
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));

    /// <summary>Runs preparation while the engine owns the startup presentation and its completion gate.</summary>
    public async BValueTask<T> PrepareAsync<T>(
        Func<BCancellationToken, BValueTask<T>> prepare, BCancellationToken cancellationToken = default,
        Func<T, BValueTask>? releaseAfterFailure = null)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        var startup = HostStartupSession.Current;
        if (startup is { IsVisible: true } && HostStartupEnvironment.LogoPng.Length > 0)
        {
            while (!_firstStartupFrame.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await BTask.Delay(8, cancellationToken);
            }
            await _firstStartupFrame.Task;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = await prepare(cancellationToken);
        try
        {
            if (startup is not null)
            {
                startup.CompletePreparation();
                await startup.WaitUntilReadyAsync(cancellationToken);
            }
            return result;
        }
        catch
        {
            if (releaseAfterFailure is not null) await releaseAfterFailure(result);
            throw;
        }
    }

    /// <summary>Also used to drain queued work after a workflow requests shutdown.</summary>
    public void Advance(TimeSpan elapsed)
    {
        try
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            if (!_disposed) HostStartupSession.Current?.Pump();
            BTaskScheduler.Main.Tick(elapsed);
            if (_disposed) return;
            _input?.ApplyCursorState();
            _input?.Poll();
            Updating?.Invoke(elapsed);
        }
        finally { Input.EndFrame(); }
    }

    private void OnLoaded()
    {
        Time.Reset();
        _render = _renderFactory.Create(_window, _target, _backend);
        try { _input = _inputFactory.Create(_window); }
        catch
        {
            _render.Dispose();
            _render = null;
            throw;
        }
        _initialized = true;
        Debug.Log($"Active graphics API: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
        Loaded?.Invoke();
    }

    private void OnUpdating(double seconds) => Advance(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    private void OnRendering(double _)
    {
        if (_disposed || _render is null) return;
        var size = _window.FramebufferSize;
        if (_frameSource() is { } frame && CanStartScenes)
        {
            DisposeStartupRenderer();
            DisplayUtility.Current.ReportDisplay(Math.Max(1, size.Width), Math.Max(1, size.Height),
                _window.IsFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            _render.Render(frame.Scenes, frame.ActiveScene, size.Width, size.Height, frame.DrawUi);
            // Close only after the first scene frame has reached the presentation surface.
            if (HostStartupSession.Current is { CanContinue: true } startup) startup.Close();
            return;
        }
        if (HostStartupSession.Current is not { IsVisible: true } session ||
            HostStartupEnvironment.LogoPng.Length == 0) return;
        var progress = session.Progress;
        var displayedProgress = progress.Progress;
        _drawingProgress = progress;
        bool presented;
        try { presented = _render.RenderFrame(Math.Max(1, size.Width), Math.Max(1, size.Height), _drawStartup); }
        finally { _drawingProgress = null; }
        if (!presented) return;
        progress.ConfirmProgressPresented(displayedProgress);
        _firstStartupFrame.TrySetResult();
    }

    private void DrawStartup(IGraphicsDevice device, GraphicsRect viewport)
    {
        _startupRenderer ??= HostStartupRenderers.Create(device);
        // Texture/shader preparation must not consume the visible logo fade.
        if (!_firstStartupFrame.IsCompleted) _drawingProgress!.RestartPresentation();
        _startupRenderer.Render(viewport.Width, viewport.Height, _drawingProgress!);
    }

    private void DisposeStartupRenderer()
    {
        if (_startupRenderer is not { } renderer) return;
        _render?.MakeCurrent();
        _startupRenderer = null;
        renderer.Dispose();
    }

    private void OnApplicationQuitting()
    {
        if (_quitting || _disposed) return;
        _quitting = true;
        _window.Close();
    }

    private void OnClosing()
    {
        if (!_quitting) { _quitting = true; Application.Quit(); }
        Closing?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _firstStartupFrame.TrySetCanceled(default);
        Application.quitting -= OnApplicationQuitting;
        _window.Loaded -= OnLoaded;
        _window.Updating -= OnUpdating;
        _window.Rendering -= OnRendering;
        _window.Closing -= OnClosing;
        _window.FocusChanged -= ApplicationHostEvents.NotifyFocusChanged;
        List<Exception>? errors = null;
        try { DisposeStartupRenderer(); } catch (Exception error) { (errors ??= []).Add(error); }
        try { _render?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        _render = null;
        try { _input?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        _input = null;
        try { _window.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is { Count: > 0 }) throw new AggregateException("Presentation shutdown failed.", errors);
    }
}

/// <summary>Scene presentation for a surface owned by an external host.</summary>
public sealed class EmbeddedScenePresentation : IDisposable
{
    private IGraphicsPresentationDevice? _device;
    private ISceneRenderer? _renderer;
    private bool _ownsDevice;

    public SceneRenderStatistics LastRenderStatistics => _renderer?.LastRenderStatistics ?? default;
    public bool CanStartScenes => HostStartupSession.Current?.CanContinue != false;

    public async BValueTask<T> PrepareAsync<T>(
        Func<BCancellationToken, BValueTask<T>> prepare, BCancellationToken cancellationToken = default,
        Func<T, BValueTask>? releaseAfterFailure = null)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        var result = await prepare(cancellationToken);
        try
        {
            if (HostStartupSession.Current is { } startup)
                startup.CompletePreparation();
            // External surfaces acknowledge the completed startup frame after
            // preparation returns. Scene activation remains gated separately.
            return result;
        }
        catch
        {
            if (releaseAfterFailure is not null) await releaseAfterFailure(result);
            throw;
        }
    }

    public void Attach(IGraphicsPresentationDevice device, bool ownsDevice = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (_device is not null) throw new InvalidOperationException("A presentation device is already attached.");
        _device = device;
        _ownsDevice = ownsDevice;
    }

    public bool Render(IServiceProvider? services, HostSceneFrame? frame, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var device = _device ?? throw new InvalidOperationException("Attach a presentation device before rendering.");
        if (frame is not { } source || !CanStartScenes) return false;
        ArgumentNullException.ThrowIfNull(services);
        _renderer ??= services.GetRequiredService<ISceneRendererFactory>().Create(device);
        device.BeginFrame(width, height);
        _renderer.RenderScene(source.Scenes, source.ActiveScene, new GraphicsRect(0, 0, width, height), source.DrawUi);
        device.Present();
        if (HostStartupSession.Current is { CanContinue: true } startup) startup.Close();
        return true;
    }

    public void Detach()
    {
        try { _renderer?.Dispose(); }
        finally
        {
            _renderer = null;
            try { if (_ownsDevice) _device?.Dispose(); }
            finally { _device = null; _ownsDevice = false; }
        }
    }

    public void Dispose() => Detach();
}

/// <summary>Shared frame policy for externally driven scene runtimes.</summary>
public static class HostRuntimeFrame
{
    public static void Advance(TimeSpan elapsed, Action<TimeSpan> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        try
        {
            BTaskScheduler.Main.Tick(elapsed);
            update(elapsed);
        }
        finally { Input.EndFrame(); }
    }

    public static bool TickScenes(IReadOnlyList<SceneRuntime> runtimes, TimeSpan elapsed)
    {
        if (!Application.isFocused && !Application.runInBackground) return false;
        SceneRuntime.TickAll(runtimes, Fix64.FromTimeSpan(elapsed));
        return true;
    }
}
