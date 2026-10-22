using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface ISceneRenderer : IDisposable
{
    GraphicsBackend Backend { get; }
    GraphicsDeviceCapabilities Capabilities { get; }
    SceneRenderStatistics LastRenderStatistics { get; }

    /// <summary>Prepares a texture through this renderer's device-owned cache;
    /// CPU decoding may run in the background and GPU creation is committed on the owner thread.</summary>
    BTask<bool> PrepareTextureAsync(string source, AsyncLoadContext? context = null);

    void RenderViewport(
        IReadOnlyList<Scene> scenes,
        Scene activeScene,
        RenderCamera camera,
        GraphicsRect viewport,
        bool initializeColor = false,
        bool drawGrid = false,
        bool drawUi = true,
        bool drawExtensions = true,
        bool drawGizmos = false,
        Predicate<GameObject>? objectFilter = null);

    void RenderCameras(
        IReadOnlyList<Scene> scenes,
        Scene activeScene,
        IReadOnlyList<Camera2D> cameras,
        GraphicsRect targetViewport,
        int targetWidth,
        int targetHeight,
        bool drawUi = true);

    void FillViewport(GraphicsRect viewport, System.Numerics.Vector4 color);
    void DrawGizmos(
        IReadOnlyList<GizmoLine2D> gizmos,
        RenderCamera camera,
        GraphicsRect viewport);
}
