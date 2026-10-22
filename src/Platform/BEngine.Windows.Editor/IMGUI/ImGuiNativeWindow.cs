using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BEngine.Build;
using BEngine.Editor.Rendering;
using BEngine.Platform;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using BEvent = BEngine.Editor.Event;
using BVector2 = BEngine.Vector2;

namespace BEngine.Editor;

/// <summary>Native window that executes Unity-style immediate GUI and submits it through the BEngine RHI.</summary>
public sealed class ImGuiNativeWindow : IEditorNativeWindow
{
    private readonly IWindow _window;
    private readonly SilkGraphicsSession _graphicsSession;
    private bool _loaded;
    private readonly bool _visibleOnLoad;
    private WindowState _windowStateOnLoad = WindowState.Normal;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly GraphicsBackend _requestedBackend;
    private readonly bool _vsync;
    private bool? _appliedVSync;
    private readonly NativeWindowFrameScheduler _frameScheduler = new();
    private readonly NativeKeyboardRepeat _keyboardRepeat = NativeKeyboardRepeat.CreateSystemDefault();
    private readonly ConcurrentQueue<BEvent> _events = new();
    private readonly List<GpuCanvasCommand> _commands = [];
    private IInputContext? _input;
    private SilkGamepadInput? _gamepads;
    private ITouchInputSource? _touchInput;
    private IGraphicsDevice? _device;
    private IGraphicsPresentationDevice? _presentation;
    private GpuCanvasRenderer? _canvas;
    private IMouse? _primaryMouse;
    private MouseCursor? _appliedMouseCursor;
    private bool _disposed;
    private bool _rendering;
    private bool _firstFrame;
    private bool _isFocused;
    private bool _focusInitialized;
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
    public event Action<ImGuiNativeFrameProfile>? frameProfiled;
    public Action<IGraphicsDevice, int, int>? renderBackground { get; set; }
    public bool isClosing => _window.IsClosing;
    public GraphicsBackend backend => _device?.Backend ?? _requestedBackend;
    public int width => Math.Max(1, _window.FramebufferSize.X);
    public int height => Math.Max(1, _window.FramebufferSize.Y);
    public Fix64 renderScale { get; private set; } = Fix64.One;
    public Fix64 deviceScale { get; private set; } = Fix64.One;
    public GameViewFrameTimingSnapshot frameTiming => _frameTiming.snapshot;
    public IGraphicsDevice? graphicsDevice => _device;
    public IntPtr nativeHandle => _window.Native?.Win32?.Hwnd ?? IntPtr.Zero;
    public BVector2 screenPosition => new(_window.Position.X, _window.Position.Y);
    public BVector2 windowSize => new(_window.Size.X, _window.Size.Y);
    public bool isMaximized => _window.WindowState == WindowState.Maximized;
    public bool isFocused => _isFocused;
    public bool leftMouseButtonPressed => _platformService.IsWindows
        ? (GetAsyncKeyState(0x01) & 0x8000) != 0
        : _primaryMouse?.IsButtonPressed(MouseButton.Left) == true;
    public NativeWindowPointerOperation pointerOperation => ResolvePointerOperation();
    private bool anyNavigationMouseButtonPressed => _platformService.IsWindows
        ? (GetAsyncKeyState(0x01) & 0x8000) != 0 ||
          (GetAsyncKeyState(0x02) & 0x8000) != 0 ||
          (GetAsyncKeyState(0x04) & 0x8000) != 0
        : _primaryMouse?.IsButtonPressed(MouseButton.Left) == true ||
          _primaryMouse?.IsButtonPressed(MouseButton.Right) == true ||
          _primaryMouse?.IsButtonPressed(MouseButton.Middle) == true;

