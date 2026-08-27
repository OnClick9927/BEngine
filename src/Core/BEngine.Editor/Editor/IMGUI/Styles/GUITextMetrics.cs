using System.Collections.Concurrent;
using System.Text;

namespace BEngine.Editor;

internal static class GUITextMetrics
{
    private static readonly ConcurrentDictionary<TextMetricKey, Fix64> Widths = new();
    private static readonly ConcurrentDictionary<TextMetricKey, Fix64> RenderedAdvances = new();
    private static readonly ConcurrentDictionary<FontMetricKey, Fix64> LineHeights = new();

    public static Fix64 MeasureWidth(string text, Fix64 fontSize, string fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 10;
        var size = Math.Max(8, (float)fontSize);
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "BEngine Built-in" : fontFamily;
        return Widths.GetOrAdd(new TextMetricKey(family, size, text), static key =>
        {
            if (EditorGpuCanvasResourceResolver.Shared.TryMeasureText(
                    key.Text, key.FontSize, key.FontFamily, out var measured) && measured > 0)
                return measured;
            return EstimateWidth(key.Text, key.FontSize);
        });
    }

    public static Fix64 MeasureLineHeight(Fix64 fontSize, string fontFamily)
    {
        var size = Math.Max(8, (float)fontSize);
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "BEngine Built-in" : fontFamily;
        return LineHeights.GetOrAdd(new FontMetricKey(family, size), static key =>
        {
            if (EditorGpuCanvasResourceResolver.Shared.TryMeasureLineHeight(
                    key.FontSize, key.FontFamily, out var measured) && measured > 0)
                return measured + 2;
            return (Fix64)Math.Ceiling(key.FontSize + 9);
        });
    }

    public static Fix64 MeasureRenderedAdvance(
        string text,
        Fix64 fontSize,
        string fontFamily,
        Fix64 renderScale)
    {
        if (string.IsNullOrEmpty(text)) return Fix64.Zero;
        renderScale = Fix64.Max(Fix64.FromDecimal(0.01m), renderScale);
        var physicalSize = Math.Max(1, (float)(fontSize * renderScale));
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "BEngine Built-in" : fontFamily;
        var physicalAdvance = RenderedAdvances.GetOrAdd(
            new TextMetricKey(family, physicalSize, text), static key =>
                EditorGpuCanvasResourceResolver.Shared.TryMeasureTextAdvance(
                    key.Text, key.FontSize, key.FontFamily, out var measured)
                    ? measured
                    : (Fix64)EstimateBuiltinGlyphAdvance(key.Text, key.FontSize));
        return (Fix64)physicalAdvance / renderScale;
    }

    private static Fix64 EstimateWidth(string text, float fontSize)
    {
        return (Fix64)Math.Ceiling(EstimateLayoutAdvance(text, fontSize) + 8f);
    }

    private static float EstimateLayoutAdvance(string text, float fontSize)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
            width += Rune.IsWhiteSpace(rune) ? fontSize * 0.5f :
                rune.Value <= 0x7f ? fontSize * 0.72f : fontSize * 1.1f;
        return width;
    }

    private static float EstimateBuiltinGlyphAdvance(string text, float fontSize) =>
        text.Length * Math.Max(1, fontSize) / 7f * 6f;

    private readonly record struct TextMetricKey(string FontFamily, float FontSize, string Text);
    private readonly record struct FontMetricKey(string FontFamily, float FontSize);
}
