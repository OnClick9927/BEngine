namespace BEngine.Editor;

public interface IEditorDesktopService
{
    IReadOnlyList<Rect> GetWorkAreas();
    IDisposable? ObserveWindowMove(IntPtr window, Action<Vector2> updated, Action<Vector2?> completed, Func<Vector2?> pointerPosition);
    bool TryShowMenu(IntPtr owner, Vector2 position, IReadOnlyList<GenericMenuItem> items, Rect? keepAliveScreenRect = null);
    bool IsKeyPressed(KeyCode key);
    bool TrySamplePointerColor(out Color color);
}
