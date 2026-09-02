namespace BEngine;

public interface ILogHandler
{
    void LogFormat(LogType logType, BObject? context, string format, params object?[] args);
    void LogException(Exception exception, BObject? context);
}
