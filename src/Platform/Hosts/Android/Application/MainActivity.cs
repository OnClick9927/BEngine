using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
using BEngine.Player;
using BEngine.Startup;
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
    private readonly Dictionary<int, BEngine.Vector2> _touchPositions = [];
    private readonly ConcurrentQueue<TouchInputState> _touchInput = new();
    private readonly HashSet<int> _connectedGamepads = [];
    private PlayerRuntimeSession? _session;
    private AndroidGlGraphicsDevice? _graphics;
    private AccessibleGameSurface? _surfaceView;
    private AndroidAccessibilityAdapter? _accessibility;
    private TimeSpan _lastFrame;
    private TimeSpan _nextFrame;
    private int _requestedFrameRate;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _fixedSurfaceWidth;
    private int _fixedSurfaceHeight;
    private volatile bool _resumed;
    private volatile bool _frameFailed;
    private bool _renderLogged;
    private bool _inputLogged;
    private int _textSyncQueued;
    private bool _autoStart;
    private bool _validateGraphics;
    private bool _postProcessingValidated;
    private bool _profileFrames;
    private readonly AndroidFrameMetrics _frameMetrics = new();
    private AotStartupPhase? _startupPhase;
    private AndroidTextInput? _textInput;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        BEngine.Platform.AndroidPlatform.Initialize();
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        base.OnCreate(savedInstanceState);
        PlatformVideoDecoding.OpenVideo = (source, bytes, hardware) => new AndroidVideoDecoder(source, bytes, hardware);
        PlatformVideoDecoding.OpenAudio = (source, bytes) => new AndroidVideoAudioDecoder(source, bytes);
        if (Intent?.GetBooleanExtra("bengineRunTests", false) == true)
        {
            System.Environment.SetEnvironmentVariable("BENGINE_TEST_RUNNER_MODE", "player");
            var result = Intent.GetStringExtra("bengineTestResult");
            if (!string.IsNullOrWhiteSpace(result) && result.StartsWith("files/", StringComparison.Ordinal) && !result.Contains("..", StringComparison.Ordinal))
                System.Environment.SetEnvironmentVariable("BENGINE_TEST_RUNNER_RESULT", Path.Combine(FilesDir!.Parent!, result));
            System.Environment.SetEnvironmentVariable("BENGINE_TEST_RUNNER_COVERAGE", Intent.GetBooleanExtra("bengineTestCoverage", false) ? "1" : "0");
            if (Intent.GetStringExtra("bengineTestIds64") is { Length: > 0 } ids)
                System.Environment.SetEnvironmentVariable("BENGINE_TEST_RUNNER_IDS", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ids)));
            System.Environment.SetEnvironmentVariable("BENGINE_ASSET_BUNDLE_REMOTE_URL", Intent.GetStringExtra("bengineTestContentUrl"));
            System.Environment.SetEnvironmentVariable("BENGINE_ASSET_BUNDLE_ALLOW_INSECURE_LOOPBACK", "1");
        }
        _autoStart = Intent?.GetBooleanExtra("bengineAutoStart", false) == true;
        _validateGraphics = Intent?.GetBooleanExtra("bengineValidateGraphics", false) == true;
        _profileFrames = Intent?.GetBooleanExtra("bengineProfileFrames", false) == true;
        if (_autoStart)
        {
            System.Environment.SetEnvironmentVariable("BENGINE_AOT_AUTO_CHECK", "1");
            System.Environment.SetEnvironmentVariable("BENGINE_AOT_AUTO_CONFIRM_UPDATE", "1");
            System.Environment.SetEnvironmentVariable("BENGINE_AOT_AUTO_CONTINUE", "1");
        }

        Window?.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
        _accessibility = new AndroidAccessibilityAdapter();
        _surfaceView = new AccessibleGameSurface(this, _accessibility) { Focusable = true, FocusableInTouchMode = true };
        _accessibility.AttachSurface(_surfaceView);
        PlatformAccessibility.adapter = _accessibility;
        _surfaceView.SetEGLContextClientVersion(3);
        _surfaceView.SetEGLConfigChooser(8, 8, 8, 8, 24, 8);
        _surfaceView.PreserveEGLContextOnPause = true;
        _surfaceView.SetRenderer(this);
        _surfaceView.RenderMode = Rendermode.Continuously;
        _surfaceView.Touch += OnTouch;
        _surfaceView.GenericMotion += OnGenericMotion;
        _surfaceView.LayoutChange += (_, _) => UpdateSurfaceSize();
        var content = new global::Android.Widget.FrameLayout(this);
        content.AddView(_surfaceView, new global::Android.Widget.FrameLayout.LayoutParams(-1, -1));
        _textInput = new AndroidTextInput(this, content, _surfaceView);
        SetContentView(content);
        _surfaceView.RequestFocus();

        var dataDirectories = (Assets!.List("BEnginePayload") ?? [])
            .Where(name => name.EndsWith("_data", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (dataDirectories.Length != 1)
            throw new InvalidDataException("The Android payload must contain exactly one Player data directory.");
        // Each packaged game gets its own extraction root, including when an application ID is reused.
        var root = Path.Combine(FilesDir!.AbsolutePath, "BEnginePlayer", dataDirectories[0]);
        ExtractAssetDirectory("BEnginePayload", root);
        var projectPath = BEnginePlayer.ResolveDefaultProjectPath(root);
        var persistentRoot = Path.Combine(FilesDir.AbsolutePath, "BEnginePersistentData");
        var services = new ServiceCollection();
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
        _resumed = true;
        BEngine.Platform.AndroidPlatform.ResumeAudioOutputs();
        QueueRuntimeLifecycle(true);
        _surfaceView?.OnResume();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
#pragma warning disable CA1422 // Legacy immersive flags are required on API 26+.
        if (hasFocus && Window?.DecorView is { } decor)
            decor.SystemUiFlags = SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen |
                SystemUiFlags.HideNavigation | SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
        if (hasFocus) _textInput?.RefreshSafeArea();
    }

    protected override void OnPause()
    {
        _resumed = false;
        // User callbacks and the scene transition scheduler belong to the GL
        // runtime thread. Calling them on the Activity thread can race a scene
        // load while GLSurfaceView waits for that same runtime frame to finish.
        QueueRuntimeLifecycle(false);
        _surfaceView?.OnPause();
        BEngine.Platform.AndroidPlatform.SuspendAudioOutputs();
        base.OnPause();
    }

    private void QueueRuntimeLifecycle(bool resumed)
    {
        _surfaceView?.QueueEvent(new Java.Lang.Runnable(() =>
        {
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
        _surfaceView?.QueueEvent(new Java.Lang.Runnable(() => _session?.RaiseLowMemory()));
        base.OnLowMemory();
    }

    public override void OnTrimMemory(global::Android.Content.TrimMemory level)
    {
        // Android sends this before the process is under hard pressure. Let
        // the runtime release unused assets on its GL thread instead of
        // waiting for the OS to kill the process while a frame is rendering.
        if (level >= global::Android.Content.TrimMemory.RunningLow)
            _surfaceView?.QueueEvent(new Java.Lang.Runnable(() => _session?.RaiseLowMemory()));
        base.OnTrimMemory(level);
    }

    protected override void OnDestroy()
    {
        _resumed = false;
        if (_surfaceView is not null)
        {
            _surfaceView.Touch -= OnTouch;
            _surfaceView.GenericMotion -= OnGenericMotion;
        }
        DetachGraphicsDevice();
        _session?.Dispose();
        _session = null;
        if (ReferenceEquals(PlatformAccessibility.adapter, _accessibility)) PlatformAccessibility.adapter = null;
        _accessibility = null;
        _surfaceView?.Dispose();
        _surfaceView = null;
        base.OnDestroy();
    }

    public void OnSurfaceCreated(IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        try
        {
            DetachGraphicsDevice();
            _graphics = new AndroidGlGraphicsDevice();
            if (_validateGraphics) AndroidGraphicsRegression.Run(_graphics);
            _session?.AttachGraphicsDevice(_graphics, ownsDevice: false);
            _lastFrame = _clock.Elapsed;
            Log.Info(LogTag,
                $"BENGINE_ANDROID_GRAPHICS_OK|backend={_graphics.Backend}|device={_graphics.Capabilities.DeviceName}");
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, "BENGINE_ANDROID_GRAPHICS_FAILED|" + exception);
            throw;
        }
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _surfaceWidth = Math.Max(1, width);
        _surfaceHeight = Math.Max(1, height);
        ApplyPresentationRate(force: true);
        DisplayUtility.Current.ReportDisplay(_surfaceWidth, _surfaceHeight, FullScreenMode.FullScreenWindow);
        _textInput?.RefreshSafeArea();
        var safe = DisplayUtility.Current.safeArea;
        Log.Info(LogTag, $"BENGINE_ANDROID_SURFACE_READY|size={_surfaceWidth}x{_surfaceHeight}|safe={safe.x},{safe.y},{safe.width},{safe.height}");
    }

    public void OnDrawFrame(IGL10? gl)
    {
        if (!_resumed || _frameFailed) return;
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
            var frameInterval = TimeSpan.FromSeconds(1d / Math.Clamp(BEngine.Application.targetFrameRate > 0 ? BEngine.Application.targetFrameRate : 60, 1, 240));
            // Keep the deadline anchored: rounding every 16.67 ms sleep up to
            // 17 ms would cap a nominal 60 Hz loop below 60 and miss VSyncs.
            _nextFrame = _nextFrame + frameInterval > now ? _nextFrame + frameInterval : now + frameInterval;
            ApplyPresentationRate();
            ApplyTouchInput();
            PollGamepads();
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
                    Log.Info(LogTag,
                        $"BENGINE_ANDROID_RENDER_OK|backend={_graphics?.Backend}|size={_surfaceWidth}x{_surfaceHeight}");
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, "BENGINE_ANDROID_FRAME_FAILED|" + exception);
            _frameFailed = true;
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

    private void ApplyPresentationRate(bool force = false)
    {
        var rate = Math.Clamp(BEngine.Application.targetFrameRate > 0 ? BEngine.Application.targetFrameRate : 60, 1, 240);
        if (!force && rate == _requestedFrameRate) return;
        _requestedFrameRate = rate;
        // Games may adapt frame cadence; FIXED_SOURCE is intended for video.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            _surfaceView?.Holder?.Surface?.SetFrameRate(rate, 0);
        RunOnUiThread(() =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(35) && _surfaceView is not null)
                _surfaceView.RequestedFrameRate = rate;
            if (Window?.Attributes is not { } attributes) return;
            attributes.PreferredRefreshRate = rate;
            Window.Attributes = attributes;
        });
    }

    private void UpdateSurfaceSize()
    {
        if (_surfaceView is not { Width: > 0, Height: > 0 } surface) return;
        var maximum = AndroidRuntimeSettings.maximumSurfaceDimension;
        var scale = maximum > 0 ? Math.Min(1d, (double)maximum / Math.Max(surface.Width, surface.Height)) : 1d;
        var targetWidth = Math.Max(1, (int)Math.Round(surface.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(surface.Height * scale));
        // LayoutChange can fire repeatedly during safe-area/insets updates.
        // Reissuing SetFixedSize for the same dimensions recreates the EGL
        // buffer queue on several phones and causes visible hitching or a
        // black frame. Resize only when the requested size actually changes.
        if (targetWidth == _fixedSurfaceWidth && targetHeight == _fixedSurfaceHeight) return;
        _fixedSurfaceWidth = targetWidth;
        _fixedSurfaceHeight = targetHeight;
        if (targetWidth == surface.Width && targetHeight == surface.Height) return;
        surface.Holder?.SetFixedSize(targetWidth, targetHeight);
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && (e.Source & (InputSourceType.Gamepad | InputSourceType.Joystick)) != 0 &&
            TryMapGamepadButton(e.KeyCode, out var button))
        {
            var index = Math.Max(0, e.DeviceId);
            _connectedGamepads.Add(index);
            Input.SetGamepadConnected(index, true, InputDevice.GetDevice(e.DeviceId)?.Name ?? "Android gamepad");
            Input.SetGamepadButtonState(index, button, e.Action != KeyEventActions.Up);
            return true;
        }
        if (_textInput?.IsEditing == true) return base.DispatchKeyEvent(e);
        if (e is not null && TryMapKey(e.KeyCode, out var key))
        {
            Input.SetKeyState(key, e.Action != KeyEventActions.Up);
            if (e.Action == KeyEventActions.Down && e.UnicodeChar is > 0 and <= char.MaxValue)
                Input.AppendTextInput((char)e.UnicodeChar);
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    private void OnTouch(object? sender, View.TouchEventArgs args)
    {
        var e = args.Event;
        if (e is null) return;
        var changedIndex = e.ActionIndex;
        var action = e.ActionMasked;
        var touches = new List<BEngine.Touch>(e.PointerCount);
        for (var index = 0; index < e.PointerCount; index++)
        {
            var id = e.GetPointerId(index);
            // Input uses a bottom-left origin on every platform; Android events
            // use a top-left origin. UI converts back to its own top-left space.
            var height = _surfaceView?.Height ?? _surfaceHeight;
            var position = new BEngine.Vector2((Fix64)(e.GetX(index) * _surfaceWidth / Math.Max(1f, _surfaceView?.Width ?? _surfaceWidth)),
                (Fix64)((height - e.GetY(index)) * _surfaceHeight / Math.Max(1f, height)));
            var previous = _touchPositions.GetValueOrDefault(id, position);
            var phase = action switch
            {
                MotionEventActions.Down or MotionEventActions.PointerDown when index == changedIndex => TouchPhase.Began,
                MotionEventActions.Up or MotionEventActions.PointerUp when index == changedIndex => TouchPhase.Ended,
                MotionEventActions.Cancel => TouchPhase.Canceled,
                _ when position == previous => TouchPhase.Stationary,
                _ => TouchPhase.Moved
            };
            touches.Add(new BEngine.Touch(id, position, position - previous,
                Time.unscaledDeltaTime, 1, phase, (Fix64)e.GetPressure(index), Fix64.One,
                (Fix64)Math.Max(e.GetTouchMajor(index) * .5f, 0), Fix64.Zero));
            if (phase is TouchPhase.Ended or TouchPhase.Canceled) _touchPositions.Remove(id);
            else _touchPositions[id] = position;
        }
        var mousePosition = e.PointerCount > 0 ? touches[0].position : BEngine.Vector2.zero;
        _touchInput.Enqueue(new TouchInputState(touches.ToArray(), mousePosition,
            action is not MotionEventActions.Up and not MotionEventActions.Cancel));
        if (!_inputLogged)
        {
            _inputLogged = true;
            Log.Info(LogTag, $"BENGINE_ANDROID_INPUT_OK|kind=touch|count={touches.Count}");
        }
        args.Handled = true;
    }

    private void ApplyTouchInput()
    {
        if (!_touchInput.TryDequeue(out var state)) return;
        // UI-thread callbacks must not mutate Input while the GL thread ticks.
        // Keep down/up in separate frames, including a tap shorter than one frame,
        // but collapse intermediate motion samples to prevent a dragging backlog.
        if (!state.HasEdge)
            while (_touchInput.TryPeek(out var next) && !next.HasEdge && _touchInput.TryDequeue(out var newer))
                state = newer;
        Input.SetTouches(state.Touches);
        Input.SetMousePosition(state.MousePosition.x, state.MousePosition.y);
        Input.SetKeyState(KeyCode.Mouse0, state.MouseHeld);
    }

    private sealed record TouchInputState(BEngine.Touch[] Touches, BEngine.Vector2 MousePosition, bool MouseHeld)
    {
        internal bool HasEdge => Touches.Any(touch => touch.phase is TouchPhase.Began or TouchPhase.Ended or TouchPhase.Canceled);
    }

    private void OnGenericMotion(object? sender, View.GenericMotionEventArgs args)
    {
        var e = args.Event;
        if (e is null || (e.Source & InputSourceType.Joystick) == 0) return;
        var index = Math.Max(0, e.DeviceId);
        _connectedGamepads.Add(index);
        Input.SetGamepadConnected(index, true, InputDevice.GetDevice(e.DeviceId)?.Name ?? "Android gamepad");
        Input.SetGamepadAxis(index, GamepadAxis.LeftStickX, (Fix64)e.GetAxisValue(Axis.X));
        Input.SetGamepadAxis(index, GamepadAxis.LeftStickY, (Fix64)(-e.GetAxisValue(Axis.Y)));
        Input.SetGamepadAxis(index, GamepadAxis.RightStickX, (Fix64)e.GetAxisValue(Axis.Z));
        Input.SetGamepadAxis(index, GamepadAxis.RightStickY, (Fix64)(-e.GetAxisValue(Axis.Rz)));
        Input.SetGamepadAxis(index, GamepadAxis.LeftTrigger, (Fix64)e.GetAxisValue(Axis.Ltrigger));
        Input.SetGamepadAxis(index, GamepadAxis.RightTrigger, (Fix64)e.GetAxisValue(Axis.Rtrigger));
        args.Handled = true;
    }

    private void PollGamepads()
    {
        var current = new HashSet<int>();
        foreach (var deviceId in InputDevice.GetDeviceIds() ?? [])
        {
            var device = InputDevice.GetDevice(deviceId);
            if (device is null || (device.Sources & (InputSourceType.Gamepad | InputSourceType.Joystick)) == 0) continue;
            var index = Math.Max(0, deviceId);
            current.Add(index);
            _connectedGamepads.Add(index);
            Input.SetGamepadConnected(index, true, device.Name ?? "Android gamepad");
        }
        foreach (var disconnected in _connectedGamepads.Except(current).ToArray())
        {
            Input.SetGamepadConnected(disconnected, false);
            _connectedGamepads.Remove(disconnected);
        }
    }

    private void DetachGraphicsDevice()
    {
        if (_graphics is null) return;
        _session?.DetachGraphicsDevice();
        _graphics.Dispose();
        _graphics = null;
    }

    private void DriveAutomaticStartup()
    {
        if (!_autoStart || _session?.StartupFlow is not { } flow) return;
        var state = flow.Current;
        if (_startupPhase != state.Phase)
        {
            _startupPhase = state.Phase;
            Log.Info(LogTag,
                $"BENGINE_ANDROID_STARTUP|phase={state.Phase}|status={state.Status}|error={state.Error}");
        }
        if (state.CanCheckForUpdates) flow.CheckForUpdates();
        else if (state.RequiresUpdateConfirmation) flow.ConfirmUpdate();
        else if (state.CanEnterGame) flow.EnterGame();
    }

    private static bool TryMapKey(Keycode code, out KeyCode key)
    {
        if (code is >= Keycode.A and <= Keycode.Z)
        {
            key = (KeyCode)((int)KeyCode.A + (int)(code - Keycode.A));
            return true;
        }
        if (code is >= Keycode.Num0 and <= Keycode.Num9)
        {
            key = (KeyCode)((int)KeyCode.Alpha0 + (int)(code - Keycode.Num0));
            return true;
        }
        key = code switch
        {
            Keycode.Space => KeyCode.Space,
            Keycode.Enter => KeyCode.Return,
            Keycode.Escape or Keycode.Back => KeyCode.Escape,
            Keycode.Tab => KeyCode.Tab,
            Keycode.Del => KeyCode.Backspace,
            Keycode.ForwardDel => KeyCode.Delete,
            Keycode.DpadUp => KeyCode.UpArrow,
            Keycode.DpadDown => KeyCode.DownArrow,
            Keycode.DpadLeft => KeyCode.LeftArrow,
            Keycode.DpadRight => KeyCode.RightArrow,
            _ => KeyCode.None
        };
        return key != KeyCode.None;
    }

    private static bool TryMapGamepadButton(Keycode code, out GamepadButton button)
    {
        button = code switch
        {
            Keycode.ButtonA => GamepadButton.South,
            Keycode.ButtonB => GamepadButton.East,
            Keycode.ButtonX => GamepadButton.West,
            Keycode.ButtonY => GamepadButton.North,
            Keycode.ButtonL1 => GamepadButton.LeftShoulder,
            Keycode.ButtonR1 => GamepadButton.RightShoulder,
            Keycode.ButtonSelect => GamepadButton.Back,
            Keycode.ButtonStart => GamepadButton.Start,
            Keycode.ButtonMode => GamepadButton.Home,
            Keycode.ButtonThumbl => GamepadButton.LeftStick,
            Keycode.ButtonThumbr => GamepadButton.RightStick,
            Keycode.DpadUp => GamepadButton.DPadUp,
            Keycode.DpadRight => GamepadButton.DPadRight,
            Keycode.DpadDown => GamepadButton.DPadDown,
            Keycode.DpadLeft => GamepadButton.DPadLeft,
            _ => (GamepadButton)(-1)
        };
        return (int)button >= 0;
    }

    private void ExtractAssetDirectory(string assetPath, string destination)
    {
        var children = Assets!.List(assetPath) ?? [];
        if (children.Length == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = Assets.Open(assetPath);
            var temporary = destination + ".extracting";
            using (var output = File.Create(temporary)) input.CopyTo(output);
            File.Move(temporary, destination, overwrite: true);
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (var child in children.Order(StringComparer.Ordinal))
            ExtractAssetDirectory($"{assetPath}/{child}", Path.Combine(destination, child));
    }
}
