namespace BEngine;

public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogType Type,
    string Message,
    string StackTrace)
{
    public LogEntry(DateTimeOffset timestamp, LogType type, string message)
        : this(timestamp, type, message, string.Empty) { }

    public string ToDetailedString()
    {
        var header = $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Type}] {Message}";
        return string.IsNullOrWhiteSpace(StackTrace)
            ? header
            : $"{header}{Environment.NewLine}{StackTrace}";
    }
}
