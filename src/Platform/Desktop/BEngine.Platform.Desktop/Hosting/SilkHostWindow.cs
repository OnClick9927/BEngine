using BEngine.Hosting;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Platform;
using Silk.NET.Windowing;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace BEngine.Platform;

public sealed class SilkHostWindow : IHostWindow
{
    private readonly IWindow _window;
    private bool _loaded;
    private bool _disposed;
    private ISceneRenderer? _preparedRenderer;
    private ExceptionDispatchInfo? _callbackFailure;

    internal SilkHostWindow(HostWindowOptions playerOptions, ISceneRendererFactory? rendererFactory = null)
    {
        ArgumentNullException.ThrowIfNull(playerOptions);
        var options = WindowOptions.Default;
        options.Title = playerOptions.Title;
        options.Size = new(playerOptions.Width, playerOptions.Height);
        options.VSync = playerOptions.VSync;
        options.UpdatesPerSecond = 0;
        options.FramesPerSecond = 0;
        (_window, Graphics) = SilkWindowFactory.CreateVerified(options, playerOptions.GraphicsBackends,
            session => _preparedRenderer = rendererFactory?.Create(session.Device));
        _window.Update += OnUpdating;
        _window.Render += OnRendering;
        _window.Closing += OnClosing;
        Graphics.ExternalFramePacing = true;
        RenderSettingsUtility.Current.vSyncCount = playerOptions.VSync ? 1 : 0;
        ApplyWindowIcon();
        if (OperatingSystem.IsWindows()) PlatformAccessibility.AttachWindow(_window.Native?.Win32?.Hwnd ?? IntPtr.Zero);
    }

    internal IWindow NativeWindow => _window;
    internal GraphicsBackend Backend => Graphics.Device.Backend;
    internal SilkGraphicsSession Graphics { get; }
    internal ISceneRenderer TakeRenderer(ISceneRendererFactory factory)
    {
        Graphics.MakeCurrent();
        var renderer = _preparedRenderer ?? factory.Create(Graphics.Device);
        _preparedRenderer = null;
        return renderer;
    }

    public event Action? Loaded;

    public event Action<double>? Updating;

    public event Action<double>? Rendering;

    public event Action? Closing;

    public event Action<bool>? FocusChanged
    {
        add => _window.FocusChanged += value;
        remove => _window.FocusChanged -= value;
    }

