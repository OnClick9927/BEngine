namespace BEngine;

/// <summary>Allocation-free token queries shared by runtime and tooling.</summary>
internal static class TextSpanExtensions
{
    internal static ReadOnlySpan<char> Before(this ReadOnlySpan<char> text, char separator)
    {
        var end = text.IndexOf(separator);
        return end < 0 ? text : text[..end];
    }

    internal static ReadOnlySpan<char> AfterLast(this ReadOnlySpan<char> text, char separator) =>
        text[(text.LastIndexOf(separator) + 1)..];

    internal static bool HasPathTraversal(this ReadOnlySpan<char> text, bool includeCurrent = true, string separators = "/\\")
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var dot = text[offset..].IndexOf('.');
            if (dot < 0) break;
            var start = offset + dot;
            offset = start + 1;
            if (start > 0 && !separators.Contains(text[start - 1])) continue;
            var end = offset;
            if (end < text.Length && text[end] == '.') end++;
            if ((end - start == 2 || includeCurrent) &&
                (end == text.Length || separators.Contains(text[end]))) return true;
        }
        return false;
    }

    internal static bool HasInvalidPathSegment(this ReadOnlySpan<char> text,
        bool whitespace = false, bool colon = false, string separators = "/\\")
    {
        foreach (var range in text.SplitAny(separators))
        {
            var segment = text[range];
            if (segment.IsEmpty || segment.SequenceEqual(".") || segment.SequenceEqual("..") ||
                whitespace && segment.IsWhiteSpace() || colon && segment.Contains(':')) return true;
        }
        return false;
    }

    internal static bool ContainsPathSegment(this ReadOnlySpan<char> text, ReadOnlySpan<char> value,
        StringComparison comparison = StringComparison.Ordinal, bool skipFirst = false)
    {
        if (text.IndexOf(value, comparison) < 0) return false;
        foreach (var range in text.SplitAny("/\\"))
        {
            if (skipFirst) { skipFirst = false; continue; }
            if (text[range].Equals(value, comparison)) return true;
        }
        return false;
    }
}
