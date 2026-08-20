using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsRenderTarget : IGraphicsResource
{
    int Width { get; }
    int Height { get; }
    IGraphicsTexture2D? ColorTexture { get; }
    IGraphicsTexture2D? DepthTexture { get; }
}
