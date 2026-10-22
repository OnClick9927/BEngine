using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface ISceneRendererFactory
{
    ISceneRenderer Create(IGraphicsDevice device, bool ownsDevice = false);
}
