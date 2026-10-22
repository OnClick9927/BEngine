using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorNativeWindow through dependency injection.</summary>
public sealed class NativeWindow : IDisposable
{
    private readonly IEditorNativeWindow _implementation;

    public NativeWindow(IEditorNativeWindow implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public event Action<double>? updating
    {
        add => _implementation.updating += value;
        remove => _implementation.updating -= value;
    }

    public event Action? closing
    {
        add => _implementation.closing += value;
        remove => _implementation.closing -= value;
    }

    public event Action? firstFrameRendered
    {
        add => _implementation.firstFrameRendered += value;
        remove => _implementation.firstFrameRendered -= value;
    }

    public event Action? gui
    {
        add => _implementation.gui += value;
        remove => _implementation.gui -= value;
    }

    public event Action<bool>? focusChanged
    {
        add => _implementation.focusChanged += value;
        remove => _implementation.focusChanged -= value;
    }

    public event Action<ImGuiNativeFrameProfile>? frameProfiled
    {
        add => _implementation.frameProfiled += value;
        remove => _implementation.frameProfiled -= value;
    }

    public Action<IGraphicsDevice, int, int>? renderBackground { get => _implementation.renderBackground; set => _implementation.renderBackground = value; }

    public bool isClosing => _implementation.isClosing;

    public GraphicsBackend backend => _implementation.backend;

    public int width => _implementation.width;

    public int height => _implementation.height;

    public Fix64 renderScale => _implementation.renderScale;

    public GameViewFrameTimingSnapshot frameTiming => _implementation.frameTiming;

    public IGraphicsDevice? graphicsDevice => _implementation.graphicsDevice;

    public IntPtr nativeHandle => _implementation.nativeHandle;

    public Vector2 screenPosition => _implementation.screenPosition;

    public Vector2 windowSize => _implementation.windowSize;

    public bool isMaximized => _implementation.isMaximized;

    public bool isFocused => _implementation.isFocused;

    public bool leftMouseButtonPressed => _implementation.leftMouseButtonPressed;

    public NativeWindowPointerOperation pointerOperation => _implementation.pointerOperation;

    public void Run() =>
        _implementation.Run();

    public void Initialize() =>
        _implementation.Initialize();

    public void Pump() =>
        _implementation.Pump();

    public void Focus() =>
        _implementation.Focus();

    public void Close() =>
        _implementation.Close();

    public void CancelCloseRequest() =>
        _implementation.CancelCloseRequest();

    public void Repaint() =>
        _implementation.Repaint();

    public void SetTitle(string title) =>
        _implementation.SetTitle(title);

    public void Move(int x, int y) =>
        _implementation.Move(x, y);

    public void Resize(int width, int height) =>
        _implementation.Resize(width, height);

    public void SetSizeLimits(int minimumWidth, int minimumHeight, int maximumWidth, int maximumHeight) =>
        _implementation.SetSizeLimits(minimumWidth, minimumHeight, maximumWidth, maximumHeight);

    public void SetMaximized(bool maximized) =>
        _implementation.SetMaximized(maximized);

    public void Minimize() =>
        _implementation.Minimize();

    public Vector2 GUIToScreen(Vector2 point, Fix64 editorScale) =>
        _implementation.GUIToScreen(point, editorScale);

    public Vector2 ScreenToGUI(Vector2 point, Fix64 editorScale) =>
        _implementation.ScreenToGUI(point, editorScale);
    public void Dispose() => _implementation.Dispose();
}
