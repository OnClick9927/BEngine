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
        var runeCount = 0;
        foreach (var rune in text.EnumerateRunes()) runeCount++;
        var types = new Directionality[runeCount];
        var brackets = new PairedBracketType[runeCount];
        var values = new int[runeCount];
        var utf16Levels = new sbyte[text.Length];
        var offset = 0;
        var simple = direction != TextDirectionMode.RightToLeft;
        // The count was measured from this immutable string.
        var runes = text.EnumerateRunes();
        for (var runeIndex = 0; runeIndex < runeCount; runeIndex++)
        {
            runes.MoveNext();
            var rune = runes.Current;
            var kind = UnicodeBidiData.Direction(rune.Value);
            types[runeIndex] = kind;
            if (simple && kind is not (Directionality.L or Directionality.EN or Directionality.ES or Directionality.ET or Directionality.CS or Directionality.NSM or Directionality.B or Directionality.S or Directionality.WS or Directionality.ON or Directionality.BN)) simple = false;
            (brackets[runeIndex], values[runeIndex]) = UnicodeBidiData.Bracket(rune.Value);
            // Retain the rune width in the output until levels are available.
            utf16Levels[offset] = (sbyte)rune.Utf16SequenceLength;
            offset += rune.Utf16SequenceLength;
        }
        // These classes resolve to level zero without RTL, Arabic numbers,
        // embeddings or isolates; explicit RTL always uses the full resolver.
        if (simple)
        {
            Array.Clear(utf16Levels);
            return new(utf16Levels, 0);
        }
        var bidi = new Bidi();
        var paragraphLevel = direction == TextDirectionMode.Auto ? (sbyte)2 : direction == TextDirectionMode.RightToLeft ? (sbyte)1 : (sbyte)0;
        bidi.Process(new(types), new(brackets), new(values), paragraphLevel, null, null, null, null);
        offset = 0;
        for (var index = 0; index < runeCount; index++)
        {
            var length = utf16Levels[offset];
            var level = bidi.ResolvedLevels[index];
            utf16Levels[offset] = level;
            if (length == 2) utf16Levels[offset + 1] = level;
            offset += length;
        }
        return new(utf16Levels, bidi.ResolvedParagraphEmbeddingLevel);
    }

    /// <summary>UAX #9 L2: visual permutation of items after L1 has reset line-end whitespace.</summary>
    internal static int[] VisualOrder(ReadOnlySpan<sbyte> levels)
    {
        var order = levels.Length == 0 ? Array.Empty<int>() : new int[levels.Length];
        var maximum = 0; var minimumOdd = int.MaxValue;
        for (var index = 0; index < levels.Length; index++)
        {
            order[index] = index;
            var level = levels[index];
            maximum = Math.Max(maximum, level);
            if ((level & 1) != 0) minimumOdd = Math.Min(minimumOdd, level);
        }
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
