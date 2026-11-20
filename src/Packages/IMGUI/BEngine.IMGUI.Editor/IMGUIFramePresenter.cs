using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;
namespace BEngine.Editor;

internal sealed class IMGUIFramePresenter(IGraphicsDevice device) : IEditorFramePresenter
{
    private readonly List<GpuCanvasCommand> _commands = [];
    private readonly GpuCanvasRenderer _renderer = new(device, resourceResolver: EditorGpuCanvasResourceResolver.Shared);
    public Fix64 Scale => GUIUtility.pixelsPerPoint;
    public Color BackgroundColor => EditorStyles.viewBackground.normal.backgroundColor;
    public bool IsEditingTextField => GUI.isEditingTextField;
    public int HotControl { get => GUIUtility.hotControl; set => GUIUtility.hotControl = value; }
    public MouseCursor Cursor => GUI.requestedMouseCursor;
    public Rect? TextInputRect => GUI.textInputRect;
    public void Begin(object owner, Event input, int width, int height, Fix64 deviceScale)
    {
        GUIUtility.devicePixelsPerPoint = deviceScale;
        GUI.SetTooltipOwner(owner);
        GUI.BeginFrame(input, width, height, _commands);
    }
    public void End() => GUI.EndFrame();
    public void Clear() => _commands.Clear();
    public void Render(int width, int height) => _renderer.Render(_commands,width,height);
    public void Dispose() { _renderer.Dispose(); _commands.Clear(); }
}
