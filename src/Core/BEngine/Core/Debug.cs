using System.Diagnostics;

namespace BEngine;

public enum LogType
{
    Info,
    Warning,
    Error
}
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
public static class Debug
{
    public static event Action<LogEntry>? MessageLogged;

    public static void Log(object? message) => Write(LogType.Info, message);
    public static void LogWarning(object? message) => Write(LogType.Warning, message);
    public static void LogError(object? message) => Write(LogType.Error, message);
    public static void LogException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(LogType.Error, exception.Message, exception.ToString());
    }

    private static void Write(LogType type, object? message, string? stackTrace = null)
    {
        stackTrace ??= new StackTrace(2, true).ToString().TrimEnd();
        var entry = new LogEntry(DateTimeOffset.Now, type, message?.ToString() ?? "null", stackTrace);
        var subscribers = MessageLogged;
        if (subscribers is not null)
        {
            foreach (Action<LogEntry> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(entry); }
                catch (Exception exception)
                {
                    Trace.WriteLine($"BEngine log subscriber {subscriber.Method.DeclaringType?.FullName}." +
                                    $"{subscriber.Method.Name} failed: {exception}");
                }
            }
        }
        Trace.WriteLine(entry.ToDetailedString());
    }
}
