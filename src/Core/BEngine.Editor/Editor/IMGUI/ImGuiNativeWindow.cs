using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
    private BVector2 _mousePosition;
    private BVector2 _lastMousePosition;
    private EventModifiers _modifiers;
    private long _lastClickTime;
    private BVector2 _lastClickPosition;
    private int _clickCount;

    public event Action<double>? updating;
    public event Action? closing;
    public event Action? firstFrameRendered;
    public event Action? gui;
    public event Action<bool>? focusChanged;
    public Action<IGraphicsDevice, int, int>? renderBackground { get; set; }
    public bool isClosing => _window.IsClosing;
    public GraphicsBackend backend => _device?.Backend ?? _requestedBackend;
    public int width => Math.Max(1, _window.FramebufferSize.X);
    public int height => Math.Max(1, _window.FramebufferSize.Y);
    public Fix64 renderScale { get; private set; } = Fix64.One;
    public IntPtr nativeHandle => _window.Native?.Win32?.Hwnd ?? IntPtr.Zero;
    public BVector2 screenPosition => new(_window.Position.X, _window.Position.Y);
    public BVector2 windowSize => new(_window.Size.X, _window.Size.Y);
    public bool isMaximized => _window.WindowState == WindowState.Maximized;
    public bool leftMouseButtonPressed => OperatingSystem.IsWindows()
        ? (GetAsyncKeyState(0x01) & 0x8000) != 0
        : _primaryMouse?.IsButtonPressed(MouseButton.Left) == true;
    private bool anyNavigationMouseButtonPressed => OperatingSystem.IsWindows()
        ? (GetAsyncKeyState(0x01) & 0x8000) != 0 ||
          (GetAsyncKeyState(0x02) & 0x8000) != 0 ||
          (GetAsyncKeyState(0x04) & 0x8000) != 0
        : _primaryMouse?.IsButtonPressed(MouseButton.Left) == true ||
          _primaryMouse?.IsButtonPressed(MouseButton.Right) == true ||
          _primaryMouse?.IsButtonPressed(MouseButton.Middle) == true;

    public ImGuiNativeWindow(string title, int width, int height, bool visible = true)
    {
        _requestedBackend = ResolveWindowBackend(GraphicsBackendSettings.PreferredBackend);
        _vsync = true;
        var options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(Math.Max(320, width), Math.Max(200, height));
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
        _window.FocusChanged += focused => Enqueue(new BEvent(
            focused ? EventType.MouseEnterWindow : EventType.MouseLeaveWindow)
        {
            mousePosition = _mousePosition
        });
        _window.FocusChanged += focused =>
            EditorCallbackDispatcher.Invoke(focusChanged, focused, nameof(focusChanged));
    }

    public void Run() => _window.Run();
    public void Initialize() { if (!_window.IsInitialized) _window.Initialize(); }
    public void Pump()
    {
        if (!_window.IsInitialized || _window.IsClosing) return;
        _window.DoEvents(); _window.DoUpdate(); _window.DoRender();
    }
    public void Focus() => _window.Focus();
    public void Close() => _window.Close();
    public void Repaint() { }
    public void SetTitle(string title) { if (_window.Title != title) _window.Title = title; }
    public void Move(int x, int y) => _window.Position = new Vector2D<int>(x, y);
    public void Resize(int width, int height) => _window.Size = new Vector2D<int>(Math.Max(320, width), Math.Max(200, height));
    public void SetMaximized(bool maximized) =>
        _window.WindowState = maximized ? WindowState.Maximized : WindowState.Normal;

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
                _device = factory.CreateDevice(GraphicsBackend.Vulkan);
                _presentation = (IGraphicsPresentationDevice)_device;
                return;
            }
            catch (Exception exception)
            {
                _device?.Dispose(); _device = null; _presentation = null;
                throw new InvalidOperationException("Vulkan IMGUI window initialization failed.", exception);
            }
        }
        _gl = GL.GetApi(_window);
        _device = new OpenGlGraphicsDevice(_gl);
    }

    private static GraphicsBackend ResolveWindowBackend(GraphicsBackend preferred)
    {
        if (preferred != GraphicsBackend.Vulkan) return GraphicsBackend.OpenGL;
        try
        {
            if (Veldrid.GraphicsDevice.IsBackendSupported(Veldrid.GraphicsBackend.Vulkan))
                return GraphicsBackend.Vulkan;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Vulkan support could not be queried; using OpenGL: {exception.Message}");
            return GraphicsBackend.OpenGL;
        }
        Debug.LogWarning("Vulkan is unavailable; using OpenGL for the editor window.");
        return GraphicsBackend.OpenGL;
    }

    private static GraphicsAPI WindowApiForBackend(GraphicsBackend backend) =>
        backend == GraphicsBackend.Vulkan
            ? GraphicsAPI.None
            : new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
                ContextFlags.ForwardCompatible, new APIVersion(3, 3));

    private void OnUpdate(double delta) =>
        EditorCallbackDispatcher.Invoke(updating, delta, nameof(updating));

    private void OnRender(double _)
    {
        if (_device is null || _canvas is null) return;
        var framebufferSize = _window.FramebufferSize;
        var frameWidth = Math.Max(1, framebufferSize.X);
        var frameHeight = Math.Max(1, framebufferSize.Y);
        var windowSize = _window.Size;
        var frameScale = DevicePixelsPerPoint(frameWidth, frameHeight,
            Math.Max(1, windowSize.X), Math.Max(1, windowSize.Y));
        renderScale = frameScale * Fix64.Clamp(GUIUtility.pixelsPerPoint,
            Fix64.FromDecimal(0.5m), (Fix64)4);
        _presentation?.BeginFrame(frameWidth, frameHeight);
        _device.SetViewport(new GraphicsRect(0, 0, frameWidth, frameHeight));
        var background = EditorAppearance.palette.Window;
        _device.Clear(GraphicsClearFlags.Color | GraphicsClearFlags.Depth,
            new System.Numerics.Vector4((float)background.r, (float)background.g,
                (float)background.b, (float)background.a));
        if (renderBackground is not null)
        {
            foreach (Action<IGraphicsDevice, int, int> backgroundCallback in renderBackground.GetInvocationList())
                EditorFeatureGuard.Invoke(
                    $"Render background {backgroundCallback.Method.DeclaringType?.FullName}." +
                    backgroundCallback.Method.Name,
                    () => backgroundCallback(_device, frameWidth, frameHeight));
        }

        Dispatch(new BEvent(EventType.Layout) { mousePosition = _mousePosition, modifiers = _modifiers },
            false, frameWidth, frameHeight, frameScale);
        while (_events.TryDequeue(out var inputEvent))
            Dispatch(inputEvent, false, frameWidth, frameHeight, frameScale);
        _commands.Clear();
        Dispatch(new BEvent(EventType.Repaint) { mousePosition = _mousePosition, modifiers = _modifiers },
            true, frameWidth, frameHeight, frameScale);
        _canvas.Render(_commands, frameWidth, frameHeight);

        if (_presentation is not null) _presentation.Present(); else _window.SwapBuffers();
        if (_firstFrame) return;
        _firstFrame = true;
        var callback = firstFrameRendered;
        firstFrameRendered = null;
        EditorCallbackDispatcher.Invoke(callback, nameof(firstFrameRendered));
    }

    private void Dispatch(BEvent evt, bool collectCommands, int frameWidth, int frameHeight, Fix64 frameScale)
    {
        try
        {
            GUIUtility.devicePixelsPerPoint = frameScale;
            GUI.BeginFrame(evt, frameWidth, frameHeight, collectCommands ? _commands : []);
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
        var standard = cursor switch
        {
            MouseCursor.Text => StandardCursor.IBeam,
            MouseCursor.ResizeVertical or MouseCursor.SplitResizeUpDown => StandardCursor.VResize,
            MouseCursor.ResizeHorizontal or MouseCursor.SplitResizeLeftRight => StandardCursor.HResize,
            MouseCursor.ResizeUpRight => StandardCursor.NeswResize,
            MouseCursor.ResizeUpLeft => StandardCursor.NwseResize,
            MouseCursor.Link => StandardCursor.Hand,
            MouseCursor.MoveArrow or MouseCursor.Pan => StandardCursor.ResizeAll,
            _ => StandardCursor.Arrow
        };
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

    private BEvent? PopQueuedEvent() => _events.TryDequeue(out var queued) ? queued : null;

    private void OnMouseMove(IMouse _, System.Numerics.Vector2 position)
    {
        _lastMousePosition = _mousePosition;
        _mousePosition = new BVector2((Fix64)position.X, (Fix64)position.Y);
        if (GUIUtility.hotControl != 0 && !anyNavigationMouseButtonPressed)
            GUIUtility.hotControl = 0;
        var type = GUIUtility.hotControl == 0 ? EventType.MouseMove : EventType.MouseDrag;
        Enqueue(new BEvent(type) { mousePosition = _mousePosition,
            delta = _mousePosition - _lastMousePosition, modifiers = _modifiers,
            pointerType = PointerType.Mouse });
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
        Enqueue(new BEvent(EventType.KeyDown) { keyCode = MapKey(key), modifiers = _modifiers,
            mousePosition = _mousePosition });
    }
    private void OnKeyUp(IKeyboard keyboard, Key key, int _)
    {
        UpdateModifiers(keyboard);
        Enqueue(new BEvent(EventType.KeyUp) { keyCode = MapKey(key), modifiers = _modifiers,
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
    private void Enqueue(BEvent evt) => _events.Enqueue(evt);
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
}
