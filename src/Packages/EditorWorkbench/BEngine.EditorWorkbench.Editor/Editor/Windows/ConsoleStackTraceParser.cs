using System.Globalization;

namespace BEngine.Editor;

internal static class ConsoleStackTraceParser
{
    public static bool TryParse(string? line, out ConsoleStackFrame frame)
    {
        frame = default;
        if (string.IsNullOrWhiteSpace(line)) return false;
        return TryParseDotNet(line, ":line ", " in ", out frame) ||
               TryParseDotNet(line, ":行号 ", " 位置 ", out frame) ||
               TryParseUnity(line, out frame) ||
               TryParseCompiler(line, out frame);
    }

    private static bool TryParseDotNet(
        string line,
        string lineMarker,
        string pathMarker,
        out ConsoleStackFrame frame)
    {
        frame = default;
        var lineIndex = line.LastIndexOf(lineMarker, StringComparison.OrdinalIgnoreCase);
        if (lineIndex < 0 || !TryReadNumber(line.AsSpan(lineIndex + lineMarker.Length), out var lineNumber, out _))
            return false;
        var pathIndex = line.LastIndexOf(pathMarker, lineIndex, StringComparison.OrdinalIgnoreCase);
        if (pathIndex < 0) return false;
        var path = line[(pathIndex + pathMarker.Length)..lineIndex].Trim().Trim('"');
        if (path.Length == 0) return false;
        frame = new ConsoleStackFrame(line[..pathIndex].TrimEnd(), path, lineNumber, 0);
        return true;
    }

    private static bool TryParseUnity(string line, out ConsoleStackFrame frame)
    {
        frame = default;
        var marker = line.LastIndexOf("(at ", StringComparison.OrdinalIgnoreCase);
        var close = line.LastIndexOf(')');
        if (marker < 0 || close <= marker + 4) return false;
        var source = line.AsSpan(marker + 4, close - marker - 4).Trim();
        var colon = source.LastIndexOf(':');
        if (colon <= 0 || !TryReadNumber(source[(colon + 1)..], out var lineNumber, out _)) return false;
        var path = source[..colon].Trim().ToString().Trim('"');
        if (path.Length == 0) return false;
        frame = new ConsoleStackFrame(line[..marker].TrimEnd(), path, lineNumber, 0);
        return true;
    }

    private static bool TryParseCompiler(string line, out ConsoleStackFrame frame)
    {
        frame = default;
        var open = line.LastIndexOf('(');
        if (open <= 0) return false;
        var close = line.IndexOf(')', open + 1);
        if (close < 0) return false;
        var location = line.AsSpan(open + 1, close - open - 1);
        if (!TryReadNumber(location, out var lineNumber, out var consumed)) return false;
        var columnNumber = 0;
        var remaining = location[consumed..].TrimStart();
        if (!remaining.IsEmpty && remaining[0] == ',')
            TryReadNumber(remaining[1..], out columnNumber, out _);
        var path = line[..open].Trim().Trim('"');
        if (!LooksLikeSourcePath(path)) return false;
        frame = new ConsoleStackFrame(string.Empty, path, lineNumber, columnNumber);
        return true;
    }

    private static bool TryReadNumber(ReadOnlySpan<char> value, out int number, out int consumed)
    {
        value = value.TrimStart();
        number = 0;
        consumed = 0;
        while (consumed < value.Length && char.IsAsciiDigit(value[consumed])) consumed++;
        return consumed > 0 && int.TryParse(value[..consumed], NumberStyles.None,
            CultureInfo.InvariantCulture, out number);
    }

    private static bool LooksLikeSourcePath(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase);
}
