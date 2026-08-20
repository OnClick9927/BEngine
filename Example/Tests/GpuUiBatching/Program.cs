using System.Numerics;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.UIElements;

namespace BEngine.ExampleTests.GpuUiBatching;

internal static class Program
{
    private static int Main()
    {
        var repository = FindRepositoryRoot();
        Resources.RegisterResourceRoot(Path.Combine(repository, "src", "Core"));
        Resources.RegisterResourceRoot(Path.Combine(repository, "src", "UIElements"));
        using var device = new RecordingGraphicsDevice();
        using var renderer = new UIElementsRenderer(device, new TextResources());
        var root = new VisualElement();
        root.style.backgroundColor = new UIColor(40, 40, 40);
        root.Add(new Label("Main Camera 2D"));
        root.Add(new Label("Foreground Sprite"));
        root.Add(new Label("Cube"));

        renderer.Render(root, 320, 180);

        var texturedMesh = device.Meshes.Single(mesh => mesh.Label.Contains("TextureDynamicMesh"));
        var textDraws = device.Draws.Where(draw => ReferenceEquals(draw.Mesh, texturedMesh)).ToArray();
        Require(texturedMesh.UpdateCount == 1,
            $"Textured UI geometry was uploaded {texturedMesh.UpdateCount} times instead of once.");
        Require(textDraws.Length == 3, $"Expected 3 text draws, got {textDraws.Length}.");
        Require(textDraws.Select(draw => draw.FirstVertex).SequenceEqual([0, 6, 12]),
            $"Text draws reused the same vertex range: {string.Join(", ", textDraws.Select(draw => draw.FirstVertex))}.");
        Require(textDraws.All(draw => draw.VertexCount == 6), "Each text quad must draw exactly six vertices.");
        Console.WriteLine("GPU_UI_BATCHING_OK|uploads=1|drawRanges=0:6,6:6,12:6");
        return 0;
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

    private sealed class TextResources : IUIRenderResourceResolver, IUITextRenderResourceResolver
    {
        public bool TryResolveTexture(string source, out UIRenderTextureData texture)
        {
            texture = default;
            return false;
        }

        public bool TryResolveText(string text, int width, int height, float fontSize,
            out UIRenderTextureData texture)
        {
            texture = new UIRenderTextureData(width, height, GraphicsTextureFormat.Rgba8Unorm,
                new byte[checked(width * height * 4)]);
            return true;
        }
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL,
            "Recording GPU",
            "1.0",
            GraphicsDeviceFeatures.Rasterization |
            GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers |
            GraphicsDeviceFeatures.SampledTextures |
            GraphicsDeviceFeatures.AlphaBlending |
            GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        public List<RecordingMesh> Meshes { get; } = [];
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
            ReadOnlySpan<byte> initialData = default) => new RecordingTexture(this, label, description);

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
        public void Update(ReadOnlySpan<byte> pixels) { }
        public void Dispose() { }
    }

    private sealed record DrawRecord(RecordingMesh Mesh, int VertexCount, int FirstVertex);
}
