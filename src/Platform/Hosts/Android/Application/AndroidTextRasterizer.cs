using System;
using System.Collections.Generic;
using System.Buffers;
using Android.Graphics;
using BEngine.Rendering;

namespace BEngine.Player.Hosts.Android;

// Uses Android's installed fonts and shaping instead of the emergency bitmap
// alphabet. Embedded OpenType assets still use the UI package's advanced path.
internal sealed class AndroidTextRasterizer : ITextRasterizer
{
    private readonly Typeface _typeface;
    private readonly object _measureGate = new();
    private readonly Dictionary<(string Text, float Size), (float Width, float Height)> _measurements = new();
    public AndroidTextRasterizer() : this("sans-serif") { }
    private AndroidTextRasterizer(string family) =>
        _typeface = Typeface.Create(family, TypefaceStyle.Normal) ?? Typeface.Default!;
    public ITextRasterizer ForFontFamily(string family) => new AndroidTextRasterizer(family);
    public float GetHorizontalPadding(float fontSize) => 4;

    private Paint CreatePaint(float size)
    {
        var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.SubpixelText)
        { TextSize = Math.Max(1, size), Color = global::Android.Graphics.Color.White };
        // Typeface is immutable. Reusing it avoids a JNI wrapper and Java/Mono
        // bridge collection for every label measurement or FPS counter update.
        paint.SetTypeface(_typeface);
        return paint;
    }

    public bool TryMeasureText(string text, float fontSize, out float width, out float height)
    {
        if (!float.IsFinite(fontSize)) { width = height = 0; return false; }
        text ??= string.Empty;
        lock (_measureGate)
        {
            if (_measurements.TryGetValue((text, fontSize), out var cached))
            { width = cached.Width; height = cached.Height; return true; }
            using var paint = CreatePaint(fontSize);
            using var metrics = paint.GetFontMetrics()!;
            width = paint.MeasureText(text);
            height = metrics.Bottom - metrics.Top;
            if (_measurements.Count >= 1024) _measurements.Clear();
            _measurements[(text, fontSize)] = (width, height);
        }
        return true;
    }

    public bool TryResolveText(string text, int width, int height, float fontSize, out RasterizedImage texture) =>
        TryResolveText(text, width, height, fontSize, false, out texture);

    public bool TryResolveText(string text, int width, int height, float fontSize, bool wordWrap, out RasterizedImage texture)
    {
        texture = default;
        if (width <= 0 || height <= 0 || !float.IsFinite(fontSize)) return false;
        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        try
        {
            using var canvas = new Canvas(bitmap);
            using var paint = CreatePaint(fontSize);
            // Single-line captions fit their content box, matching desktop text.
            if (!wordWrap && !string.IsNullOrEmpty(text))
            {
                var measured = paint.MeasureText(text);
                if (measured > width - 4)
                    paint.TextSize = Math.Max(1, paint.TextSize * Math.Max(1, width - 4) / measured);
            }
            using var metrics = paint.GetFontMetrics()!;
            float baseline = -metrics.Top, lineHeight = metrics.Bottom - metrics.Top;
            foreach (var paragraph in (text ?? string.Empty).Split('\n'))
            {
                int offset = 0;
                do
                {
                    var remaining = paragraph[offset..];
                    int count = wordWrap && remaining.Length > 0
                        ? Math.Max(1, paint.BreakText(remaining, true, Math.Max(1, width - 4), null)) : remaining.Length;
                    if (count < remaining.Length && count > 0 && char.IsHighSurrogate(remaining[count - 1])) count++;
                    canvas.DrawText(remaining[..count], 2, baseline, paint);
                    baseline += lineHeight;
                    offset += count;
                    if (!wordWrap || baseline + metrics.Top >= height) break;
                } while (offset < paragraph.Length);
                if (baseline + metrics.Top >= height) break;
            }
            int countPixels = checked(width * height);
            var colors = ArrayPool<int>.Shared.Rent(countPixels);
            try
            {
                bitmap.GetPixels(colors, 0, width, 0, 0, width, height);
                var pixels = new byte[checked(countPixels * 4)];
                for (int i = 0; i < countPixels; i++)
                {
                    pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = 255;
                    pixels[i * 4 + 3] = (byte)((uint)colors[i] >> 24);
                }
                texture = new RasterizedImage(width, height, BEngine.Rendering.Rhi.GraphicsTextureFormat.Rgba8Unorm, pixels);
                return true;
            }
            finally { ArrayPool<int>.Shared.Return(colors); }
        }
        finally { bitmap.Recycle(); }
    }
}
