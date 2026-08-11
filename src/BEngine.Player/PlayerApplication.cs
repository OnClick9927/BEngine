using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace BEngine.Player;

internal sealed class PlayerApplication : IDisposable
{
    private readonly IWindow _window;
    private readonly Scene _scene;
    private readonly SceneRuntime _runtime;
    private readonly bool _uiElementsEnabled;
    private readonly bool _terrainEnabled;
    private GL? _gl;
    private EngineRenderer? _renderer;
    private IInputContext? _input;
    private System.Numerics.Vector2? _lastMousePosition;

    public PlayerApplication(string projectPath)
    {
        var workspace = ProjectWorkspace.Open(projectPath);
        Directory.SetCurrentDirectory(workspace.RootPath);
        ProjectScriptCompiler.CompileAndLoad(workspace);
        var packages = new BPackageManager(workspace);
        _uiElementsEnabled = packages.IsEnabled("com.bengine.ui-elements");
        _terrainEnabled = packages.IsEnabled("com.bengine.terrain");
        _scene = new YamlSceneSerializer().Load(workspace.StartupScenePath);
        Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
        _runtime = new SceneRuntime(_scene);

        var options = WindowOptions.Default;
        options.Title = workspace.Project.Window.Title;
        options.Size = new Vector2D<int>(workspace.Project.Window.Width, workspace.Project.Window.Height);
        options.VSync = workspace.Project.Window.VSync;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
            ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    public void Dispose()
    {
        _renderer?.Dispose();
        _input?.Dispose();
        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnLoad()
    {
        WindowIcon.Apply(_window);
        _gl = GL.GetApi(_window);
        var graphicsDevices = new GraphicsDeviceFactory();
        graphicsDevices.RegisterProvider(new OpenGlGraphicsDeviceProvider(_gl));
        _renderer = new EngineRenderer(
            graphicsDevices.CreateDevice(GraphicsBackend.OpenGL),
            ownsGraphicsDevice: true);
        _input = _window.CreateInput();
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnMouseScroll;
        }

        _runtime.Start();
    }

    private void OnUpdate(double deltaSeconds)
    {
        PollMouseInput();
        _runtime.Tick((Fix64)deltaSeconds);
    }

    private void OnRender(double _)
    {
        if (_renderer is null)
        {
            return;
        }

        var size = _window.FramebufferSize;
        Screen.SetResolution(Math.Max(1, size.X), Math.Max(1, size.Y),
            _window.WindowState == WindowState.Fullscreen);
        _renderer.Render(_scene, EngineRenderer.ResolveGameCamera(_scene), size.X, size.Y,
            drawUi: _uiElementsEnabled, drawTerrain: _terrainEnabled);
    }

    private void OnClosing() => _runtime.Stop();

    private void OnKeyDown(IKeyboard _, Key key, int __)
    {
        if (key == Key.Escape)
        {
            _window.Close();
        }

        if (TryMap(key, out var code))
        {
            Input.SetKeyState(code, true);
        }
    }

    private static void OnKeyUp(IKeyboard _, Key key, int __)
    {
        if (TryMap(key, out var code))
        {
            Input.SetKeyState(code, false);
        }
    }

    private void OnMouseMove(IMouse _, System.Numerics.Vector2 position)
    {
        if (_lastMousePosition is { } previous)
        {
            var delta = position - previous;
            Input.SetMouseDelta((Fix64)delta.X, (Fix64)delta.Y);
        }

        _lastMousePosition = position;
        SetMousePosition(position);
    }

    private static void OnMouseDown(IMouse _, MouseButton button) =>
        Input.SetKeyState(ToMouseKey(button), true);

    private static void OnMouseUp(IMouse _, MouseButton button) =>
        Input.SetKeyState(ToMouseKey(button), false);

    private static void OnMouseScroll(IMouse _, ScrollWheel scroll) =>
        Input.SetMouseScroll((Fix64)scroll.X, (Fix64)scroll.Y);

    private static KeyCode ToMouseKey(MouseButton button) => button switch
    {
        MouseButton.Left => KeyCode.Mouse0,
        MouseButton.Right => KeyCode.Mouse1,
        MouseButton.Middle => KeyCode.Mouse2,
        MouseButton.Button4 => KeyCode.Mouse3,
        MouseButton.Button5 => KeyCode.Mouse4,
        _ => KeyCode.Mouse0
    };

    private void PollMouseInput()
    {
        if (_input?.Mice.FirstOrDefault() is not { } mouse) return;
        SetMousePosition(mouse.Position);
        Input.SetKeyState(KeyCode.Mouse0, mouse.IsButtonPressed(MouseButton.Left));
        Input.SetKeyState(KeyCode.Mouse1, mouse.IsButtonPressed(MouseButton.Right));
        Input.SetKeyState(KeyCode.Mouse2, mouse.IsButtonPressed(MouseButton.Middle));
    }

    private void SetMousePosition(System.Numerics.Vector2 position)
    {
        var framebuffer = _window.FramebufferSize;
        var window = _window.Size;
        var scaleX = framebuffer.X / (float)Math.Max(1, window.X);
        var scaleY = framebuffer.Y / (float)Math.Max(1, window.Y);
        Input.SetMousePosition(
            (Fix64)(position.X * scaleX),
            (Fix64)((window.Y - position.Y) * scaleY));
    }

    private static bool TryMap(Key key, out KeyCode code)
    {
        code = key switch
        {
            Key.W => KeyCode.W,
            Key.A => KeyCode.A,
            Key.S => KeyCode.S,
            Key.D => KeyCode.D,
            Key.Q => KeyCode.Q,
            Key.E => KeyCode.E,
            Key.Space => KeyCode.Space,
            Key.ShiftLeft => KeyCode.LeftShift,
            Key.Up => KeyCode.UpArrow,
            Key.Down => KeyCode.DownArrow,
            Key.Left => KeyCode.LeftArrow,
            Key.Right => KeyCode.RightArrow,
            _ => default
        };
        return key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E or Key.Space or Key.ShiftLeft or
            Key.Up or Key.Down or Key.Left or Key.Right;
    }
}
