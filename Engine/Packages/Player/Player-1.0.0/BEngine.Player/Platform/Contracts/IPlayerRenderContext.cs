using BEngine.Rendering.Rhi;

namespace BEngine.Player;

public interface IPlayerRenderContext : IDisposable
{
    GraphicsBackend Backend { get; }
    void Render(
        IReadOnlyList<Scene> scenes,
        Scene activeScene,
        int width,
        int height,
        bool drawUi);
}
