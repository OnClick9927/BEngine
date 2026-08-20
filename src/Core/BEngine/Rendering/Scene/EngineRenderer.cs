using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.OpenGL;
using Silk.NET.OpenGL;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public sealed class EngineRenderer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly PortableSceneRenderer _renderer;

    public GraphicsBackend Backend => _device.Backend;
    public GraphicsDeviceCapabilities Capabilities => _device.Capabilities;

    public EngineRenderer(GL gl) : this(new OpenGlGraphicsDevice(gl), ownsGraphicsDevice: true) { }

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

    public static RenderCamera ResolveGameCamera(Scene scene) =>
        TryResolveGameCamera(scene, out var camera) ? camera : RenderCamera.Default;

    public static bool TryResolveGameCamera(Scene scene, out RenderCamera camera)
    {
        var component = ResolveGameCameras([scene]).FirstOrDefault(item => item.isMain) ??
                        ResolveGameCameras([scene]).FirstOrDefault();
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
        var cameras = new List<(Camera2D Camera, int Sequence)>();
        var sequence = 0;
        foreach (var scene in scenes)
        {
            if (scene is null || !scene.isLoaded) continue;
            foreach (var camera in scene.QueryComponents<Camera2D>())
                if (camera.gameObject.activeInHierarchy && camera.enabled)
                    cameras.Add((camera, sequence++));
        }
        cameras.Sort(static (left, right) =>
        {
            var depth = left.Camera.depth.CompareTo(right.Camera.depth);
            return depth != 0 ? depth : left.Sequence.CompareTo(right.Sequence);
        });
        return cameras.Select(item => item.Camera).ToArray();
    }

    public void Dispose() => _renderer.Dispose();
}
