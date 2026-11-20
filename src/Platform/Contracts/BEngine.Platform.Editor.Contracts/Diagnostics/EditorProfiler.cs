using BEngine.Rendering;
namespace BEngine.Editor;
/// <summary>Optional sampling facade. No history, reflection or frame buffers are created without a sink.</summary>
public static class EditorProfiler
{
    public const int DefaultHistoryCapacity = 300, MaximumHistoryCapacity = 10_000;
    public static IEditorProfilerSink? Sink { get; set; }
    public static event Action? FrameCaptured;
    /// <summary>Called by a profiler sink after committing a captured frame.</summary>
    public static void NotifyFrameCaptured() => FrameCaptured?.Invoke();
    public static bool CaptureCallStacks { get => Sink?.CaptureCallStacks ?? true; set { if(Sink is {} sink) sink.CaptureCallStacks=value; } }
    public static bool Recording { get => Sink?.Recording ?? false; set { if(Sink is {} sink) sink.Recording=value; } }
    public static int HistoryCapacity { get => Sink?.HistoryCapacity ?? DefaultHistoryCapacity; set { if(value<1||value>MaximumHistoryCapacity) throw new ArgumentOutOfRangeException(nameof(value)); if(Sink is {} sink) sink.HistoryCapacity=value; } }
    public static long Version => Sink?.Version ?? 0;
    public static EditorProfilerFrameScope BeginFrame() { if(Sink is {} sink) { return sink.BeginFrame(); } return default; }
    public static EditorProfilerSampleScope BeginSample(EditorProfilerArea area) { if(Sink is {} sink) { return sink.BeginSample(area); } return default; }
    public static EditorProfilerMethodScope BeginMethodSample(
        EditorProfilerDomain domain = EditorProfilerDomain.Runtime) { if(Sink is {} sink) { return sink.BeginMethodSample(domain); } return default; }
    public static EditorProfilerMethodScope BeginMethodSample(
        Type declaringType,
        string methodName,
        EditorProfilerDomain domain) { if(Sink is {} sink) { return sink.BeginMethodSample(declaringType, methodName, domain); } return default; }
    public static void ReportCounter(string moduleId, string counterName, double value) { if(Sink is {} sink) { sink.ReportCounter(moduleId, counterName, value); return; }  }
    /// <summary>Forwards runtime samples supplied by a profiling bridge to the active editor sink.</summary>
    public static void ReportRuntimeMethodSample(BEngine.Profiling.ProfilerSample sample) { if(Sink is {} sink) { sink.ReportRuntimeMethodSample(sample); return; }  }
    public static void ReportSample(EditorProfilerArea area, double milliseconds) { if(Sink is {} sink) { sink.ReportSample(area, milliseconds); return; }  }
    public static void ReportRenderStatistics(SceneRenderStatistics statistics) { if(Sink is {} sink) { sink.ReportRenderStatistics(statistics); return; }  }
    public static void Clear() { if(Sink is {} sink) { sink.Clear(); return; }  }
    public static EditorProfilerSnapshot GetSnapshot() { if(Sink is {} sink) { return sink.GetSnapshot(); } return new(0,false,DefaultHistoryCapacity,[]); }
    public static EditorProfilerMetadata GetMetadata() { if(Sink is {} sink) { return sink.GetMetadata(); } return new(0,false,DefaultHistoryCapacity,0); }
    public static int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out EditorProfilerMetadata metadata) { if(Sink is {} sink) { return sink.CopyFrames(destination, out metadata); } metadata = new(0,false,DefaultHistoryCapacity,0); return default; }
    public static int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out long snapshotVersion,
        out bool recording,
        out int capacity) { if(Sink is {} sink) { return sink.CopyFrames(destination, out snapshotVersion, out recording, out capacity); } snapshotVersion = default; recording = default; capacity = DefaultHistoryCapacity; return default; }
    internal static void EndFrame(long token) { if(Sink is {} sink) { sink.EndFrame(token); return; }  }
    internal static void EndSample(long token, EditorProfilerArea area, long startedTimestamp) { if(Sink is {} sink) { sink.EndSample(token, area, startedTimestamp); return; }  }
    internal static void EndMethodSample(
        long token,
        int id,
        int parentId,
        EditorProfilerDomain domain,
        string typeName,
        string methodName,
        long startedTimestamp,
        long allocatedBytesAtStart,
        int threadId,
        string threadName,
        string[] callStack) { if(Sink is {} sink) { sink.EndMethodSample(token, id, parentId, domain, typeName, methodName, startedTimestamp, allocatedBytesAtStart, threadId, threadName, callStack); return; }  }
}
public interface IEditorProfilerSink
{
    bool CaptureCallStacks { get; set; }
    bool Recording { get; set; }
    int HistoryCapacity { get; set; }
    long Version { get; }
    EditorProfilerFrameScope BeginFrame();
    EditorProfilerSampleScope BeginSample(EditorProfilerArea area);
    EditorProfilerMethodScope BeginMethodSample(
        EditorProfilerDomain domain = EditorProfilerDomain.Runtime);
    EditorProfilerMethodScope BeginMethodSample(
        Type declaringType,
        string methodName,
        EditorProfilerDomain domain);
    void ReportCounter(string moduleId, string counterName, double value);
    void ReportRuntimeMethodSample(BEngine.Profiling.ProfilerSample sample);
    void ReportSample(EditorProfilerArea area, double milliseconds);
    void ReportRenderStatistics(SceneRenderStatistics statistics);
    void Clear();
    EditorProfilerSnapshot GetSnapshot();
    EditorProfilerMetadata GetMetadata();
    int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out EditorProfilerMetadata metadata);
    int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out long snapshotVersion,
        out bool recording,
        out int capacity);
    void EndFrame(long token);
    void EndSample(long token, EditorProfilerArea area, long startedTimestamp);
    void EndMethodSample(
        long token,
        int id,
        int parentId,
        EditorProfilerDomain domain,
        string typeName,
        string methodName,
        long startedTimestamp,
        long allocatedBytesAtStart,
        int threadId,
        string threadName,
        string[] callStack);
}
