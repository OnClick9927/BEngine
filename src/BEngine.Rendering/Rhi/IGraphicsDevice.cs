using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsResource : IDisposable
{
    IGraphicsDevice Device { get; }
    string Label { get; }
}

public interface IGraphicsProgram : IGraphicsResource
{
    void Bind();
    void SetMatrix4x4(string name, NumericsMatrix4x4 value);
    void SetVector4(string name, NumericsVector4 value);
    void SetVector3(string name, NumericsVector3 value);
    void SetFloat(string name, float value);
    void SetInt(string name, int value);
}

public interface IGraphicsMesh : IGraphicsResource
{
    GraphicsVertexLayout Layout { get; }
    GraphicsPrimitiveTopology Topology { get; }
    GraphicsBufferUsage Usage { get; }
    int VertexCount { get; }
    void Update(ReadOnlySpan<float> vertices);
}

public interface IGraphicsTexture2D : IGraphicsResource
{
    GraphicsTextureDescription Description { get; }
    void Update(ReadOnlySpan<byte> pixels);
}

public interface IGraphicsRenderTarget : IGraphicsResource
{
    int Width { get; }
    int Height { get; }
    IGraphicsTexture2D? ColorTexture { get; }
    IGraphicsTexture2D? DepthTexture { get; }
}

public interface IGraphicsDevice : IDisposable
{
    GraphicsBackend Backend { get; }
    GraphicsDeviceCapabilities Capabilities { get; }

    IGraphicsProgram CreateProgram(GraphicsShaderProgramDescription description);
    IGraphicsMesh CreateMesh(GraphicsMeshDescription description);
    IGraphicsTexture2D CreateTexture2D(
        string label,
        GraphicsTextureDescription description,
        ReadOnlySpan<byte> initialData = default);
    IGraphicsRenderTarget CreateRenderTarget(string label, GraphicsRenderTargetDescription description);

    IDisposable PushRenderTarget(IGraphicsRenderTarget renderTarget);
    void SetViewport(GraphicsRect viewport);
    /// <summary>Sets a top-left-origin rectangle relative to the current viewport, or disables clipping.</summary>
    void SetScissor(GraphicsRect? scissor);
    void Clear(GraphicsClearFlags flags, NumericsVector4 color);
    void SetDepthState(GraphicsDepthState state);
    void SetBlendMode(GraphicsBlendMode mode);
    void SetRasterizerState(GraphicsRasterizerState state);
    void BindTexture(int slot, IGraphicsTexture2D texture);
    void Draw(IGraphicsMesh mesh);
    void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0);
}

public interface IGraphicsDeviceProvider
{
    GraphicsBackend Backend { get; }
    GraphicsBackendSupport QuerySupport();
    IGraphicsDevice CreateDevice();
}
