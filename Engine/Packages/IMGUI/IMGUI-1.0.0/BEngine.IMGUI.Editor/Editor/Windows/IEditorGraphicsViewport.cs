using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public interface IEditorGraphicsViewport
{
    bool TryGetGraphicsViewport(out EditorGraphicsViewport viewport);
    void RenderGraphicsViewport(
        IGraphicsDevice device,
        GraphicsRect viewport,
        int targetWidth,
        int targetHeight);
    void ReleaseGraphicsDevice(IGraphicsDevice device);
}
