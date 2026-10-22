using System.Globalization;

namespace BEngine.UIElements;

internal static class StandardControlValues
{
    private static Fix64[] Parse(string value, int count)
    {
        var parts = value.Split(',');
        return Enumerable.Range(0, count).Select(i => i < parts.Length &&
            double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
                ? (Fix64)n : Fix64.Zero).ToArray();
    }
    internal static Vector2 Vector2(string value) { var n = Parse(value, 2); return new(n[0], n[1]); }
    internal static Vector4 Vector4(string value) { var n = Parse(value, 4); return new(n[0], n[1], n[2], n[3]); }
    internal static string Format(Vector2 v) => FormattableString.Invariant($"{v.x},{v.y}");
    internal static string Format(Vector4 v) => FormattableString.Invariant($"{v.x},{v.y},{v.z},{v.w}");
}
