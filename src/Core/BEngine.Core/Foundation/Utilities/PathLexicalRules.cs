namespace BEngine;

/// <summary>Pure lexical queries for portable resource paths, independent of file-system access.</summary>
/// <remarks>These queries do not resolve symbolic links, roots or platform-specific invalid characters.
/// The caller owns its accepted path grammar and must apply the appropriate storage policy.</remarks>
public static class PathLexicalRules
{
    /// <summary>Matches a complete slash/backslash-delimited segment. Empty segments are preserved;
    /// skipFirst skips exactly the first segment, including an empty leading segment.</summary>
    public static bool ContainsSegment(ReadOnlySpan<char> path, ReadOnlySpan<char> segment,
        StringComparison comparison = StringComparison.Ordinal, bool skipFirst = false) =>
        path.ContainsPathSegment(segment, comparison, skipFirst);

    /// <summary>Recognizes complete '..' segments, and '.' when includeCurrent is true.
    /// A separator string is a set of delimiter characters; an empty set treats the path as one segment.</summary>
    public static bool HasTraversal(ReadOnlySpan<char> path, bool includeCurrent = true, string separators = "/\\")
    {
        ArgumentNullException.ThrowIfNull(separators);
        return path.HasPathTraversal(includeCurrent, separators);
    }

    /// <summary>Recognizes empty, '.' or '..' segments; optionally whitespace-only or colon-containing segments.</summary>
    public static bool HasInvalidSegments(ReadOnlySpan<char> path, bool whitespace = false,
        bool colon = false, string separators = "/\\")
    {
        ArgumentNullException.ThrowIfNull(separators);
        return path.HasInvalidPathSegment(whitespace, colon, separators);
    }
}
