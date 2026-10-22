using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Player;

public interface IPlayerRenderContextFactory
{
    IPlayerRenderContext Create(
        IPlayerWindow window,
        BuildTargetManifest buildTarget,
        GraphicsBackend preferredBackend);
}
