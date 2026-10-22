using BEngine.Rendering.Rhi;
using System.Buffers;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed class EngineRenderer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly PortableSceneRenderer _renderer;

    public GraphicsBackend Backend => _device.Backend;
    public GraphicsDeviceCapabilities Capabilities => _device.Capabilities;

    public EngineRenderer(IGraphicsDevice graphicsDevice, bool ownsGraphicsDevice = false)
    {
        _device = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _renderer = new PortableSceneRenderer(graphicsDevice, ownsGraphicsDevice);
    }

    public void Render(Scene scene, RenderCamera camera, int width, int height,
        bool drawGrid = false, bool drawUi = true, bool drawExtensions = true, bool drawGizmos = false) =>
        _renderer.Render(scene, camera, width, height, drawGrid, drawUi, drawExtensions, drawGizmos);

    public void Clear(NVector4 color, int width, int height) =>
        _renderer.FillViewport(new GraphicsRect(0, 0, Math.Max(1, width), Math.Max(1, height)), color);

    public void RenderCameras(IReadOnlyList<Scene> scenes, Scene activeScene,
        IReadOnlyList<Camera2D> cameras, int width, int height, bool drawUi = true) =>
        _renderer.RenderCameras(scenes, activeScene, cameras,
            new GraphicsRect(0, 0, Math.Max(1, width), Math.Max(1, height)), drawUi);

    public static RenderCamera ResolveGameCamera(Scene scene) =>
        TryResolveGameCamera(scene, out var camera) ? camera : RenderCamera.Default;

    public static bool TryResolveGameCamera(Scene scene, out RenderCamera camera)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Camera2D? component = null;
        if (scene.isLoaded)
        {
            foreach (var gameObject in scene.GameObjectsSpanUnchecked)
            {
                if (!gameObject.activeInHierarchy) continue;
                foreach (var candidate in gameObject.ComponentsSpanUnchecked)
                {
                    if (candidate is not Camera2D { enabled: true } current) continue;
                    // Prefer a main camera, then the lowest priority; keep insertion order for ties.
                    if (component is null || current.isMain && !component.isMain ||
                        current.isMain == component.isMain && current.priority < component.priority)
                        component = current;
                }
            }
        }
        if (component is null)
        {
            camera = default;
            return false;
        }
        camera = RenderCamera.From(component);
        return true;
    }

    public static IReadOnlyList<Camera2D> ResolveGameCameras(IReadOnlyList<Scene> scenes)
    {
        var cameras = new List<Camera2D>();
        FillGameCameras(scenes, cameras);
        return cameras.ToArray();
    }

    /// <summary>Fills reusable storage, stably ordered by priority. Equal priorities keep scene/component order.</summary>
    public static void FillGameCameras(IReadOnlyList<Scene> scenes, List<Camera2D> cameras)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(cameras);
        cameras.Clear();
        for (var sceneIndex = 0; sceneIndex < scenes.Count; sceneIndex++)
        {
            var scene = scenes[sceneIndex];
            if (scene is null || !scene.isLoaded) continue;
            foreach (var camera in scene.ComponentsSpanUnchecked<Camera2D>())
                if (camera.enabled && camera.gameObject.activeInHierarchy) cameras.Add(camera);
        }
        if (cameras.Count < 2) return;
        var scratch = ArrayPool<Camera2D>.Shared.Rent(cameras.Count);
        try
        {
            for (var run = 1; run < cameras.Count; run = run > cameras.Count / 2 ? cameras.Count : run * 2)
            {
                for (var start = 0; start < cameras.Count;)
                {
                    var middle = start + Math.Min(run, cameras.Count - start);
                    var end = middle + Math.Min(run, cameras.Count - middle);
                    var left = start; var right = middle;
                    for (var write = start; write < end; write++)
                        scratch[write] = left < middle && (right >= end || cameras[left].priority <= cameras[right].priority)
                            ? cameras[left++] : cameras[right++];
                    start = end;
                }
                for (var index = 0; index < cameras.Count; index++) cameras[index] = scratch[index];
            }
        }
        finally { ArrayPool<Camera2D>.Shared.Return(scratch, clearArray: true); }
    }

    public void Dispose() => _renderer.Dispose();
}
