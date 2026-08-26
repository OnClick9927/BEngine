using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal sealed class RecordingBundleGraphicsDevice : IGraphicsDevice
{
    public GraphicsBackend Backend => GraphicsBackend.OpenGL;
    public GraphicsDeviceCapabilities Capabilities { get; } = new(
        GraphicsBackend.OpenGL, "Bundle Test GPU", "1.0",
        GraphicsDeviceFeatures.Rasterization | GraphicsDeviceFeatures.ShaderPrograms |
        GraphicsDeviceFeatures.DynamicVertexBuffers | GraphicsDeviceFeatures.SampledTextures |
        GraphicsDeviceFeatures.AlphaBlending | GraphicsDeviceFeatures.ScissorRectangles,
        [GraphicsShaderLanguage.Glsl]);
    internal List<RecordedBundleTexture> Textures { get; } = [];
    internal int DrawCount { get; private set; }

    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description) =>
        new RecordingProgram(this, description.Label);

    public IGraphicsMesh CreateMesh(GraphicsMeshDescription description) =>
        new RecordingMesh(this, description);

    public IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default)
    {
        var texture = new RecordedBundleTexture(this, label, description, initialData.ToArray());
        Textures.Add(texture);
        return texture;
    }

    public IGraphicsRenderTarget CreateRenderTarget(
        string label,
        GraphicsRenderTargetDescription description) => throw new NotSupportedException();

    public IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget) => throw new NotSupportedException();
    public void SetViewport(GraphicsRect viewport) { }
    public void SetScissor(GraphicsRect? scissor) { }
    public void Clear(GraphicsClearFlags flags, System.Numerics.Vector4 color) { }
    public void SetDepthState(GraphicsDepthState state) { }
    public void SetBlendMode(GraphicsBlendMode mode) { }
    public void SetRasterizerState(GraphicsRasterizerState state) { }
    public void BindTexture(int slot, IGraphicsTexture2D texture) { }
    public void Draw(IGraphicsMesh mesh) => Draw(mesh, mesh.VertexCount);
    public void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0) => DrawCount++;
    public void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0) => DrawCount++;
    public void Dispose() { }

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
        internal RecordingMesh(IGraphicsDevice device, GraphicsMeshDescription description)
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
}

internal sealed class RecordedBundleTexture(
    IGraphicsDevice device,
    string label,
    GraphicsTextureDescription description,
    byte[] initialData) : IGraphicsTexture2D
{
    public IGraphicsDevice Device => device;
    public string Label => label;
    public GraphicsTextureDescription Description => description;
    internal byte[] InitialData { get; private set; } = initialData;
    public void Update(ReadOnlySpan<byte> pixels) => InitialData = pixels.ToArray();
    public void Dispose() { }
}
