using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Hosting;

public interface IHostRenderContext : IDisposable
{
    GraphicsBackend Backend { get; }
    void MakeCurrent();

    /// <summary>Acquires a frame, invokes application drawing, then presents it.
    /// A failed callback propagates without presenting a partial frame.</summary>
    bool RenderFrame(int width, int height, Action<IGraphicsDevice, GraphicsRect> draw) => false;

    void Render(IReadOnlyList<Scene> scenes, Scene activeScene, int width, int height, bool drawUi);
}

public interface IHostRenderContextFactory
{
    IHostRenderContext Create(IHostWindow window, BuildTargetDescriptor buildTarget, GraphicsBackend preferredBackend);
}
