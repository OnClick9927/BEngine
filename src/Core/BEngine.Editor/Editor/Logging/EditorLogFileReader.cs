using System.Text;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class EditorLogFileReader
{
    internal static LogEntry[] Read(string path) =>
        Read(
            path,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<LogTimestampUtility>());

    internal static LogEntry[] Read(string path, FileUtility fileSystem) =>
        Read(
            path,
            fileSystem,
            EditorServiceAccess.GetRequiredService<LogTimestampUtility>());

    internal static LogEntry[] Read(
        string path,
        FileUtility fileSystem,
        LogTimestampUtility timestampParser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(timestampParser);
        if (!fileSystem.FileExists(path)) return [];

        var fallbackTimestamp = timestampParser.FromUtcFileTimestamp(
            fileSystem.GetLastWriteTimeUtc(path));
        using var stream = fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var entries = new List<LogEntry>();
        PendingEntry? pending = null;
        while (reader.ReadLine() is { } line)
        {
            if (TryParseHeader(
                    line, timestampParser, out var timestamp, out var type, out var message))
            {
                Flush(entries, ref pending);
                pending = new PendingEntry(timestamp, type, message);
                continue;
            }

            if (pending is not null && IsContinuation(line))
            {
                var continuation = pending.Value;
                continuation.StackTrace.AppendLine(line);
                pending = continuation;
                continue;
            }

            Flush(entries, ref pending);
            if (string.IsNullOrWhiteSpace(line)) continue;
            entries.Add(new LogEntry(fallbackTimestamp, InferType(line), line));
        }
        Flush(entries, ref pending);
        return entries.ToArray();
    }

    private static bool TryParseHeader(
        string line,
        LogTimestampUtility timestampParser,
        out DateTimeOffset timestamp,
        out LogType type,
        out string message)
    {
        timestamp = default;
        type = LogType.Info;
        message = string.Empty;
        if (line.Length < 4 || line[0] != '[') return false;
        var timestampEnd = line.IndexOf(']');
        if (timestampEnd <= 1 ||
            !timestampParser.TryParse(line.AsSpan(1, timestampEnd - 1), out timestamp)) return false;

        var cursor = timestampEnd + 1;
        var hasExplicitType = false;
        while (cursor < line.Length && char.IsWhiteSpace(line[cursor])) cursor++;
        if (cursor < line.Length && line[cursor] == '[')
        {
            var typeEnd = line.IndexOf(']', cursor + 1);
            if (typeEnd > cursor && Enum.TryParse<LogType>(line.AsSpan(cursor + 1,
                    typeEnd - cursor - 1), true, out var parsedType))
            {
                type = parsedType;
                hasExplicitType = true;
                cursor = typeEnd + 1;
            }
        }
        while (cursor < line.Length && char.IsWhiteSpace(line[cursor])) cursor++;
        message = cursor < line.Length ? line[cursor..] : string.Empty;
        if (!hasExplicitType) type = InferType(message);
        return true;
    }

    private static bool IsContinuation(string line) =>
        string.IsNullOrWhiteSpace(line) ||
        char.IsWhiteSpace(line[0]) ||
        line.StartsWith("at ", StringComparison.Ordinal) ||
        line.StartsWith("---", StringComparison.Ordinal) ||
        line.StartsWith("--->", StringComparison.Ordinal) ||
        line.StartsWith("Caused by", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Inner Exception", StringComparison.OrdinalIgnoreCase);

    private static LogType InferType(string line)
    {
        if (ContainsWord(line, "error") || ContainsWord(line, "exception") ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase)) return LogType.Error;
        return ContainsWord(line, "warning") || ContainsWord(line, "warn")
            ? LogType.Warning
            : LogType.Info;
    }

    private static bool ContainsWord(string value, string word)
    {
        var index = value.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var end = index + word.Length;
            var after = end >= value.Length || !char.IsLetterOrDigit(value[end]);
            if (before && after) return true;
            index = value.IndexOf(word, end, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static void Flush(List<LogEntry> entries, ref PendingEntry? pending)
    {
        if (pending is null) return;
        var entry = pending.Value;
        entries.Add(new LogEntry(entry.Timestamp, entry.Type,
            string.IsNullOrWhiteSpace(entry.Message) ? "(no message)" : entry.Message,
            entry.StackTrace.ToString().TrimEnd()));
        pending = null;
    }

    private struct PendingEntry(DateTimeOffset timestamp, LogType type, string message)
    {
        public DateTimeOffset Timestamp { get; } = timestamp;
        public LogType Type { get; } = type;
        public string Message { get; } = message;
        public StringBuilder StackTrace { get; } = new();
    }
}
