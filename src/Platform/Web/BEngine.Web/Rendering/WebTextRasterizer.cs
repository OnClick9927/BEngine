using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Platform;

/// <summary>Uses the browser's font fallback and shaping for default UI text.</summary>
public sealed class WebTextRasterizer : ITextRasterizer
{
    private const int MeasurementCapacity = 2048;
    private const long MaximumPixelBytes = 256L * 1024 * 1024;
    private readonly string _fontFamily;
    private readonly Dictionary<(string Text, float Size), (float Width, float Height)> _measurements = [];
    private readonly Queue<(string Text, float Size)> _measurementOrder = [];

    public WebTextRasterizer() : this("") { }

    public WebTextRasterizer(string fontFamily) => _fontFamily = fontFamily?.Trim() ?? "";

    public ITextRasterizer ForFontFamily(string family) => new WebTextRasterizer(family);

    public float GetHorizontalPadding(float fontSize) => 4;

    public bool TryMeasureText(string text, float fontSize, out float width, out float height)
    {
        width = height = 0;
        if (!OperatingSystem.IsBrowser() || !float.IsFinite(fontSize) || fontSize <= 0) return false;
        var key = (text ?? "", fontSize);
        if (_measurements.TryGetValue(key, out var cached))
        {
            (width, height) = cached;
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(WebTextInterop.Measure(key.Item1, fontSize, _fontFamily));
            var root = document.RootElement;
            var measuredWidth = root.GetProperty("width").GetSingle();
            var measuredHeight = root.GetProperty("height").GetSingle();
            if (!float.IsFinite(measuredWidth) || !float.IsFinite(measuredHeight) ||
                measuredWidth < 0 || measuredHeight <= 0) return false;
            if (_measurements.Count >= MeasurementCapacity)
                _measurements.Remove(_measurementOrder.Dequeue());
            _measurements.Add(key, (measuredWidth, measuredHeight));
            _measurementOrder.Enqueue(key);
            (width, height) = (measuredWidth, measuredHeight);
            return true;
        }
        catch (Exception exception) when (exception is JSException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public bool TryResolveText(string text, int width, int height, float fontSize,
        out RasterizedImage texture) => TryResolveText(text, width, height, fontSize, false, out texture);

    public bool TryResolveText(string text, int width, int height, float fontSize, bool wordWrap,
        out RasterizedImage texture)
    {
        texture = default;
        if (!OperatingSystem.IsBrowser() || string.IsNullOrEmpty(text) || !float.IsFinite(fontSize) ||
            fontSize <= 0 || width <= 0 || height <= 0 || width > 16384 || height > 16384 ||
            (long)width * height * 4 > MaximumPixelBytes) return false;
        try
        {
            var pixels = new byte[checked(width * height * 4)];
            if (!WebTextInterop.Rasterize(text, width, height, fontSize, _fontFamily, wordWrap, pixels))
                return false;
            texture = new RasterizedImage(width, height, GraphicsTextureFormat.Rgba8Unorm, pixels);
            return true;
        }
        catch (Exception exception) when (exception is JSException or FormatException)
        {
            return false;
        }
    }
}

internal static partial class WebTextInterop
{
    [JSImport("globalThis.BEngineWebText.measure")]
    internal static partial string Measure(string text, float fontSize, string fontFamily);

    [JSImport("globalThis.BEngineWebText.rasterize")]
    internal static partial bool Rasterize(string text, int width, int height, float fontSize,
        string fontFamily, bool wordWrap, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
}
