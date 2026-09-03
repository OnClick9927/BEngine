using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using BEngine.Rendering.Rhi.Vulkan;
using BEngine.Build;
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
    private ISceneRuntimeFactory _sceneRuntimeFactory;
    private IRuntimeSceneManager _sceneManager;
    private readonly List<SceneRuntime> _runtimes = [];
    private Scene _scene = null!;
    private readonly bool _uiElementsEnabled;
    private readonly GraphicsBackend _preferredBackend;
    private readonly BuildTargetManifest _buildTarget;
    private readonly PlayerAssetBundleBootstrap? _assetBundles;
    private readonly PlayerStagedRuntimeCoordinator? _stagedRuntime;
    private GL? _gl;
    private EngineRenderer? _renderer;
    private PortableSceneRenderer? _portableRenderer;
    private IGraphicsPresentationDevice? _presentationDevice;
    private IInputContext? _input;
    private IMouse? _primaryMouse;
    private readonly HashSet<IGamepad> _registeredGamepads = [];
    private System.Numerics.Vector2? _lastMousePosition;
    private bool? _appliedCursorVisible;
    private CursorLockMode? _appliedCursorLockState;
    private bool _runtimeStarted;
    private bool _isAotStage;
    private bool _transitioning;
    private ServiceProvider? _ownedServices;
    private Action? _windowReady;
    private bool _disposed;

    public PlayerApplication(string projectPath) : this(CreateStandaloneServices(projectPath)) { }

    internal PlayerApplication(
        ProjectWorkspace workspace,
        ProjectSettingsData projectSettings,
        IServiceProvider services,
        ISceneRuntimeFactory sceneRuntimeFactory,
        IRuntimeSceneManager sceneManager,
        PlayerAssetBundleBootstrap? assetBundles = null,
        PlayerHotUpdateSession? hotUpdate = null,
        bool initializeContent = true,
        string? startupScene = null,
        bool sceneAlreadyLoaded = false,
        bool isAotStage = false,
        PlayerStagedRuntimeCoordinator? stagedRuntime = null,
        PlayerBuiltInResourceProvider? packagedResources = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(services);
        _sceneRuntimeFactory = sceneRuntimeFactory ?? throw new ArgumentNullException(nameof(sceneRuntimeFactory));
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        _assetBundles = assetBundles;
        _stagedRuntime = stagedRuntime;
        _isAotStage = isAotStage;
        if (stagedRuntime is null)
            PlayerAssetEnvironment.Initialize(workspace, packagedResources);
        Directory.SetCurrentDirectory(workspace.RootPath);
        _uiElementsEnabled = true;
        _preferredBackend = GraphicsBackendDefaults.Parse(projectSettings.GraphicsBackend);
        _buildTarget = BuildTargetManifestSerializer.LoadCurrent();
        Application.companyName = projectSettings.CompanyName;
        Application.productName = projectSettings.ProductName;
        Application.isEditor = false;
        Application.isPlaying = true;
        GraphicsBackendSettings.PreferredBackend = _preferredBackend;
        Time.fixedDeltaTime = Fix64.Parse(workspace.Project.FixedDeltaTime);
        try
        {
            if (initializeContent)
                assetBundles?.InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            hotUpdate?.Activate(services);
            AttachSceneManager();
            var requestedScene = startupScene ?? workspace.Project.StartupScene;
            if (!sceneAlreadyLoaded)
                _sceneManager.LoadScene(requestedScene, LoadSceneMode.Single);
            else
                foreach (var loaded in _sceneManager.LoadedScenes.ToArray()) OnSceneLoaded(loaded, LoadSceneMode.Single);
            if (!_isAotStage)
                PlayerStartupDiagnostics.Phase("06_SCENE_LOADED", $"scene={requestedScene}");
        }
        catch (Exception exception)
        {
            assetBundles?.RollbackAfterStartupFailure(exception);
            throw;
        }

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
        _window.FocusChanged += Application.SetFocus;
    }

    internal PlayerApplication(PlayerStagedRuntimeCoordinator stagedRuntime)
        : this(
            stagedRuntime.Workspace,
            stagedRuntime.ProjectSettings,
            stagedRuntime.AotStage.Services,
            stagedRuntime.AotStage.SceneRuntimeFactory,
            stagedRuntime.AotStage.SceneManager,
            stagedRuntime.UpdateBootstrap,
            hotUpdate: null,
            initializeContent: false,
            startupScene: stagedRuntime.AotStage.ScenePath,
            sceneAlreadyLoaded: true,
            isAotStage: true,
            stagedRuntime: stagedRuntime) { }

    private PlayerApplication((ProjectWorkspace Workspace, ServiceProvider Services) startup)
        : this(
            startup.Workspace,
            startup.Services.GetRequiredService<ProjectSettingsData>(),
            startup.Services,
            startup.Services.GetRequiredService<ISceneRuntimeFactory>(),
            startup.Services.GetRequiredService<IRuntimeSceneManager>(),
            startup.Services.GetService<PlayerAssetBundleBootstrap>(),
            startup.Services.GetService<PlayerHotUpdateSession>()) =>
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

    public void Run() => _window.Run();

    internal void Run(Action windowReady)
    {
        _windowReady = windowReady ?? throw new ArgumentNullException(nameof(windowReady));
        _window.Run();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachSceneManager();
        StopAllRuntimes();
        _portableRenderer?.Dispose();
        _presentationDevice = null;
        _renderer?.Dispose();
        _input?.Dispose();
        _window.Dispose();
        DisposeLoadedScenes();
        _stagedRuntime?.Dispose();
        var ownedServices = _ownedServices;
        _ownedServices = null;
        ownedServices?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnLoad()
    {
        WindowIcon.Apply(_window);
        var failures = new List<string>();
        foreach (var backend in GraphicsBackendSelector.GetCandidates(_buildTarget, _preferredBackend))
        {
            try
            {
                switch (backend)
                {
                    case GraphicsBackend.Vulkan:
                        CreateVulkanRenderer();
                        break;
                    case GraphicsBackend.OpenGL:
                        CreateOpenGlRenderer();
                        break;
                    default:
                        failures.Add($"{GraphicsBackendCatalog.Get(backend).DisplayName}: " +
                                     "no Player provider is installed");
                        continue;
                }
                break;
            }
            catch (Exception exception)
            {
                failures.Add($"{backend}: {exception.Message}");
                _portableRenderer?.Dispose();
                _portableRenderer = null;
                _presentationDevice = null;
                _renderer?.Dispose();
                _renderer = null;
            }
        }
        if (_renderer is null && _portableRenderer is null)
            throw new PlatformNotSupportedException(
                $"No renderer could start for '{_buildTarget.TargetId}': {string.Join("; ", failures)}");
        if (failures.Count != 0)
            Debug.LogWarning("Graphics backend fallback: " + string.Join("; ", failures));
        Debug.Log($"Active graphics API: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
        InitializeInput();
        _runtimeStarted = true;
        try
        {
            foreach (var runtime in _runtimes.ToArray()) runtime.Start();
            if (_isAotStage)
            {
                PlayerStartupDiagnostics.Phase("03_AOT_SCENE_STARTED",
                    $"graphics={SystemInfo.graphicsDeviceType};scene={_scene.path}");
                _stagedRuntime!.Flow.Start();
            }
            else
            {
                _assetBundles?.CommitStartup();
                PlayerStartupDiagnostics.Phase("07_GAME_STARTED",
                    $"graphics={SystemInfo.graphicsDeviceType};scene={_scene.path}");
            }
            Interlocked.Exchange(ref _windowReady, null)?.Invoke();
        }
        catch (Exception exception)
        {
            try { StopAllRuntimes(); }
            catch (Exception stopFailure) { PlayerStartupDiagnostics.Failure("GAME_STOP", stopFailure); }
            _assetBundles?.RollbackAfterStartupFailure(exception);
            throw;
        }
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
        foreach (var gamepad in _input.Gamepads) RegisterGamepad(gamepad);
        _input.ConnectionChanged += OnInputConnectionChanged;
        ApplyCursorState();
    }

    private void OnUpdate(double deltaSeconds)
    {
        try
        {
            if (_isAotStage) _stagedRuntime!.Flow.Pump();
            ApplyCursorState();
            PollMouseInput();
            PollGamepadInput();
            if (!Application.isFocused && !Application.runInBackground) return;
            foreach (var runtime in _runtimes.ToArray()) runtime.Tick((Fix64)deltaSeconds);
            if (_isAotStage)
            {
                _stagedRuntime!.Flow.Pump();
                TryEnterGame();
            }
        }
        finally { Input.EndFrame(); }
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

    private void TryEnterGame()
    {
        if (_transitioning || !_stagedRuntime!.Flow.EnterGameRequested) return;
        _transitioning = true;
        PlayerApplicationStage next;
        try { next = _stagedRuntime.CreateGameStage(); }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("HOTUPDATE_STAGE", exception);
            _stagedRuntime.Flow.ReportTransitionFailure(exception);
            _transitioning = false;
            return;
        }

        try
        {
            StopAllRuntimes();
            DetachSceneManager();
            DisposeLoadedScenes();
            _runtimes.Clear();
            _sceneRuntimeFactory = next.SceneRuntimeFactory;
            _sceneManager = next.SceneManager;
            AttachSceneManager();
            foreach (var loaded in _sceneManager.LoadedScenes.ToArray())
                OnSceneLoaded(loaded, LoadSceneMode.Single);
            _runtimeStarted = true;
            foreach (var runtime in _runtimes.ToArray()) runtime.Start();
            _isAotStage = false;
            _assetBundles?.CommitStartup();
            _stagedRuntime.CompleteTransition();
            PlayerStartupDiagnostics.Phase("06_SCENE_LOADED", $"scene={next.ScenePath}");
            PlayerStartupDiagnostics.Phase("07_GAME_STARTED",
                $"graphics={SystemInfo.graphicsDeviceType};scene={_scene.path}");
        }
        catch (Exception exception)
        {
            _assetBundles?.RollbackAfterStartupFailure(exception);
            throw;
        }
        finally { _transitioning = false; }
    }

    private void AttachSceneManager()
    {
        _sceneManager.SceneLoaded += OnSceneLoaded;
        _sceneManager.SceneUnloaded += OnSceneUnloaded;
        _sceneManager.ActiveSceneChanged += OnActiveSceneChanged;
    }

    private void DetachSceneManager()
    {
        _sceneManager.SceneLoaded -= OnSceneLoaded;
        _sceneManager.SceneUnloaded -= OnSceneUnloaded;
        _sceneManager.ActiveSceneChanged -= OnActiveSceneChanged;
    }

    private void DisposeLoadedScenes()
    {
        foreach (var scene in _sceneManager.LoadedScenes.ToArray())
            _sceneManager.UnregisterScene(scene, disposeScene: true);
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

    private static void OnKeyChar(IKeyboard _, char character) => Input.AppendTextInput(character);

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

    private void RegisterGamepad(IGamepad gamepad)
    {
        if (!_registeredGamepads.Add(gamepad)) return;
        Input.SetGamepadConnected(gamepad.Index, gamepad.IsConnected, gamepad.Name);
        gamepad.ButtonDown += OnGamepadButtonDown;
        gamepad.ButtonUp += OnGamepadButtonUp;
    }

    private void OnInputConnectionChanged(IInputDevice device, bool connected)
    {
        if (device is not IGamepad gamepad) return;
        if (connected) RegisterGamepad(gamepad);
        Input.SetGamepadConnected(gamepad.Index, connected, gamepad.Name);
    }

    private static void OnGamepadButtonDown(IGamepad gamepad, Button button)
    {
        if (TryMap(button.Name, out var mapped)) Input.SetGamepadButtonState(gamepad.Index, mapped, true);
    }

    private static void OnGamepadButtonUp(IGamepad gamepad, Button button)
    {
        if (TryMap(button.Name, out var mapped)) Input.SetGamepadButtonState(gamepad.Index, mapped, false);
    }

    private void PollGamepadInput()
    {
        if (_input is null) return;
        foreach (var gamepad in _input.Gamepads)
        {
            RegisterGamepad(gamepad);
            Input.SetGamepadConnected(gamepad.Index, gamepad.IsConnected, gamepad.Name);
            foreach (var button in gamepad.Buttons)
                if (TryMap(button.Name, out var mapped))
                    Input.SetGamepadButtonState(gamepad.Index, mapped, button.Pressed);
            foreach (var stick in gamepad.Thumbsticks)
            {
                var x = stick.Index == 0 ? GamepadAxis.LeftStickX : GamepadAxis.RightStickX;
                var y = stick.Index == 0 ? GamepadAxis.LeftStickY : GamepadAxis.RightStickY;
                Input.SetGamepadAxis(gamepad.Index, x, (Fix64)stick.X);
                Input.SetGamepadAxis(gamepad.Index, y, (Fix64)stick.Y);
            }
            foreach (var trigger in gamepad.Triggers)
                Input.SetGamepadAxis(gamepad.Index,
                    trigger.Index == 0 ? GamepadAxis.LeftTrigger : GamepadAxis.RightTrigger,
                    (Fix64)trigger.Position);
        }
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
            >= Key.A and <= Key.Z => (KeyCode)((int)KeyCode.A + key - Key.A),
            >= Key.Number0 and <= Key.Number9 => (KeyCode)((int)KeyCode.Alpha0 + key - Key.Number0),
            >= Key.F1 and <= Key.F15 => (KeyCode)((int)KeyCode.F1 + key - Key.F1),
            >= Key.Keypad0 and <= Key.Keypad9 => (KeyCode)((int)KeyCode.Keypad0 + key - Key.Keypad0),
            Key.Enter => KeyCode.Return,
            Key.Right => KeyCode.RightArrow,
            Key.Left => KeyCode.LeftArrow,
            Key.Down => KeyCode.DownArrow,
            Key.Up => KeyCode.UpArrow,
            Key.ShiftLeft => KeyCode.LeftShift,
            Key.ShiftRight => KeyCode.RightShift,
            Key.ControlLeft => KeyCode.LeftControl,
            Key.ControlRight => KeyCode.RightControl,
            Key.AltLeft => KeyCode.LeftAlt,
            Key.AltRight => KeyCode.RightAlt,
            Key.SuperLeft => KeyCode.LeftCommand,
            Key.SuperRight => KeyCode.RightCommand,
            Key.Apostrophe => KeyCode.Quote,
            Key.Equal => KeyCode.Equals,
            Key.BackSlash => KeyCode.Backslash,
            Key.GraveAccent => KeyCode.BackQuote,
            Key.PrintScreen => KeyCode.Print,
            Key.NumLock => KeyCode.Numlock,
            Key.KeypadDecimal => KeyCode.KeypadPeriod,
            Key.KeypadSubtract => KeyCode.KeypadMinus,
            Key.KeypadAdd => KeyCode.KeypadPlus,
            Key.KeypadEqual => KeyCode.KeypadEquals,
            Key.Unknown or >= Key.F16 => KeyCode.None,
            _ when Enum.TryParse<KeyCode>(key.ToString(), ignoreCase: false, out var direct) => direct,
            _ => default
        };
        return code != KeyCode.None;
    }

    private static bool TryMap(ButtonName button, out GamepadButton mapped)
    {
        mapped = button switch
        {
            ButtonName.A => GamepadButton.South,
            ButtonName.B => GamepadButton.East,
            ButtonName.X => GamepadButton.West,
            ButtonName.Y => GamepadButton.North,
            ButtonName.LeftBumper => GamepadButton.LeftShoulder,
            ButtonName.RightBumper => GamepadButton.RightShoulder,
            ButtonName.Back => GamepadButton.Back,
            ButtonName.Start => GamepadButton.Start,
            ButtonName.Home => GamepadButton.Home,
            ButtonName.LeftStick => GamepadButton.LeftStick,
            ButtonName.RightStick => GamepadButton.RightStick,
            ButtonName.DPadUp => GamepadButton.DPadUp,
            ButtonName.DPadRight => GamepadButton.DPadRight,
            ButtonName.DPadDown => GamepadButton.DPadDown,
            ButtonName.DPadLeft => GamepadButton.DPadLeft,
            _ => default
        };
        return button != ButtonName.Unknown;
    }
}
