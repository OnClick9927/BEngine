namespace BEngine;

public enum LogType
{
    Info,
    Warning,
    Error
}

public readonly record struct LogEntry(DateTimeOffset Timestamp, LogType Type, string Message);

public static class Debug
{
    public static event Action<LogEntry>? MessageLogged;

    public static void Log(object? message) => Write(LogType.Info, message);
    public static void LogWarning(object? message) => Write(LogType.Warning, message);
    public static void LogError(object? message) => Write(LogType.Error, message);

    private static void Write(LogType type, object? message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, type, message?.ToString() ?? "null");
        MessageLogged?.Invoke(entry);
        Console.WriteLine($"[{entry.Type}] {entry.Message}");
    }
}
