using System.Runtime.InteropServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform;

/// <summary>Optional native FreeType capability. Register only when the host links libBEngineWebFont.</summary>
[PreserveNativeCode]
public sealed class WebFontRasterizer : IPlatformFontRasterizer
{
    public static WebFontRasterizer Shared { get; } = new();

    /// <summary>Called by a host that selected the embedded FreeType binding. Allocates no native faces.</summary>
    public static void Register()
    {
        RuntimeServiceContributions.RegisterDefault<IPlatformFontRasterizer>(Shared);
        RuntimeServiceContributions.Register(Configure);
    }

    private static void Configure(IServiceCollection services) => services.TryAddSingleton<IPlatformFontRasterizer>(Shared);

    public unsafe IPlatformFontFace OpenFace(ReadOnlySpan<byte> fontData)
    {
        if (fontData.Length is < 12 or > 128 * 1024 * 1024) throw new InvalidDataException("Invalid embedded font source.");
        IntPtr handle;
        fixed (byte* data = fontData) handle = Native.Create(data, fontData.Length);
        if (handle == IntPtr.Zero) throw new InvalidDataException("The embedded font is not a supported OpenType font.");
        try { return new Face(handle); }
        catch { Native.Destroy(handle); throw; }
    }

    [PreserveNativeCode]
    private sealed class Face(IntPtr handle) : IPlatformFontFace
    {
        private IntPtr _handle = handle;
        private IntPtr Handle => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(IPlatformFontFace));

        public unsafe PlatformFontMetrics GetMetrics(float size)
        {
            var handle = Handle;
            // A fixed-size unmanaged local avoids the interpreter's dynamic localloc path.
            var metrics = default(PlatformFontMetrics);
            if (Native.Metrics(handle, size, (float*)&metrics) != 0 || !float.IsFinite(metrics.Ascent) ||
                !float.IsFinite(metrics.Descent) || !float.IsFinite(metrics.Leading))
                throw new InvalidDataException("The embedded font has invalid metrics.");
            return metrics;
        }

        public uint GetGlyph(uint codepoint) => Native.Glyph(Handle, codepoint);

        public unsafe void DrawRun(float size, ReadOnlySpan<uint> glyphs, ReadOnlySpan<float> positions,
            float x, float baseline, uint rgba, Span<byte> pixels, int width, int height)
        {
            var handle = Handle;
            if (glyphs.Length > 1048576 || positions.Length != glyphs.Length * 2)
                throw new ArgumentException("A glyph run requires exactly one x/y pair per glyph.", nameof(positions));
            if (width is < 1 or > 8192 || height is < 1 or > 8192 || pixels.Length < (long)width * height * 4)
                throw new ArgumentException("The RGBA8 destination does not contain the requested image.", nameof(pixels));
            if (glyphs.IsEmpty) return;
            fixed (uint* ids = glyphs)
            fixed (float* xy = positions)
            fixed (byte* destination = pixels)
            {
                var result = Native.DrawRun(handle, size, ids, xy, glyphs.Length, x, baseline, rgba, destination, width, height);
                if (result < 0) throw new InvalidDataException($"Unable to rasterize an embedded font run (error {result}).");
            }
        }

        public void Dispose()
        {
            var handle = _handle; _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero) Native.Destroy(handle);
        }
    }

    [PreserveNativeCode]
    private static unsafe class Native
    {
        [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create(byte* data, int length);
        [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(IntPtr font);
        [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_metrics", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Metrics(IntPtr font, float size, float* metrics);
        [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_glyph", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint Glyph(IntPtr font, uint codepoint);
        [DllImport("libBEngineWebFont", EntryPoint = "bengine_font_draw_run", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DrawRun(IntPtr font, float size, uint* glyphs, float* xy,
            int count, float x, float baseline, uint rgba, byte* pixels, int width, int height);
    }
}
