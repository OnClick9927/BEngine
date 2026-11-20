using System.Globalization;

namespace BEngine;

internal static class TextElementUtility
{
    internal static int PreviousBoundary(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        var previous = 0;
        for (var offset = 0; offset < text.Length && offset < index;)
        {
            previous = offset;
            offset += StringInfo.GetNextTextElementLength(text.AsSpan(offset));
        }
        return previous;
    }

    internal static int NextBoundary(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var offset = 0; offset < text.Length;)
        {
            if (offset > index) return offset;
            offset += StringInfo.GetNextTextElementLength(text.AsSpan(offset));
        }
        return text.Length;
    }

    internal static int BoundaryAtOrBefore(ReadOnlySpan<char> text, int index)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var next = offset + StringInfo.GetNextTextElementLength(text[offset..]);
            if (next > index) break;
            offset = next;
        }
        return offset;
    }

    internal static int Count(ReadOnlySpan<char> text)
    {
        var count = 0;
        while (!text.IsEmpty)
        {
            text = text[StringInfo.GetNextTextElementLength(text)..];
            count++;
        }
        return count;
    }

    internal static int OffsetForColumn(ReadOnlySpan<char> text, int column)
    {
        var offset = 0;
        while (column-- > 0 && offset < text.Length)
            offset += StringInfo.GetNextTextElementLength(text[offset..]);
        return offset;
    }
}
