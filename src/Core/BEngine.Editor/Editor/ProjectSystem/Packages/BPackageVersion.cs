using System.Text.RegularExpressions;

namespace BEngine.ProjectSystem;

/// <summary>Semantic release ordering shared by package discovery, installation and publishing.</summary>
public static class BPackageVersion
{
    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant);

    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);

    public static void Validate(string value)
    {
        var match = Pattern.Match(value ?? string.Empty);
        if (!match.Success || !Version.TryParse($"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}", out _) ||
            match.Groups[4].Value.Split('.').Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit)))
            throw new InvalidDataException($"Invalid semantic version '{value}'. Use major.minor.patch, optionally with a prerelease/build suffix.");
    }

    public static int Compare(string? left, string? right)
    {
        Validate(left!); Validate(right!);
        var a = Pattern.Match(left!); var b = Pattern.Match(right!);
        var result = Version.Parse($"{a.Groups[1]}.{a.Groups[2]}.{a.Groups[3]}")
            .CompareTo(Version.Parse($"{b.Groups[1]}.{b.Groups[2]}.{b.Groups[3]}"));
        if (result != 0) return result;
        var ap = a.Groups[4].Value; var bp = b.Groups[4].Value;
        if (ap.Length == 0 || bp.Length == 0) return (ap.Length == 0 ? 1 : 0).CompareTo(bp.Length == 0 ? 1 : 0);
        var aa = ap.Split('.'); var bb = bp.Split('.');
        for (var i = 0; i < Math.Min(aa.Length, bb.Length); i++)
        {
            var an = aa[i].All(char.IsAsciiDigit); var bn = bb[i].All(char.IsAsciiDigit);
            result = an && bn ? aa[i].Length.CompareTo(bb[i].Length) : an != bn ? (an ? -1 : 1) : 0;
            if (result == 0) result = string.CompareOrdinal(aa[i], bb[i]);
            if (result != 0) return result;
        }
        return aa.Length.CompareTo(bb.Length);
    }
}
