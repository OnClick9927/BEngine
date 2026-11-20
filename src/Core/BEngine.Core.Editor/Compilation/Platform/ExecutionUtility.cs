namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IEditorExecutionEnvironment through dependency injection.</summary>
public sealed class ExecutionUtility
{
    private readonly IEditorExecutionEnvironment _implementation;

    public ExecutionUtility(IEditorExecutionEnvironment implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static ExecutionUtility Current => BEngine.Editor.EditorServices.GetRequiredService<ExecutionUtility>();

    public int CurrentManagedThreadId => _implementation.CurrentManagedThreadId;

    public string? CurrentManagedThreadName => _implementation.CurrentManagedThreadName;

    public int ProcessorCount => _implementation.ProcessorCount;

    public object? GetRuntimeData(string name) =>
        _implementation.GetRuntimeData(name);

    public EditorProcessMemory ReadCurrentProcessMemory() =>
        _implementation.ReadCurrentProcessMemory();
}
