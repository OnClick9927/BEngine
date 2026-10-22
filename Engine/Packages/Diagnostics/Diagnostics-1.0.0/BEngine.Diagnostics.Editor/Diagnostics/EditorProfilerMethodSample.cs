namespace BEngine.Editor;

/// <summary>One nested method marker captured during an editor frame.</summary>
public readonly record struct EditorProfilerMethodSample(
    int Id,
    int ParentId,
    EditorProfilerDomain Domain,
    string TypeName,
    string MethodName,
    double TotalMilliseconds,
    double SelfMilliseconds,
    int Calls,
    long AllocatedBytes,
    long SelfAllocatedBytes,
    int ThreadId,
    string ThreadName,
    string[] CallStack)
{
    public string DisplayName => string.IsNullOrWhiteSpace(TypeName)
        ? MethodName
        : $"{TypeName}.{MethodName}";
}
