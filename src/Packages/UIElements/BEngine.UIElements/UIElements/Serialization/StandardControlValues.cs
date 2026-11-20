using System.Globalization;

namespace BEngine.UIElements;

internal static class StandardControlValues
{
    private static void Parse(string value, Span<Fix64> result)
    {
        var text = value.AsSpan(0, value.Length);
        for (var index = 0; index < result.Length; index++)
        {
            var separator = text.IndexOf(',');
            var part = separator < 0 ? text : text[..separator];
            result[index] = double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
                ? (Fix64)n : Fix64.Zero;
            if (separator < 0)
            {
                result[(index + 1)..].Clear();
                break;
            }
            text = text[(separator + 1)..];
        }
    }
    internal static Vector2 Vector2(string value) { Span<Fix64> n = stackalloc Fix64[2]; Parse(value, n); return new(n[0], n[1]); }
    internal static Vector4 Vector4(string value) { Span<Fix64> n = stackalloc Fix64[4]; Parse(value, n); return new(n[0], n[1], n[2], n[3]); }
    internal static string Format(Vector2 v) => FormattableString.Invariant($"{v.x},{v.y}");
    internal static string Format(Vector4 v) => FormattableString.Invariant($"{v.x},{v.y},{v.z},{v.w}");
}
