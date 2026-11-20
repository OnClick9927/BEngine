using System.Runtime.CompilerServices;
using BEngine.Profiling;

namespace BEngine.Diagnostics;

/// <summary>Detailed nested sampling and call stacks, supplied by the optional diagnostics module.</summary>
internal sealed class ProfilerRecorder : IProfilerRecorder
{
    private Profiler.RecorderRegistration? _registration;
    private static long _tokenSequence;
    [ThreadStatic] private static Stack<ActiveSample>? s_samples;
    [ThreadStatic] private static string? s_unnamedThreadName;
    [ModuleInitializer]
    internal static void Initialize()
    {
        var recorder = new ProfilerRecorder();
        var registration = Profiler.RegisterRecorder(recorder);
        recorder._registration = registration;
        RuntimeAssemblyCleanup.Register(assembly =>
        {
            if (assembly == typeof(ProfilerRecorder).Assembly) registration.Dispose();
        });
    }
    public long BeginSample(string category, string name, bool captureCallstacks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var stack = s_samples ??= new Stack<ActiveSample>();
        var token = Interlocked.Increment(ref _tokenSequence);
        var parent = stack.Count > 0 ? stack.Peek().Token : 0;
        string[] callStack = [];
        if (captureCallstacks)
        {
            var platform = RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>();
            callStack = platform.CaptureStackTrace(1)
                .Split(platform.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        var thread = RuntimeServices.GetRequiredService<BEngine.Instrumentation.IRuntimeDiagnosticsPlatform>().CurrentThread;
        stack.Push(new ActiveSample(token, parent, category.Trim(), name.Trim(),
            RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>().MonotonicTimestamp,
            GC.GetAllocatedBytesForCurrentThread(),
            thread.Id,
            thread.Name ?? (s_unnamedThreadName ??= $"Thread {thread.Id}"), callStack));
        return token;
    }

    public void EndSample()
    {
        var stack = s_samples;
        if (stack is not { Count: > 0 }) return;
        EndSample(stack.Peek().Token);
    }

    public void EndSample(long token)
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
            RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>().GetElapsedTime(active.StartedTimestamp).TotalMilliseconds,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - active.AllocatedBytes),
            active.ThreadId, active.ThreadName, active.CallStack);
        _registration?.PublishSample(sample);
    }


    private readonly record struct ActiveSample(long Token, long ParentToken, string Category, string Name,
        long StartedTimestamp, long AllocatedBytes, int ThreadId, string ThreadName, string[] CallStack);
}
