namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorDesktopService through dependency injection.</summary>
public sealed class DesktopUtility
{
    private readonly IEditorDesktopService _implementation;

    public DesktopUtility(IEditorDesktopService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static DesktopUtility Current => EditorServices.GetRequiredService<DesktopUtility>();

    public IReadOnlyList<Rect> GetWorkAreas() =>
        _implementation.GetWorkAreas();

    public IDisposable? ObserveWindowMove(IntPtr window, Action<Vector2> updated, Action<Vector2?> completed, Func<Vector2?> pointerPosition) =>
        _implementation.ObserveWindowMove(window, updated, completed, pointerPosition);

    public bool TryShowMenu(IntPtr owner, Vector2 position, IReadOnlyList<GenericMenuItem> items, Rect? keepAliveScreenRect = null) =>
        _implementation.TryShowMenu(owner, position, items, keepAliveScreenRect);

    public bool IsKeyPressed(KeyCode key) =>
        _implementation.IsKeyPressed(key);

    public bool TrySamplePointerColor(out Color color) =>
        _implementation.TrySamplePointerColor(out color);
}
