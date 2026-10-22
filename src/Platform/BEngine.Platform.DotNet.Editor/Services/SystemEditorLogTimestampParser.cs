using System.Globalization;

namespace BEngine.Editor;

public sealed class SystemEditorLogTimestampParser : IEditorLogTimestampParser
{
    public DateTimeOffset FromUtcFileTimestamp(DateTime timestampUtc) =>
        new DateTimeOffset(DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)).ToLocalTime();

    public bool TryParse(ReadOnlySpan<char> value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out timestamp);
}
