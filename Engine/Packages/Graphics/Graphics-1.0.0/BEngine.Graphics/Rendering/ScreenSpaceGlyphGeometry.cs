using System.Runtime.InteropServices;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering.Rhi;

/// <summary>One immutable local glyph rectangle with its atlas coordinates.</summary>
public readonly struct ScreenSpaceGlyphPlacement(
    float x, float y, float width, float height, float u0, float v0, float u1, float v1)
{
    public readonly float X = x, Y = y, Width = width, Height = height;
    public readonly float U0 = u0, V0 = v0, U1 = u1, V1 = v1;
}

/// <summary>Bounds of translated glyph rectangles, before UI clipping.</summary>
public readonly struct ScreenSpaceGlyphBounds(float left, float top, float right, float bottom)
{
    public readonly float Left = left, Top = top, Right = right, Bottom = bottom;
}

/// <summary>
/// Builds screen-space text geometry from already laid out glyphs. Layout, atlas
/// lifetime, clipping and draw order stay with the caller; no input is retained.
/// </summary>
public static class ScreenSpaceGlyphGeometry
{
    /// <summary>Measures the translated rectangles in their original iteration order.</summary>
    public static ScreenSpaceGlyphBounds MeasureBounds(
        ReadOnlySpan<ScreenSpaceGlyphPlacement> glyphs, float originX, float originY)
    {
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (ref readonly var glyph in glyphs)
        {
            var x = originX + glyph.X; var y = originY + glyph.Y;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x + glyph.Width); bottom = Math.Max(bottom, y + glyph.Height);
        }
        return new(left, top, right, bottom);
    }

    /// <summary>Appends six position/color/UV vertices per glyph in one operation.</summary>
    public static void Append(List<float> output, ReadOnlySpan<ScreenSpaceGlyphPlacement> glyphs,
        float originX, float originY, NVector4 color)
    {
        ArgumentNullException.ThrowIfNull(output);
        var offset = output.Count;
        var floatCount = checked(glyphs.Length * 48);
        if (floatCount != 0) CollectionsMarshal.SetCount(output, checked(offset + floatCount));
        var vertices = MemoryMarshal.Cast<float, Vertex>(CollectionsMarshal.AsSpan(output).Slice(offset, floatCount));
        for (var index = 0; index < glyphs.Length; index++)
        {
            ref readonly var glyph = ref glyphs[index];
            // Keep the translation before adding width/height. Precomputed
            // local right/bottom coordinates change floating-point rounding.
            var x0 = originX + glyph.X; var y0 = originY + glyph.Y;
            var x1 = x0 + glyph.Width; var y1 = y0 + glyph.Height;
            var a = new NVector2(x0, y0); var b = new NVector2(x1, y0);
            var c = new NVector2(x1, y1); var d = new NVector2(x0, y1);
            var uvA = new NVector2(glyph.U0, glyph.V0); var uvB = new NVector2(glyph.U1, glyph.V0);
            var uvC = new NVector2(glyph.U1, glyph.V1); var uvD = new NVector2(glyph.U0, glyph.V1);
            var first = index * 6;
            vertices[first] = new(a, color, uvA); vertices[first + 1] = new(b, color, uvB);
            vertices[first + 2] = new(c, color, uvC); vertices[first + 3] = new(a, color, uvA);
            vertices[first + 4] = new(c, color, uvC); vertices[first + 5] = new(d, color, uvD);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = sizeof(float))]
    private readonly struct Vertex(NVector2 position, NVector4 color, NVector2 uv)
    {
        private readonly NVector2 _position = position;
        private readonly NVector4 _color = color;
        private readonly NVector2 _uv = uv;
    }
}
