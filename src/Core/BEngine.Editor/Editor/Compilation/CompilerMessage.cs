namespace BEngine.Editor;

public readonly record struct CompilerMessage(
    string message,
    string file,
    int line,
    int column,
    CompilerMessageType type);
