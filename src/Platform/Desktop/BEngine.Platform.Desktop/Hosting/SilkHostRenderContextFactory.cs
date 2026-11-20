using BEngine.Hosting;
using BEngine.Build;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Platform;

namespace BEngine.Platform;

public sealed class SilkHostRenderContextFactory : IHostRenderContextFactory
{
    private readonly ISceneRendererFactory _sceneRendererFactory;

    public SilkHostRenderContextFactory(ISceneRendererFactory sceneRendererFactory) =>
        _sceneRendererFactory = sceneRendererFactory ??
                                throw new ArgumentNullException(nameof(sceneRendererFactory));

    public IHostRenderContext Create(
        IHostWindow window,
        BuildTargetDescriptor buildTarget,
        GraphicsBackend preferredBackend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(buildTarget);
        if (window is not SilkHostWindow silkWindow)
            throw new ArgumentException(
                $"{nameof(SilkHostRenderContextFactory)} requires a {nameof(SilkHostWindow)}.",
                nameof(window));

        // The window already owns a device that passed real GPU initialization and readback.
        // Creating another device here would discard the automatic selection and its lifetime.
        var session = silkWindow.Graphics;
        return new RenderContext(session, silkWindow.TakeRenderer(_sceneRendererFactory));
    }

    private sealed class RenderContext(SilkGraphicsSession session, ISceneRenderer renderer) : IHostRenderContext
    {
        public void MakeCurrent() => session.MakeCurrent();
        public bool RenderFrame(int width, int height, Action<IGraphicsDevice, GraphicsRect> draw)
        {
            ArgumentNullException.ThrowIfNull(draw);
            var viewport = session.BeginFrame(width, height);
            draw(session.Device, viewport);
            session.Present();
            return true;
        }
        public GraphicsBackend Backend => session.Device.Backend;
        public void Render(IReadOnlyList<Scene> scenes, Scene activeScene, int width, int height, bool drawUi)
        {
            var cpuProbe = DesktopFrameProbe.CpuCurrent;
            cpuProbe?.BeginCpuRender();
            var viewport = session.BeginFrame(width, height);
            cpuProbe?.EndCpuBegin();
            renderer.RenderScene(scenes, activeScene, viewport, drawUi);
            cpuProbe?.EndCpuDraw(renderer.LastRenderStatistics);
            session.Present();
            cpuProbe?.EndCpuPresent();
        }
        public void Dispose()
        {
            session.MakeCurrent();
            renderer.Dispose();
        }
    }
}
