using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsProgram : IGraphicsResource
{
    void Bind();
    void SetMatrix4x4(string name, NumericsMatrix4x4 value);
    void SetVector4(string name, NumericsVector4 value);
    void SetFloat(string name, float value);
    void SetInt(string name, int value);
}
