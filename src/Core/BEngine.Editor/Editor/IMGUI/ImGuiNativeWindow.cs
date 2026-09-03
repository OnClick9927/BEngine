using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BEngine.Build;
using BEngine.Editor.Diagnostics;
using BEngine.Editor.Rendering;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.Rendering.Rhi.Vulkan;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using BEvent = BEngine.Editor.Event;
using BVector2 = BEngine.Vector2;

namespace BEngine.Editor;

/// <summary>Native window that executes Unity-style immediate GUI and submits it through the BEngine RHI.</summary>
internal sealed class ImGuiNativeWindow : IDisposable
{
    private readonly IWindow _window;
    private readonly GraphicsBackend _requestedBackend;
    private readonly bool _vsync;
    private readonly NativeWindowFrameScheduler _frameScheduler = new();
    private readonly NativeKeyboardRepeat _keyboardRepeat = NativeKeyboardRepeat.CreateSystemDefault();
    private readonly ConcurrentQueue<BEvent> _events = new();
    private readonly List<GpuCanvasCommand> _commands = [];
    private IInputContext? _input;
    private IGraphicsDevice? _device;
    private IGraphicsPresentationDevice? _presentation;
    private GpuCanvasRenderer? _canvas;
    private GL? _gl;
    private IMouse? _primaryMouse;
    private MouseCursor? _appliedMouseCursor;
    private bool _disposed;
    private bool _firstFrame;
    private bool _isFocused;
    private BVector2 _mousePosition;
    private BVector2 _lastMousePosition;
    private EventModifiers _modifiers;
    private long _lastClickTime;
    private BVector2 _lastClickPosition;
    private int _clickCount;
    private Vector2D<int> _minimumSize;
    private Vector2D<int> _maximumSize;
    private readonly GameViewFrameTiming _frameTiming = new();

    public event Action<double>? updating;
    public event Action? closing;
    public event Action? firstFrameRendered;
    public event Action? gui;
    public event Action<bool>? focusChanged;
    internal event Action<ImGuiNativeFrameProfile>? frameProfiled;
    public Action<IGraphicsDevice, int, int>? renderBackground { get; set; }
    public bool isClosing => _window.IsClosing;
    public GraphicsBackend backend => _device?.Backend ?? _requestedBackend;
    public int width => Math.Max(1, _window.FramebufferSize.X);
    public int height => Math.Max(1, _window.FramebufferSize.Y);
    public Fix64 renderScale { get; private set; } = Fix64.One;
    public Fix64 deviceScale { get; private set; } = Fix64.One;
    internal GameViewFrameTimingSnapshot frameTiming => _frameTiming.snapshot;
    internal IGraphicsDevice? graphicsDevice => _device;
    public IntPtr nativeHandle => _window.Native?.Win32?.Hwnd ?? IntPtr.Zero;
    public BVector2 screenPosition => new(_window.Position.X, _window.Position.Y);
    public BVector2 windowSize => new(_window.Size.X, _window.Size.Y);
    public bool isMaximized => _window.WindowState == WindowState.Maximized;
    public bool isFocused => _isFocused;
    public bool leftMouseButtonPressed => OperatingSystem.IsWindows()
        ? (GetAsyncKeyState(0x01) & 0x8000) != 0
        : _primaryMouse?.IsButtonPressed(MouseButton.Left) == true;
    internal NativeWindowPointerOperation pointerOperation => ResolvePointerOperation();
    private bool anyNavigationMouseButtonPressed => OperatingSystem.IsWindows()
        ? (GetAsyncKeyState(0x01) & 0x8000) != 0 ||
          (GetAsyncKeyState(0x02) & 0x8000) != 0 ||
          (GetAsyncKeyState(0x04) & 0x8000) != 0
        : _primaryMouse?.IsButtonPressed(MouseButton.Left) == true ||
          _primaryMouse?.IsButtonPressed(MouseButton.Right) == true ||
          _primaryMouse?.IsButtonPressed(MouseButton.Middle) == true;

