using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsMesh : IGraphicsResource
{
    GraphicsVertexLayout Layout { get; }
    GraphicsPrimitiveTopology Topology { get; }
    GraphicsBufferUsage Usage { get; }
    int VertexCount { get; }
    void Update(ReadOnlySpan<float> vertices);
}
