using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsTexture2D : IGraphicsResource
{
    GraphicsTextureDescription Description { get; }
    void Update(ReadOnlySpan<byte> pixels);
}
