using System.Runtime.InteropServices;

namespace BEngine.Platform;

/// <summary>Opens immutable embedded fonts for coverage, metrics and positioned glyph rasterization.
/// The host selects the native font engine. Text shaping and layout belong to the consumer.</summary>
public interface IPlatformFontRasterizer
{
    /// <summary>The returned face owns its font bytes; the input is borrowed only during this call.</summary>
    IPlatformFontFace OpenFace(ReadOnlySpan<byte> fontData);
}

/// <summary>Owned, synchronous font face. Use and dispose on its owning thread.</summary>
public interface IPlatformFontFace : IDisposable
{
    PlatformFontMetrics GetMetrics(float size);
    uint GetGlyph(uint codepoint);
    /// <summary>Draws glyphs into caller-owned premultiplied RGBA8. Positions are interleaved x/y floats,
    /// one pair per glyph; rgba is 0xRRGGBBAA. All spans are borrowed until the call returns.</summary>
    void DrawRun(float size, ReadOnlySpan<uint> glyphs, ReadOnlySpan<float> positions,
        float x, float baseline, uint rgba, Span<byte> pixels, int width, int height);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct PlatformFontMetrics(float Ascent, float Descent, float Leading);
