#if BENGINE_WEB_FONTS
using System.Runtime.InteropServices;
using System.Text;
using HarfBuzzSharp;
using HBFont = HarfBuzzSharp.Font;

namespace BEngine.UIElements;

/// <summary>
/// The Web font backend retains HarfBuzz's OpenType shaping and uses FreeType
/// directly for coverage, metrics and positioned glyphs. It does not reshape
/// text with browser fonts or depend on asynchronous FontFace registration.
/// </summary>
internal sealed class WebAdvancedFontBackend : IAdvancedFontBackend
{
    // Match SKShaper's OpenType scale exactly; using upem directly changes the
    // integer rounding of advances and can change existing line breaks.
    private const int ShapingScale = 512;
    private readonly IntPtr[] _faces;
    private readonly HBFont[] _fonts;
    private readonly GCHandle[] _fontData;
    private bool _disposed;

    internal WebAdvancedFontBackend(IReadOnlyList<DynamicFontSource> sources)
    {
        if (sources.Count is < 1 or > 32) throw new InvalidDataException("Invalid embedded font chain.");
        _faces = new IntPtr[sources.Count];
        _fonts = new HBFont[sources.Count];
        _fontData = new GCHandle[sources.Count];
        try
        {
            for (var index = 0; index < sources.Count; index++)
            {
                var data = sources[index].Data;
                if (data is null || data.Length is < 12 or > 64 * 1024 * 1024)
                    throw new InvalidDataException("Invalid embedded font source.");
                _fontData[index] = GCHandle.Alloc(data, GCHandleType.Pinned);
                var pointer = _fontData[index].AddrOfPinnedObject();
                _faces[index] = WebFontNative.Create(pointer, data.Length);
                if (_faces[index] == IntPtr.Zero)
                    throw new InvalidDataException($"Embedded font '{sources[index].Name}' is not a supported OpenType font.");
                // The pinned asset owns these bytes until the HarfBuzz font is
                // destroyed. No release callback or second HB copy is needed.
                using var blob = new Blob(pointer, data.Length, MemoryMode.ReadOnly);
                blob.MakeImmutable();
                using var face = new Face(blob, 0) { Index = 0 };
                if (face.UnitsPerEm == 0)
                    throw new InvalidDataException($"Embedded font '{sources[index].Name}' has no valid OpenType em size.");
                _fonts[index] = new HBFont(face);
                _fonts[index].SetScale(ShapingScale, ShapingScale);
                _fonts[index].SetFunctionsOpenType();
            }
        }
        catch { Dispose(); throw; }
    }

    public int FontCount => _faces.Length;

    public AdvancedFontMetrics GetMetrics(int index, float size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var metrics = new float[3];
        if (WebFontNative.Metrics(_faces[index], size, metrics) != 0 ||
            !float.IsFinite(metrics[0]) || !float.IsFinite(metrics[1]) || !float.IsFinite(metrics[2]))
            throw new InvalidDataException("The embedded font has invalid metrics.");
        return new(metrics[0], metrics[1], metrics[2]);
    }

    public bool ContainsGlyphs(int index, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // SKFont's UTF-16 conversion produces an empty nominal glyph array
        // for malformed UTF-16. Its ContainsGlyphs(empty) result is true.
        // Keep this separate from HB, which replaces malformed code units.
        if (!TryCountRunes(text, out _)) return true;
        foreach (var rune in text.EnumerateRunes())
            if (WebFontNative.Glyph(_faces[index], (uint)rune.Value) == 0) return false;
        return true;
    }

    public uint[] GetGlyphs(int index, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryCountRunes(text, out var count)) return [];
        var result = new uint[count];
        var offset = 0;
        foreach (var rune in text.EnumerateRunes()) result[offset++] = WebFontNative.Glyph(_faces[index], (uint)rune.Value);
        return result;
    }

    private static bool TryCountRunes(string text, out int count)
    {
        count = 0;
        for (var index = 0; index < text.Length; index++, count++)
        {
            if (!char.IsSurrogate(text[index])) continue;
            if (!char.IsHighSurrogate(text[index]) || index + 1 >= text.Length ||
                !char.IsLowSurrogate(text[++index])) return false;
        }
        return true;
    }

    public AdvancedGlyphRun Shape(int index, HarfBuzzSharp.Buffer buffer, float size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fonts[index].Shape(buffer);
        var infos = buffer.GetGlyphInfoSpan();
        var positions = buffer.GetGlyphPositionSpan();
        var glyphs = new uint[infos.Length];
        var clusters = new uint[infos.Length];
        var points = new AdvancedGlyphPoint[infos.Length];
        var scale = size / ShapingScale;
        var x = 0f;
        var y = 0f;
        for (var offset = 0; offset < infos.Length; offset++)
        {
            glyphs[offset] = infos[offset].Codepoint;
            clusters[offset] = infos[offset].Cluster;
            points[offset] = new(x + positions[offset].XOffset * scale, y - positions[offset].YOffset * scale);
            x += positions[offset].XAdvance * scale;
            y += positions[offset].YAdvance * scale;
        }
        return new(glyphs, clusters, points, x);
    }

    public AdvancedTextImage Render(IReadOnlyList<AdvancedGlyphDrawRun> runs, int width, int height,
        float size, UIColor foreground)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pixels = new byte[checked(width * height * 4)];
        var color = (uint)(foreground.R << 24 | foreground.G << 16 | foreground.B << 8 | foreground.A);
        foreach (var run in runs)
        {
            if (run.Result.Codepoints.Length == 0) continue;
            var result = WebFontNative.DrawRun(_faces[run.FontIndex], size, run.Result.Codepoints,
                run.Result.Points, run.Result.Codepoints.Length, run.X, run.Baseline, color, pixels, width, height);
            if (result < 0)
                throw new InvalidDataException($"Unable to rasterize an embedded font run (error {result}).");
        }
        var colored = false;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            if (pixels[offset + 3] > 0 && (pixels[offset] != pixels[offset + 1] || pixels[offset + 1] != pixels[offset + 2]))
            { colored = true; break; }
        return new(width, height, pixels, colored);
    }

    public void Dispose()
    {
        if (_disposed) return;
        // HarfBuzz can retain font-table views, so destroy it before unpinning
        // the source bytes; FreeType owns its independent, bounded font copy.
        foreach (var font in _fonts) font?.Dispose();
        foreach (var face in _faces) if (face != IntPtr.Zero) WebFontNative.Destroy(face);
        foreach (var data in _fontData) if (data.IsAllocated) data.Free();
        _disposed = true;
    }
}

internal static class WebFontNative
{
    [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Create(IntPtr data, int length);

    [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_destroy", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Destroy(IntPtr font);

    [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_metrics", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Metrics(IntPtr font, float size, [Out] float[] metrics);

    [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_glyph", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint Glyph(IntPtr font, uint codepoint);

    [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_draw_run", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DrawRun(IntPtr font, float size, [In] uint[] glyphs,
        [In] AdvancedGlyphPoint[] points, int count, float x, float baseline, uint rgba,
        [In, Out] byte[] pixels, int width, int height);
}
#endif
