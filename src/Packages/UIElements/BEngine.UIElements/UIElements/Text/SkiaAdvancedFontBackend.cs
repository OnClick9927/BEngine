#if !BENGINE_WEB_FONTS
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace BEngine.UIElements;

internal sealed class SkiaAdvancedFontBackend : IAdvancedFontBackend
{
    private readonly SKTypeface[] _typefaces;
    private readonly SKShaper[] _shapers;
    private readonly SKFont[] _coverageFonts;

    internal SkiaAdvancedFontBackend(IReadOnlyList<DynamicFontSource> sources)
    {
        if (sources.Count == 0) throw new InvalidDataException("The embedded font chain is empty.");
        _typefaces = new SKTypeface[sources.Count];
        _shapers = new SKShaper[sources.Count];
        _coverageFonts = new SKFont[sources.Count];
        try
        {
            for (var index = 0; index < sources.Count; index++)
            {
                using var data = SKData.CreateCopy(sources[index].Data);
                _typefaces[index] = SKTypeface.FromData(data) ??
                    throw new InvalidDataException($"Embedded font '{sources[index].Name}' is not a supported OpenType font.");
                _shapers[index] = new SKShaper(_typefaces[index]);
                _coverageFonts[index] = new SKFont(_typefaces[index], 12);
            }
        }
        catch { Dispose(); throw; }
    }

    public int FontCount => _typefaces.Length;

    public AdvancedFontMetrics GetMetrics(int index, float size)
    {
        using var font = CreateFont(index, size);
        var metrics = font.Metrics;
        return new(metrics.Ascent, metrics.Descent, metrics.Leading);
    }

    public bool ContainsGlyphs(int index, string text) => _coverageFonts[index].ContainsGlyphs(text);
    public uint[] GetGlyphs(int index, string text) => (_coverageFonts[index].GetGlyphs(text) is { } projectionSource1656 ? projectionSource1656.Length == 0 ? global::System.Array.Empty<uint>() : global::System.Array.ConvertAll<ushort, uint>(projectionSource1656, value => (uint)value) : throw new global::System.ArgumentNullException("source"));

    public AdvancedGlyphRun Shape(int index, HarfBuzzSharp.Buffer buffer, float size)
    {
        using var font = CreateFont(index, size);
        var result = _shapers[index].Shape(buffer, font);
        return new(result.Codepoints, result.Clusters,
            (result.Points is { } projectionSource2002 ? projectionSource2002.Length == 0 ? global::System.Array.Empty<global::BEngine.UIElements.AdvancedGlyphPoint>() : global::System.Array.ConvertAll<global::SkiaSharp.SKPoint, global::BEngine.UIElements.AdvancedGlyphPoint>(projectionSource2002, point => new AdvancedGlyphPoint(point.X, point.Y)) : throw new global::System.ArgumentNullException("source")), result.Width);
    }

    public AdvancedTextImage Render(IReadOnlyList<AdvancedGlyphDrawRun> runs, int width, int height,
        float size, UIColor foreground)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = new SKColor(foreground.R, foreground.G, foreground.B, foreground.A), IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        foreach (var run in runs)
        {
            using var font = CreateFont(run.FontIndex, size);
            using var builder = new SKTextBlobBuilder();
            var projectionSource2789 = run.Result.Codepoints;
            ArgumentNullException.ThrowIfNull(projectionSource2789, "source");
            var projectionResult2789 = projectionSource2789.Length == 0 ? global::System.Array.Empty<ushort>() : new ushort[projectionSource2789.Length];
            for (var projectionIndex2789 = 0; projectionIndex2789 < projectionSource2789.Length; projectionIndex2789++)
            {
                var projectionItem2789 = projectionSource2789[projectionIndex2789];
                projectionResult2789[projectionIndex2789] = checked((ushort)projectionItem2789);
            }
            
            var glyphs = projectionResult2789;
            var projectionSource2887 = run.Result.Points;
            ArgumentNullException.ThrowIfNull(projectionSource2887, "source");
            var projectionResult2887 = projectionSource2887.Length == 0 ? global::System.Array.Empty<global::SkiaSharp.SKPoint>() : new global::SkiaSharp.SKPoint[projectionSource2887.Length];
            for (var projectionIndex2887 = 0; projectionIndex2887 < projectionSource2887.Length; projectionIndex2887++)
            {
                var projectionItem2887 = projectionSource2887[projectionIndex2887];
                projectionResult2887[projectionIndex2887] = new SKPoint(projectionItem2887.X + run.X, projectionItem2887.Y + run.Baseline);
            }
            
            var points = projectionResult2887;
            builder.AddPositionedRun(glyphs, font, points);
            using var blob = builder.Build();
            canvas.DrawText(blob, 0, 0, paint);
        }
        canvas.Flush();
        var pixels = bitmap.GetPixelSpan().ToArray();
        var colored = false;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            if (pixels[offset + 3] > 0 && (pixels[offset] != pixels[offset + 1] || pixels[offset + 1] != pixels[offset + 2]))
            { colored = true; break; }
        return new(width, height, pixels, colored);
    }

    private SKFont CreateFont(int index, float size) => new(_typefaces[index], size) { Subpixel = true };

    public void Dispose()
    {
        foreach (var font in _coverageFonts) font?.Dispose();
        foreach (var shaper in _shapers) shaper?.Dispose();
        foreach (var typeface in _typefaces) typeface?.Dispose();
    }
}
#endif
