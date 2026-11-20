namespace BEngine.Profiling;

public readonly record struct ProfilerSample(
    long token,
    long parentToken,
    string category,
    string name,
    double elapsedMilliseconds,
    long allocatedBytes,
    int threadId,
    string threadName,
    string[] callStack);
