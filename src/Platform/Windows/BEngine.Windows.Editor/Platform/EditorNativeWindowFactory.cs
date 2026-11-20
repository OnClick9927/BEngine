using BEngine.Platform;

namespace BEngine.Editor;

public sealed class EditorNativeWindowFactory : IEditorNativeWindowFactory
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;

    public EditorNativeWindowFactory()
        : this(
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformUtility>())
    {
    }

    public EditorNativeWindowFactory(
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
        new EditorNativeWindow(
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
        EditorNativeWindow.GUIToClient(point, editorScale);

    public bool TryGetPointerScreenPosition(out Vector2 point) =>
        EditorNativeWindow.TryGetPointerScreenPosition(_platformService, out point);
}
