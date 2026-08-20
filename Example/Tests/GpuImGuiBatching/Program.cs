using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.GpuImGuiBatching;

internal static class Program
{
    private static int Main()
    {
        try
        {
            Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
            using var device = new RecordingGraphicsDevice();
            var resources = new RecordingResources();
            using var renderer = new GpuCanvasRenderer(device, resourceResolver: resources);
            var clip = new GpuCanvasRect(0, 0, 640, 360);
            GpuCanvasCommand[] commands =
            [
                new(GpuCanvasCommandType.SolidRect, new GpuCanvasRect(0, 0, 640, 360), clip,
                    new GpuCanvasColor(32, 34, 37)),
                new(GpuCanvasCommandType.Text, new GpuCanvasRect(8, 8, 180, 24), clip,
                    new GpuCanvasColor(230, 230, 230), "Hierarchy", 13),
                new(GpuCanvasCommandType.Image, new GpuCanvasRect(196, 8, 16, 16), clip,
                    new GpuCanvasColor(255, 255, 255), "Icons/Search.png"),
                new(GpuCanvasCommandType.Text, new GpuCanvasRect(220, 8, 180, 24), clip,
                    new GpuCanvasColor(230, 230, 230), "Inspector", 13),
                new(GpuCanvasCommandType.SolidRect, new GpuCanvasRect(0, 38, 640, 1), clip,
                    new GpuCanvasColor(18, 18, 18))
            ];

            renderer.Render(commands, 640, 360);
            Require(renderer.LastRenderStats is
                { CommandCount: 5, VisibleCommandCount: 5, BatchCount: 1, DrawCallCount: 1,
                    BufferUploadCount: 1, VertexCount: 30 },
                $"Unexpected first-frame batch statistics: {renderer.LastRenderStats}.");
            Require(device.Draws.Count == 1 && device.Draws[0].VertexCount == 30,
                "Interleaved IMGUI background, text and icon commands were not submitted as one draw.");
            Require(device.Meshes.Single(mesh => mesh.Label.EndsWith("TextureDynamic")).UpdateCount == 1,
                "The combined IMGUI vertex buffer was uploaded more than once.");
            Require(device.Textures.Single().UpdateCount == 1,
                "New atlas resources were not coalesced into one texture upload.");

            renderer.Render(commands, 640, 360);
            Require(renderer.LastRenderStats.DrawCallCount == 1 && device.Draws.Count == 2,
                "Cached IMGUI resources regressed to per-command draws.");
            Require(device.Textures.Single().UpdateCount == 1 && resources.ResolveCount == 3,
                "Cached text or icons were rasterized/uploaded again on an unchanged frame.");

            var gradient = commands[0] with
            {
                Type = GpuCanvasCommandType.GradientRect,
                Color = new GpuCanvasColor(255, 255, 255),
                Color2 = new GpuCanvasColor(255, 0, 0),
                Color3 = new GpuCanvasColor(0, 0, 0),
                Color4 = new GpuCanvasColor(0, 0, 0)
            };
            renderer.Render([commands[0], gradient, commands[4]], 640, 360);
            Require(renderer.LastRenderStats is
                { CommandCount: 3, VisibleCommandCount: 3, BatchCount: 1, DrawCallCount: 1, VertexCount: 18 },
                $"GPU color gradients escaped the shared IMGUI batch: {renderer.LastRenderStats}.");

            var otherClip = new GpuCanvasRect(320, 0, 320, 360);
            renderer.Render(
            [
                commands[0],
                commands[0] with { ClipRect = otherClip },
                commands[0]
            ], 640, 360);
            Require(renderer.LastRenderStats.DrawCallCount == 3,
                "Non-consecutive clip regions were reordered while batching.");
            Console.WriteLine("GPU_IMGUI_BATCHING_OK|commands=5|draws=1|gradients=shared|meshUploads=1|atlasUploads=1");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GPU_IMGUI_BATCHING_FAILED|{exception}");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private sealed class RecordingResources : IGpuCanvasResourceResolver, IGpuCanvasTextResolver
    {
        public int ResolveCount { get; private set; }

        public bool TryResolveTexture(string source, out GpuCanvasTextureData texture)
        {
            ResolveCount++;
            texture = CreateTexture(8, 8);
            return true;
        }

        public bool TryResolveText(string text, int width, int height, float fontSize, string fontFamily,
            out GpuCanvasTextureData texture)
        {
            ResolveCount++;
            texture = CreateTexture(width, height);
            return true;
        }

        private static GpuCanvasTextureData CreateTexture(int width, int height) =>
            new(width, height, GraphicsTextureFormat.Rgba8Unorm,
                Enumerable.Repeat(byte.MaxValue, checked(width * height * 4)).ToArray());
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(GraphicsBackend.OpenGL, "Batch GPU", "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.SampledTextures |
            GraphicsDeviceFeatures.AlphaBlending | GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public List<RecordingMesh> Meshes { get; } = [];
        public List<RecordingTexture> Textures { get; } = [];
        public List<DrawRecord> Draws { get; } = [];

        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new RecordingProgram(this, description.Label);

        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description)
        {
            var mesh = new RecordingMesh(this, description);
            Meshes.Add(mesh);
            return mesh;
        }

        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default)
        {
            var texture = new RecordingTexture(this, label, description);
            Textures.Add(texture);
            return texture;
        }

        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            throw new NotSupportedException();
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) => throw new NotSupportedException();
        public void SetViewport(GraphicsRect viewport) { }
        public void SetScissor(GraphicsRect? scissor) { }
        public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) { }
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) { }
        public void BindTexture(int slot, IGraphicsTexture2D texture) { }
        public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) =>
            Draws.Add(new DrawRecord((RecordingMesh)mesh, vertexCount, firstVertex));
        public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0) { }
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

    private sealed class RecordingMesh(IGraphicsDevice device, GraphicsMeshDescription description) : IGraphicsMesh
    {
        public IGraphicsDevice Device => device;
        public string Label => description.Label;
        public GraphicsVertexLayout Layout => description.Layout;
        public GraphicsPrimitiveTopology Topology => description.Topology;
        public GraphicsBufferUsage Usage => description.Usage;
        public int VertexCount { get; private set; }
        public int UpdateCount { get; private set; }
        public void Update(ReadOnlySpan<float> vertices)
        {
            UpdateCount++;
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        }
        public void Dispose() { }
    }

    private sealed class RecordingTexture(
        IGraphicsDevice device,
        string label,
        GraphicsTextureDescription description) : IGraphicsTexture2D
    {
        public IGraphicsDevice Device => device;
        public string Label => label;
        public GraphicsTextureDescription Description => description;
        public int UpdateCount { get; private set; }
        public void Update(ReadOnlySpan<byte> pixels) => UpdateCount++;
        public void Dispose() { }
    }

    private sealed record DrawRecord(RecordingMesh Mesh, int VertexCount, int FirstVertex);
}
