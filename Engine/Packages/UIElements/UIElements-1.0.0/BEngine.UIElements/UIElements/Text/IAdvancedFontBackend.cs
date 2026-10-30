using HarfBuzzSharp;
using System.Runtime.InteropServices;

namespace BEngine.UIElements;

// Layout, bidi, fallback selection and cache ownership are shared by every
// backend. Only font access and glyph rasterization vary by platform.
internal interface IAdvancedFontBackend : IDisposable
{
    int FontCount { get; }
    AdvancedFontMetrics GetMetrics(int index, float size);
    bool ContainsGlyphs(int index, string text);
    uint[] GetGlyphs(int index, string text);
    AdvancedGlyphRun Shape(int index, HarfBuzzSharp.Buffer buffer, float size);
    AdvancedTextImage Render(IReadOnlyList<AdvancedGlyphDrawRun> runs, int width, int height,
        float size, UIColor foreground);
}

internal readonly record struct AdvancedFontMetrics(float Ascent, float Descent, float Leading);
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct AdvancedGlyphPoint(float X, float Y);
internal sealed record AdvancedGlyphRun(uint[] Codepoints, uint[] Clusters, AdvancedGlyphPoint[] Points, float Width);
internal readonly record struct AdvancedGlyphDrawRun(int FontIndex, float X, float Baseline, AdvancedGlyphRun Result);
