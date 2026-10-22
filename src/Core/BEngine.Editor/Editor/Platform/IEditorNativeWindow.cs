using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public interface IEditorNativeWindow : IDisposable
{
    event Action<double>? updating;
    event Action? closing;
    event Action? firstFrameRendered;
    event Action? gui;
    event Action<bool>? focusChanged;
    event Action<ImGuiNativeFrameProfile>? frameProfiled;

    Action<IGraphicsDevice, int, int>? renderBackground { get; set; }
    bool isClosing { get; }
    GraphicsBackend backend { get; }
    int width { get; }
    int height { get; }
    Fix64 renderScale { get; }
    GameViewFrameTimingSnapshot frameTiming { get; }
    IGraphicsDevice? graphicsDevice { get; }
    IntPtr nativeHandle { get; }
    Vector2 screenPosition { get; }
    Vector2 windowSize { get; }
    bool isMaximized { get; }
    bool isFocused { get; }
    bool leftMouseButtonPressed { get; }
    NativeWindowPointerOperation pointerOperation { get; }

    void Run();
    void Initialize();
    void Pump();
    void Focus();
    void Close();
    void CancelCloseRequest();
    void Repaint();
    void SetTitle(string title);
    void Move(int x, int y);
    void Resize(int width, int height);
    void SetSizeLimits(int minimumWidth, int minimumHeight, int maximumWidth, int maximumHeight);
    void SetMaximized(bool maximized);
    void Minimize();
    Vector2 GUIToScreen(Vector2 point, Fix64 editorScale);
    Vector2 ScreenToGUI(Vector2 point, Fix64 editorScale);
}
