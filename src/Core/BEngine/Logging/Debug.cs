namespace BEngine;

public static class Debug
{
    private static readonly Logger UnityLogger = new(new DefaultLogHandler());
    private static readonly int[] StackTracePolicies = [-1, -1, -1];
    public static event Action<LogEntry>? MessageLogged;
    public static ILogger logger => UnityLogger;
    public static bool developerConsoleVisible { get; set; }

    /// <summary>Whether this level captures a managed call stack when none was supplied.</summary>
    public static bool GetStackTraceEnabled(LogType type)
    {
        var policy = Volatile.Read(ref StackTracePolicies[StackTracePolicyIndex(type)]);
        return policy < 0 ? type != LogType.Info || Application.isEditor : policy != 0;
    }

    /// <summary>Overrides the default call-stack policy for one log level.</summary>
    public static void SetStackTraceEnabled(LogType type, bool enabled) =>
        Volatile.Write(ref StackTracePolicies[StackTracePolicyIndex(type)], enabled ? 1 : 0);

    /// <summary>Restores Editor call stacks and Player warning/error call stacks.</summary>
    public static void ResetStackTraceEnabled(LogType type) =>
        Volatile.Write(ref StackTracePolicies[StackTracePolicyIndex(type)], -1);

    private static int StackTracePolicyIndex(LogType type) => type switch
    {
        LogType.Info => 0,
        LogType.Warning => 1,
        LogType.Error => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

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
        RuntimeServiceAccess.PlatformService.BreakIntoDebugger();
    }

    internal static void Publish(LogType type, object? message, string? stackTrace = null)
    {
        var platform = RuntimeServiceAccess.PlatformService;
        stackTrace ??= GetStackTraceEnabled(type) ? platform.CaptureStackTrace(2) : string.Empty;
        var entry = new LogEntry(
            platform.UtcNow,
            type,
            message?.ToString() ?? "null",
            stackTrace);
        var subscribers = MessageLogged;
        if (subscribers is not null)
        {
            foreach (Action<LogEntry> subscriber in Delegate.EnumerateInvocationList(subscribers))
            {
                try { subscriber(entry); }
                catch (Exception exception)
                {
                    platform.WriteDiagnostic(
                        $"BEngine log subscriber {subscriber.Method.DeclaringType?.FullName}." +
                        $"{subscriber.Method.Name} failed: {exception}");
                }
            }
        }
        platform.WriteDiagnostic(entry.ToDetailedString());
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