    public ImGuiNativeWindow(string title, int width, int height, bool visible = true,
        bool vsync = true, int minimumWidth = 320, int minimumHeight = 200,
        int maximumWidth = int.MaxValue, int maximumHeight = int.MaxValue)
    {
        _requestedBackend = ResolveWindowBackend(GraphicsBackendSettings.PreferredBackend);
        _vsync = vsync;
        _minimumSize = new Vector2D<int>(Math.Max(1, minimumWidth), Math.Max(1, minimumHeight));
        _maximumSize = new Vector2D<int>(Math.Max(_minimumSize.X, maximumWidth),
            Math.Max(_minimumSize.Y, maximumHeight));
        var options = WindowOptions.Default;
        options.Title = title;
        options.Size = ConstrainSize(width, height);
        options.VSync = _vsync;
        options.ShouldSwapAutomatically = false;
        options.IsVisible = visible;
        options.WindowBorder = WindowBorder.Resizable;
        options.API = WindowApiForBackend(_requestedBackend);
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += () => EditorCallbackDispatcher.Invoke(closing, nameof(closing));
        _window.FocusChanged += focused =>
        {
            _isFocused = focused;
            if (!focused) _keyboardRepeat.Clear();
            Enqueue(new BEvent(focused ? EventType.MouseEnterWindow : EventType.MouseLeaveWindow)
            {
                mousePosition = _mousePosition
            });
            EditorCallbackDispatcher.Invoke(focusChanged, focused, nameof(focusChanged));
        };
    }

    public void Run() => _window.Run();
    public void Initialize() { if (!_window.IsInitialized) _window.Initialize(); }
    public void Pump()
    {
        if (!_window.IsInitialized || _window.IsClosing) return;
        var previousFramebufferSize = _window.FramebufferSize;
        var previousWindowSize = _window.Size;
        _window.DoEvents();
        _window.DoUpdate();
        if (_window.FramebufferSize != previousFramebufferSize || _window.Size != previousWindowSize)
            _frameScheduler.RequestRender();

        var decision = _frameScheduler.Evaluate(_isFocused,
            _window.WindowState == WindowState.Minimized, Stopwatch.GetTimestamp());
        if (!decision.ShouldRender) return;
        _window.DoRender();
        _frameScheduler.NotifyRendered(decision);
    }
    public void Focus()
    {
        // GLFW requires a live native handle. Layout restoration can request focus
        // before the main window has been initialized; the first rendered frame
        // performs the real focus handoff.
        var initialized = _window.IsInitialized;
        if (!CanQueryNativeFocusState(_disposed, initialized, _isFocused)) return;
        // IsClosing itself enters GLFW, so it must only be queried after initialization.
        if (!CanInvokeNativeFocus(_disposed, initialized, _window.IsClosing, _isFocused)) return;
        _window.Focus();
    }

    internal static bool CanQueryNativeFocusState(bool disposed, bool initialized, bool focused) =>
        !disposed && initialized && !focused;

    internal static bool CanInvokeNativeFocus(
        bool disposed,
        bool initialized,
        bool closing,
        bool focused) =>
        CanQueryNativeFocusState(disposed, initialized, focused) && !closing;
    public void Close() => _window.Close();
    public void Repaint() => _frameScheduler.RequestRender();
    public void SetTitle(string title) { if (_window.Title != title) _window.Title = title; }
    public void Move(int x, int y) => _window.Position = new Vector2D<int>(x, y);
    public void Resize(int width, int height)
    {
        _window.Size = ConstrainSize(width, height);
        Repaint();
    }
    public void SetSizeLimits(int minimumWidth, int minimumHeight, int maximumWidth, int maximumHeight)
    {
        _minimumSize = new Vector2D<int>(Math.Max(1, minimumWidth), Math.Max(1, minimumHeight));
        _maximumSize = new Vector2D<int>(Math.Max(_minimumSize.X, maximumWidth),
            Math.Max(_minimumSize.Y, maximumHeight));
        var constrained = ConstrainSize(_window.Size.X, _window.Size.Y);
        if (_window.Size != constrained) Resize(constrained.X, constrained.Y);
    }
    public void SetMaximized(bool maximized) =>
        _window.WindowState = maximized ? WindowState.Maximized : WindowState.Normal;
    public void Minimize() => _window.WindowState = WindowState.Minimized;

