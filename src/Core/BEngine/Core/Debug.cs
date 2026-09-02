using System.Diagnostics;

namespace BEngine;

public static class Debug
{
    private static readonly Logger UnityLogger = new(new DefaultLogHandler());
    public static event Action<LogEntry>? MessageLogged;
    public static ILogger logger => UnityLogger;
    public static bool developerConsoleVisible { get; set; }

    public static void Log(object? message) => UnityLogger.Log(message);
    public static void Log(object? message, BObject? context) => UnityLogger.Log(LogType.Info, message, context);
    public static void LogWarning(object? message) => UnityLogger.LogWarning(message);
    public static void LogWarning(object? message, BObject? context) => UnityLogger.LogWarning(message, context);
    public static void LogError(object? message) => UnityLogger.LogError(message);
    public static void LogError(object? message, BObject? context) => UnityLogger.LogError(message, context);
    public static void LogFormat(string format, params object?[] args) =>
        UnityLogger.LogFormat(LogType.Info, null, format, args);
    public static void LogWarningFormat(string format, params object?[] args) =>
        UnityLogger.LogFormat(LogType.Warning, null, format, args);
    public static void LogErrorFormat(string format, params object?[] args) =>
        UnityLogger.LogFormat(LogType.Error, null, format, args);
    public static void LogException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        UnityLogger.LogException(exception, null);
    }
    public static void Assert(bool condition, object? message = null, BObject? context = null)
    {
        if (!condition) UnityLogger.Log(LogType.Error, message ?? "Assertion failed", context);
    }
    public static void Break()
    {
        if (Debugger.IsAttached) Debugger.Break();
    }

    internal static void Publish(LogType type, object? message, string? stackTrace = null)
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

    private sealed class DefaultLogHandler : ILogHandler
    {
        public void LogFormat(LogType logType, BObject? context, string format, params object?[] args)
        {
            ArgumentNullException.ThrowIfNull(format);
            string message;
            try { message = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args); }
            catch (FormatException) { message = format; }
            if (context is not null) message = $"{message} ({context.name})";
            Publish(logType, message);
        }

        public void LogException(Exception exception, BObject? context)
        {
            ArgumentNullException.ThrowIfNull(exception);
            var message = context is null ? exception.Message : $"{exception.Message} ({context.name})";
            Publish(LogType.Error, message, exception.ToString());
        }
    }
}
