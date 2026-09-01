using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.Rendering.Rhi.Vulkan;
using BEngine.ProjectSystem;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.DependencyInjection;
using BEngine.SceneManagement;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

internal sealed class PlayerApplication : IDisposable
{
    private readonly IWindow _window;
    private readonly ISceneRuntimeFactory _sceneRuntimeFactory;
    private readonly IRuntimeSceneManager _sceneManager;
    private readonly List<SceneRuntime> _runtimes = [];
    private Scene _scene = null!;
    private readonly bool _uiElementsEnabled;
    private readonly GraphicsBackend _preferredBackend;
    private GL? _gl;
    private EngineRenderer? _renderer;
    private PortableSceneRenderer? _portableRenderer;
    private IGraphicsPresentationDevice? _presentationDevice;
    private IInputContext? _input;
    private IMouse? _primaryMouse;
    private System.Numerics.Vector2? _lastMousePosition;
    private bool? _appliedCursorVisible;
    private CursorLockMode? _appliedCursorLockState;
    private bool _runtimeStarted;
    private ServiceProvider? _ownedServices;
    private bool _disposed;

    public PlayerApplication(string projectPath) : this(CreateStandaloneServices(projectPath)) { }

    internal PlayerApplication(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        IServiceProvider services,
        ISceneRuntimeFactory sceneRuntimeFactory,
        IRuntimeSceneManager sceneManager,
        PlayerAssetBundleBootstrap? assetBundles = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(services);
        _sceneRuntimeFactory = sceneRuntimeFactory ?? throw new ArgumentNullException(nameof(sceneRuntimeFactory));
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        PlayerAssetEnvironment.Initialize(workspace);
        Directory.SetCurrentDirectory(workspace.RootPath);
        RegisterCoreResourceRoot();
        _uiElementsEnabled = true;
        _preferredBackend = GraphicsBackendDefaults.Parse(projectSettings.GraphicsBackend);
        GraphicsBackendSettings.PreferredBackend = _preferredBackend;
        Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
        assetBundles?.InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        _sceneManager.SceneLoaded += OnSceneLoaded;
        _sceneManager.SceneUnloaded += OnSceneUnloaded;
        _sceneManager.ActiveSceneChanged += OnActiveSceneChanged;
        _sceneManager.LoadScene(workspace.StartupScenePath, LoadSceneMode.Single);

        var options = WindowOptions.Default;
        options.Title = workspace.Project.Window.Title;
        options.Size = new Vector2D<int>(workspace.Project.Window.Width, workspace.Project.Window.Height);
        options.VSync = workspace.Project.Window.VSync;
        options.ShouldSwapAutomatically = false;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
            ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
    }

    private PlayerApplication((ProjectWorkspace Workspace, ServiceProvider Services) startup)
        : this(
            startup.Workspace,
            startup.Services.GetRequiredService<ProjectSettingsData>(),
            startup.Services,
            startup.Services.GetRequiredService<ISceneRuntimeFactory>(),
            startup.Services.GetRequiredService<IRuntimeSceneManager>(),
            startup.Services.GetService<PlayerAssetBundleBootstrap>()) =>
        _ownedServices = startup.Services;

    private static (ProjectWorkspace Workspace, ServiceProvider Services) CreateStandaloneServices(
        string projectPath)
    {
        var services = new ServiceCollection().AddBEnginePlayer(projectPath);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        return (provider.GetRequiredService<ProjectWorkspace>(), provider);
    }

