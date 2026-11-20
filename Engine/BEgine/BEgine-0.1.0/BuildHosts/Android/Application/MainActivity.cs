using BEngine.Player;
using BEngine.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Android.App;
using Android.Content.PM;
using Android.Opengl;
using Android.OS;
using Android.Util;
using Android.Views;
using BEngine.Platform;
using BEngine.Platform.AndroidNative;
using Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;
using Microsoft.Extensions.DependencyInjection;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BEngine.Player.Hosts.Android;

[Activity(
    Label = "BEngine",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.FullSensor,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity, GLSurfaceView.IRenderer
{
    private const string LogTag = "BEngine";
    private readonly Stopwatch _clock = new();
    private AndroidInputAdapter? _input;
    private AndroidSurfacePresentation? _presentation;
    private IPlayerRuntimeSession? _session;
    private AndroidGlGraphicsDevice? _graphics;
    private AccessibleGameSurface? _surfaceView;
    private AndroidAccessibilityAdapter? _accessibility;
    private TimeSpan _lastFrame;
    private TimeSpan _nextFrame;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private volatile bool _resumed;
    private volatile bool _frameFailed;
    private bool _renderLogged;
    private int _textSyncQueued;
    private bool _autoStart;
    private bool _validateGraphics;
    private bool _postProcessingValidated;
    private bool _profileFrames;
    private readonly AndroidFrameMetrics _frameMetrics = new();
    private string? _startupPhase;
    private AndroidTextInput? _textInput;
    private AndroidStartupView? _startupView;
    private HostStartupSession? _engineStartup;
    private AndroidRuntimeThread? _runtimeThread;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        BEngine.Platform.AndroidPlatform.Initialize();
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        base.OnCreate(savedInstanceState);
        PlatformVideoDecoding.OpenVideo = (source, bytes, hardware) => new AndroidVideoDecoder(source, bytes, hardware);
        PlatformVideoDecoding.OpenAudio = (source, bytes) => new AndroidVideoAudioDecoder(source, bytes);
        if (Intent?.GetStringExtra(HostLaunchEnvironment.ArgumentName) is { Length: > 0 } environment)
            HostLaunchEnvironment.Apply(environment, FilesDir!.AbsolutePath);
        _autoStart = Intent?.GetBooleanExtra("bengineAutoStart", false) == true;
        _validateGraphics = Intent?.GetBooleanExtra("bengineValidateGraphics", false) == true;
        _profileFrames = Intent?.GetBooleanExtra("bengineProfileFrames", false) == true;
        Window?.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
        _accessibility = new AndroidAccessibilityAdapter();
        _surfaceView = new AccessibleGameSurface(this, _accessibility) { Focusable = true, FocusableInTouchMode = true };
        var surface = _surfaceView;
        _runtimeThread = new AndroidRuntimeThread(
            action => surface.QueueEvent(new Java.Lang.Runnable(action)),
            StopRuntimeOnRenderThread);
        surface.BeforeRenderThreadExit = StopRuntimeAndWait;
        _accessibility.AttachSurface(_surfaceView);
        PlatformAccessibility.adapter = _accessibility;
        _surfaceView.SetEGLContextClientVersion(3);
        _surfaceView.SetEGLConfigChooser(8, 8, 8, 8, 24, 8);
        _surfaceView.PreserveEGLContextOnPause = true;
        _surfaceView.SetRenderer(this);
        _surfaceView.RenderMode = Rendermode.Continuously;
        _input = new AndroidInputAdapter(_surfaceView);
        _presentation = new AndroidSurfacePresentation(this, _surfaceView);
        _surfaceView.LayoutChange += (_, _) => _presentation?.UpdateSurfaceSize();
        var content = new global::Android.Widget.FrameLayout(this);
        content.AddView(_surfaceView, new global::Android.Widget.FrameLayout.LayoutParams(-1, -1));
        _textInput = new AndroidTextInput(this, content, _surfaceView);
        _engineStartup = HostStartupSession.Begin(allowRuntimeThreadHandoff: true);
        _startupView = new AndroidStartupView(this);
        content.AddView(_startupView, new global::Android.Widget.FrameLayout.LayoutParams(-1, -1));
        SetContentView(content);
        _surfaceView.RequestFocus();

    }

    private void InitializeRuntime()
    {
        var dataDirectories = (Assets!.List("BEnginePayload") ?? [])
            .Where(name => name.EndsWith("_data", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (dataDirectories.Length != 1)
            throw new InvalidDataException("The Android payload must contain exactly one Player data directory.");
        // Each packaged game gets its own extraction root, including when an application ID is reused.
        var root = Path.Combine(FilesDir!.AbsolutePath, "BEnginePlayer", dataDirectories[0]);
        AndroidAssetFiles.ExtractDirectory(Assets, "BEnginePayload", root,
            path => path.EndsWith("/StreamingAssets", StringComparison.Ordinal));
        var projectPath = BEnginePlayer.ResolveDefaultProjectPath(root);
        var persistentRoot = Path.Combine(FilesDir.AbsolutePath, "BEnginePersistentData");
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(AndroidStreamingAssets.Mount(
            Assets, ApplicationInfo!.SourceDir!, $"BEnginePayload/{dataDirectories[0]}/StreamingAssets/",
            Path.Combine(root, dataDirectories[0], "StreamingAssets")));
        services.AddSingleton<BEngine.Rendering.ITextRasterizer, AndroidTextRasterizer>();
        _session = BEnginePlayer.CreateRuntimeSession(
            services, projectPath, declareAotInterpreter: true,
            persistentDataPath: persistentRoot);
        _session.Start();
        _clock.Start();
        Log.Info(LogTag, $"BENGINE_ANDROID_HOST_READY|project={projectPath}");
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_runtimeThread?.IsStopping == true) return;
        _resumed = true;
        BEngine.Platform.AndroidPlatform.ResumeAudioOutputs();
        QueueRuntimeLifecycle(true);
        _surfaceView?.OnResume();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) _presentation?.RestoreImmersiveMode();
        if (hasFocus) _textInput?.RefreshSafeArea();
    }

    protected override void OnPause()
    {
        _resumed = false;
        // User callbacks and the scene transition scheduler belong to the GL
        // runtime thread. Calling them on the Activity thread can race a scene
        // load while GLSurfaceView waits for that same runtime frame to finish.
        QueueRuntimeLifecycle(false);
        if (IsFinishing) StopRuntimeAndWait();
        _surfaceView?.OnPause();
        BEngine.Platform.AndroidPlatform.SuspendAudioOutputs();
        base.OnPause();
    }

    private void QueueRuntimeLifecycle(bool resumed)
    {
        if (_runtimeThread?.IsStopping == true) return;
        _surfaceView?.QueueEvent(new Java.Lang.Runnable(() =>
        {
            if (_runtimeThread?.IsStopping == true) return;
            if (resumed)
            {
                _lastFrame = _clock.Elapsed;
                _nextFrame = _lastFrame;
                _session?.SetPaused(false);
                _session?.SetFocused(true);
            }
            else
            {
                _session?.SetFocused(false);
                _session?.SetPaused(true);
            }
            Log.Info(LogTag, $"BENGINE_ANDROID_LIFECYCLE|resumed={resumed}");
        }));
    }

    public override void OnLowMemory()
    {
        if (_runtimeThread?.IsStopping != true)
            _surfaceView?.QueueEvent(new Java.Lang.Runnable(() => _session?.RaiseLowMemory()));
        base.OnLowMemory();
    }

    public override void OnTrimMemory(global::Android.Content.TrimMemory level)
    {
        // Android sends this before the process is under hard pressure. Let
        // the runtime release unused assets on its GL thread instead of
        // waiting for the OS to kill the process while a frame is rendering.
        if (level >= global::Android.Content.TrimMemory.RunningLow && _runtimeThread?.IsStopping != true)
            _surfaceView?.QueueEvent(new Java.Lang.Runnable(() => _session?.RaiseLowMemory()));
        base.OnTrimMemory(level);
    }

    protected override void OnDestroy()
    {
        _resumed = false;
        StopRuntimeAndWait();
        _input?.Dispose();
        _input = null;
        _presentation = null;
        // The GL owner has now released the runtime's initialization scope.
        // The UI-held startup scope can be released without ending a live run.
        _engineStartup?.Dispose();
        _engineStartup = null;
        if (ReferenceEquals(PlatformAccessibility.adapter, _accessibility)) PlatformAccessibility.adapter = null;
        _accessibility = null;
        _startupView?.Dispose();
        _startupView = null;
        _surfaceView?.Dispose();
        _surfaceView = null;
        base.OnDestroy();
    }

    private void StopRuntimeAndWait()
    {
        try { _runtimeThread?.StopAndWait(); }
        catch (Exception error) { Log.Error(LogTag, "BENGINE_ANDROID_SHUTDOWN_FAILED|" + error); }
    }

    private void StopRuntimeOnRenderThread()
    {
        try
        {
            if (_session is { } session)
            {
                session.Dispose();
                while (session.IsDisposalPending)
                {
                    // I/O cancellation may finish between scheduler updates.
                    // Keep its continuations on this owner until teardown ends.
                    Thread.Sleep(1);
                    if (session.IsDisposalPending) session.Tick(TimeSpan.Zero);
                }
                session.Dispose();
            }
        }
        finally
        {
            _session = null;
            try { _graphics?.Dispose(); }
            finally { _graphics = null; }
        }
    }

    public void OnSurfaceCreated(IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        _runtimeThread!.BindCurrentThread();
        if (_runtimeThread.IsStopping) return;
        try
        {
            DetachGraphicsDevice();
            _graphics = new AndroidGlGraphicsDevice();
            if (_validateGraphics) AndroidGraphicsRegression.Run(_graphics);
            if (_session is null) InitializeRuntime();
            _session?.AttachGraphicsDevice(_graphics, ownsDevice: false);
            _lastFrame = _clock.Elapsed;
            Log.Info(LogTag,
                $"BENGINE_ANDROID_GRAPHICS_OK|backend={_graphics.Backend}|device={_graphics.Capabilities.DeviceName}");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, "BENGINE_ANDROID_GRAPHICS_FAILED|" + exception);
            _frameFailed = true;
            _runtimeThread.StopOnOwner();
            ReportFrameFailure(exception);
        }
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _surfaceWidth = Math.Max(1, width);
        _surfaceHeight = Math.Max(1, height);
        _input?.SetSurfaceSize(_surfaceWidth, _surfaceHeight);
        _presentation?.ApplyFrameRate(force: true);
        DisplayUtility.Current.ReportDisplay(_surfaceWidth, _surfaceHeight, FullScreenMode.FullScreenWindow);
        _textInput?.RefreshSafeArea();
        var safe = DisplayUtility.Current.safeArea;
        Log.Info(LogTag, $"BENGINE_ANDROID_SURFACE_READY|size={_surfaceWidth}x{_surfaceHeight}|safe={safe.x},{safe.y},{safe.width},{safe.height}");
    }

    public void OnDrawFrame(IGL10? gl)
    {
        if (!_resumed || _frameFailed || _runtimeThread?.IsStopping == true) return;
        // Always clear the surface, including the short interval before the
        // Player AOT scene has submitted its first frame. This prevents a
        // device-dependent black flash while content is still being loaded.
        GLES20.GlClearColor(.025f, .045f, .07f, 1f);
        GLES20.GlDisable(0x0C11);
        GLES20.GlClear(0x00004000);
        try
        {
            // GLSurfaceView otherwise runs at 90/120 Hz even when the game asks
            // for 60. Sleep instead of spinning so the cap also reduces heat.
            var remaining = _nextFrame - _clock.Elapsed;
            if (remaining.TotalMilliseconds >= 1) Thread.Sleep((int)remaining.TotalMilliseconds);
            var now = _clock.Elapsed;
            var requestedFrameRate = RenderSettingsUtility.Current.targetFrameRate;
            var frameInterval = TimeSpan.FromSeconds(1d / Math.Clamp(requestedFrameRate > 0 ? requestedFrameRate : 60, 1, 240));
            // Keep the deadline anchored: rounding every 16.67 ms sleep up to
            // 17 ms would cap a nominal 60 Hz loop below 60 and miss VSyncs.
            _nextFrame = _nextFrame + frameInterval > now ? _nextFrame + frameInterval : now + frameInterval;
            _presentation?.ApplyFrameRate();
            _input?.ApplyFrameInput();
            var tickStarted = _profileFrames ? Stopwatch.GetTimestamp() : 0;
            _session?.Tick(now - _lastFrame);
            var tickEnded = _profileFrames ? Stopwatch.GetTimestamp() : 0;
            if (_textInput is { NeedsSync: true } && Interlocked.Exchange(ref _textSyncQueued, 1) == 0)
                RunOnUiThread(() => { try { _textInput?.Sync(); } finally { Volatile.Write(ref _textSyncQueued, 0); } });
            DriveAutomaticStartup();
            _lastFrame = now;
            if (_surfaceWidth > 0 && _surfaceHeight > 0)
            {
                var rendered = _session?.Render(_surfaceWidth, _surfaceHeight) == true;
                if (_profileFrames && rendered)
                    _frameMetrics.Record(tickStarted, tickEnded, Stopwatch.GetTimestamp(), _graphics?.DrawStatistics ?? default, _session!.LastRenderStatistics);
                if (_validateGraphics && !_postProcessingValidated && rendered && _graphics is not null)
                    _postProcessingValidated = AndroidGraphicsRegression.TryRunPostProcessing(_graphics);
                if (rendered && !_renderLogged)
                {
                    _renderLogged = true;
                    RunOnUiThread(() =>
                    {
                        if (_startupView?.Parent is ViewGroup parent) parent.RemoveView(_startupView);
                        _startupView?.Dispose();
                        _startupView = null;
                    });
                    Log.Info(LogTag,
                        $"BENGINE_ANDROID_RENDER_OK|backend={_graphics?.Backend}|size={_surfaceWidth}x{_surfaceHeight}");
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, "BENGINE_ANDROID_FRAME_FAILED|" + exception);
            _frameFailed = true;
            _runtimeThread?.StopOnOwner();
            ReportFrameFailure(exception);
            return;
        }
    }

    private void ReportFrameFailure(Exception exception)
    {
        var report = DateTimeOffset.UtcNow.ToString("O") + "\n" + exception;
        try { File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath, "bengine-last-error.txt"), report); }
        catch (Exception error) { Log.Warn(LogTag, "BENGINE_ERROR_REPORT_FAILED|" + error.Message); }
        // A render-loop exception must remain diagnosable on a phone without
        // adb. Previously each subsequent frame cleared to black indefinitely.
        RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed) return;
            bool chinese = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
            using var builder = new AlertDialog.Builder(this);
            builder.SetTitle(chinese ? "运行错误" : "Runtime error");
            builder.SetMessage(exception.GetType().Name + ": " + exception.Message);
            builder.SetCancelable(false);
            builder.SetPositiveButton(chinese ? "关闭游戏" : "Close", (_, _) => Finish());
            builder.SetNeutralButton(chinese ? "复制错误详情" : "Copy details", (_, _) => { });
            var dialog = builder.Create();
            if (dialog is null) return;
            dialog.Show();
            dialog.GetButton((int)global::Android.Content.DialogButtonType.Neutral)!.Click += (_, _) =>
            {
                if (GetSystemService(ClipboardService) is global::Android.Content.ClipboardManager clipboard)
                    clipboard.PrimaryClip = global::Android.Content.ClipData.NewPlainText("BEngine error", report);
            };
        });
    }

    public override bool DispatchKeyEvent(KeyEvent? e) =>
        _input?.TryHandleKeyEvent(e, _textInput?.IsEditing == true) == true || base.DispatchKeyEvent(e);

    private void DetachGraphicsDevice()
    {
        if (_graphics is null) return;
        _session?.DetachGraphicsDevice();
        _graphics.Dispose();
        _graphics = null;
    }

    private void DriveAutomaticStartup()
    {
        if (!_autoStart || _session?.StartupControl is not { } flow) return;
        var state = flow.Current;
        if (_startupPhase != state.Phase)
        {
            _startupPhase = state.Phase;
            Log.Info(LogTag,
                $"BENGINE_ANDROID_STARTUP|phase={state.Phase}|status={state.Status}|error={state.Error}");
        }
        if (state.CanAdvance) flow.Advance();
    }

}
