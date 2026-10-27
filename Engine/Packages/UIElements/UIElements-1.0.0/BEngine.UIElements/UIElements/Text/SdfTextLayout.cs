using System.Buffers;
using System.Text;
using BEngine.Rendering.Rhi;

namespace BEngine.UIElements;

// Keep the concrete array in the cached layout.  Iterating an IReadOnlyList
// through its interface boxes an enumerator for every text command, which is
// especially costly for the many short HUD labels in a live frame.
internal sealed record SdfTextLayout(ScreenSpaceGlyphPlacement[] Glyphs, float Width, float Height)
{
    internal static SdfTextLayout Build(SdfFontAsset font, string text, float size, float availableWidth, bool wrap)
    {
        var scale = size / font.samplingSize;
        var lineHeight = font.lineHeight * scale;
        var runes = ArrayPool<Rune>.Shared.Rent(Math.Max(4, text.Length));
        ScreenSpaceGlyphPlacement[]? placements = null;
        var runeCount = 0;
        try
        {
            foreach (var rune in text.EnumerateRunes())
                if (rune.Value != '\r') runes[runeCount++] = rune;
            using var budget = LoopExecutionGuard.AllowIterations(runeCount + 1);
            placements = ArrayPool<ScreenSpaceGlyphPlacement>.Shared.Rent(Math.Max(1, runeCount));
            var count = 0; var x = 0f; var y = 0f; var maximum = 0f;
        // Accumulate in the font's sampling units, just as MeasureAdvance does.
        // Scaling each glyph first can round an exactly fitting line above the
        // width used by centered/right-aligned text and spuriously wrap it.
        float ScaledAdvance(float advance) => advance * size / font.samplingSize;
        void NextLine() { maximum = Math.Max(maximum, x); x = 0; y += lineHeight; }
        float Advance(Rune rune) => rune.Value == '\t' ? font.Glyph(' ').Advance * 4 : font.Glyph(rune.Value).Advance;
        for (var i = 0; i < runeCount; i++)
        {
            var rune = runes[i];
            if (rune.Value == '\n') { NextLine(); continue; }
            var whitespace = Rune.IsWhiteSpace(rune);
            if (wrap && !whitespace && x > 0 && (i == 0 || Rune.IsWhiteSpace(runes[i - 1])))
            {
                var wordEnd = x;
                for (var j = i; j < runeCount && !Rune.IsWhiteSpace(runes[j]); j++) wordEnd += Advance(runes[j]);
                if (ScaledAdvance(wordEnd) > availableWidth) NextLine();
            }
            var advance = Advance(rune);
            if (wrap && x > 0 && ScaledAdvance(x + advance) > availableWidth)
            { NextLine(); if (whitespace) continue; }
            var glyph = font.Glyph(rune.Value);
            if (!whitespace && glyph.Width > 0)
                placements[count++] = new(ScaledAdvance(x) + glyph.BearingX * scale, y + glyph.BearingY * scale,
                    glyph.Width * scale, glyph.Height * scale,
                    glyph.X / (float)font.atlasWidth, glyph.Y / (float)font.atlasHeight,
                    (glyph.X + glyph.Width) / (float)font.atlasWidth, (glyph.Y + glyph.Height) / (float)font.atlasHeight);
            x += advance;
        }
            // Only the final, exact array escapes into the immutable cached layout.
            return new(placements.AsSpan(0, count).ToArray(), ScaledAdvance(Math.Max(maximum, x)), y + lineHeight);
        }
        finally
        {
            ArrayPool<Rune>.Shared.Return(runes);
            if (placements is not null) ArrayPool<ScreenSpaceGlyphPlacement>.Shared.Return(placements);
        }
    }
}
