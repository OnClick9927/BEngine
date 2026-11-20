using BEngine;
using BEngine.Rendering.Rhi;
using Matrix = System.Numerics.Matrix4x4;
using Vec4 = System.Numerics.Vector4;

sealed class RecordingDevice : IGraphicsDevice
{
    public GraphicsBackend Backend => GraphicsBackend.OpenGL;
    public GraphicsDeviceCapabilities Capabilities { get; } = new(GraphicsBackend.OpenGL, "Boundary recording device", "1", (GraphicsDeviceFeatures)1023, [GraphicsShaderLanguage.Glsl]);
    public int Draws, Vertices, ResourcesCreated, ResourcesDisposed;
    public IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription d) => new ProgramResource(this, d.Label);
    public IGraphicsMesh CreateMesh(GraphicsMeshDescription d) => new Mesh(this, d);
    public IGraphicsTexture2D CreateTexture2D(string label, GraphicsTextureDescription d, ReadOnlySpan<byte> data = default) => new Texture(this, label, d);
    public IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription d) => new Target(this, label, d);
    public IDisposable PushRenderTarget(IGraphicsRenderTarget target) => new Lease();
    public void SetViewport(GraphicsRect v) { }
    public void SetScissor(GraphicsRect? v) { }
    public void Clear(GraphicsClearFlags f, Vec4 c) { }
    public void SetDepthState(GraphicsDepthState s) { }
    public void SetBlendMode(GraphicsBlendMode s) { }
    public void SetRasterizerState(GraphicsRasterizerState s) { }
    public void BindTexture(int slot, IGraphicsTexture2D t) { }
    public void Draw(IGraphicsMesh m) => Draw(m, m.VertexCount);
    public void Draw(IGraphicsMesh m, int count, int firstVertex = 0) { Draws++; Vertices += count; }
    public void Draw(int count, GraphicsPrimitiveTopology t, int firstVertex = 0) { Draws++; Vertices += count; }
    public void Dispose() { }
    sealed class Lease : IDisposable { public void Dispose() { } }
    abstract class Resource(RecordingDevice device, string label) : IGraphicsResource
    {
        private readonly int _registration = ++device.ResourcesCreated;
        private bool _disposed;
        public IGraphicsDevice Device => device;
        public string Label => label;
        public void Dispose() { if (!_disposed) { _disposed = true; device.ResourcesDisposed++; } }
    }
    sealed class ProgramResource(RecordingDevice device, string label) : Resource(device, label), IGraphicsProgram
    {
        public void Bind() { }
        public void SetMatrix4x4(string name, Matrix v) { }
        public void SetVector4(string name, Vec4 v) { }
        public void SetVector4Array(string name, ReadOnlySpan<Vec4> values) { }
        public void SetFloat(string name, float v) { }
        public void SetInt(string name, int v) { }
    }
    sealed class Mesh(RecordingDevice device, GraphicsMeshDescription d) : Resource(device, d.Label), IGraphicsMesh
    {
        public GraphicsVertexLayout Layout => d.Layout;
        public GraphicsPrimitiveTopology Topology => d.Topology;
        public GraphicsBufferUsage Usage => d.Usage;
        public int VertexCount { get; private set; } = d.VertexCount;
        public void Update(ReadOnlySpan<float> data) => VertexCount = data.Length * sizeof(float) / Layout.StrideBytes;
    }
    sealed class Texture(RecordingDevice device, string label, GraphicsTextureDescription d) : Resource(device, label), IGraphicsTexture2D
    {
        public GraphicsTextureDescription Description => d;
        public void Update(ReadOnlySpan<byte> data) { }
    }
    sealed class Target(RecordingDevice device, string label, GraphicsRenderTargetDescription d) : Resource(device, label), IGraphicsRenderTarget
    {
        public int Width => d.Width;
        public int Height => d.Height;
        public IGraphicsTexture2D? ColorTexture => null;
        public IGraphicsTexture2D? DepthTexture => null;
    }
}

sealed class DrawingPlatform : IGraphicsPlatformService
{
    public GraphicsBackend DefaultBackend => GraphicsBackend.OpenGL;
    public GraphicsBackend PreferredBackend { get; set; }
    public GraphicsDeviceCapabilities? ActiveCapabilities => null;
    public IReadOnlyList<GraphicsBackendDescriptor> Backends => [];
    public void ReportActive(GraphicsDeviceCapabilities c) { }
    public GraphicsShaderPair ResolveShaderVariant(GraphicsDeviceCapabilities c, string stem) => new(
        new(GraphicsShaderStage.Vertex, GraphicsShaderLanguage.Glsl, "void main(){}"),
        new(GraphicsShaderStage.Fragment, GraphicsShaderLanguage.Glsl, "void main(){}"));
    public GraphicsRect ResolveViewport(GraphicsRect target, Rect normalized, GraphicsBackend backend) => target;
}
