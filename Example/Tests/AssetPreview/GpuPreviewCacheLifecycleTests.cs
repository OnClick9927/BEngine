using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;
using EditorAssetPreview = BEngine.Editor.AssetPreview;

namespace BEngine.ExampleTests.AssetPreview;

internal static class GpuPreviewCacheLifecycleTests
{
    private const string FirstCanonical = "C:/x.png";
    private const string SecondCanonical = "C:/y.png";
    private const string RevisionQuery = "?bengine-preview=";

    internal static void Run()
    {
        TestAssert.Require(typeof(IGraphicsResourceRetirement).IsAssignableFrom(
                typeof(BEngine.Rendering.Rhi.Vulkan.VulkanGraphicsDevice)),
            "The Vulkan backend does not expose fence-backed graphics resource retirement.");
        Resources.RegisterResourceRoot(Path.Combine(FindRepositoryRoot(), "src", "Core"));
        EditorAssetPreview.ClearTemporaryAssetPreviews();
        using var device = new RecordingGraphicsDevice();
        var resources = new RecordingResources();
        using var renderer = new GpuCanvasRenderer(device, resourceResolver: resources);

        Render(renderer, FirstCanonical + RevisionQuery + "1");
        var first = device.PreviewTextures.Single();
        TestAssert.Require(first.DisposeCount == 0 && resources.Sources.SequenceEqual(
                               [FirstCanonical + RevisionQuery + "1"]),
            "The first GPU asset-preview revision was not created exactly once.");

        Render(renderer, FirstCanonical + RevisionQuery + "2");
        var second = device.PreviewTextures.Single(texture => !ReferenceEquals(texture, first));
        TestAssert.Require(device.PreviewTextures.Count == 2 && first.DisposeCount == 1 &&
                           second.DisposeCount == 0 && resources.Sources.Count == 2,
            "A new revision did not replace and dispose the previous canonical preview texture.");
        Render(renderer, FirstCanonical + RevisionQuery + "2");
        TestAssert.Require(device.PreviewTextures.Count == 2 && resources.Sources.Count == 2 &&
                           second.DisposeCount == 0,
            "Rendering an unchanged preview revision allocated another GPU texture.");

        EditorAssetPreview.Invalidate(FirstCanonical);
        TestAssert.Require(second.DisposeCount == 0,
            "AssetPreview.Invalidate disposed GPU state outside the renderer frame boundary.");
        renderer.Render([], 128, 128);
        TestAssert.Require(second.DisposeCount == 1,
            "The next GPU Render did not release the explicitly invalidated preview texture.");

        Render(renderer,
            SecondCanonical + RevisionQuery + "same-frame-1",
            SecondCanonical + RevisionQuery + "same-frame-2");
        var sameFrame = device.PreviewTextures.TakeLast(2).ToArray();
        TestAssert.Require(sameFrame[0].DisposeCount == 1 && sameFrame[1].DisposeCount == 0 &&
                           device.RetirementCount > 0,
            "Replacing a preview revision in one frame did not retire the old texture after drawing.");
        EditorAssetPreview.Invalidate(SecondCanonical);
        renderer.Render([], 128, 128);

        Render(renderer, FirstCanonical + RevisionQuery + "3");
        Render(renderer, SecondCanonical + RevisionQuery + "1");
        var live = device.PreviewTextures.Where(texture => texture.DisposeCount == 0).ToArray();
        TestAssert.Require(live.Length == 2,
            $"Expected two live canonical previews before clearing, found {live.Length}.");
        EditorAssetPreview.ClearTemporaryAssetPreviews();
        TestAssert.Require(live.All(texture => texture.DisposeCount == 0),
            "ClearTemporaryAssetPreviews disposed GPU state outside the renderer frame boundary.");
        renderer.Render([], 128, 128);
        TestAssert.Require(live.All(texture => texture.DisposeCount == 1) &&
                           device.PreviewTextures.All(texture => texture.DisposeCount == 1),
            "ClearTemporaryAssetPreviews did not release every cached GPU preview texture.");

        var boundedStart = device.PreviewTextures.Count;
        for (var index = 0; index < 64; index++)
            Render(renderer, $"C:/bounded-{index}.png{RevisionQuery}1");
        Render(renderer, $"C:/bounded-0.png{RevisionQuery}1");
        Render(renderer, $"C:/bounded-64.png{RevisionQuery}1");
        var bounded = device.PreviewTextures.Skip(boundedStart).ToArray();
        TestAssert.Require(bounded.Length == 65 && bounded[0].DisposeCount == 0 &&
                           bounded[1].DisposeCount == 1 &&
                           bounded.Count(texture => texture.DisposeCount == 0) == 64,
            "The GPU asset-preview cache did not evict its least-recently-used texture at capacity.");
        EditorAssetPreview.ClearTemporaryAssetPreviews();
        renderer.Render([], 128, 128);
        TestAssert.Require(bounded.All(texture => texture.DisposeCount == 1),
            "Clearing a full GPU asset-preview cache left live textures behind.");

        Render(renderer, FirstCanonical + RevisionQuery + "4");
        var disposedWithRenderer = device.PreviewTextures.Single(texture => texture.DisposeCount == 0);
        renderer.Dispose();
        TestAssert.Require(disposedWithRenderer.DisposeCount == 1,
            "GpuCanvasRenderer.Dispose did not release its remaining preview texture.");
        EditorAssetPreview.ClearTemporaryAssetPreviews();
        TestAssert.Require(disposedWithRenderer.DisposeCount == 1,
            "A disposed GpuCanvasRenderer retained its preview invalidation subscription.");
    }

