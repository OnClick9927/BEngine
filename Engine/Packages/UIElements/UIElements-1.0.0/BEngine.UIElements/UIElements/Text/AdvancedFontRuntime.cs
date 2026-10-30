using HarfBuzzSharp;
using System.Globalization;
using System.Text;
using BEngine.UIElements.Unicode;

namespace BEngine.UIElements;

public enum TextDirectionMode
{
    Auto,
    LeftToRight,
    RightToLeft
}

internal readonly record struct AdvancedTextImage(int Width, int Height, byte[] Pixels, bool PreserveColors);

internal sealed class AdvancedFontRuntime : IDisposable
{
    private readonly IAdvancedFontBackend _backend;
    private bool _disposed;
    // Measure and Render use exactly the same shaped result. Bound both the
    // number of captions and retained glyph arrays, evicting the oldest entry
    // rather than making every recently used caption cold at once.
    private readonly BoundedLruCache<MeasurementKey, CachedLayout> _layouts =
        new(release: static value => value.Layout.Dispose());
    private const int MaximumLayouts = 256;
    private const long MaximumLayoutBytes = 8 * 1024 * 1024;
    // Coverage depends on the immutable embedded font chain and grapheme,
    // rather than point size, wrapping width or the rest of the caption.
    // Prefix shaping and independent labels can share this bounded result.
    private readonly BoundedLruCache<string, int> _fontChoices = new(StringComparer.Ordinal);
    private const int MaximumFontChoices = 4096;
    private const long MaximumFontChoiceBytes = 512 * 1024;
    private readonly record struct MeasurementKey(string Text, float Size, float Width, bool Wrap, TextDirectionMode Direction);

    internal AdvancedFontRuntime(IReadOnlyList<DynamicFontSource> sources)
#if BENGINE_WEB_FONTS
        : this(sources, new WebAdvancedFontBackend(sources)) { }
#else
        : this(sources, new SkiaAdvancedFontBackend(sources)) { }
#endif

    internal AdvancedFontRuntime(IReadOnlyList<DynamicFontSource> sources, IAdvancedFontBackend backend)
    {
        if (sources.Count == 0) throw new InvalidDataException("The embedded font chain is empty.");
        _backend = backend;
    }

    internal AdvancedTextMetrics Measure(string text, float size, float width, bool wrap, TextDirectionMode direction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var lease = AcquireLayout(text, size, width, wrap, direction);
        return lease.Value.Metrics;
    }

