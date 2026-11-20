using BEngine.Rendering.Rhi;
namespace BEngine.Rendering;

/// <summary>A scene presentation capability. Camera and geometry policy belong to its provider.</summary>
public interface ISceneRenderer : IDisposable
{
    GraphicsBackend Backend { get; }
    GraphicsDeviceCapabilities Capabilities { get; }
    SceneRenderStatistics LastRenderStatistics { get; }
    BTask<bool> PrepareTextureAsync(string source, AsyncLoadContext? context = null);
    void RenderScene(IReadOnlyList<Scene> scenes, Scene activeScene, GraphicsRect viewport, bool drawUi = true);
    void FillViewport(GraphicsRect viewport, System.Numerics.Vector4 color);
}
