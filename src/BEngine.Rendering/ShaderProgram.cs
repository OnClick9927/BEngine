using Silk.NET.OpenGL;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

internal sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        _handle = gl.CreateProgram();
        gl.AttachShader(_handle, vertex);
        gl.AttachShader(_handle, fragment);
        gl.LinkProgram(_handle);
        gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException($"OpenGL program link failed: {gl.GetProgramInfoLog(_handle)}");
        }

        gl.DetachShader(_handle, vertex);
        gl.DetachShader(_handle, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
    }

    public void Use() => _gl.UseProgram(_handle);

    public unsafe void SetMatrix(string name, NMatrix4x4 matrix)
    {
        var location = _gl.GetUniformLocation(_handle, name);
        _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
    }

    public void SetVector(string name, NVector4 value) =>
        _gl.Uniform4(_gl.GetUniformLocation(_handle, name), value.X, value.Y, value.Z, value.W);

    public void SetVector(string name, NVector3 value) =>
        _gl.Uniform3(_gl.GetUniformLocation(_handle, name), value.X, value.Y, value.Z);

    public void SetFloat(string name, float value) =>
        _gl.Uniform1(_gl.GetUniformLocation(_handle, name), value);

    public void SetInt(string name, int value) =>
        _gl.Uniform1(_gl.GetUniformLocation(_handle, name), value);

    public void Dispose()
    {
        _gl.DeleteProgram(_handle);
        GC.SuppressFinalize(this);
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException($"OpenGL shader compile failed: {_gl.GetShaderInfoLog(shader)}");
        }

        return shader;
    }
}