    internal AdvancedTextImage Render(string text, int width, int height, float size, bool wrap,
        TextDirectionMode direction, UIColor foreground)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Clamp(width, 1, 8192); height = Math.Clamp(height, 1, 8192);
        using var lease = AcquireLayout(text, size, width, wrap, direction);
        var layout = lease.Value.Layout;
        var runs = new List<AdvancedGlyphDrawRun>();
        var y = layout.Baseline;
        foreach (var line in layout.Lines)
        {
            foreach (var run in line.Runs)
            {
                runs.Add(new(run.FontIndex, run.X, y, run.Result));
            }
            y += layout.LineHeight;
        }
        return _backend.Render(runs, width, height, layout.Size, foreground);
    }

    private LayoutLease AcquireLayout(string text, float size, float width, bool wrap, TextDirectionMode direction)
    {
        size = !float.IsFinite(size) || size <= 0 ? 14 : Math.Clamp(size, 1, 512);
        // Layout ignores width when wrapping is disabled or the constraint is
        // nonpositive/nonfinite. Canonicalizing only those equivalent inputs
        // lets measurement and rendering share their immutable shape safely.
        if (!wrap || !float.IsFinite(width) || width <= 0) { wrap = false; width = 0; }
        var key = new MeasurementKey(text, size, width, wrap, direction);
        if (_layouts.TryGetValue(key, out var cached))
        {
            UIRenderBudgetDiagnostics.textMeasurementCacheHits++;
            return new(cached, retained: true);
        }
        var layout = Layout(text, size, width, wrap, direction);
        try
        {
            var fonts = new HashSet<int>();
            var substitutions = false;
            var bytes = 256L + text.Length * 2L + layout.Lines.Count * 256L;
            foreach (var line in layout.Lines)
                foreach (var run in line.Runs)
                {
                    fonts.Add(run.FontIndex); substitutions |= run.AppliedSubstitutions;
                    // Includes all Result arrays, run/list overhead and spare
                    // collection capacity with a conservative per-glyph bound.
                    bytes += 256L + run.Result.Codepoints.Length * 128L;
                }
            var value = new CachedLayout(layout, new(layout.Width, layout.Height, layout.GlyphCount,
                new StringInfo(text).LengthInTextElements, fonts.Count, substitutions, layout.Direction));
            if (text.Length <= 65536 && bytes <= MaximumLayoutBytes)
            {
                _layouts.Add(key, value, bytes);
                _layouts.Trim(MaximumLayouts, MaximumLayoutBytes);
                return new(value, retained: true);
            }
            return new(value, retained: false);
        }
        catch { layout.Dispose(); throw; }
    }

    private sealed record CachedLayout(ShapedLayout Layout, AdvancedTextMetrics Metrics);
    private readonly struct LayoutLease(CachedLayout value, bool retained) : IDisposable
    {
        internal CachedLayout Value => value;
        public void Dispose() { if (!retained) value.Layout.Dispose(); }
    }

    private ShapedLayout Layout(string text, float size, float wrappingWidth, bool wrap, TextDirectionMode requested)
    {
        size = !float.IsFinite(size) || size <= 0 ? 14 : Math.Clamp(size, 1, 512);
        var direction = ResolveDirection(text, requested);
        var metrics = _backend.GetMetrics(0, size);
        var lineHeight = Math.Max(1, metrics.Descent - metrics.Ascent + metrics.Leading);
        var baseline = Math.Max(0, -metrics.Ascent);
        var lines = new List<ShapedLine>();
        foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
            AddWrappedLines(lines, paragraph, size, wrappingWidth, wrap, requested);
        if (lines.Count == 0) lines.Add(ShapeLine("", size, direction));
        return new(lines, lines.Max(line => line.Width), lines.Count * lineHeight, lineHeight, baseline, size,
            lines.Sum(line => line.Runs.Sum(run => run.Result.Codepoints.Length)), direction);
    }

    private void AddWrappedLines(List<ShapedLine> lines, string paragraph, float size, float width, bool wrap,
        TextDirectionMode direction)
    {
        var bidi = TextBidiResolver.Analyze(paragraph, direction);
        if (!wrap || !float.IsFinite(width) || width <= 0)
        {
            lines.Add(ShapeLine(paragraph, size, direction, bidi));
            return;
        }
        var elements = TextElements(paragraph);
        var offsets = new int[elements.Count];
        for (var index = 1; index < offsets.Length; index++) offsets[index] = offsets[index - 1] + elements[index - 1].Length;
        if (elements.Count == 0) { lines.Add(ShapeLine("", size, direction)); return; }
        var start = 0;
        while (start < elements.Count)
        {
            var accepted = start + 1;
            var lastBreak = -1;
            for (var end = start + 1; end <= elements.Count; end++)
            {
                var candidate = string.Concat(elements.GetRange(start, end - start));
                var shaped = ShapeLine(candidate, size, direction, bidi, offsets[start]);
                if (shaped.Width <= width || end == start + 1)
                {
                    accepted = end;
                    if (char.IsWhiteSpace(elements[end - 1], 0)) lastBreak = end;
                    shaped.Dispose();
                    continue;
                }
                shaped.Dispose();
                if (lastBreak > start) accepted = lastBreak;
                break;
            }
            var count = Math.Max(1, accepted - start);
            var lineText = string.Concat(elements.GetRange(start, count)).TrimEnd();
            lines.Add(ShapeLine(lineText, size, direction, bidi, offsets[start]));
            start += count;
            while (start < elements.Count && char.IsWhiteSpace(elements[start], 0)) start++;
        }
    }

    private ShapedLine ShapeLine(string text, float size, TextDirectionMode baseDirection, BidiParagraph? paragraph = null, int offset = 0)
    {
        paragraph ??= TextBidiResolver.Analyze(text, baseDirection);
        var logicalRuns = BuildLogicalRuns(text, paragraph, offset);
        var order = TextBidiResolver.VisualOrder(logicalRuns.Select(run => run.Level).ToArray());
        var shaped = new List<ShapedRun>(logicalRuns.Count);
        var x = 0f;
        foreach (var index in order)
        {
            var run = logicalRuns[index];
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(run.Text);
            buffer.Direction = (run.Level & 1) == 0 ? Direction.LeftToRight : Direction.RightToLeft;
            buffer.GuessSegmentProperties();
            var result = _backend.Shape(run.FontIndex, buffer, size);
            var nominalGlyphs = _backend.GetGlyphs(run.FontIndex, run.Text);
            var substituted = !result.Codepoints.SequenceEqual(nominalGlyphs);
            shaped.Add(new(run.FontIndex, x, result, substituted));
            x += Math.Max(0, result.Width);
        }
        return new(shaped, x);
    }

    private List<LogicalRun> BuildLogicalRuns(string text, BidiParagraph paragraph, int offset)
    {
        var result = new List<LogicalRun>();
        var pending = new StringBuilder();
        var currentFont = -1;
        var levels = paragraph.Utf16Levels.AsSpan(offset, text.Length).ToArray();
        var tail = text.Length;
        foreach (var rune in text.EnumerateRunes().Reverse())
        {
            var kind = UnicodeBidiData.Direction(rune.Value);
            if (kind is not (Directionality.WS or Directionality.S or Directionality.BN or Directionality.LRE or Directionality.RLE or Directionality.LRO or Directionality.RLO or Directionality.PDF)) break;
            tail -= rune.Utf16SequenceLength;
            Array.Fill(levels, (sbyte)paragraph.ParagraphLevel, tail, rune.Utf16SequenceLength);
        }
        var currentLevel = (sbyte)paragraph.ParagraphLevel;
        var position = 0;
        foreach (var element in TextElements(text))
        {
            var font = ResolveFont(element);
            var level = levels[position]; position += element.Length;
            if (pending.Length > 0 && (font != currentFont || level != currentLevel))
            {
                result.Add(new(pending.ToString(), currentFont, currentLevel)); pending.Clear();
            }
            currentFont = font; currentLevel = level; pending.Append(element);
        }
        if (pending.Length > 0) result.Add(new(pending.ToString(), currentFont, currentLevel));
        return result;
    }

    private int ResolveFont(string textElement)
    {
        if (_fontChoices.TryGetValue(textElement, out var cached)) return cached;
        var selected = 0;
        for (var i = 0; i < _backend.FontCount; i++)
            if (_backend.ContainsGlyphs(i, textElement)) { selected = i; break; }
        // A maliciously large combining cluster must not displace the cache
        // or keep an unbounded source string alive.
        if (textElement.Length <= 256)
        {
            _fontChoices.Add(textElement, selected, 128L + textElement.Length * 2L);
            _fontChoices.Trim(MaximumFontChoices, MaximumFontChoiceBytes);
        }
        return selected;
    }

    private static List<string> TextElements(string text)
    {
        var result = new List<string>();
        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext()) result.Add(iterator.GetTextElement());
        return result;
    }

    private static bool? StrongDirection(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var kind = UnicodeBidiData.Direction(rune.Value);
            if (kind is Directionality.R or Directionality.AL) return true;
            if (kind == Directionality.L) return false;
        }
        return null;
    }

    private static TextDirectionMode ResolveDirection(string text, TextDirectionMode requested)
    {
        if (requested != TextDirectionMode.Auto) return requested;
        return TextBidiResolver.Analyze(text, TextDirectionMode.Auto).ParagraphLevel == 0 ? TextDirectionMode.LeftToRight : TextDirectionMode.RightToLeft;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _layouts.Clear();
        _fontChoices.Clear();
        _backend.Dispose();
        _disposed = true;
    }

    private readonly record struct LogicalRun(string Text, int FontIndex, sbyte Level);
    private sealed record ShapedRun(int FontIndex, float X, AdvancedGlyphRun Result, bool AppliedSubstitutions);
    private sealed record ShapedLine(List<ShapedRun> Runs, float Width) : IDisposable
    {
        public void Dispose() { }
    }
    private sealed record ShapedLayout(List<ShapedLine> Lines, float Width, float Height, float LineHeight,
        float Baseline, float Size, int GlyphCount, TextDirectionMode Direction) : IDisposable
    {
        public void Dispose() { foreach (var line in Lines) line.Dispose(); }
    }
}