    private static void RegisterCoreResourceRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
                 directory = directory.Parent)
            {
                var coreRoot = Path.Combine(directory.FullName, "src", "Core");
                if (!Directory.Exists(Path.Combine(coreRoot, "Resources"))) continue;
                Resources.RegisterResourceRoot(coreRoot);
                return;
            }
        }
    }

    public void Run() => _window.Run();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sceneManager.SceneLoaded -= OnSceneLoaded;
        _sceneManager.SceneUnloaded -= OnSceneUnloaded;
        _sceneManager.ActiveSceneChanged -= OnActiveSceneChanged;
        StopAllRuntimes();
        _portableRenderer?.Dispose();
        _presentationDevice = null;
        _renderer?.Dispose();
        _input?.Dispose();
        _window.Dispose();
        foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            _sceneManager.UnregisterScene(scene, disposeScene: true);
        var ownedServices = _ownedServices;
        _ownedServices = null;
        ownedServices?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnLoad()
    {
        WindowIcon.Apply(_window);
        if (_preferredBackend == GraphicsBackend.Vulkan)
        {
            try
            {
                CreateVulkanRenderer();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Vulkan player initialization failed; falling back to OpenGL: {exception.Message}");
                _portableRenderer?.Dispose();
                _portableRenderer = null;
                _presentationDevice = null;
            }
        }
        if (_portableRenderer is null) CreateOpenGlRenderer();
        Debug.Log($"Active graphics API: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
        InitializeInput();
        _runtimeStarted = true;
        foreach (var runtime in _runtimes.ToArray()) runtime.Start();
    }

    private void CreateVulkanRenderer()
    {
        var win32 = _window.Native?.Win32 ??
                    throw new PlatformNotSupportedException("The Vulkan player currently requires a Win32 window.");
        var devices = new GraphicsDeviceFactory();
        var size = _window.FramebufferSize;
        devices.RegisterProvider(new VulkanGraphicsDeviceProvider(
            win32.Hwnd,
            win32.HInstance,
            Math.Max(1, size.X),
            Math.Max(1, size.Y),
            _window.VSync));
        var device = devices.CreateDevice(GraphicsBackend.Vulkan);
        try
        {
            _presentationDevice = device as IGraphicsPresentationDevice ??
                                  throw new InvalidOperationException("The Vulkan provider did not create a presentation device.");
            _portableRenderer = new PortableSceneRenderer(device, ownsDevice: true);
        }
        catch
        {
            _presentationDevice = null;
            device.Dispose();
            throw;
        }
    }

    private void CreateOpenGlRenderer()
    {
        _gl = GL.GetApi(_window);
        var graphicsDevices = new GraphicsDeviceFactory();
        graphicsDevices.RegisterProvider(new OpenGlGraphicsDeviceProvider(_gl));
        _renderer = new EngineRenderer(
            graphicsDevices.CreateDevice(GraphicsBackend.OpenGL),
            ownsGraphicsDevice: true);
    }

    private void InitializeInput()
    {
        _input = _window.CreateInput();
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in _input.Mice)
        {
            _primaryMouse ??= mouse;
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnMouseScroll;
        }
        ApplyCursorState();
    }

    private void OnUpdate(double deltaSeconds)
    {
        ApplyCursorState();
        PollMouseInput();
        foreach (var runtime in _runtimes.ToArray()) runtime.Tick((Fix64)deltaSeconds);
    }

    private void OnRender(double _)
    {
        if (_renderer is null && _portableRenderer is null)
        {
            return;
        }

        var size = _window.FramebufferSize;
        Screen.SetResolution(Math.Max(1, size.X), Math.Max(1, size.Y),
            _window.WindowState == WindowState.Fullscreen);
        var cameras = EngineRenderer.ResolveGameCameras(_sceneManager.LoadedScenes);
        if (_presentationDevice is not null && _portableRenderer is not null)
        {
            _presentationDevice.BeginFrame(size.X, size.Y);
            _portableRenderer.RenderCameras(_sceneManager.LoadedScenes, _scene, cameras,
                new GraphicsRect(0, 0, size.X, size.Y), _uiElementsEnabled);
            _presentationDevice.Present();
        }
        else
        {
            _renderer!.RenderCameras(_sceneManager.LoadedScenes, _scene, cameras,
                size.X, size.Y, _uiElementsEnabled);
            _window.SwapBuffers();
        }
    }

    private void OnClosing()
    {
        Application.Quit();
        StopAllRuntimes();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        _scene = scene;
        if (_runtimes.Any(item => ReferenceEquals(item.Scene, scene))) return;
        var runtime = _sceneRuntimeFactory.Create(scene);
        _runtimes.Add(runtime);
        if (_runtimeStarted) runtime.Start();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        _runtimes.RemoveAll(item => ReferenceEquals(item.Scene, scene));
    }

    private void OnActiveSceneChanged(Scene? _, Scene? current)
    {
        if (current is not null) _scene = current;
    }

    private void StopAllRuntimes()
    {
        for (var index = _runtimes.Count - 1; index >= 0; index--) _runtimes[index].Stop();
        _runtimeStarted = false;
    }

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
        if (_primaryMouse is not { } mouse) return;
        SetMousePosition(mouse.Position);
        Input.SetKeyState(KeyCode.Mouse0, mouse.IsButtonPressed(MouseButton.Left));
        Input.SetKeyState(KeyCode.Mouse1, mouse.IsButtonPressed(MouseButton.Right));
        Input.SetKeyState(KeyCode.Mouse2, mouse.IsButtonPressed(MouseButton.Middle));
    }

    private void ApplyCursorState()
    {
        if (_primaryMouse is not { } mouse ||
            _appliedCursorVisible == Cursor.visible && _appliedCursorLockState == Cursor.lockState) return;
        _appliedCursorVisible = Cursor.visible;
        _appliedCursorLockState = Cursor.lockState;
        var mode = Cursor.lockState == CursorLockMode.Locked
            ? Silk.NET.Input.CursorMode.Disabled
            : Cursor.visible ? Silk.NET.Input.CursorMode.Normal : Silk.NET.Input.CursorMode.Hidden;
        try
        {
            mouse.Cursor.IsConfined = Cursor.lockState == CursorLockMode.Confined;
            if (mouse.Cursor.IsSupported(mode)) mouse.Cursor.CursorMode = mode;
        }
        catch (NotSupportedException)
        {
            mouse.Cursor.IsConfined = false;
            if (mouse.Cursor.IsSupported(Silk.NET.Input.CursorMode.Normal))
                mouse.Cursor.CursorMode = Silk.NET.Input.CursorMode.Normal;
        }
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
