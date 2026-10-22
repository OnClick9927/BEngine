using System.Globalization;
using System.Text.Json;

namespace BEngine.Localization;

/// <summary>Unicode CLDR 48 cardinal and ordinal rules, including visible decimal digits.
/// Data: unicode-org/cldr-json 48.0.0; see CldrPluralRules-LICENSE.txt.</summary>
public static class CldrPluralRules
{
    private sealed record Relation(char Operand, decimal Modulus, bool Negated, (decimal Low, decimal High)[] Ranges)
    {
        internal bool Matches(Operands operands)
        {
            var value = operands.Get(Operand); if (Modulus != 0) value %= Modulus;
            var matches = decimal.Truncate(value) == value && Ranges.Any(range => value >= range.Low && value <= range.High);
            return Negated ? !matches : matches;
        }
    }
    private sealed record Rule(string Category, Relation[][] Alternatives)
    {
        internal bool Matches(Operands operands) => Alternatives.Any(group => group.All(relation => relation.Matches(operands)));
    }
    private readonly record struct Operands(decimal N, decimal I, decimal V, decimal W, decimal F, decimal T, int Exponent)
    {
        internal static Operands Create(decimal number, int exponent)
        {
            var n = Math.Abs(number); var i = decimal.Truncate(n); var scale = (decimal.GetBits(n)[3] >> 16) & 0xff;
            decimal factor = 1; for (var digit = 0; digit < scale; digit++) factor *= 10;
            var f = (n - i) * factor; var t = f; var w = scale;
            while (w > 0 && t % 10 == 0) { t /= 10; w--; }
            return new(n, i, scale, w, f, t, exponent);
        }
        internal decimal Get(char operand) => operand switch
        { 'n' => N, 'i' => I, 'v' => V, 'w' => W, 'f' => F, 't' => T, 'e' or 'c' => Exponent, _ => throw new InvalidDataException("Invalid CLDR operand.") };
    }
    private static readonly Dictionary<string, Rule[]> Cardinal;
    private static readonly Dictionary<string, Rule[]> Ordinal;
    static CldrPluralRules()
    {
        // Source packages are compiled by the editor without their original .csproj resources.
        using var document = JsonDocument.Parse(CldrPluralRuleData.Json);
        Cardinal = Load(document.RootElement.GetProperty("cardinal")); Ordinal = Load(document.RootElement.GetProperty("ordinal"));
    }
    public static IReadOnlyCollection<string> CardinalLocales => Cardinal.Keys;
    public static IReadOnlyCollection<string> OrdinalLocales => Ordinal.Keys;
    public static string Select(decimal number, CultureInfo culture, bool ordinal = false, int compactExponent = 0)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Select(number, culture.Name, ordinal, compactExponent);
    }
    public static string Select(decimal number, string locale, bool ordinal = false, int compactExponent = 0)
    {
        var rules = ordinal ? Ordinal : Cardinal; var name = locale.Replace('_', '-');
        Rule[]? selected = null;
        while (name.Length > 0)
        {
            if (rules.TryGetValue(name, out selected)) break;
            var separator = name.LastIndexOf('-'); name = separator < 0 ? "" : name[..separator];
        }
        if (selected is null) return "other";
        var operands = Operands.Create(number, compactExponent);
        foreach (var rule in selected) if (rule.Matches(operands)) return rule.Category;
        return "other";
    }
    private static Dictionary<string, Rule[]> Load(JsonElement locales)
    {
        var result = new Dictionary<string, Rule[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in locales.EnumerateObject())
            result.Add(locale.Name, locale.Value.EnumerateObject().Where(value => value.Name != "other")
                .Select(value => new Rule(value.Name, value.Value.GetString()!.Split(" or ", StringSplitOptions.None)
                    .Select(group => group.Split(" and ", StringSplitOptions.None).Select(Parse).ToArray()).ToArray())).ToArray());
        return result;
    }
    private static Relation Parse(string expression)
    {
        var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries); var index = 1; decimal modulus = 0;
        if (tokens[index] == "%") { modulus = decimal.Parse(tokens[++index], CultureInfo.InvariantCulture); index++; }
        var op = tokens[index++]; if (op is not ("=" or "!=")) throw new InvalidDataException("Invalid CLDR relation.");
        var ranges = tokens[index].Split(',').Select(range =>
        {
            var bounds = range.Split("..", StringSplitOptions.None); var low = decimal.Parse(bounds[0], CultureInfo.InvariantCulture);
            return (low, bounds.Length == 1 ? low : decimal.Parse(bounds[1], CultureInfo.InvariantCulture));
        }).ToArray();
        return new(tokens[0][0], modulus, op == "!=", ranges);
    }
}
