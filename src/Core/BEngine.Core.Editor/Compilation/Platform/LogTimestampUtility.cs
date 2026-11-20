namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorLogTimestampParser through dependency injection.</summary>
public sealed class LogTimestampUtility
{
    private readonly IEditorLogTimestampParser _implementation;

    public LogTimestampUtility(IEditorLogTimestampParser implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static LogTimestampUtility Current => BEngine.Editor.EditorServices.GetRequiredService<LogTimestampUtility>();

    public DateTimeOffset FromUtcFileTimestamp(DateTime timestampUtc) =>
        _implementation.FromUtcFileTimestamp(timestampUtc);

    public bool TryParse(ReadOnlySpan<char> value, out DateTimeOffset timestamp) =>
        _implementation.TryParse(value, out timestamp);
}
