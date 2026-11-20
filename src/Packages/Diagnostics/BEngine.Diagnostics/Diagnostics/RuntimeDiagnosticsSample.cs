using BEngine.Instrumentation;
using BEngine.Diagnostics;
using BEngine.Platform;
using BEngine.Profiling;
using BEngine.Rendering.Rhi;

namespace BEngine.Diagnostics;

public sealed record RuntimeProfilerEvent(
    string Category,
    string Name,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    int ThreadId);

public sealed record RuntimeProfilerCounter(string Category, string Name, double Value);

public sealed record RuntimeDiagnosticsSample(
    DateTimeOffset CapturedAtUtc,
    int ProcessId,
    string ProcessName,
    double ProcessCpuPercent,
    long ManagedHeapBytes,
    long ManagedFragmentedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    int EngineObjectCount,
    GraphicsFrameTiming GpuFrame,
    RuntimeProfilerEvent[] RecentEvents,
    RuntimeProfilerCounter[] Counters)
{
    public static RuntimeDiagnosticsSample Capture() => RuntimeDiagnosticsTelemetry.Capture();
}

internal static class RuntimeDiagnosticsTelemetry
{
    private const int EventCapacity = 256;
    private static readonly Lock Gate = new();
    private static readonly Queue<RuntimeProfilerEvent> Events = new();
    private static readonly Dictionary<(string Category, string Name), double> Counters = new();

    static RuntimeDiagnosticsTelemetry()
    {
        Profiler.sampleCompleted += sample =>
        {
            lock (Gate)
            {
                Events.Enqueue(new RuntimeProfilerEvent(
                    sample.category,
                    sample.name,
                    sample.elapsedMilliseconds,
                    sample.allocatedBytes,
                    sample.threadId));
                while (Events.Count > EventCapacity) Events.Dequeue();
            }
        };
        Profiler.counterUpdated += (category, name, value) =>
        {
            lock (Gate) Counters[(category, name)] = value;
        };
    }

    internal static RuntimeDiagnosticsSample Capture()
    {
        var process = RuntimeServices.GetRequiredService<IRuntimeDiagnosticsPlatform>().CaptureProcess();
        RuntimeProfilerEvent[] events;
        RuntimeProfilerCounter[] counters;
        lock (Gate)
        {
            events = Events.ToArray();
            counters = Counters.OrderBy(pair => pair.Key.Category, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Name, StringComparer.Ordinal)
                .Select(pair => new RuntimeProfilerCounter(
                    pair.Key.Category, pair.Key.Name, pair.Value)).ToArray();
        }
        var memory = GC.GetGCMemoryInfo();
        return new RuntimeDiagnosticsSample(
            RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>().UtcNow,
            RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>().ProcessId,
            process.Name,
            process.CpuPercent,
            Math.Max(0, memory.HeapSizeBytes),
            Math.Max(0, memory.FragmentedBytes),
            Math.Max(0, process.WorkingSetBytes),
            Math.Max(0, process.PrivateBytes),
            BEngine.Instrumentation.RuntimeObjectDiagnostics.CountCurrentDomainObjects(),
            GraphicsFrameTimingService.Latest,
            events,
            counters);
    }
}
