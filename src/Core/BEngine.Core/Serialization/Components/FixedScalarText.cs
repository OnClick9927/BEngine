namespace BEngine.Serialization;

/// <summary>Reads the fixed-size scalar tuples used in component documents without creating token strings.</summary>
internal static class FixedScalarText
{
    internal static void Parse(string value, Span<Fix64> destination)
    {
        var text = value.AsSpan(0, value.Length);
        if (text.Count(',') != destination.Length - 1)
            throw new FormatException($"Expected {destination.Length} comma-separated values.");
        for (var index = 0; index < destination.Length; index++)
        {
            var separator = text.IndexOf(',');
            if (separator < 0)
            {
                destination[index] = Fix64.Parse(text.Trim());
                break;
            }
            destination[index] = Fix64.Parse(text[..separator].Trim());
            text = text[(separator + 1)..];
        }
    }
}
