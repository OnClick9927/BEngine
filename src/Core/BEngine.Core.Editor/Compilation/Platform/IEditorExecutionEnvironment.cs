namespace BEngine.Editor;

public interface IEditorExecutionEnvironment
{
    int CurrentManagedThreadId { get; }
    string? CurrentManagedThreadName { get; }
    int ProcessorCount { get; }
    object? GetRuntimeData(string name);
    EditorProcessMemory ReadCurrentProcessMemory();
}
