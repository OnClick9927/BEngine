using BEngine.Rendering.Rhi;
namespace BEngine.Editor;

/// <summary>Optional viewport instrumentation and overlays supplied by editor extensions.</summary>
public static class EditorViewportExtensions
{
    public static event Action? Changed;
    public static Func<object,IGraphicsDevice,string,GraphicsRect,int,int,IDisposable?>? BeginRenderHandler { get; set; }
    public static Func<object,bool>? IsDebugTargetHandler { get; set; }
    public static Func<object,bool>? RequiresRenderHandler { get; set; }
    public static Action<object>? ReleaseTargetHandler { get; set; }
    public static Action<IEditorView,Rect>? DrawOverlayHandler { get; set; }
    public static void NotifyChanged() => Changed?.Invoke();
    public static IDisposable? BeginRender(object target,IGraphicsDevice device,string name,GraphicsRect area,int width,int height) => BeginRenderHandler?.Invoke(target,device,name,area,width,height);
    public static bool IsDebugTarget(object target) => IsDebugTargetHandler?.Invoke(target) ?? false;
    public static bool RequiresRender(object target) => RequiresRenderHandler?.Invoke(target) ?? false;
    public static void Release(object target) => ReleaseTargetHandler?.Invoke(target);
    public static void DrawOverlay(IEditorView target,Rect area) => DrawOverlayHandler?.Invoke(target,area);
}
