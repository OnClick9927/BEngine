namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorNativeWindowFactory through dependency injection.</summary>
public sealed class NativeWindowUtility
{
    private readonly IEditorNativeWindowFactory _implementation;

    public NativeWindowUtility(IEditorNativeWindowFactory implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static NativeWindowUtility Current => BEngine.Editor.EditorServices.GetRequiredService<NativeWindowUtility>();

    public NativeWindow Create(string title, int width, int height, bool visible = true, bool vsync = true, int minimumWidth = 320, int minimumHeight = 200, int maximumWidth = int.MaxValue, int maximumHeight = int.MaxValue) =>
        new(_implementation.Create(title, width, height, visible, vsync, minimumWidth, minimumHeight, maximumWidth, maximumHeight));

    public Vector2 GuiToClient(Vector2 point, Fix64 editorScale) =>
        _implementation.GuiToClient(point, editorScale);

    public bool TryGetPointerScreenPosition(out Vector2 point) =>
        _implementation.TryGetPointerScreenPosition(out point);
}
