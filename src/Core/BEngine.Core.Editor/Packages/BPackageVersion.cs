using System.Text.RegularExpressions;

namespace BEngine.ProjectSystem;

/// <summary>Semantic release ordering shared by package discovery, installation and publishing.</summary>
public static class BPackageVersion
{
    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant);

    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);

    public static void Validate(string value) => Parse(value);

    private static (Match Match, Version Number) Parse(string? value)
    {
        var match = Pattern.Match(value ?? string.Empty);
        if (!match.Success || !Version.TryParse($"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}", out var number) ||
            !ValidPrerelease(match.Groups[4].ValueSpan))
            throw new InvalidDataException($"Invalid semantic version '{value}'. Use major.minor.patch, optionally with a prerelease/build suffix.");
        return (match, number);
    }

    private static bool ValidPrerelease(ReadOnlySpan<char> value)
    {
        foreach (var range in value.Split('.'))
        {
            var part = value[range];
            if (part.Length > 1 && part[0] == '0' && part.IndexOfAnyExceptInRange('0', '9') < 0) return false;
        }
        return true;
    }

    public static int Compare(string? left, string? right)
    {
        var (a, aNumber) = Parse(left); var (b, bNumber) = Parse(right);
        var result = aNumber.CompareTo(bNumber);
        if (result != 0) return result;
        var ap = a.Groups[4].ValueSpan; var bp = b.Groups[4].ValueSpan;
        if (ap.IsEmpty || bp.IsEmpty) return (ap.IsEmpty ? 1 : 0).CompareTo(bp.IsEmpty ? 1 : 0);
        var aa = ap.Split('.'); var bb = bp.Split('.');
        while (aa.MoveNext())
        {
            if (!bb.MoveNext()) return 1;
            var av = ap[aa.Current]; var bv = bp[bb.Current];
            var an = av.IndexOfAnyExceptInRange('0', '9') < 0;
            var bn = bv.IndexOfAnyExceptInRange('0', '9') < 0;
            result = an && bn ? av.Length.CompareTo(bv.Length) : an != bn ? (an ? -1 : 1) : 0;
            if (result == 0) result = av.SequenceCompareTo(bv);
            if (result != 0) return result;
        }
        return bb.MoveNext() ? -1 : 0;
    }
}