    public ImGuiNativeWindow(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        string title,
        int width,
        int height,
        bool visible = true,
        bool vsync = true, int minimumWidth = 320, int minimumHeight = 200,
        int maximumWidth = int.MaxValue, int maximumHeight = int.MaxValue)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _vsync = vsync;
        _minimumSize = new Vector2D<int>(Math.Max(1, minimumWidth), Math.Max(1, minimumHeight));
        _maximumSize = new Vector2D<int>(Math.Max(_minimumSize.X, maximumWidth),
            Math.Max(_minimumSize.Y, maximumHeight));
        WindowsEditorTaskbar.InitializeProcess();
        _visibleOnLoad = visible;
        var options = WindowOptions.Default;
        options.Title = title;
        options.Size = ConstrainSize(width, height);
        options.VSync = _vsync;
        options.ShouldSwapAutomatically = false;
        // The shell queries the icon at the first ShowWindow, before Silk's Load
        // callback. Finish assigning native icons before making the window visible.
        options.IsVisible = false;
        options.WindowBorder = WindowBorder.Resizable;
        var target = BuildTargetManifestSerializer.LoadCurrent(null, _fileSystem, _pathService, _platformService);
        (_window, _graphicsSession) = SilkWindowFactory.CreateVerified(options,
            GraphicsBackendSelector.GetCandidates(target, GraphicsBackendSettings.PreferredBackend), session =>
            {
                var device = EditorInstrumentation.Decorate(session.Device);
                try { _canvas = new GpuCanvasRenderer(device, resourceResolver: EditorGpuCanvasResourceResolver.Shared); }
                catch { device.Dispose(); throw; }
                _device = device;
                _presentation = session.Device as IGraphicsPresentationDevice;
            });
        _requestedBackend = _graphicsSession.Device.Backend;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.FocusChanged += OnFocusChanged;
    }

    public void Run() { Initialize(); _window.Run(); }
    public void Initialize()
    {
        if (_loaded || _disposed) return;
        _loaded = true;
        OnLoad();
    }
    public void Pump()
    {
        if (_rendering)
        {
            _frameScheduler.RequestRender();
            return;
        }
        Initialize();
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
    public void CancelCloseRequest()
    {
        if (!_disposed) _window.IsClosing = false;
    }
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
        SetWindowState(maximized ? WindowState.Maximized : WindowState.Normal);
    public void Minimize() => SetWindowState(WindowState.Minimized);

    private void SetWindowState(WindowState state)
    {
        if (!_loaded) _windowStateOnLoad = state;
        _window.WindowState = state;
    }

    private Vector2D<int> ConstrainSize(int width, int height) => new(
        Math.Clamp(width, _minimumSize.X, _maximumSize.X),
        Math.Clamp(height, _minimumSize.Y, _maximumSize.Y));

    public BVector2 ClientToScreen(BVector2 point)
    {
        if (_platformService.IsWindows && nativeHandle != IntPtr.Zero)
        {
            var nativePoint = new NativePoint((int)point.x, (int)point.y);
            if (ClientToScreenNative(nativeHandle, ref nativePoint))
                return new BVector2(nativePoint.X, nativePoint.Y);
        }
        return screenPosition + point;
    }

    public BVector2 ScreenToClient(BVector2 point)
    {
        if (_platformService.IsWindows && nativeHandle != IntPtr.Zero)
        {
            var nativePoint = new NativePoint((int)point.x, (int)point.y);
            if (ScreenToClientNative(nativeHandle, ref nativePoint))
                return new BVector2(nativePoint.X, nativePoint.Y);
        }
        return point - screenPosition;
    }

    public BVector2 GUIToScreen(BVector2 point, Fix64 editorScale) =>
        ClientToScreen(GUIToClient(point, editorScale, deviceScale));

    public BVector2 ScreenToGUI(BVector2 point, Fix64 editorScale) =>
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

    public static bool TryGetPointerScreenPosition(out BVector2 point) =>
        TryGetPointerScreenPosition(
            EditorServiceAccess.GetRequiredService<PlatformUtility>(), out point);

    internal static bool TryGetPointerScreenPosition(
        PlatformUtility platformService,
        out BVector2 point)
    {
        ArgumentNullException.ThrowIfNull(platformService);
        if (platformService.IsWindows && GetCursorPos(out var nativePoint))
        {
            point = new BVector2(nativePoint.X, nativePoint.Y);
            return true;
        }
        point = default;
        return false;
    }

    private NativeWindowPointerOperation ResolvePointerOperation()
    {
        if (!_platformService.IsWindows || nativeHandle == IntPtr.Zero ||
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
        ReleaseNativeResources();
        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnClosing()
    {
        EditorCallbackDispatcher.Invoke(closing, nameof(closing));
        // Run() resets GLFW after Closing. Unregister input callbacks while its
        // native handle is still alive, and only after save/cancel handlers finish.
        if (_window.IsClosing) ReleaseNativeResources();
    }

    private void ReleaseNativeResources()
    {
        if (_window.IsInitialized && _window.GLContext is { IsCurrent: false } context) context.MakeCurrent();
        var input = _input;
        _gamepads?.Dispose(); _gamepads = null;
        _touchInput?.Dispose(); _touchInput = null;
        _input = null;
        _primaryMouse = null;
        input?.Dispose();
        _canvas?.Dispose(); _canvas = null;
        _device?.Dispose(); _device = null; _presentation = null;
        _graphicsSession.Dispose();
    }

    private void OnLoad()
    {
        if (_window.GLContext is { IsCurrent: false } context) context.MakeCurrent();
        WindowIcon.Apply(_window);
        if (_platformService.IsWindows) WindowsEditorTaskbar.Apply(nativeHandle);
        if (_visibleOnLoad)
        {
            _window.IsVisible = true;
            // Silk skips the initial window state when created hidden. Apply any
            // restored maximize/minimize state once the icons are ready as well.
            if (_windowStateOnLoad != WindowState.Normal) _window.WindowState = _windowStateOnLoad;
        }
        _input = _window.CreateInput();
        _gamepads = new SilkGamepadInput(_input, () => _isFocused);
        _touchInput = RuntimeServiceAccess.GetService<TouchInputUtility>()?.Attach(nativeHandle, suppressEmulatedMouse: false);
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
        SynchronizeNativeFocus();
    }


    private void SynchronizeNativeFocus()
    {
        // Creation can focus the window before GLFW registers its callbacks. A stale false
        // value makes Game View clear hover and release held buttons on every editor update.
        if (_disposed || !_platformService.IsWindows || nativeHandle == IntPtr.Zero) return;
        OnFocusChanged(GetForegroundWindow() == nativeHandle);
    }

    private void OnFocusChanged(bool focused)
    {
        if (_focusInitialized && _isFocused == focused) return;
        _focusInitialized = true;
        _isFocused = focused;
        if (!focused)
        {
            _gamepads?.ReleaseInput();
            _keyboardRepeat.Clear();
            if (_window.IsInitialized)
                RuntimeServiceAccess.GetService<TextInputUtility>()?.SetCaret(nativeHandle, null);
        }
        Enqueue(new BEvent(focused ? EventType.MouseEnterWindow : EventType.MouseLeaveWindow)
        {
            mousePosition = _mousePosition
        });
        EditorCallbackDispatcher.Invoke(focusChanged, focused, nameof(focusChanged));
    }

    private void OnUpdate(double delta)
    {
        SynchronizeNativeFocus();
        _gamepads?.Poll();
        if (_touchInput is not null)
        {
            var windowWidth = Math.Max(1, _window.Size.X); var windowHeight = Math.Max(1, _window.Size.Y);
            foreach (var touch in _touchInput.Poll(windowWidth, windowHeight, (Fix64)Math.Max(0, delta)))
                Enqueue(new BEvent(touch.phase switch
                { TouchPhase.Began => EventType.TouchDown, TouchPhase.Ended or TouchPhase.Canceled => EventType.TouchUp, _ => EventType.TouchMove })
                {
                    mousePosition = new(touch.position.x, windowHeight - touch.position.y),
                    delta = new(touch.deltaPosition.x, -touch.deltaPosition.y), pressure = touch.pressure,
                    pointerId = touch.fingerId, pointerType = PointerType.Touch, pointerCanceled = touch.phase == TouchPhase.Canceled,
                    clickCount = touch.tapCount, modifiers = _modifiers
                });
        }
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
        if (_disposed || _device is null || _canvas is null) return;
        // GLFW can invoke its refresh callback while a native call below pumps
        // messages. Finish this frame before servicing that repaint request.
        if (_rendering)
        {
            _frameScheduler.RequestRender();
            return;
        }
        _rendering = true;
        try { RenderFrame(deltaSeconds); }
        catch (Exception exception) { EditorFeatureGuard.Report("Native IMGUI presentation", exception); }
        finally { _rendering = false; }
    }

    private void RenderFrame(double deltaSeconds)
    {
        if (_window.GLContext is { IsCurrent: false } context) context.MakeCurrent();
        var profileFrame = EditorInstrumentation.MeasureFrames ? frameProfiled : null;
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
        var vsync = _vsync && RenderSettingsUtility.Current.vSyncCount > 0;
        if (_appliedVSync != vsync)
        {
            if (_presentation is not null) _presentation.SetVSync(vsync);
            else _window.VSync = vsync;
            _appliedVSync = vsync;
        }
        _presentation?.BeginFrame(frameWidth, frameHeight);
        long backgroundStarted, backgroundEnded, layoutEnded, inputEnded, repaintEnded, canvasEnded;
        try
        {
            _device!.SetViewport(new GraphicsRect(0, 0, frameWidth, frameHeight));
            var background = EditorStyles.viewBackground.normal.backgroundColor;
            _device.Clear(GraphicsClearFlags.Color | GraphicsClearFlags.Depth,
                new System.Numerics.Vector4((float)background.r, (float)background.g,
                    (float)background.b, (float)background.a));
            backgroundStarted = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
            if (renderBackground is not null)
            {
                foreach (Action<IGraphicsDevice, int, int> backgroundCallback in Delegate.EnumerateInvocationList(renderBackground))
                    EditorFeatureGuard.Invoke(
                        $"Render background {backgroundCallback.Method.DeclaringType?.FullName}." +
                        backgroundCallback.Method.Name,
                        () => backgroundCallback(_device, frameWidth, frameHeight));
            }
            backgroundEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();

            Dispatch(new BEvent(EventType.Layout) { mousePosition = _mousePosition, modifiers = _modifiers },
                frameWidth, frameHeight, frameScale);
            layoutEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
            while (_events.TryDequeue(out var inputEvent))
                Dispatch(inputEvent, frameWidth, frameHeight, frameScale);
            inputEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
            _commands.Clear();
            Dispatch(new BEvent(EventType.Repaint) { mousePosition = _mousePosition, modifiers = _modifiers },
                frameWidth, frameHeight, frameScale);
            repaintEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
            _canvas!.Render(_commands, frameWidth, frameHeight);
            canvasEnded = profileFrame is null ? 0 : Stopwatch.GetTimestamp();
        }
        catch (Exception exception)
        {
            EditorFeatureGuard.Report("Native IMGUI rendering", exception);
            return;
        }
        finally
        {
            // A failed canvas upload or draw must not leave a begun frame open.
            if (_presentation is not null) _presentation.Present(); else _window.SwapBuffers();
        }
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
            GUI.SetTooltipOwner(this);
            GUI.BeginFrame(evt, frameWidth, frameHeight, _commands);
            BEvent.BindQueue(PopQueuedEvent, () => _events.Count);
            EditorCallbackDispatcher.Invoke(gui, nameof(gui));
        }
        catch (ExitGUIException) { }
        catch (Exception exception) { EditorFeatureGuard.Report("Native IMGUI frame", exception); }
        finally
        {
            ApplyMouseCursor(GUI.requestedMouseCursor);
            if (evt.rawType == EventType.Repaint)
            {
                var caret = GUI.textInputRect;
                if (caret is { } rect)
                    caret = new Rect(rect.x / frameScale, rect.y / frameScale, rect.width / frameScale, rect.height / frameScale);
                RuntimeServiceAccess.GetService<TextInputUtility>()?.SetCaret(nativeHandle, caret);
            }
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
            pointerType = NativeMousePointerType });
    }

    internal static EventType ResolveMouseMoveEventType(bool anyMouseButtonPressed)
    {
        if (GUIUtility.hotControl != 0 && !anyMouseButtonPressed)
            GUIUtility.hotControl = 0;
        return anyMouseButtonPressed ? EventType.MouseDrag : EventType.MouseMove;
    }

    private void OnMouseDown(IMouse _, MouseButton button)
    {
        var now = _platformService.TickCount64;
        var near = Math.Abs((double)(_mousePosition.x - _lastClickPosition.x)) < 4 &&
                   Math.Abs((double)(_mousePosition.y - _lastClickPosition.y)) < 4;
        _clickCount = now - _lastClickTime < 350 && near ? _clickCount + 1 : 1;
        _lastClickTime = now; _lastClickPosition = _mousePosition;
        Enqueue(new BEvent(button == MouseButton.Right ? EventType.ContextClick : EventType.MouseDown)
        {
            mousePosition = _mousePosition, button = ToButton(button), clickCount = _clickCount,
            modifiers = _modifiers, pointerType = NativeMousePointerType
        });
    }

    private void OnMouseUp(IMouse _, MouseButton button) => Enqueue(new BEvent(EventType.MouseUp)
    {
        mousePosition = _mousePosition, button = ToButton(button), clickCount = _clickCount,
        modifiers = _modifiers, pointerType = NativeMousePointerType
    });

    private void OnMouseScroll(IMouse _, ScrollWheel wheel) => Enqueue(new BEvent(EventType.ScrollWheel)
    {
        mousePosition = _mousePosition, delta = new BVector2((Fix64)wheel.X, (Fix64)(-wheel.Y)),
        modifiers = _modifiers, pointerType = PointerType.Mouse
    });

    private void OnKeyDown(IKeyboard keyboard, Key key, int _)
    {
        if (RuntimeServiceAccess.GetService<TextInputUtility>()?.IsComposing(nativeHandle) == true) return;
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
    private static PointerType NativeMousePointerType =>
        ((nuint)GetMessageExtraInfo() & 0xffffff00u) == 0xff515700u ? PointerType.Touch : PointerType.Mouse;
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
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

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessageNative(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

}
