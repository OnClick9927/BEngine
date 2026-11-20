namespace BEngine.Editor;

public interface IEditorLogTimestampParser
{
    DateTimeOffset FromUtcFileTimestamp(DateTime timestampUtc);
    bool TryParse(ReadOnlySpan<char> value, out DateTimeOffset timestamp);
}
