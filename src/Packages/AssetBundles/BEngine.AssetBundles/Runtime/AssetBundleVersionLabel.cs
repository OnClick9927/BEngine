namespace BEngine.AssetBundles;

internal static class AssetBundleVersionLabel
{
    internal static string NormalizePortable(string? value, string fieldName)
    {
        var version = value?.Trim() ?? string.Empty;
        if (version.Length is 0 or > 128 || !IsLowerAsciiLetterOrDigit(version[0]) ||
            version.Skip(1).Any(static character =>
                !IsLowerAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-') ||
            version.EndsWith('.') || IsWindowsReservedName(version))
            throw new InvalidDataException(
                $"{fieldName} must use 1-128 lowercase ASCII letters, digits, '.', '_' or '-', " +
                "start with a letter or digit, and be a portable file name.");
        return version;
    }

    internal static void ValidatePortable(string value, string fieldName)
    {
        var normalized = NormalizePortable(value, fieldName);
        if (!normalized.Equals(value, StringComparison.Ordinal))
            throw new InvalidDataException($"{fieldName} is not canonical; expected '{normalized}'.");
    }

    internal static string NormalizeHotResourceVersion(string? value)
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

    internal static int CompareHotResourceVersions(string left, string right)
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
