using BEngine.Rendering.Rhi;
namespace BEngine.Editor;

/// <summary>Presents an editor frame without requiring a particular UI toolkit.</summary>
public interface IEditorFramePresenter : IDisposable
{
    Fix64 Scale { get; }
    Color BackgroundColor { get; }
    bool IsEditingTextField { get; }
    int HotControl { get; set; }
    MouseCursor Cursor { get; }
    Rect? TextInputRect { get; }
    void Begin(object owner, Event input, int width, int height, Fix64 deviceScale);
    void End();
    void Clear();
    void Render(int width, int height);
}
public static class EditorFramePresenters
{
    public static Func<IGraphicsDevice,IEditorFramePresenter>? Factory { get; set; }
    public static IEditorFramePresenter Create(IGraphicsDevice device) =>
        Factory?.Invoke(device) ?? throw new InvalidOperationException("No editor frame presenter is registered.");
}
public static class EditorInputRouting
{
    public static Func<int>? HotControlProvider { get; set; }
    public static Func<int>? KeyboardControlProvider { get; set; }
    internal static int HotControl => HotControlProvider?.Invoke() ?? 0;
    internal static int KeyboardControl => KeyboardControlProvider?.Invoke() ?? 0;
}