    public HostWindowSize Size => new(_window.Size.X, _window.Size.Y);
    public HostWindowSize FramebufferSize => new(_window.FramebufferSize.X, _window.FramebufferSize.Y);
    public bool IsFullscreen => _window.WindowState == WindowState.Fullscreen;
    public bool VSync => _window.VSync;

    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded)
        {
            _loaded = true;
            Loaded?.Invoke();
            if (!_window.IsClosing) _window.IsVisible = true;
        }
        // Keep the native handle alive until the presentation owner releases its renderer
        // and input callbacks. Silk's parameterless Run resets it before that cleanup.
        using var pacer = new DesktopFramePacer();
        var passSampleInterval = int.TryParse(Environment.GetEnvironmentVariable("BENGINE_GPU_PASS_PROBE_SAMPLE_INTERVAL"), out var requestedInterval)
            && requestedInterval > 0 ? requestedInterval : 0;
        var gpuProbe = Environment.GetEnvironmentVariable("BENGINE_GPU_FRAME_PROBE") == "1" || passSampleInterval > 0;
        using var gpuTimingLease = gpuProbe ? GraphicsFrameTimingService.AcquireFrameOnly() : null;
        var probe = Environment.GetEnvironmentVariable("BENGINE_FRAME_PROBE") == "1" || gpuProbe
            ? new DesktopFrameProbe(gpuProbe, passSampleInterval > 0, collectGraphicsPhases: true) : null;
        var passSampleFrame = 0;
        Graphics.ProbeTimings = probe is not null;
        try
        {
            _window.Run(() =>
            {
                var start = probe is null ? 0 : Stopwatch.GetTimestamp();
                var allocated = probe is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
                pacer.Wait(RenderSettingsUtility.Current.targetFrameRate);
                var waited = probe is null ? 0 : Stopwatch.GetTimestamp();
                if (probe?.CpuEnabled == true) probe.BeginCpuEvents(waited);
                _window.DoEvents();
                if (StopAfterCallbackFailure()) return;
                var events = probe is null ? 0 : Stopwatch.GetTimestamp();
                if (probe?.CpuEnabled == true) probe.EndCpuEvents(events);
                if (probe?.CpuEnabled == true) probe.BeginCpuUpdate(events);
                if (!_window.IsClosing) _window.DoUpdate();
                if (StopAfterCallbackFailure()) return;
                var updated = probe is null ? 0 : Stopwatch.GetTimestamp();
                if (probe?.CpuEnabled == true) probe.EndCpuUpdate(updated);
                if (_window.IsClosing) return;
                var completedFrames = probe is null ? 0 : Graphics.ProbeCompletedFrames;
                // Individual timestamp queries can serialize GPU work and disable
                // draw merging. Keep them opt-in and confined to sparse diagnostic
                // frames; ordinary and intervening frame submissions stay intact.
                using (passSampleInterval > 0 && ++passSampleFrame % passSampleInterval == 0
                    ? GraphicsFrameTimingService.Acquire() : null)
                    _window.DoRender();
                if (StopAfterCallbackFailure()) return;
                if (probe is not null && !_window.IsClosing && Graphics.ProbeCompletedFrames != completedFrames)
                {
                    probe.RecordGraphicsTiming(Graphics.ProbeGraphicsTiming);
                    probe.Record(start, waited, events, updated, Stopwatch.GetTimestamp(),
                    Graphics.ProbeBeginTicks, Graphics.ProbePresentTicks, GC.GetAllocatedBytesForCurrentThread()-allocated);
                    if (gpuProbe) probe.RecordGpu(GraphicsFrameTimingService.Latest);
                    if (passSampleInterval > 0) probe.RecordGpuPasses();
                }
            });
        }
        catch (Exception exception)
        {
            _callbackFailure ??= ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            try { if (!_window.IsClosing) _window.Close(); }
            catch (Exception exception) { _callbackFailure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        _callbackFailure?.Throw();
        probe?.Report();
    }
    public void Close()
    {
        _window.Close();
        _callbackFailure?.Throw();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Update -= OnUpdating;
        _window.Render -= OnRendering;
        _window.Closing -= OnClosing;
        Updating = null;
        Rendering = null;
        Closing = null;
        _callbackFailure = null;
        if (OperatingSystem.IsWindows()) PlatformAccessibility.AttachWindow(IntPtr.Zero);
        Graphics.MakeCurrent();
        try
        {
            try { _preparedRenderer?.Dispose(); }
            finally { Graphics.Dispose(); }
        }
        finally { _window.Dispose(); }
    }

    private void ApplyWindowIcon() => WindowIcon.Apply(_window);

    // Silk's DoUpdate/DoRender clear their render-loop guard only after the
    // event returns. Defer host callback failures until they have returned so
    // the native window can still be reset and disposed during shutdown.
    private void OnUpdating(double delta)
    {
        try { Updating?.Invoke(delta); }
        catch (Exception exception) { _callbackFailure ??= ExceptionDispatchInfo.Capture(exception); }
    }

    private void OnRendering(double delta)
    {
        try { Rendering?.Invoke(delta); }
        catch (Exception exception) { _callbackFailure ??= ExceptionDispatchInfo.Capture(exception); }
    }

    private void OnClosing()
    {
        try { Closing?.Invoke(); }
        catch (Exception exception) { _callbackFailure ??= ExceptionDispatchInfo.Capture(exception); }
    }

    private bool StopAfterCallbackFailure()
    {
        if (_callbackFailure is null) return false;
        if (!_window.IsClosing) _window.Close();
        return true;
    }
}

