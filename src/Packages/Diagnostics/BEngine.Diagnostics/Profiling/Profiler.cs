namespace BEngine.Profiling;

public static class Profiler
{
    private static long _tokenSequence;
    private static readonly Stack<ActiveSample> Samples = [];

    public static bool enabled { get; set; }
    public static bool enableAllocationCallstacks { get; set; } = true;
    public static string logFile { get; set; } = string.Empty;
    public static event Action<ProfilerSample>? sampleCompleted;
    public static event Action<string, string, double>? counterUpdated;

    public static long usedHeapSizeLong => GC.GetTotalMemory(false);
    public static long GetMonoUsedSizeLong() => GC.GetTotalMemory(false);
    public static long GetMonoHeapSizeLong() => GC.GetGCMemoryInfo().HeapSizeBytes;
    public static long GetTotalAllocatedMemoryLong() => GC.GetTotalAllocatedBytes(false);
    public static long GetTotalReservedMemoryLong() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    public static long GetTotalUnusedReservedMemoryLong() =>
        Math.Max(0, GetTotalReservedMemoryLong() - GetMonoHeapSizeLong());

    public static ProfilerScope BeginSample(string name) => BeginSample("Scripts", name);

    public static ProfilerScope BeginSample(string category, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!enabled) return default;
        var stack = Samples;
        var token = ++_tokenSequence;
        var parent = stack.Count > 0 ? stack.Peek().Token : 0;
        string[] callStack = [];
        if (enableAllocationCallstacks)
        {
            var platform = RuntimeServiceAccess.PlatformService;
            callStack = platform.CaptureStackTrace(1)
                .Split(platform.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        stack.Push(new ActiveSample(token, parent, category.Trim(), name.Trim(),
            RuntimeServiceAccess.PlatformService.MonotonicTimestamp,
            GC.GetTotalAllocatedBytes(false),
            1, "Main", callStack));
        return new ProfilerScope(token);
    }

    public static void EndSample()
    {
        var stack = Samples;
        if (stack is not { Count: > 0 }) return;
        EndSample(stack.Peek().Token);
    }

    internal static void EndSample(long token)
    {
        if (token == 0) return;
        var stack = Samples;
        if (stack is not { Count: > 0 }) return;
        ActiveSample active;
        if (stack.Peek().Token == token) active = stack.Pop();
        else
        {
            var retained = new Stack<ActiveSample>();
            ActiveSample? match = null;
            while (stack.Count > 0)
            {
                var candidate = stack.Pop();
                if (candidate.Token == token) { match = candidate; break; }
                retained.Push(candidate);
            }
            while (retained.Count > 0) stack.Push(retained.Pop());
            if (match is null) return;
            active = match.Value;
        }
        var sample = new ProfilerSample(active.Token, active.ParentToken, active.Category, active.Name,
            RuntimeServiceAccess.PlatformService.GetElapsedTime(active.StartedTimestamp).TotalMilliseconds,
            Math.Max(0, GC.GetTotalAllocatedBytes(false) - active.AllocatedBytes),
            active.ThreadId, active.ThreadName, active.CallStack);
        sampleCompleted?.Invoke(sample);
    }

    public static void SetCounterValue(string category, string name, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (enabled) counterUpdated?.Invoke(category.Trim(), name.Trim(), value);
    }

    private readonly record struct ActiveSample(long Token, long ParentToken, string Category, string Name,
        long StartedTimestamp, long AllocatedBytes, int ThreadId, string ThreadName, string[] CallStack);
}
