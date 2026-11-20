using BEngine.Player;
using BEngine.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using BEngine.Platform.IosNative;
using BEngine.Rendering.Rhi;
using Foundation;
using UIKit;

namespace BEngine.Player.Hosts.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    private readonly Stopwatch _clock = new();
    private IPlayerRuntimeSession? _session;
    private IGraphicsPresentationDevice? _graphics;
    private NSTimer? _timer;
    private IOSNativeWindow? _native;
    private TimeSpan _lastFrame;
    private bool _renderLogged;
    private IOSStartupView? _startupView;
    private HostStartupSession? _engineStartup;

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        try
        {
            StartRuntime();
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_STARTUP_FAILED|" + exception);
            DisposeRuntime();
            Window?.Dispose();
            Window = null;
            return false;
        }
    }

    private void StartRuntime()
    {
        IOSNativeWindow.InitializePlatform();
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        _native = new IOSNativeWindow();
        Window = _native.Window;

        _engineStartup = HostStartupSession.Begin();
        _startupView = new IOSStartupView(_native.View.Bounds);
        _native.View.AddSubview(_startupView);
        _startupView.AnimateLogo();
        // Commit the Core Animation fade before preparing the environment.
        // The compositor keeps animating while the main-thread runtime initializes.
        CoreAnimation.CATransaction.Flush();
        BeginInvokeOnMainThread(() =>
        {
            try { InitializeRuntime(); }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BENGINE_IOS_STARTUP_FAILED|" + exception);
                DisposeRuntime();
            }
        });
    }

    private void InitializeRuntime()
    {
        if (_native is null || Window is null) return;
        var payload = Path.Combine(NSBundle.MainBundle.ResourcePath!, "BEnginePayload");
        var projectPath = BEnginePlayer.ResolveDefaultProjectPath(payload);
        _session = BEnginePlayer.CreateRuntimeSession(
            projectPath, declareAotInterpreter: true,
            persistentDataPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEnginePlayer"));
        _graphics = _native.CreateGraphicsDevice();
        Console.WriteLine($"BENGINE_IOS_GRAPHICS_OK|backend={_graphics.Backend}|device={_graphics.Capabilities.DeviceName}");
        _session.AttachGraphicsDevice(_graphics, ownsDevice: false);
        _session.Start();
        _clock.Start();
        _lastFrame = _clock.Elapsed;
        _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromSeconds(1d / 60d), _ => Tick());
        Console.WriteLine($"BENGINE_IOS_HOST_READY|graphics=Metal|project={projectPath}");
    }

    public override void OnActivated(UIApplication application)
    {
        IOSAudioSession.Activate();
        _session?.SetPaused(false);
        _session?.SetFocused(true);
        _lastFrame = _clock.Elapsed;
        if (_session is not null) Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=active");
    }

    public override void OnResignActivation(UIApplication application)
    {
        _session?.SetFocused(false);
        _session?.SetPaused(true);
        IOSAudioSession.Deactivate();
        if (_session is not null) Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=inactive");
    }

    public override void WillEnterForeground(UIApplication application)
    {
        if (_session is not null) Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=foreground");
    }

    public override void DidEnterBackground(UIApplication application)
    {
        if (_session is not null) Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=background");
    }

    public override void ReceiveMemoryWarning(UIApplication application)
    {
        _session?.RaiseLowMemory();
        if (_session is not null) Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=low-memory");
    }

    public override void WillTerminate(UIApplication application)
    {
        Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=terminating");
        DisposeRuntime();
    }

    private void Tick()
    {
        if (_session is null || _native is null) return;
        try
        {
            _native.PollInput();
            var now = _clock.Elapsed;
            _session.Tick(now - _lastFrame);
            _native.SyncInput();
            _lastFrame = now;
            var (width, height) = _native.ReportDisplay();
            if (_session.Render(width, height) && !_renderLogged)
            {
                _renderLogged = true;
                _startupView?.RemoveFromSuperview();
                _startupView?.Dispose();
                _startupView = null;
                Console.WriteLine($"BENGINE_IOS_RENDER_OK|backend={_graphics?.Backend}|size={width}x{height}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_FRAME_FAILED|" + exception);
            _timer?.Invalidate();
        }
    }

    private void DisposeRuntime()
    {
        _startupView?.RemoveFromSuperview();
        _startupView?.Dispose();
        _startupView = null;
        _timer?.Invalidate();
        _timer?.Dispose();
        _timer = null;
        _session?.Dispose();
        _session = null;
        _engineStartup?.Dispose();
        _engineStartup = null;
        _graphics?.Dispose();
        _graphics = null;
        _native?.Dispose();
        _native = null;
        Window = null;
    }
}
