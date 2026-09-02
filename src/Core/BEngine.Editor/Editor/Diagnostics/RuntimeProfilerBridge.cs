using BEngine.Profiling;

namespace BEngine.Editor;

[InitializeOnLoad]
internal static class RuntimeProfilerBridge
{
    static RuntimeProfilerBridge()
    {
        Profiler.sampleCompleted += EditorProfiler.ReportRuntimeMethodSample;
        Profiler.counterUpdated += EditorProfiler.ReportCounter;
    }
}
