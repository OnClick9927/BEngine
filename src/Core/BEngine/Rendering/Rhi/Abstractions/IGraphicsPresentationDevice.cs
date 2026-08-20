using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsPresentationDevice : IGraphicsDevice
{
    void BeginFrame(int width, int height);
    void Present();
}
