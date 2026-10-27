using System.Runtime.InteropServices;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

/// <summary>Four already projected screen coordinates. No layout or camera conversion is performed.</summary>
public readonly struct ScreenSpaceQuad(float x0, float y0, float x1, float y1)
{
    public float X0 { get; } = x0;
    public float Y0 { get; } = y0;
    public float X1 { get; } = x1;
    public float Y1 { get; } = y1;
}

/// <summary>A screen rectangle and its matching top-left/bottom-right texture coordinates.</summary>
public readonly struct ScreenSpaceTexturedQuad(
    float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1)
{
    public float X0 { get; } = x0;
    public float Y0 { get; } = y0;
    public float X1 { get; } = x1;
    public float Y1 { get; } = y1;
    public float U0 { get; } = u0;
    public float V0 { get; } = v0;
    public float U1 { get; } = u1;
    public float V1 { get; } = v1;
}

/// <summary>
/// Appends tightly packed triangle vertices at the graphics boundary. Callers retain
/// layout, resource and batching ownership; this writer retains none of its inputs.
/// </summary>
public static class ScreenSpaceGeometryWriter
{
    /// <summary>Appends six vertices with position.xy/color.rgba (36 floats).</summary>
    public static void AppendSolidQuad(List<float> output,
        float x0, float y0, float x1, float y1, NVector4 color)
    {
        var vertices = ReserveSolid(output, 1);
        WriteSolid(vertices, x0, y0, x1, y1, color);
    }

    /// <summary>Appends six vertices with position.xy/color.rgba/uv.xy (48 floats).</summary>
    public static void AppendTexturedQuad(List<float> output,
        float x0, float y0, float x1, float y1, NVector4 color,
        float u0, float v0, float u1, float v1)
    {
        var vertices = ReserveTextured(output, 1);
        WriteTextured(vertices, x0, y0, x1, y1, color, u0, v0, u1, v1);
    }

    /// <summary>Appends an ordered span of solid quads sharing one color.</summary>
    public static void AppendSolidQuads(List<float> output, ReadOnlySpan<ScreenSpaceQuad> quads, NVector4 color)
    {
        var vertices = ReserveSolid(output, quads.Length);
        for (var index = 0; index < quads.Length; index++)
        {
            ref readonly var quad = ref quads[index];
            WriteSolid(vertices.Slice(index * 6, 6), quad.X0, quad.Y0, quad.X1, quad.Y1, color);
        }
    }

    /// <summary>Appends an ordered span of textured quads sharing one color.</summary>
    public static void AppendTexturedQuads(List<float> output,
        ReadOnlySpan<ScreenSpaceTexturedQuad> quads, NVector4 color)
    {
        var vertices = ReserveTextured(output, quads.Length);
        for (var index = 0; index < quads.Length; index++)
        {
            ref readonly var quad = ref quads[index];
            WriteTextured(vertices.Slice(index * 6, 6), quad.X0, quad.Y0, quad.X1, quad.Y1,
                color, quad.U0, quad.V0, quad.U1, quad.V1);
        }
    }

    private static Span<SolidVertex> ReserveSolid(List<float> output, int quadCount) =>
        MemoryMarshal.Cast<float, SolidVertex>(Reserve(output, checked(quadCount * 36)));

    private static Span<TexturedVertex> ReserveTextured(List<float> output, int quadCount) =>
        MemoryMarshal.Cast<float, TexturedVertex>(Reserve(output, checked(quadCount * 48)));

    private static Span<float> Reserve(List<float> output, int floatCount)
    {
        ArgumentNullException.ThrowIfNull(output);
        var offset = output.Count;
        if (floatCount != 0) CollectionsMarshal.SetCount(output, checked(offset + floatCount));
        return CollectionsMarshal.AsSpan(output).Slice(offset, floatCount);
    }

    private static void WriteSolid(Span<SolidVertex> vertices,
        float x0, float y0, float x1, float y1, NVector4 color)
    {
        var a = new NVector2(x0, y0); var b = new NVector2(x1, y0);
        var c = new NVector2(x1, y1); var d = new NVector2(x0, y1);
        vertices[0] = new(a, color); vertices[1] = new(b, color); vertices[2] = new(c, color);
        vertices[3] = new(a, color); vertices[4] = new(c, color); vertices[5] = new(d, color);
    }

    private static void WriteTextured(Span<TexturedVertex> vertices,
        float x0, float y0, float x1, float y1, NVector4 color,
        float u0, float v0, float u1, float v1)
    {
        var a = new NVector2(x0, y0); var b = new NVector2(x1, y0);
        var c = new NVector2(x1, y1); var d = new NVector2(x0, y1);
        var uvA = new NVector2(u0, v0); var uvB = new NVector2(u1, v0);
        var uvC = new NVector2(u1, v1); var uvD = new NVector2(u0, v1);
        vertices[0] = new(a, color, uvA); vertices[1] = new(b, color, uvB); vertices[2] = new(c, color, uvC);
        vertices[3] = new(a, color, uvA); vertices[4] = new(c, color, uvC); vertices[5] = new(d, color, uvD);
    }

    [StructLayout(LayoutKind.Sequential, Pack = sizeof(float))]
    private readonly struct SolidVertex(NVector2 position, NVector4 color)
    {
        private readonly NVector2 _position = position;
        private readonly NVector4 _color = color;
    }

    [StructLayout(LayoutKind.Sequential, Pack = sizeof(float))]
    private readonly struct TexturedVertex(NVector2 position, NVector4 color, NVector2 uv)
    {
        private readonly NVector2 _position = position;
        private readonly NVector4 _color = color;
        private readonly NVector2 _uv = uv;
    }
}
