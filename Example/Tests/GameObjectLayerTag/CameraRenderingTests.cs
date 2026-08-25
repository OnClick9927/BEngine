using BEngine.Documents;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class CameraRenderingTests
{
    public static void Run()
    {
        VerifyCameraContractAndLegacyData();
        VerifyViewportAndVisibility();
        VerifyCameraStackRendering();
    }

    private static void VerifyCameraContractAndLegacyData()
    {
        var cameraType = typeof(Camera2D);
        TestAssert.Require(cameraType.GetProperty("projection") is null &&
                           cameraType.GetProperty("fieldOfView") is null &&
                           cameraType.GetProperty("nearClipPlane") is null &&
                           cameraType.GetProperty("farClipPlane") is null,
            "Camera2D exposed a non-orthographic projection API.");

        var scene = new Scene("Camera contract");
        try
        {
            var later = scene.CreateGameObject("Later").AddComponent<Camera2D>();
            later.priority = 20;
            var earlier = scene.CreateGameObject("Earlier").AddComponent<Camera2D>();
            earlier.priority = -10;
            var tied = scene.CreateGameObject("Tied").AddComponent<Camera2D>();
            tied.priority = 20;
            var ordered = EngineRenderer.ResolveGameCameras([scene]);
            TestAssert.Require(ordered.SequenceEqual([earlier, later, tied]),
                "Camera priority did not preserve ascending, stable render order.");

            var legacy = scene.CreateGameObject("Legacy").AddComponent<Camera2D>();
            ComponentFieldSerializer.Deserialize(legacy,
                new Dictionary<string, string> { ["depth"] = "7.5" });
            TestAssert.Require(legacy.priority == Fix64.Parse("7.5"),
                "Legacy Camera2D.depth data was not migrated to priority.");
            var serialized = ComponentFieldSerializer.Serialize(legacy);
            TestAssert.Require(serialized.ContainsKey("priority") && !serialized.ContainsKey("depth"),
                "Camera serialization did not emit only the current priority field.");

            legacy.cullingMask = ulong.MaxValue;
            TestAssert.Require(legacy.cullingMask == SortingLayer.AllMask,
                "Camera culling mask retained the reserved bit zero.");
            legacy.viewportRect = new Rect(Fix64.Parse("0.9"), Fix64.Parse("-1"), 1, 0);
            TestAssert.Require(legacy.viewportRect.xMax <= 1 && legacy.viewportRect.y >= 0 &&
                               legacy.viewportRect.height > 0,
                "Camera viewport was not clamped to a non-empty normalized rectangle.");
        }
        finally
        {
            if (scene.isCreated) scene.Dispose();
        }
    }

    private static void VerifyViewportAndVisibility()
    {
        var lowerLeft = new Rect(0, 0, Fix64.Half, Fix64.Half);
        var target = new GraphicsRect(10, 20, 400, 200);
        TestAssert.Require(
            PortableSceneRenderer.ResolveViewport(target, lowerLeft, GraphicsBackend.OpenGL) ==
            new GraphicsRect(10, 20, 200, 100),
            "OpenGL normalized camera viewport did not use a bottom-left origin.");
        TestAssert.Require(
            PortableSceneRenderer.ResolveViewport(target, lowerLeft, GraphicsBackend.Vulkan) ==
            new GraphicsRect(10, 120, 200, 100),
            "Vulkan normalized camera viewport did not convert to its top-left origin.");

        var camera = RenderCamera.Default;
        TestAssert.Require(camera.ContainsLayer(SortingLayer.Default),
            "Default render camera does not include the default sorting layer.");
        TestAssert.Require(camera.IsVisible(Vector2.zero, 0, Vector2.one, 200, 100),
            "A centered sprite was rejected by camera frustum culling.");
        TestAssert.Require(!camera.IsVisible(new Vector2(100, 100), 0, Vector2.one, 200, 100),
            "An offscreen sprite was not rejected by camera frustum culling.");
        TestAssert.Require(camera.IsVisible(new Vector2(Fix64.Parse("10.2"), 0), 45,
                new Vector2(2, 2), 200, 100),
            "A rotated sprite crossing the camera edge was incorrectly culled.");
    }

    private static void VerifyCameraStackRendering()
    {
        Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
        var scene = new Scene("Camera stack");
        try
        {
            var secondWorldLayer = SortingLayer.FromIndex(2);
            var leftCamera = scene.CreateGameObject("Left Camera").AddComponent<Camera2D>();
            leftCamera.priority = 10;
            leftCamera.clearMode = CameraClearMode.Color;
            leftCamera.cullingMask = SortingLayer.Default;
            leftCamera.viewportRect = new Rect(0, 0, Fix64.Half, 1);

            var rightCamera = scene.CreateGameObject("Right Camera").AddComponent<Camera2D>();
            rightCamera.priority = 20;
            rightCamera.clearMode = CameraClearMode.DepthOnly;
            rightCamera.cullingMask = secondWorldLayer;
            rightCamera.viewportRect = new Rect(Fix64.Half, 0, Fix64.Half, 1);

            scene.CreateGameObject("Visible left").AddComponent<SpriteRenderer>();
            var offscreen = scene.CreateGameObject("Offscreen left");
            offscreen.transform.position = new Vector2(100, 100);
            offscreen.AddComponent<SpriteRenderer>();
            var visibleRight = scene.CreateGameObject("Visible right").AddComponent<SpriteRenderer>();
            visibleRight.sortingLayer = secondWorldLayer;

            var device = new RecordingGraphicsDevice();
            using var renderer = new PortableSceneRenderer(device);
            renderer.RenderCameras([scene], scene, [rightCamera, leftCamera],
                new GraphicsRect(0, 0, 400, 200), drawUi: false);

            TestAssert.Require(device.Viewports.Contains(new GraphicsRect(0, 0, 200, 200)) &&
                               device.Viewports.Contains(new GraphicsRect(200, 0, 200, 200)),
                "Camera stack did not apply each normalized viewport.");
            TestAssert.Require(device.Clears.Count(item =>
                                   (item.Flags & GraphicsClearFlags.Depth) != 0) == 2,
                "Color and DepthOnly cameras did not each clear depth.");
            TestAssert.Require(device.Draws.Count == 4 && device.Draws.All(item => item.VertexCount == 6),
                "Camera layer/frustum filtering rendered an unexpected number of quads.");
            TestAssert.Require(device.RasterizerStates.Contains(GraphicsRasterizerState.CullBackFaces),
                "Scene rendering did not enable back-face triangle culling.");
        }
        finally
        {
            if (scene.isCreated) scene.Dispose();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice
    {
        private GraphicsRect _viewport;
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL, "Recording GPU", "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.AlphaBlending |
            GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public List<GraphicsRect> Viewports { get; } = [];
        public List<(GraphicsClearFlags Flags, GraphicsRect Viewport)> Clears { get; } = [];
        public List<GraphicsRasterizerState> RasterizerStates { get; } = [];
        public List<DrawRecord> Draws { get; } = [];

        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new RecordingProgram(this, description.Label);
        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description) =>
            new RecordingMesh(this, description);
        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default) => throw new NotSupportedException();
        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            throw new NotSupportedException();
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) => throw new NotSupportedException();
        public void SetViewport(GraphicsRect viewport) { _viewport = viewport; Viewports.Add(viewport); }
        public void SetScissor(GraphicsRect? scissor) { }
        public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) =>
            Clears.Add((flags, _viewport));
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) => RasterizerStates.Add(state);
        public void BindTexture(int slot, IGraphicsTexture2D texture) { }
        public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) =>
            Draws.Add(new DrawRecord(_viewport, vertexCount));
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0) =>
            Draws.Add(new DrawRecord(_viewport, vertexCount));
        public void Dispose() { }
    }

    private sealed class RecordingProgram(IGraphicsDevice device, string label) : IGraphicsProgram
    {
        public IGraphicsDevice Device => device;
        public string Label => label;
        public void Bind() { }
        public void SetMatrix4x4(string name, System.Numerics.Matrix4x4 value) { }
        public void SetVector4(string name, System.Numerics.Vector4 value) { }
        public void SetFloat(string name, float value) { }
        public void SetInt(string name, int value) { }
        public void Dispose() { }
    }

    private sealed class RecordingMesh : IGraphicsMesh
    {
        public RecordingMesh(IGraphicsDevice device, GraphicsMeshDescription description)
        {
            Device = device;
            Label = description.Label;
            Layout = description.Layout;
            Topology = description.Topology;
            Usage = description.Usage;
        }
        public IGraphicsDevice Device { get; }
        public string Label { get; }
        public GraphicsVertexLayout Layout { get; }
        public GraphicsPrimitiveTopology Topology { get; }
        public GraphicsBufferUsage Usage { get; }
        public int VertexCount { get; private set; }
        public void Update(ReadOnlySpan<float> vertices) =>
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        public void Dispose() { }
    }

    private readonly record struct DrawRecord(GraphicsRect Viewport, int VertexCount);
}
