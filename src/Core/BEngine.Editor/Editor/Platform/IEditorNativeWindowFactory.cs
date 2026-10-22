namespace BEngine.Editor;

public interface IEditorNativeWindowFactory
{
    IEditorNativeWindow Create(
        string title,
        int width,
        int height,
        bool visible = true,
        bool vsync = true,
        int minimumWidth = 320,
        int minimumHeight = 200,
        int maximumWidth = int.MaxValue,
        int maximumHeight = int.MaxValue);

    Vector2 GuiToClient(Vector2 point, Fix64 editorScale);
    bool TryGetPointerScreenPosition(out Vector2 point);
}
