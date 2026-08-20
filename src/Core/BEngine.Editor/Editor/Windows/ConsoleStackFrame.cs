namespace BEngine.Editor;

internal readonly record struct ConsoleStackFrame(
    string Prefix,
    string FilePath,
    int LineNumber,
    int ColumnNumber)
{
    public string SourceLocation => ColumnNumber > 0
        ? $"{FilePath}:line {LineNumber}:{ColumnNumber}"
        : $"{FilePath}:line {LineNumber}";
}
