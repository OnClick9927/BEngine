using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AVFoundation;
using BEngine.Player;
using BEngine.Platform;
using BEngine.Rendering.Rhi;
using CoreGraphics;
using Foundation;
using GameController;
using UIKit;
using Veldrid;

namespace BEngine.Player.Hosts.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    private readonly Stopwatch _clock = new();
    private PlayerRuntimeSession? _session;
    private VulkanGraphicsDevice? _graphics;
    private NSTimer? _timer;
    private PlayerView? _view;
    private IOSAccessibilityAdapter? _accessibility;
    private TimeSpan _lastFrame;
    private bool _renderLogged;
    private IOSTextInput? _textInput;

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
        BEngine.Platform.iOSPlatform.Initialize();
        PlatformVideoDecoding.OpenVideo = (source, bytes, hardware) => new IOSVideoDecoder(source, bytes, hardware);
        PlatformVideoDecoding.OpenAudio = (source, bytes) => new IOSVideoAudioDecoder(source, bytes);
        AotInterpreterRuntimeHost.DeclareInterpreterEnabled();
        ConfigureAudioSession();

#pragma warning disable CA1422
        Window = new UIWindow(UIScreen.MainScreen.Bounds);
#pragma warning restore CA1422
        _view = new PlayerView(Window.Bounds)
        {
            BackgroundColor = UIColor.Black,
            MultipleTouchEnabled = true,
            ContentScaleFactor = UIScreen.MainScreen.Scale
        };
        var controller = new PlayerViewController(_view);
        Window.RootViewController = controller;
        Window.MakeKeyAndVisible();
        _textInput = new IOSTextInput(_view);
        _accessibility = new IOSAccessibilityAdapter(_view);
        PlatformAccessibility.adapter = _accessibility;

        var payload = Path.Combine(NSBundle.MainBundle.ResourcePath!, "BEnginePayload");
        var projectPath = BEnginePlayer.ResolveDefaultProjectPath(payload);
        _session = BEnginePlayer.CreateRuntimeSession(
            projectPath, declareAotInterpreter: true,
            persistentDataPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEnginePlayer"));
        _graphics = CreateGraphicsDevice(_view);
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
        ActivateAudioSession();
        _session?.SetPaused(false);
        _session?.SetFocused(true);
        _lastFrame = _clock.Elapsed;
        if (_session is not null) Console.WriteLine("BENGINE_IOS_LIFECYCLE|state=active");
    }

    public override void OnResignActivation(UIApplication application)
    {
        _session?.SetFocused(false);
        _session?.SetPaused(true);
        DeactivateAudioSession();
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

    private static void ConfigureAudioSession()
    {
        var session = AVAudioSession.SharedInstance();
        ThrowIfAudioSessionFailed(session.SetCategory(AVAudioSessionCategory.SoloAmbient), "set category");
        ThrowIfAudioSessionFailed(session.SetActive(true), "activate");
        Console.WriteLine("BENGINE_IOS_AUDIO_SESSION_OK|state=active");
    }

    private static void ActivateAudioSession()
    {
        try
        {
            ThrowIfAudioSessionFailed(AVAudioSession.SharedInstance().SetActive(true), "reactivate");
            BEngine.Platform.iOSPlatform.ResumeAudioOutputs();
            Console.WriteLine("BENGINE_IOS_AUDIO_SESSION_OK|state=active");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_AUDIO_SESSION_FAILED|state=active|" + exception);
        }
    }

    private static void DeactivateAudioSession()
    {
        try
        {
            BEngine.Platform.iOSPlatform.SuspendAudioOutputs();
            ThrowIfAudioSessionFailed(AVAudioSession.SharedInstance().SetActive(false,
                AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation), "deactivate");
            Console.WriteLine("BENGINE_IOS_AUDIO_SESSION_OK|state=inactive");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_AUDIO_SESSION_FAILED|state=inactive|" + exception);
        }
    }

    private static void ThrowIfAudioSessionFailed(NSError? error, string operation)
    {
        if (error is null) return;
        throw new InvalidOperationException($"AVAudioSession could not {operation}: {error}.");
    }

    private void Tick()
    {
        if (_session is null || _view is null) return;
        try
        {
            PollGamepads();
            var now = _clock.Elapsed;
            _session.Tick(now - _lastFrame);
            _textInput?.Sync();
            _lastFrame = now;
            var scale = Math.Max(1, _view.ContentScaleFactor);
            var width = Math.Max(1, (int)Math.Ceiling(_view.Bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(_view.Bounds.Height * scale));
            DisplayUtility.Current.ReportDisplay(width, height, FullScreenMode.FullScreenWindow);
            var insets = _view.SafeAreaInsets;
            DisplayUtility.Current.ReportSafeArea(new((Fix64)(insets.Left * scale), (Fix64)(insets.Bottom * scale),
                (Fix64)(insets.Right * scale), (Fix64)(insets.Top * scale)));
            if (_session.Render(width, height) && !_renderLogged)
            {
                _renderLogged = true;
                Console.WriteLine($"BENGINE_IOS_RENDER_OK|backend={_graphics?.Backend}|size={width}x{height}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("BENGINE_IOS_FRAME_FAILED|" + exception);
            _timer?.Invalidate();
        }
    }

    private static VulkanGraphicsDevice CreateGraphicsDevice(UIView view)
    {
        var scale = Math.Max(1, view.ContentScaleFactor);
        var width = Math.Max(1, (int)Math.Ceiling(view.Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(view.Bounds.Height * scale));
        var options = new GraphicsDeviceOptions(false, PixelFormat.D24_UNorm_S8_UInt, true,
            ResourceBindingModel.Improved, true, true);
        var swapchain = new SwapchainDescription(SwapchainSource.CreateUIView(view.Handle),
            (uint)width, (uint)height, PixelFormat.D24_UNorm_S8_UInt, true);
        return new VulkanGraphicsDevice(
            GraphicsDevice.CreateMetal(options, swapchain), BEngine.Rendering.Rhi.GraphicsBackend.Metal);
    }

    private static void PollGamepads()
    {
        var controllers = GCController.Controllers;
        for (var index = 0; index < controllers.Length; index++)
        {
            var controller = controllers[index];
            Input.SetGamepadConnected(index, true, controller.VendorName ?? "iOS gamepad");
            var pad = controller.ExtendedGamepad;
            if (pad is null) continue;
            Input.SetGamepadAxis(index, GamepadAxis.LeftStickX, (Fix64)pad.LeftThumbstick.XAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.LeftStickY, (Fix64)pad.LeftThumbstick.YAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.RightStickX, (Fix64)pad.RightThumbstick.XAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.RightStickY, (Fix64)pad.RightThumbstick.YAxis.Value);
            Input.SetGamepadAxis(index, GamepadAxis.LeftTrigger, (Fix64)pad.LeftTrigger.Value);
            Input.SetGamepadAxis(index, GamepadAxis.RightTrigger, (Fix64)pad.RightTrigger.Value);
            SetButton(index, GamepadButton.South, pad.ButtonA);
            SetButton(index, GamepadButton.East, pad.ButtonB);
            SetButton(index, GamepadButton.West, pad.ButtonX);
            SetButton(index, GamepadButton.North, pad.ButtonY);
            SetButton(index, GamepadButton.LeftShoulder, pad.LeftShoulder);
            SetButton(index, GamepadButton.RightShoulder, pad.RightShoulder);
            SetButton(index, GamepadButton.LeftStick, pad.LeftThumbstickButton);
            SetButton(index, GamepadButton.RightStick, pad.RightThumbstickButton);
            SetButton(index, GamepadButton.DPadUp, pad.DPad.Up);
            SetButton(index, GamepadButton.DPadRight, pad.DPad.Right);
            SetButton(index, GamepadButton.DPadDown, pad.DPad.Down);
            SetButton(index, GamepadButton.DPadLeft, pad.DPad.Left);
        }
        for (var index = controllers.Length; index < Input.gamepadCount; index++)
            Input.SetGamepadConnected(index, false);
    }

    private static void SetButton(int index, GamepadButton button, GCControllerButtonInput? input) =>
        Input.SetGamepadButtonState(index, button, input?.IsPressed == true);

    private void DisposeRuntime()
    {
        _timer?.Invalidate();
        _timer?.Dispose();
        _timer = null;
        _session?.Dispose();
        _session = null;
        if (ReferenceEquals(PlatformAccessibility.adapter, _accessibility)) PlatformAccessibility.adapter = null;
        _accessibility = null;
        _graphics?.Dispose();
        _graphics = null;
        _view?.Dispose();
        _view = null;
    }
}

internal sealed class PlayerView(CGRect frame) : UIView(frame)
{
    private readonly Dictionary<nint, BEngine.Touch> _touches = [];
    private bool _inputLogged;

    public override void TouchesBegan(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Began);
    public override void TouchesMoved(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Moved);
    public override void TouchesEnded(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Ended);
    public override void TouchesCancelled(NSSet touches, UIEvent? evt) => ApplyTouches(touches, TouchPhase.Canceled);

    private void ApplyTouches(NSSet nativeTouches, TouchPhase forcedPhase)
    {
        var ended = new List<nint>();
        var scale = Math.Max(1, ContentScaleFactor);
        foreach (UITouch touch in nativeTouches)
        {
            var id = touch.Handle;
            var point = touch.LocationInView(this);
            var position = new BEngine.Vector2((Fix64)(point.X * scale), (Fix64)(point.Y * scale));
            var previous = _touches.TryGetValue(id, out var previousTouch) ? previousTouch.position : position;
            var phase = forcedPhase == TouchPhase.Moved && position == previous
                ? TouchPhase.Stationary : forcedPhase;
            _touches[id] = new BEngine.Touch(id.GetHashCode() & int.MaxValue, position, position - previous,
                Time.unscaledDeltaTime, (int)touch.TapCount, phase,
                (Fix64)(double)touch.Force, (Fix64)(double)touch.MaximumPossibleForce,
                (Fix64)(touch.MajorRadius * scale), (Fix64)(touch.MajorRadiusTolerance * scale));
            if (phase is TouchPhase.Ended or TouchPhase.Canceled) ended.Add(id);
        }
        Input.SetTouches(_touches.Values);
        if (!_inputLogged && forcedPhase == TouchPhase.Began && _touches.Count > 0)
        {
            _inputLogged = true;
            Console.WriteLine($"BENGINE_IOS_INPUT_OK|kind=touch|count={_touches.Count}");
        }
        var primary = _touches.Values.FirstOrDefault();
        if (_touches.Count > 0)
        {
            Input.SetMousePosition(primary.position.x, primary.position.y);
            Input.SetKeyState(KeyCode.Mouse0, forcedPhase is not TouchPhase.Ended and not TouchPhase.Canceled);
        }
        foreach (var id in ended) _touches.Remove(id);
        foreach (var pair in _touches.ToArray())
            _touches[pair.Key] = pair.Value with
            {
                phase = TouchPhase.Stationary,
                deltaPosition = BEngine.Vector2.zero,
                deltaTime = Fix64.Zero
            };
    }
}

internal sealed class PlayerViewController(PlayerView view) : UIViewController
{
    public override void LoadView() => View = view;

    public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
    {
        ApplyPresses(presses, true);
        base.PressesBegan(presses, evt);
    }

    public override void PressesEnded(NSSet<UIPress> presses, UIPressesEvent evt)
    {
        ApplyPresses(presses, false);
        base.PressesEnded(presses, evt);
    }

    private static void ApplyPresses(NSSet<UIPress> presses, bool pressed)
    {
        foreach (var press in presses)
        {
            if (press.Key is not { } nativeKey || !TryMapKey(nativeKey.KeyCode, out var key)) continue;
            Input.SetKeyState(key, pressed);
            if (pressed)
                foreach (var character in nativeKey.Characters) Input.AppendTextInput(character);
        }
    }

    private static bool TryMapKey(UIKeyboardHidUsage code, out KeyCode key)
    {
        if (code is >= UIKeyboardHidUsage.KeyboardA and <= UIKeyboardHidUsage.KeyboardZ)
        {
            key = (KeyCode)((int)KeyCode.A + (int)(code - UIKeyboardHidUsage.KeyboardA));
            return true;
        }
        if (code is >= UIKeyboardHidUsage.Keyboard1 and <= UIKeyboardHidUsage.Keyboard9)
        {
            key = (KeyCode)((int)KeyCode.Alpha1 + (int)(code - UIKeyboardHidUsage.Keyboard1));
            return true;
        }
        if (code == UIKeyboardHidUsage.Keyboard0)
        {
            key = KeyCode.Alpha0;
            return true;
        }
        key = code switch
        {
            UIKeyboardHidUsage.KeyboardSpacebar => KeyCode.Space,
            UIKeyboardHidUsage.KeyboardReturnOrEnter => KeyCode.Return,
            UIKeyboardHidUsage.KeyboardEscape => KeyCode.Escape,
            UIKeyboardHidUsage.KeyboardTab => KeyCode.Tab,
            UIKeyboardHidUsage.KeyboardDeleteOrBackspace => KeyCode.Backspace,
            UIKeyboardHidUsage.KeyboardDeleteForward => KeyCode.Delete,
            UIKeyboardHidUsage.KeyboardUpArrow => KeyCode.UpArrow,
            UIKeyboardHidUsage.KeyboardDownArrow => KeyCode.DownArrow,
            UIKeyboardHidUsage.KeyboardLeftArrow => KeyCode.LeftArrow,
            UIKeyboardHidUsage.KeyboardRightArrow => KeyCode.RightArrow,
            _ => KeyCode.None
        };
        return key != KeyCode.None;
    }
}