    private Vector2D<int> ConstrainSize(int width, int height) => new(
        Math.Clamp(width, _minimumSize.X, _maximumSize.X),
        Math.Clamp(height, _minimumSize.Y, _maximumSize.Y));

    public BVector2 ClientToScreen(BVector2 point)
    {
        if (OperatingSystem.IsWindows() && nativeHandle != IntPtr.Zero)
        {
            var nativePoint = new NativePoint((int)point.x, (int)point.y);
            if (ClientToScreenNative(nativeHandle, ref nativePoint))
                return new BVector2(nativePoint.X, nativePoint.Y);
        }
        return screenPosition + point;
    }

    public BVector2 ScreenToClient(BVector2 point)
    {
        if (OperatingSystem.IsWindows() && nativeHandle != IntPtr.Zero)
        {
            var nativePoint = new NativePoint((int)point.x, (int)point.y);
            if (ScreenToClientNative(nativeHandle, ref nativePoint))
                return new BVector2(nativePoint.X, nativePoint.Y);
        }
        return point - screenPosition;
    }

    internal BVector2 GUIToScreen(BVector2 point, Fix64 editorScale) =>
        ClientToScreen(GUIToClient(point, editorScale, deviceScale));

    internal BVector2 ScreenToGUI(BVector2 point, Fix64 editorScale) =>
        ClientToGUI(ScreenToClient(point), editorScale, deviceScale);

    internal static BVector2 GUIToClient(BVector2 point, Fix64 editorScale) =>
        point * NormalizeEditorScale(editorScale);

    internal static BVector2 ClientToGUI(BVector2 point, Fix64 editorScale) =>
        point / NormalizeEditorScale(editorScale);

    internal static BVector2 GUIToClient(BVector2 point, Fix64 editorScale, Fix64 deviceScale) =>
        point * NormalizeEditorScale(editorScale) * NormalizeDeviceScale(deviceScale);

    internal static BVector2 ClientToGUI(BVector2 point, Fix64 editorScale, Fix64 deviceScale) =>
        point / (NormalizeEditorScale(editorScale) * NormalizeDeviceScale(deviceScale));

    private static Fix64 NormalizeEditorScale(Fix64 editorScale) =>
        Fix64.Clamp(editorScale, Fix64.FromDecimal(0.5m), (Fix64)4);

    private static Fix64 NormalizeDeviceScale(Fix64 scale) =>
        Fix64.Clamp(scale, Fix64.FromDecimal(0.5m), (Fix64)4);

