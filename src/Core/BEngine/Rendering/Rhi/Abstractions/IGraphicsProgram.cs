using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

public interface IGraphicsProgram : IGraphicsResource
{
    void Bind();
    void SetMatrix4x4(string name, NumericsMatrix4x4 value);
    void SetVector4(string name, NumericsVector4 value);
    /// <summary>Updates a contiguous vec4 array beginning at its [0] uniform.</summary>
    void SetVector4Array(string firstElementName, ReadOnlySpan<NumericsVector4> values)
    {
        if (!firstElementName.EndsWith("[0]", StringComparison.Ordinal)) throw new ArgumentException("Expected an array [0] uniform.", nameof(firstElementName));
        var prefix = firstElementName[..^3];
        for (var i = 0; i < values.Length; i++) SetVector4(prefix + "[" + i + "]", values[i]);
    }
    void SetFloat(string name, float value);
    void SetInt(string name, int value);
}
