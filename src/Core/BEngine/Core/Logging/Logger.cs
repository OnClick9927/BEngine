using System.Globalization;

namespace BEngine;

public sealed class Logger : ILogger
{
    public ILogHandler logHandler
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }
    public bool logEnabled { get; set; } = true;
    public LogType filterLogType { get; set; } = LogType.Info;

    public Logger(ILogHandler logHandler) => this.logHandler = logHandler;

    public bool IsLogTypeAllowed(LogType logType) => logEnabled && logType >= filterLogType;

    public void Log(LogType logType, object? message, BObject? context = null)
    {
        if (!IsLogTypeAllowed(logType)) return;
        logHandler.LogFormat(logType, context, "{0}", message ?? "null");
    }

    public void Log(object? message) => Log(LogType.Info, message);
    public void LogWarning(object? message, BObject? context = null) => Log(LogType.Warning, message, context);
    public void LogError(object? message, BObject? context = null) => Log(LogType.Error, message, context);

    public void LogFormat(LogType logType, BObject? context, string format, params object?[] args)
    {
        if (IsLogTypeAllowed(logType)) logHandler.LogFormat(logType, context, format, args);
    }

    public void LogException(Exception exception, BObject? context)
    {
        if (IsLogTypeAllowed(LogType.Error)) logHandler.LogException(exception, context);
    }
}