    public static bool TryGetPointerScreenPosition(out BVector2 point)
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var nativePoint))
        {
            point = new BVector2(nativePoint.X, nativePoint.Y);
            return true;
        }
        point = default;
        return false;
    }

    private NativeWindowPointerOperation ResolvePointerOperation()
    {
        if (!OperatingSystem.IsWindows() || nativeHandle == IntPtr.Zero ||
            !GetCursorPos(out var point)) return NativeWindowPointerOperation.Unknown;
        var packed = unchecked((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16));
        var hit = (int)SendMessageNative(nativeHandle, 0x0084, IntPtr.Zero, (IntPtr)(long)packed);
        return hit switch
        {
            2 => NativeWindowPointerOperation.CaptionMove,
            >= 10 and <= 18 => NativeWindowPointerOperation.BorderResize,
            _ => NativeWindowPointerOperation.Unknown
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas?.Dispose(); _canvas = null;
        _device?.Dispose(); _device = null; _presentation = null;
        _gl?.Dispose(); _gl = null;
        _input?.Dispose(); _input = null;
        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnLoad()
    {
        WindowIcon.Apply(_window);
        CreateDevice();
        _canvas = new GpuCanvasRenderer(_device!, resourceResolver: EditorGpuCanvasResourceResolver.Shared);
        _input = _window.CreateInput();
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }
        foreach (var mouse in _input.Mice)
        {
            _primaryMouse ??= mouse;
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnMouseScroll;
        }
    }

    private void CreateDevice()
    {
        if (_requestedBackend == GraphicsBackend.Vulkan)
        {
            try
            {
                var native = _window.Native?.Win32 ??
                    throw new PlatformNotSupportedException("Vulkan editor windows require Win32.");
                var factory = new GraphicsDeviceFactory();
                factory.RegisterProvider(new VulkanGraphicsDeviceProvider(native.Hwnd, native.HInstance,
                    width, height, _vsync));
                var nativeDevice = factory.CreateDevice(GraphicsBackend.Vulkan);
                _presentation = (IGraphicsPresentationDevice)nativeDevice;
                _device = new FrameDebugGraphicsDevice(nativeDevice, ownsDevice: true);
                return;
            }
            catch (Exception exception)
            {
                _device?.Dispose(); _device = null; _presentation = null;
                throw new InvalidOperationException("Vulkan IMGUI window initialization failed.", exception);
            }
        }
        _gl = GL.GetApi(_window);
        _device = new FrameDebugGraphicsDevice(new OpenGlGraphicsDevice(_gl), ownsDevice: true);
    }

    private static GraphicsBackend ResolveWindowBackend(GraphicsBackend preferred)
    {
        var target = BuildTargetManifestSerializer.LoadCurrent();
        foreach (var candidate in GraphicsBackendSelector.GetCandidates(target, preferred))
        {
            if (candidate == GraphicsBackend.OpenGL) return GraphicsBackend.OpenGL;
            if (candidate != GraphicsBackend.Vulkan) continue;
            try
            {
                if (Veldrid.GraphicsDevice.IsBackendSupported(Veldrid.GraphicsBackend.Vulkan))
                    return GraphicsBackend.Vulkan;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Vulkan support could not be queried: {exception.Message}");
            }
        }
        Debug.LogWarning("No preferred editor backend is available; using OpenGL.");
        return GraphicsBackend.OpenGL;
    }

    private static GraphicsAPI WindowApiForBackend(GraphicsBackend backend) =>
        backend == GraphicsBackend.Vulkan
            ? GraphicsAPI.None
            : new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
                ContextFlags.ForwardCompatible, new APIVersion(3, 3));

    private void OnUpdate(double delta)
    {
        if (GUI.isEditingTextField &&
            _keyboardRepeat.TryGetRepeat(Stopwatch.GetTimestamp(), out var repeatedKey))
            Enqueue(new BEvent(EventType.KeyDown)
            {
                keyCode = repeatedKey,
                modifiers = _modifiers,
                mousePosition = _mousePosition
            });
        EditorCallbackDispatcher.Invoke(updating, delta, nameof(updating));
    }

    private void OnRender(double deltaSeconds)
    {
        if (_device is null || _canvas is null) return;
        var profileFrame = EditorProfiler.Recording ? frameProfiled : null;
        var frameStarted = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
        _frameTiming.RecordSample(deltaSeconds);
        var framebufferSize = _window.FramebufferSize;
        var frameWidth = Math.Max(1, framebufferSize.X);
        var frameHeight = Math.Max(1, framebufferSize.Y);
        var windowSize = _window.Size;
        var frameScale = DevicePixelsPerPoint(frameWidth, frameHeight,
            Math.Max(1, windowSize.X), Math.Max(1, windowSize.Y));
        deviceScale = frameScale;
        renderScale = frameScale * Fix64.Clamp(GUIUtility.pixelsPerPoint,
            Fix64.FromDecimal(0.5m), (Fix64)4);
        _presentation?.BeginFrame(frameWidth, frameHeight);
        _device.SetViewport(new GraphicsRect(0, 0, frameWidth, frameHeight));
        var background = EditorStyles.viewBackground.normal.backgroundColor;
        _device.Clear(GraphicsClearFlags.Color | GraphicsClearFlags.Depth,
            new System.Numerics.Vector4((float)background.r, (float)background.g,
                (float)background.b, (float)background.a));
        var backgroundStarted = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
        if (renderBackground is not null)
        {
            foreach (Action<IGraphicsDevice, int, int> backgroundCallback in renderBackground.GetInvocationList())
                EditorFeatureGuard.Invoke(
                    $"Render background {backgroundCallback.Method.DeclaringType?.FullName}." +
                    backgroundCallback.Method.Name,
                    () => backgroundCallback(_device, frameWidth, frameHeight));
        }
        var backgroundEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();

        Dispatch(new BEvent(EventType.Layout) { mousePosition = _mousePosition, modifiers = _modifiers },
            frameWidth, frameHeight, frameScale);
        var layoutEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
        while (_events.TryDequeue(out var inputEvent))
            Dispatch(inputEvent, frameWidth, frameHeight, frameScale);
        var inputEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
        _commands.Clear();
        Dispatch(new BEvent(EventType.Repaint) { mousePosition = _mousePosition, modifiers = _modifiers },
            frameWidth, frameHeight, frameScale);
        var repaintEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
        _canvas.Render(_commands, frameWidth, frameHeight);
        var canvasEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();

        if (_presentation is not null) _presentation.Present(); else _window.SwapBuffers();
        if (profileFrame is not null)
        {
            var frameEnded = Stopwatch.GetTimestamp();
            var sample = new ImGuiNativeFrameProfile(
                deltaSeconds,
                ElapsedMilliseconds(backgroundStarted, backgroundEnded),
                ElapsedMilliseconds(backgroundEnded, layoutEnded),
                ElapsedMilliseconds(layoutEnded, inputEnded),
                ElapsedMilliseconds(inputEnded, repaintEnded),
                ElapsedMilliseconds(repaintEnded, canvasEnded),
                ElapsedMilliseconds(canvasEnded, frameEnded),
                ElapsedMilliseconds(frameStarted, frameEnded));
            EditorCallbackDispatcher.Invoke(profileFrame, sample, nameof(frameProfiled));
        }
        if (_firstFrame) return;
        _firstFrame = true;
        var callback = firstFrameRendered;
        firstFrameRendered = null;
        EditorCallbackDispatcher.Invoke(callback, nameof(firstFrameRendered));
    }

    private static double ElapsedMilliseconds(long started, long ended) =>
        (ended - started) * 1000d / Stopwatch.Frequency;

    private void Dispatch(BEvent evt, int frameWidth, int frameHeight, Fix64 frameScale)
    {
        try
        {
            GUIUtility.devicePixelsPerPoint = frameScale;
            GUI.BeginFrame(evt, frameWidth, frameHeight, _commands);
            BEvent.BindQueue(PopQueuedEvent, () => _events.Count);
            EditorCallbackDispatcher.Invoke(gui, nameof(gui));
        }
        catch (ExitGUIException) { }
        catch (Exception exception) { EditorFeatureGuard.Report("Native IMGUI frame", exception); }
        finally
        {
            ApplyMouseCursor(GUI.requestedMouseCursor);
            GUI.EndFrame();
        }
    }

    private static Fix64 DevicePixelsPerPoint(
        int framebufferWidth, int framebufferHeight, int windowWidth, int windowHeight)
    {
        var horizontal = (Fix64)framebufferWidth / Math.Max(1, windowWidth);
        var vertical = (Fix64)framebufferHeight / Math.Max(1, windowHeight);
        return Fix64.Clamp(Fix64.Min(horizontal, vertical), Fix64.FromDecimal(0.5m), (Fix64)4);
    }

    private void ApplyMouseCursor(MouseCursor cursor)
    {
        if (_primaryMouse is not { } mouse || _appliedMouseCursor == cursor) return;
        var standard = ResolveStandardCursor(cursor);
        try
        {
            if (!mouse.Cursor.IsSupported(standard)) standard = StandardCursor.Arrow;
            mouse.Cursor.Type = CursorType.Standard;
            mouse.Cursor.StandardCursor = standard;
            _appliedMouseCursor = cursor;
        }
        catch (NotSupportedException)
        {
            _appliedMouseCursor = null;
        }
    }

    private static StandardCursor ResolveStandardCursor(MouseCursor cursor) => cursor switch
    {
        MouseCursor.Text => StandardCursor.IBeam,
        MouseCursor.ResizeVertical or MouseCursor.SplitResizeUpDown => StandardCursor.VResize,
        MouseCursor.ResizeHorizontal or MouseCursor.SplitResizeLeftRight => StandardCursor.HResize,
        MouseCursor.ResizeUpRight => StandardCursor.NeswResize,
        MouseCursor.ResizeUpLeft => StandardCursor.NwseResize,
        MouseCursor.Link or MouseCursor.Pan => StandardCursor.Hand,
        MouseCursor.MoveArrow => StandardCursor.ResizeAll,
        _ => StandardCursor.Arrow
    };

    private BEvent? PopQueuedEvent() => _events.TryDequeue(out var queued) ? queued : null;

    private void OnMouseMove(IMouse _, System.Numerics.Vector2 position)
    {
        _lastMousePosition = _mousePosition;
        _mousePosition = new BVector2((Fix64)position.X, (Fix64)position.Y);
        var type = ResolveMouseMoveEventType(anyNavigationMouseButtonPressed);
        Enqueue(new BEvent(type) { mousePosition = _mousePosition,
            delta = _mousePosition - _lastMousePosition, modifiers = _modifiers,
            pointerType = PointerType.Mouse });
    }

    internal static EventType ResolveMouseMoveEventType(bool anyMouseButtonPressed)
    {
        if (GUIUtility.hotControl != 0 && !anyMouseButtonPressed)
            GUIUtility.hotControl = 0;
        return anyMouseButtonPressed ? EventType.MouseDrag : EventType.MouseMove;
    }

    private void OnMouseDown(IMouse _, MouseButton button)
    {
        var now = Environment.TickCount64;
        var near = Math.Abs((double)(_mousePosition.x - _lastClickPosition.x)) < 4 &&
                   Math.Abs((double)(_mousePosition.y - _lastClickPosition.y)) < 4;
        _clickCount = now - _lastClickTime < 350 && near ? _clickCount + 1 : 1;
        _lastClickTime = now; _lastClickPosition = _mousePosition;
        Enqueue(new BEvent(button == MouseButton.Right ? EventType.ContextClick : EventType.MouseDown)
        {
            mousePosition = _mousePosition, button = ToButton(button), clickCount = _clickCount,
            modifiers = _modifiers, pointerType = PointerType.Mouse
        });
    }

    private void OnMouseUp(IMouse _, MouseButton button) => Enqueue(new BEvent(EventType.MouseUp)
    {
        mousePosition = _mousePosition, button = ToButton(button), clickCount = _clickCount,
        modifiers = _modifiers, pointerType = PointerType.Mouse
    });

    private void OnMouseScroll(IMouse _, ScrollWheel wheel) => Enqueue(new BEvent(EventType.ScrollWheel)
    {
        mousePosition = _mousePosition, delta = new BVector2((Fix64)wheel.X, (Fix64)(-wheel.Y)),
        modifiers = _modifiers, pointerType = PointerType.Mouse
    });

    private void OnKeyDown(IKeyboard keyboard, Key key, int _)
    {
        UpdateModifiers(keyboard);
        var keyCode = MapKey(key);
        _keyboardRepeat.KeyDown(keyCode, Stopwatch.GetTimestamp());
        Enqueue(new BEvent(EventType.KeyDown) { keyCode = keyCode, modifiers = _modifiers,
            mousePosition = _mousePosition });
    }
    private void OnKeyUp(IKeyboard keyboard, Key key, int _)
    {
        UpdateModifiers(keyboard);
        var keyCode = MapKey(key);
        _keyboardRepeat.KeyUp(keyCode);
        Enqueue(new BEvent(EventType.KeyUp) { keyCode = keyCode, modifiers = _modifiers,
            mousePosition = _mousePosition });
    }
    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        UpdateModifiers(keyboard);
        Enqueue(new BEvent(EventType.KeyDown) { character = character, modifiers = _modifiers,
            mousePosition = _mousePosition });
    }
    private void UpdateModifiers(IKeyboard keyboard)
    {
        _modifiers = EventModifiers.None;
        if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight)) _modifiers |= EventModifiers.Shift;
        if (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)) _modifiers |= EventModifiers.Control;
        if (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight)) _modifiers |= EventModifiers.Alt;
        if (keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight)) _modifiers |= EventModifiers.Command;
    }
    private void Enqueue(BEvent evt)
    {
        _events.Enqueue(evt);
        _frameScheduler.RequestRender();
    }
    private static int ToButton(MouseButton button) => button switch
    { MouseButton.Left => 0, MouseButton.Right => 1, MouseButton.Middle => 2, MouseButton.Button4 => 3, MouseButton.Button5 => 4, _ => 0 };

    private static KeyCode MapKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return (KeyCode)((int)KeyCode.A + (int)(key - Key.A));
        if (key is >= Key.Number0 and <= Key.Number9) return (KeyCode)((int)KeyCode.Alpha0 + (int)(key - Key.Number0));
        return key switch
        {
            Key.Space => KeyCode.Space, Key.Enter or Key.KeypadEnter => KeyCode.Return,
            Key.Escape => KeyCode.Escape, Key.Tab => KeyCode.Tab, Key.Backspace => KeyCode.Backspace,
            Key.Delete => KeyCode.Delete, Key.Home => KeyCode.Home, Key.End => KeyCode.End,
            Key.PageUp => KeyCode.PageUp, Key.PageDown => KeyCode.PageDown,
            Key.ShiftLeft => KeyCode.LeftShift, Key.ShiftRight => KeyCode.RightShift,
            Key.ControlLeft => KeyCode.LeftControl, Key.ControlRight => KeyCode.RightControl,
            Key.AltLeft => KeyCode.LeftAlt, Key.AltRight => KeyCode.RightAlt,
            Key.Up => KeyCode.UpArrow, Key.Down => KeyCode.DownArrow, Key.Left => KeyCode.LeftArrow,
            Key.Right => KeyCode.RightArrow, Key.F1 => KeyCode.F1, Key.F2 => KeyCode.F2, Key.F3 => KeyCode.F3,
            Key.F4 => KeyCode.F4, Key.F5 => KeyCode.F5, Key.F6 => KeyCode.F6, Key.F7 => KeyCode.F7,
            Key.F8 => KeyCode.F8, Key.F9 => KeyCode.F9, Key.F10 => KeyCode.F10, Key.F11 => KeyCode.F11,
            Key.F12 => KeyCode.F12, _ => KeyCode.None
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y) { X = x; Y = y; }
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "ClientToScreen", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreenNative(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "ScreenToClient", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClientNative(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessageNative(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