    private static void Render(GpuCanvasRenderer renderer, string source)
        => Render(renderer, [source]);

    private static void Render(GpuCanvasRenderer renderer, params string[] sources)
    {
        var clip = new GpuCanvasRect(0, 0, 128, 128);
        renderer.Render(sources.Select((source, index) =>
                new GpuCanvasCommand(GpuCanvasCommandType.Image,
                new GpuCanvasRect(8, 8, 96, 64), clip,
                    new GpuCanvasColor(255, 255, 255), source)).ToArray(), 128, 128);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }

    private sealed class RecordingResources : IGpuCanvasResourceResolver
    {
        internal List<string> Sources { get; } = [];

        public bool TryResolveTexture(string source, out GpuCanvasTextureData texture)
        {
            Sources.Add(source);
            texture = new GpuCanvasTextureData(4, 4, GraphicsTextureFormat.Rgba8Unorm,
                Enumerable.Repeat(byte.MaxValue, 4 * 4 * 4).ToArray());
            return true;
        }
    }

    private sealed class RecordingGraphicsDevice : IGraphicsDevice, IGraphicsResourceRetirement
    {
        public GraphicsBackend Backend => GraphicsBackend.OpenGL;
        public GraphicsDeviceCapabilities Capabilities { get; } = new(
            GraphicsBackend.OpenGL, "Preview Cache GPU", "1.0",
            GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
            GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.SampledTextures |
            GraphicsDeviceFeatures.AlphaBlending | GraphicsDeviceFeatures.ScissorRectangles,
            [GraphicsShaderLanguage.Glsl]);
        internal List<RecordingTexture> Textures { get; } = [];
        internal int RetirementCount { get; private set; }
        internal List<RecordingTexture> PreviewTextures => Textures
            .Where(texture => texture.Label.StartsWith("BEngine.GpuCanvas.AssetPreview.",
                StringComparison.Ordinal)).ToList();

        public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
            new RecordingProgram(this, description.Label);

        public IGraphicsMesh CreateMesh(GraphicsMeshDescription description) =>
            new RecordingMesh(this, description);

        public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription description,
            ReadOnlySpan<byte> initialData = default)
        {
            var texture = new RecordingTexture(this, label, description);
            Textures.Add(texture);
            return texture;
        }

        public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description) =>
            throw new NotSupportedException();
        public void RetireResource(IDisposable resource)
        {
            RetirementCount++;
            resource.Dispose();
        }
        public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) => throw new NotSupportedException();
        public void SetViewport(GraphicsRect viewport) { }
        public void SetScissor(GraphicsRect? scissor) { }
        public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) { }
        public void SetDepthState(GraphicsDepthState state) { }
        public void SetBlendMode(GraphicsBlendMode mode) { }
        public void SetRasterizerState(GraphicsRasterizerState state) { }
        public void BindTexture(int slot, IGraphicsTexture2D texture)
        {
            if (texture is RecordingTexture { DisposeCount: > 0 })
                throw new InvalidOperationException("The renderer bound a disposed preview texture.");
        }
        public void Draw(IGraphicsMesh mesh) { }
        public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) { }
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

    private sealed class RecordingMesh(IGraphicsDevice device,
        GraphicsMeshDescription description) : IGraphicsMesh
    {
        public IGraphicsDevice Device => device;
        public string Label => description.Label;
        public GraphicsVertexLayout Layout => description.Layout;
        public GraphicsPrimitiveTopology Topology => description.Topology;
        public GraphicsBufferUsage Usage => description.Usage;
        public int VertexCount { get; private set; }
        public void Update(ReadOnlySpan<float> vertices) =>
            VertexCount = vertices.Length * sizeof(float) / Layout.StrideBytes;
        public void Dispose() { }
    }

    private sealed class RecordingTexture(IGraphicsDevice device, string label,
        GraphicsTextureDescription description) : IGraphicsTexture2D
    {
        public IGraphicsDevice Device => device;
        public string Label => label;
        public GraphicsTextureDescription Description => description;
        internal int DisposeCount { get; private set; }
        public void Update(ReadOnlySpan<byte> pixels) { }
        public void Dispose() => DisposeCount++;
    }
}
