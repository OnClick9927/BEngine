using BEngine.Rendering;
namespace BEngine.Editor;
internal sealed class EditorProfilerSink : IEditorProfilerSink
{
    public bool CaptureCallStacks { get => EditorProfilerCollector.CaptureCallStacks; set => EditorProfilerCollector.CaptureCallStacks=value; }
    public bool Recording { get => EditorProfilerCollector.Recording; set => EditorProfilerCollector.Recording=value; }
    public int HistoryCapacity { get => EditorProfilerCollector.HistoryCapacity; set => EditorProfilerCollector.HistoryCapacity=value; }
    public long Version { get => EditorProfilerCollector.Version; }
    public EditorProfilerFrameScope BeginFrame() => EditorProfilerCollector.BeginFrame();
    public EditorProfilerSampleScope BeginSample(EditorProfilerArea area) => EditorProfilerCollector.BeginSample(area);
    public EditorProfilerMethodScope BeginMethodSample(
        EditorProfilerDomain domain = EditorProfilerDomain.Runtime) => EditorProfilerCollector.BeginMethodSample(domain);
    public EditorProfilerMethodScope BeginMethodSample(
        Type declaringType,
        string methodName,
        EditorProfilerDomain domain) => EditorProfilerCollector.BeginMethodSample(declaringType, methodName, domain);
    public void ReportCounter(string moduleId, string counterName, double value) => EditorProfilerCollector.ReportCounter(moduleId, counterName, value);
    public void ReportRuntimeMethodSample(BEngine.Profiling.ProfilerSample sample) => EditorProfilerCollector.ReportRuntimeMethodSample(sample);
    public void ReportSample(EditorProfilerArea area, double milliseconds) => EditorProfilerCollector.ReportSample(area, milliseconds);
    public void ReportRenderStatistics(SceneRenderStatistics statistics) => EditorProfilerCollector.ReportRenderStatistics(statistics);
    public void Clear() => EditorProfilerCollector.Clear();
    public EditorProfilerSnapshot GetSnapshot() => EditorProfilerCollector.GetSnapshot();
    public EditorProfilerMetadata GetMetadata() => EditorProfilerCollector.GetMetadata();
    public int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out EditorProfilerMetadata metadata) => EditorProfilerCollector.CopyFrames(destination, out metadata);
    public int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out long snapshotVersion,
        out bool recording,
        out int capacity) => EditorProfilerCollector.CopyFrames(destination, out snapshotVersion, out recording, out capacity);
    public void EndFrame(long token) => EditorProfilerCollector.EndFrame(token);
    public void EndSample(long token, EditorProfilerArea area, long startedTimestamp) => EditorProfilerCollector.EndSample(token, area, startedTimestamp);
    public void EndMethodSample(
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
        string[] callStack) => EditorProfilerCollector.EndMethodSample(token, id, parentId, domain, typeName, methodName, startedTimestamp, allocatedBytesAtStart, threadId, threadName, callStack);
}
