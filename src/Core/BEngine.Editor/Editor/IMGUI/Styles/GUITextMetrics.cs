using System.Collections.Concurrent;
using System.Text;

namespace BEngine.Editor;

internal static class GUITextMetrics
{
    private static readonly ConcurrentDictionary<TextMetricKey, Fix64> Widths = new();
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

    private static Fix64 EstimateWidth(string text, float fontSize)
    {
        var width = 8f;
        foreach (var rune in text.EnumerateRunes())
            width += Rune.IsWhiteSpace(rune) ? fontSize * 0.5f :
                rune.Value <= 0x7f ? fontSize * 0.72f : fontSize * 1.1f;
        return (Fix64)Math.Ceiling(width);
    }

    private readonly record struct TextMetricKey(string FontFamily, float FontSize, string Text);
    private readonly record struct FontMetricKey(string FontFamily, float FontSize);
}
