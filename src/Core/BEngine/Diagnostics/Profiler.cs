using System.Diagnostics;

namespace BEngine.Profiling;

public static class Profiler
{
    private static long _tokenSequence;
    [ThreadStatic] private static Stack<ActiveSample>? s_samples;

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
        var stack = s_samples ??= new Stack<ActiveSample>();
        var token = Interlocked.Increment(ref _tokenSequence);
        var parent = stack.Count > 0 ? stack.Peek().Token : 0;
        string[] callStack = [];
        if (enableAllocationCallstacks)
        {
            var frames = new StackTrace(1, false).GetFrames();
            if (frames is { Length: > 0 })
                callStack = frames.Select(static frame => FormatMethod(frame.GetMethod())).ToArray();
        }
        stack.Push(new ActiveSample(token, parent, category.Trim(), name.Trim(),
            Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(),
            Environment.CurrentManagedThreadId,
            Thread.CurrentThread.Name ?? $"Thread {Environment.CurrentManagedThreadId}", callStack));
        return new ProfilerScope(token);
    }

    public static void EndSample()
    {
        var stack = s_samples;
        if (stack is not { Count: > 0 }) return;
        EndSample(stack.Peek().Token);
    }

    internal static void EndSample(long token)
    {
        if (token == 0) return;
        var stack = s_samples;
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
            Stopwatch.GetElapsedTime(active.StartedTimestamp).TotalMilliseconds,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - active.AllocatedBytes),
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

    private static string FormatMethod(System.Reflection.MethodBase? method) => method is null
        ? "Unknown"
        : $"{method.DeclaringType?.FullName ?? "Unknown"}.{method.Name}";

    private readonly record struct ActiveSample(long Token, long ParentToken, string Category, string Name,
        long StartedTimestamp, long AllocatedBytes, int ThreadId, string ThreadName, string[] CallStack);
}
