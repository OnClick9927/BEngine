namespace BEngine.Player;

public static class ContentVersionLabel
{
    public static string NormalizePortable(string? value, string fieldName)
    {
        var version = value?.Trim() ?? string.Empty;
        if (version.Length is 0 or > 128 || !IsLowerAsciiLetterOrDigit(version[0]) || Query316(version.Skip(1)) || version.EndsWith('.') || IsWindowsReservedName(version))
            throw new InvalidDataException($"{fieldName} must use 1-128 lowercase ASCII letters, digits, '.', '_' or '-', " + "start with a letter or digit, and be a portable file name.");
        [global::BEngine.SkipLoopGuard]
        bool Query316(global::System.Collections.Generic.IEnumerable<char> querySource316)
        {
            ArgumentNullException.ThrowIfNull(querySource316, "source");
            bool Matches316(char character) => !IsLowerAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-';
            global::System.ReadOnlySpan<char> queryValues316;
            if (querySource316.GetType() == typeof(char[]))
                queryValues316 = (char[])querySource316;
            else if (querySource316.GetType() == typeof(global::System.Collections.Generic.List<char>))
                queryValues316 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<char>)querySource316);
            else
            {
                foreach (var character in querySource316)
                    if (Matches316(character))
                        return true;
                return false;
            }
        
            foreach (var character in queryValues316)
                if (Matches316(character))
                    return true;
            return false;
        }
        return version;
    }

    public static void ValidatePortable(string value, string fieldName)
    {
        var normalized = NormalizePortable(value, fieldName);
        if (!normalized.Equals(value, StringComparison.Ordinal))
            throw new InvalidDataException($"{fieldName} is not canonical; expected '{normalized}'.");
    }

    public static string NormalizeHotResourceVersion(string? value)
    {
        var version = NormalizePortable(value, "Hot Resource Version");
        if (!version.Equals(value, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Hot Resource Version is not canonical; expected '{version}'.");
        if (version.Length < 2 || version[0] != 'v' || version[1] is not (>= '1' and <= '9') ||
            version.AsSpan(2).IndexOfAnyExceptInRange('0', '9') >= 0)
            throw new InvalidDataException(
                "Hot Resource Version must use lowercase v followed by a positive integer without " +
                "leading zeroes, for example v1, v2 or v10.");
        return version;
    }

    public static int CompareHotResourceVersions(string left, string right)
    {
        left = NormalizeHotResourceVersion(left);
        right = NormalizeHotResourceVersion(right);
        var leftNumber = left.AsSpan(1);
        var rightNumber = right.AsSpan(1);
        var length = leftNumber.Length.CompareTo(rightNumber.Length);
        return length != 0 ? length : leftNumber.SequenceCompareTo(rightNumber);
    }

    private static bool IsLowerAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsWindowsReservedName(string value)
    {
        var separator = value.IndexOf('.');
        var name = separator < 0 ? value : value[..separator];
        return name is "con" or "prn" or "aux" or "nul" ||
               name.Length == 4 && name[3] is >= '1' and <= '9' &&
               (name.StartsWith("com", StringComparison.Ordinal) ||
                name.StartsWith("lpt", StringComparison.Ordinal));
    }
}
