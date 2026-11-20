using BEngine.Rendering.Rhi;
#if BENGINE_DESKTOP_OPENGL
using Silk.NET.OpenGL;
#endif
using Silk.NET.Windowing;

namespace BEngine.Platform;

public sealed class SilkGraphicsSession : IDisposable
{
    private readonly IWindow _window;
    private readonly IGraphicsPresentationDevice? _presentation;
    private readonly IGraphicsFrameTimingSource? _frameTiming;
    private readonly IGraphicsCpuFrameTimingSource? _cpuFrameTiming;
#if BENGINE_DESKTOP_OPENGL
    private readonly GL? _gl;
#endif
    private DisplayResolutionRequest? _appliedResolution;
    private bool? _vsync;
    private int? _frameRate;
    private bool _externalFramePacing;
    private bool _disposed;
    public IGraphicsDevice Device { get; }
    /// <summary>Collect CPU timing of frame acquisition and presentation for diagnostic hosts.</summary>
    public bool ProbeTimings
    {
        get => _probeTimings;
        set { _probeTimings = value; if (_cpuFrameTiming is not null) _cpuFrameTiming.CollectCpuFrameTiming = value; }
    }
    private bool _probeTimings;
    public GraphicsCpuFrameTiming ProbeGraphicsTiming => _cpuFrameTiming?.CpuFrameTiming ?? default;
    public long ProbeBeginTicks { get; private set; }
    public long ProbePresentTicks { get; private set; }
    public long ProbeCompletedFrames { get; private set; }

    /// <summary>Use when the owner schedules one update and render per frame.</summary>
    public bool ExternalFramePacing
    {
        get => _externalFramePacing;
        set
        {
            if (_externalFramePacing == value) return;
            _externalFramePacing = value;
            _frameRate = null;
        }
    }

    public SilkGraphicsSession(IWindow window, GraphicsBackend backend)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        var factory = new GraphicsDeviceFactory();
#if BENGINE_DESKTOP_VULKAN
        if (backend == GraphicsBackend.Vulkan)
        {
            var native = window.Native?.Win32 ?? throw new PlatformNotSupportedException("The Vulkan desktop provider requires a Win32 window.");
            factory.RegisterProvider(new VulkanGraphicsDeviceProvider(native.Hwnd, native.HInstance,
                Math.Max(1, window.FramebufferSize.X), Math.Max(1, window.FramebufferSize.Y), window.VSync));
        }
        else
#endif
#if BENGINE_DESKTOP_OPENGL
        if (backend == GraphicsBackend.OpenGL)
        {
            _gl = GL.GetApi(window);
            factory.RegisterProvider(new OpenGlGraphicsDeviceProvider(_gl));
        }
        else
#endif
        throw new PlatformNotSupportedException($"No compiled desktop graphics provider for {backend}.");
        try { Device = factory.CreateDevice(backend); }
        catch { DisposeGl(); throw; }
        _presentation = Device as IGraphicsPresentationDevice;
        _frameTiming = Device as IGraphicsFrameTimingSource;
        _cpuFrameTiming = Device as IGraphicsCpuFrameTimingSource;
    }

    public GraphicsRect BeginFrame(int width, int height)
    {
        var probeStart = ProbeTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ProbePresentTicks = 0;
        MakeCurrent();
        var settings = RenderSettingsUtility.Current;
        var vsync = settings.vSyncCount > 0;
        if (_vsync != vsync)
        {
            if (_presentation is not null) _presentation.SetVSync(vsync);
            else _window.VSync = vsync;
            _vsync = vsync;
        }
        // A Player-owned loop paces both update and rendering together. Other
        // hosts can retain Silk's render-only limiter.
        var frameRate = settings.targetFrameRate;
        if (_frameRate != frameRate)
        {
            _window.FramesPerSecond = _externalFramePacing ? 0 : Math.Max(0, frameRate);
            _frameRate = frameRate;
        }
        var display = DisplayUtility.Current;
        if (display.PendingResolution is { } request && !ReferenceEquals(request, _appliedResolution))
        {
            _window.WindowState = request.Mode == FullScreenMode.Windowed ? WindowState.Normal : WindowState.Fullscreen;
            if (request.Mode == FullScreenMode.Windowed)
                _window.Size = new(request.Resolution.width, request.Resolution.height);
            _appliedResolution = request;
        }
        var size = _window.FramebufferSize;
        display.ReportDisplay(size.X, size.Y, _window.WindowState == WindowState.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        width = Math.Max(1, size.X);
        height = Math.Max(1, size.Y);
        _frameTiming?.BeginGpuFrameTiming();
        _presentation?.BeginFrame(width, height);
        if (ProbeTimings) ProbeBeginTicks = System.Diagnostics.Stopwatch.GetTimestamp()-probeStart;
        return new GraphicsRect(0, 0, width, height);
    }

    public void Present()
    {
        var probeStart = ProbeTimings ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        _frameTiming?.EndGpuFrameTiming();
        if (_presentation is not null) _presentation.Present();
        else _window.SwapBuffers();
        if (ProbeTimings)
        {
            ProbePresentTicks = System.Diagnostics.Stopwatch.GetTimestamp()-probeStart;
            ProbeCompletedFrames++;
        }
    }
    internal void BeginProbeFrame()
    {
        MakeCurrent();
        var size = _window.FramebufferSize;
        _frameTiming?.BeginGpuFrameTiming();
        _presentation?.BeginFrame(Math.Max(1, size.X), Math.Max(1, size.Y));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window.IsInitialized) MakeCurrent();
        try { Device.Dispose(); }
        finally { DisposeGl(); }
    }

    public void MakeCurrent()
    {
        if (_window.IsInitialized && _window.GLContext is { IsCurrent: false } context)
        {
            context.MakeCurrent();
            (Device as IGraphicsStateCache)?.InvalidateState();
        }
    }

    private void DisposeGl()
    {
#if BENGINE_DESKTOP_OPENGL
        _gl?.Dispose();
#endif
    }
}
