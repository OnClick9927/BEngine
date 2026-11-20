namespace BEngine.Profiling;

/// <summary>Host-owned implementation of detailed nested CPU/allocation sampling.</summary>
public interface IProfilerRecorder
{
    long BeginSample(string category, string name, bool captureCallstacks);
    void EndSample();
    void EndSample(long token);
}

/// <summary>Lightweight instrumentation API. Detailed recording is an optional registered service.</summary>
public static class Profiler
{
    private static readonly object Gate = new();
    private static RecorderRegistration? _recorder;
    private static volatile bool s_enabled;
    public static bool enabled { get => s_enabled; set => s_enabled = value; }
    public static bool enableAllocationCallstacks { get; set; } = true;
    public static string logFile { get; set; } = string.Empty;
    public static event Action<ProfilerSample>? sampleCompleted;
    public static event Action<string, string, double>? counterUpdated;

    public static long usedHeapSizeLong => GC.GetTotalMemory(false);
    public static long GetMonoUsedSizeLong() => GC.GetTotalMemory(false);
    public static long GetMonoHeapSizeLong() => GC.GetGCMemoryInfo().HeapSizeBytes;
    public static long GetTotalAllocatedMemoryLong() => GC.GetTotalAllocatedBytes(false);
    public static long GetTotalReservedMemoryLong() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    public static long GetTotalUnusedReservedMemoryLong() => Math.Max(0, GetTotalReservedMemoryLong() - GetMonoHeapSizeLong());

    public static RecorderRegistration RegisterRecorder(IProfilerRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        lock (Gate)
        {
            var registration = new RecorderRegistration(recorder, _recorder);
            Volatile.Write(ref _recorder, registration);
            return registration;
        }
    }
    public static ProfilerScope BeginSample(string name) => BeginSample("Scripts", name);
    public static ProfilerScope BeginSample(string category, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!enabled) return default;
        var registration = Volatile.Read(ref _recorder);
        var recorder = registration?.Recorder;
        return recorder is null ? default : new ProfilerScope(recorder.BeginSample(category, name, enableAllocationCallstacks), registration!);
    }
    public static void EndSample() => Volatile.Read(ref _recorder)?.Recorder?.EndSample();
    internal static void PublishSample(ProfilerSample sample) => sampleCompleted?.Invoke(sample);
    public static void SetCounterValue(string category, string name, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (enabled) counterUpdated?.Invoke(category.Trim(), name.Trim(), value);
    }
    /// <summary>Owned registration and sample sink. Disposal stops publication from this recorder.</summary>
    public sealed class RecorderRegistration : IDisposable
    {
        internal IProfilerRecorder? Recorder;
        private readonly RecorderRegistration? _previous;
        internal RecorderRegistration(IProfilerRecorder recorder, RecorderRegistration? previous)
        { Recorder = recorder; _previous = previous; }

        public void PublishSample(ProfilerSample sample)
        {
            if (Volatile.Read(ref Recorder) is not null) Profiler.PublishSample(sample);
        }
        public void Dispose()
        {
            lock (Gate)
            {
                Volatile.Write(ref Recorder, null);
                var active = _recorder;
                while (active is not null && active.Recorder is null) active = active._previous;
                Volatile.Write(ref _recorder, active);
            }
        }
    }
}
