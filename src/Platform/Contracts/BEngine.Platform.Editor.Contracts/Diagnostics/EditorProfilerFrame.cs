using BEngine.Rendering;

namespace BEngine.Editor;

/// <summary>A completed editor frame held in the profiler history.</summary>
public readonly record struct EditorProfilerFrame(
    long FrameIndex,
    DateTimeOffset TimestampUtc,
    double FrameMilliseconds,
    double UpdateMilliseconds,
    double RuntimeMilliseconds,
    double RenderMilliseconds,
    double ImGuiMilliseconds,
    double PresentMilliseconds,
    long ManagedAllocatedBytes,
    long ManagedHeapBytes,
    long ManagedFragmentedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    SceneRenderStatistics RenderStatistics)
{
    public EditorProfilerMethodSample[] MethodSamples { get; init; } = [];
    public EditorProfilerCounterSample[] CounterSamples { get; init; } = [];
    public bool HasGpuTiming { get; init; }
    public double GpuMilliseconds { get; init; }
    public string GpuTimingMethod { get; init; } = string.Empty;
    public string GpuBackend { get; init; } = string.Empty;
}
