namespace BEngine;

public interface ILogger : ILogHandler
{
    ILogHandler logHandler { get; set; }
    bool logEnabled { get; set; }
    LogType filterLogType { get; set; }
    bool IsLogTypeAllowed(LogType logType);
    void Log(LogType logType, object? message, BObject? context = null);
    void Log(object? message);
    void LogWarning(object? message, BObject? context = null);
    void LogError(object? message, BObject? context = null);
}
