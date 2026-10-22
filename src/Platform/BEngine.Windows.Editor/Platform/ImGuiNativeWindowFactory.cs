using BEngine.Platform;

namespace BEngine.Editor;

public sealed class ImGuiNativeWindowFactory : IEditorNativeWindowFactory
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;

    public ImGuiNativeWindowFactory()
        : this(
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformUtility>())
    {
    }

    public ImGuiNativeWindowFactory(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public IEditorNativeWindow Create(
        string title,
        int width,
        int height,
        bool visible = true,
        bool vsync = true,
        int minimumWidth = 320,
        int minimumHeight = 200,
        int maximumWidth = int.MaxValue,
        int maximumHeight = int.MaxValue) =>
        new ImGuiNativeWindow(
            _fileSystem,
            _pathService,
            _platformService,
            title,
            width,
            height,
            visible,
            vsync,
            minimumWidth,
            minimumHeight,
            maximumWidth,
            maximumHeight);

    public Vector2 GuiToClient(Vector2 point, Fix64 editorScale) =>
        ImGuiNativeWindow.GUIToClient(point, editorScale);

    public bool TryGetPointerScreenPosition(out Vector2 point) =>
        ImGuiNativeWindow.TryGetPointerScreenPosition(_platformService, out point);
}
