#if !BENGINE_PLAYER_PORTABLE
using BEngine.Build;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Platform;

namespace BEngine.Player;

public sealed class SilkPlayerRenderContextFactory : IPlayerRenderContextFactory
{
    private readonly ISceneRendererFactory _sceneRendererFactory;

    public SilkPlayerRenderContextFactory(ISceneRendererFactory sceneRendererFactory) =>
        _sceneRendererFactory = sceneRendererFactory ??
                                throw new ArgumentNullException(nameof(sceneRendererFactory));

    public IPlayerRenderContext Create(
        IPlayerWindow window,
        BuildTargetManifest buildTarget,
        GraphicsBackend preferredBackend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(buildTarget);
        if (window is not SilkPlayerWindow silkWindow)
            throw new ArgumentException(
                $"{nameof(SilkPlayerRenderContextFactory)} requires a {nameof(SilkPlayerWindow)}.",
                nameof(window));

        // The window already owns a device that passed real GPU initialization and readback.
        // Creating another device here would discard the automatic selection and its lifetime.
        var session = silkWindow.Graphics;
        return new RenderContext(session, silkWindow.TakeRenderer(_sceneRendererFactory));
    }

    private sealed class RenderContext(SilkGraphicsSession session, ISceneRenderer renderer) : IPlayerRenderContext
    {
        private readonly List<Camera2D> _cameras = [];
        public GraphicsBackend Backend => session.Device.Backend;
        public void Render(IReadOnlyList<Scene> scenes, Scene activeScene, int width, int height, bool drawUi)
        {
            var cpuProbe = DesktopFrameProbe.CpuCurrent;
            cpuProbe?.BeginCpuRender();
            var viewport = session.BeginFrame(width, height);
            cpuProbe?.EndCpuBegin();
            EngineRenderer.FillGameCameras(scenes, _cameras);
            renderer.RenderCameras(scenes, activeScene, _cameras,
                viewport, viewport.Width, viewport.Height, drawUi);
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
#endif
