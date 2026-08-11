using Silk.NET.OpenGL;

namespace BEngine.Rendering;

internal sealed class MeshBuffer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;

    public uint Primitive { get; }
    public uint VertexCount { get; }

    public unsafe MeshBuffer(GL gl, float[] vertices, uint primitive = (uint)GLEnum.Triangles)
    {
        _gl = gl;
        Primitive = primitive;
        VertexCount = (uint)(vertices.Length / 6);
        _vertexArray = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (float* pointer = vertices)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)),
                pointer, BufferUsageARB.StaticDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float),
            (void*)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vertexArray);
        _gl.DrawArrays((PrimitiveType)Primitive, 0, VertexCount);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteVertexArray(_vertexArray);
        GC.SuppressFinalize(this);
    }
}
