namespace BEngine.Editor;

/// <summary>Completes a nested method marker and publishes its duration, allocation, and call stack.</summary>
public readonly struct EditorProfilerMethodScope : IDisposable
{
    private readonly long _frameToken;
    private readonly int _id;
    private readonly int _parentId;
    private readonly EditorProfilerDomain _domain;
    private readonly string? _typeName;
    private readonly string? _methodName;
    private readonly long _startedTimestamp;
    private readonly long _allocatedBytesAtStart;
    private readonly int _threadId;
    private readonly string? _threadName;
    private readonly string[]? _callStack;

    internal EditorProfilerMethodScope(
        long frameToken,
        int id,
        int parentId,
        EditorProfilerDomain domain,
        string typeName,
        string methodName,
        long startedTimestamp,
        long allocatedBytesAtStart,
        int threadId,
        string threadName,
        string[] callStack)
    {
        _frameToken = frameToken;
        _id = id;
        _parentId = parentId;
        _domain = domain;
        _typeName = typeName;
        _methodName = methodName;
        _startedTimestamp = startedTimestamp;
        _allocatedBytesAtStart = allocatedBytesAtStart;
        _threadId = threadId;
        _threadName = threadName;
        _callStack = callStack;
    }

    public void Dispose() => EditorProfiler.EndMethodSample(
        _frameToken,
        _id,
        _parentId,
        _domain,
        _typeName ?? string.Empty,
        _methodName ?? string.Empty,
        _startedTimestamp,
        _allocatedBytesAtStart,
        _threadId,
        _threadName ?? string.Empty,
        _callStack ?? []);
}
