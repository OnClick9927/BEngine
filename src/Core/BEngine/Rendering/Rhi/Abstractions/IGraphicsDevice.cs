using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

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
    void Draw(IGraphicsMesh mesh, int vertexCount, int firstVertex = 0);
    void Draw(int vertexCount, GraphicsPrimitiveTopology topology, int firstVertex = 0);
}
