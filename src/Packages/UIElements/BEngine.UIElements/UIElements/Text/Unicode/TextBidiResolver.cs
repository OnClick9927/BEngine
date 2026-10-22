using BEngine.UIElements.Unicode.Utilities;

namespace BEngine.UIElements.Unicode;

internal sealed record BidiParagraph(sbyte[] Utf16Levels, int ParagraphLevel);

internal static class TextBidiResolver
{
    internal static Bidi Resolve(int[] codepoints, sbyte paragraphLevel)
    {
        var types = new Directionality[codepoints.Length];
        var brackets = new PairedBracketType[codepoints.Length];
        var values = new int[codepoints.Length];
        for (var index = 0; index < codepoints.Length; index++)
        {
            types[index] = UnicodeBidiData.Direction(codepoints[index]);
            (brackets[index], values[index]) = UnicodeBidiData.Bracket(codepoints[index]);
        }
        var bidi = new Bidi();
        bidi.Process(new(types), new(brackets), new(values), paragraphLevel, null, null, null, null);
        return bidi;
    }

    internal static BidiParagraph Analyze(string text, TextDirectionMode direction)
    {
        var codepoints = text.EnumerateRunes().Select(rune => rune.Value).ToArray();
        var bidi = Resolve(codepoints, direction == TextDirectionMode.Auto ? (sbyte)2 : direction == TextDirectionMode.RightToLeft ? (sbyte)1 : (sbyte)0);
        var utf16Levels = new sbyte[text.Length];
        var offset = 0;
        for (var index = 0; index < codepoints.Length; index++)
        {
            var length = codepoints[index] > 0xFFFF ? 2 : 1;
            Array.Fill(utf16Levels, bidi.ResolvedLevels[index], offset, length); offset += length;
        }
        return new(utf16Levels, bidi.ResolvedParagraphEmbeddingLevel);
    }

    /// <summary>UAX #9 L2: visual permutation of items after L1 has reset line-end whitespace.</summary>
    internal static int[] VisualOrder(ReadOnlySpan<sbyte> levels)
    {
        var order = Enumerable.Range(0, levels.Length).ToArray();
        var maximum = 0; var minimumOdd = int.MaxValue;
        foreach (var level in levels)
        { maximum = Math.Max(maximum, level); if ((level & 1) != 0) minimumOdd = Math.Min(minimumOdd, level); }
        for (var level = maximum; level >= minimumOdd; level--)
        {
            var start = 0;
            while (start < order.Length)
            {
                if (levels[order[start]] < level) { start++; continue; }
                var end = start + 1;
                while (end < order.Length && levels[order[end]] >= level) end++;
                Array.Reverse(order, start, end - start); start = end;
            }
        }
        return order;
    }
}
