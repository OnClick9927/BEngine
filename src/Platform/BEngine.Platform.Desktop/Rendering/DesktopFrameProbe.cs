using System.Diagnostics;
using System.Runtime.InteropServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Platform;

/// <summary>Opt-in, buffered native frame timings. No logging occurs inside the measured loop.</summary>
public sealed class DesktopFrameProbe
{
    [ThreadStatic] private static DesktopFrameProbe? _cpuCurrent;
    /// <summary>The opt-in CPU probe for the current desktop loop thread.</summary>
    public static DesktopFrameProbe? CpuCurrent => _cpuCurrent;
    public bool CpuEnabled { get; }
    private readonly long _started = Stopwatch.GetTimestamp();
    // Capture the wall-clock correspondence before allocating the sample buffer.
    // The two monotonic reads bound the UTC sample's skew without touching the
    // measured loop or making a managed allocation.
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly long _startedUtcCaptured = Stopwatch.GetTimestamp();
    private readonly Sample[] _samples = new Sample[65536];
    private int _count;
    private long _sequence;
    private readonly double[] _gpuSamples;
    private readonly GraphicsPassTiming[] _gpuPassSamples;
    private readonly double _collectFrom;
    private readonly double _collectUntil;
    private readonly bool _reportAllDetails;
    private readonly CpuSample[] _cpuSamples;
    private readonly GraphicsCpuFrameTiming[] _graphicsSamples;
    private GraphicsCpuFrameTiming _graphicsFrame;
    private long _eventsStartTicks, _eventsEndTicks;
    private ulong _eventsStartCycles, _eventsEndCycles;
    private CpuSample _cpuFrame;
    private readonly uint _nativeThreadId;
    private int _gpuCount;
    private long _gpuSequence;
    private long _gpuPassVersion;
    private int _gpuPassCount;
    private long _gpuPassDropped;
    private int _gen0 = GC.CollectionCount(0), _gen1 = GC.CollectionCount(1), _gen2 = GC.CollectionCount(2);

    public DesktopFrameProbe(bool collectGpu = false, bool collectGpuPasses = false)
        : this(collectGpu, collectGpuPasses, collectGraphicsPhases: false) { }

    public DesktopFrameProbe(bool collectGpu, bool collectGpuPasses, bool collectGraphicsPhases)
    {
        _gpuSamples = collectGpu ? new double[65536] : [];
        _gpuPassSamples = collectGpuPasses ? new GraphicsPassTiming[131072] : [];
        _graphicsSamples = collectGraphicsPhases ? new GraphicsCpuFrameTiming[_samples.Length] : [];
        _gpuPassVersion = collectGpuPasses ? GraphicsPassTimingService.Version : 0;
        _collectFrom = ReadSeconds("BENGINE_FRAME_PROBE_START_SECONDS", 10);
        _collectUntil = ReadSeconds("BENGINE_FRAME_PROBE_END_SECONDS", double.PositiveInfinity);
        if (_collectUntil <= _collectFrom) _collectUntil = double.PositiveInfinity;
        _reportAllDetails = Environment.GetEnvironmentVariable("BENGINE_FRAME_PROBE_DETAILS_ALL") == "1";
        CpuEnabled = OperatingSystem.IsWindows() &&
            Environment.GetEnvironmentVariable("BENGINE_FRAME_PROBE_CPU") == "1";
        _cpuSamples = CpuEnabled ? new CpuSample[_samples.Length] : [];
        if (CpuEnabled)
        {
            _nativeThreadId = GetCurrentThreadId();
            _cpuCurrent = this;
        }
    }

    // These checkpoints are diagnostic only. CPU cycles are deliberately kept
    // in their native unit: CPU frequency and core migration prevent an exact
    // conversion to wall milliseconds. A failed read remains explicitly zero.
    public void BeginCpuUpdate(long timestamp)
    {
        _cpuFrame = default;
        _cpuFrame.UpdateStartTicks = timestamp;
        _cpuFrame.UpdateStartCycles = ReadThreadCycles();
        _cpuFrame.EventsStartTicks = _eventsStartTicks; _cpuFrame.EventsEndTicks = _eventsEndTicks;
        _cpuFrame.EventsStartCycles = _eventsStartCycles; _cpuFrame.EventsEndCycles = _eventsEndCycles;
        _eventsStartTicks = _eventsEndTicks = 0; _eventsStartCycles = _eventsEndCycles = 0;
    }

    public void BeginCpuEvents(long timestamp) { _eventsStartTicks = timestamp; _eventsStartCycles = ReadThreadCycles(); }
    public void EndCpuEvents(long timestamp) { _eventsEndTicks = timestamp; _eventsEndCycles = ReadThreadCycles(); }
    public void RecordGraphicsTiming(GraphicsCpuFrameTiming timing) => _graphicsFrame = timing;

    public void EndCpuUpdate(long timestamp)
    {
        _cpuFrame.UpdateEndTicks = timestamp;
        _cpuFrame.UpdateEndCycles = ReadThreadCycles();
    }

    public void BeginCpuRender() => CaptureCpuStamp(out _cpuFrame.RenderStartTicks, out _cpuFrame.RenderStartCycles);
    public void EndCpuBegin() => CaptureCpuStamp(out _cpuFrame.BeginEndTicks, out _cpuFrame.BeginEndCycles);
    public void EndCpuDraw(SceneRenderStatistics statistics)
    {
        CaptureCpuStamp(out _cpuFrame.DrawEndTicks, out _cpuFrame.DrawEndCycles);
        _cpuFrame.Statistics = statistics;
    }
    public void EndCpuPresent() => CaptureCpuStamp(out _cpuFrame.PresentEndTicks, out _cpuFrame.PresentEndCycles);

    private static void CaptureCpuStamp(out long timestamp, out ulong cycles)
    {
        cycles = ReadThreadCycles();
        timestamp = Stopwatch.GetTimestamp();
    }

    private static ulong ReadThreadCycles() =>
        OperatingSystem.IsWindows() && QueryThreadCycleTime(new IntPtr(-2), out var cycles) ? cycles : 0;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    private struct CpuSample
    {
        public long EventsStartTicks, EventsEndTicks;
        public ulong EventsStartCycles, EventsEndCycles;
        public long UpdateStartTicks, UpdateEndTicks, RenderStartTicks, BeginEndTicks, DrawEndTicks, PresentEndTicks;
        public ulong UpdateStartCycles, UpdateEndCycles, RenderStartCycles, BeginEndCycles, DrawEndCycles, PresentEndCycles;
        public SceneRenderStatistics Statistics;
    }

    private static double ReadSeconds(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0
            ? value : fallback;

    private bool InWindow(long timestamp)
    {
        var seconds = Stopwatch.GetElapsedTime(_started, timestamp).TotalSeconds;
        return seconds >= _collectFrom && seconds < _collectUntil;
    }

    public void RecordGpu(GraphicsFrameTiming timing)
    {
        if (_gpuCount == _gpuSamples.Length || timing.Sequence <= _gpuSequence ||
            !InWindow(Stopwatch.GetTimestamp())) return;
        _gpuSequence = timing.Sequence;
        _gpuSamples[_gpuCount++] = timing.Milliseconds;
    }

    public void RecordGpuPasses()
    {
        if (_gpuPassSamples.Length == 0 || GraphicsPassTimingService.Version == _gpuPassVersion) return;
        var samples = GraphicsPassTimingService.Snapshot(out var version);
        var published = version - _gpuPassVersion;
        _gpuPassVersion = version;
        if (!InWindow(Stopwatch.GetTimestamp())) return;
        // The service retains a bounded recent queue for the live profiler. Only
        // newly published entries belong to this poll; fence completion can make
        // them appear several frames after the sampled recording.
        var available = (int)Math.Min(samples.Length, published);
        _gpuPassDropped += Math.Max(0, published - samples.Length);
        foreach (var sample in samples.AsSpan(samples.Length - available))
        {
            if (_gpuPassCount == _gpuPassSamples.Length) { _gpuPassDropped++; continue; }
            _gpuPassSamples[_gpuPassCount++] = sample;
        }
    }
    private readonly record struct Sample(double At, double Wait, double Events, double Update,
        double Begin, double Draw, double Present, double Total, long Allocated, int Gen0, int Gen1, int Gen2,
        long Sequence, long StartTicks, long WaitedTicks, long EventsTicks, long UpdatedTicks,
        long EndTicks, long BeginTicks, long PresentTicks);

    public void Record(long start, long waited, long events, long updated, long rendered,
        long beginTicks, long presentTicks, long allocated)
    {
        var sequence = ++_sequence;
        var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1); var gen2 = GC.CollectionCount(2);
        var collected0 = gen0 - _gen0; var collected1 = gen1 - _gen1; var collected2 = gen2 - _gen2;
        _gen0 = gen0; _gen1 = gen1; _gen2 = gen2;
        if (_count == _samples.Length || !InWindow(start)) return;
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        var renderTicks = rendered - updated;
        if (CpuEnabled) _cpuSamples[_count] = _cpuFrame;
        if (_graphicsSamples.Length != 0) _graphicsSamples[_count] = _graphicsFrame;
        _graphicsFrame = default;
        _samples[_count++] = new(Stopwatch.GetElapsedTime(_started, start).TotalSeconds,
            Ms(waited-start), Ms(events-waited), Ms(updated-events),
            Ms(beginTicks), Ms(Math.Max(0,renderTicks-beginTicks-presentTicks)), Ms(presentTicks),
            Ms(rendered-start), allocated, collected0, collected1, collected2,
            sequence, start, waited, events, updated, rendered, beginTicks, presentTicks);
    }

    public void Report()
    {
        if (ReferenceEquals(_cpuCurrent, this)) _cpuCurrent = null;
        if (_count == 0) return;
        Debug.Log(FormattableString.Invariant($"BENGINE_NATIVE_FRAME_ORIGIN|utc={_startedUtc:O}|stopwatchTimestamp={_started}|stopwatchFrequency={Stopwatch.Frequency}|utcReadUncertaintyMs={Stopwatch.GetElapsedTime(_started, _startedUtcCaptured).TotalMilliseconds:F6}"));
        var values = new double[_count];
        void ReportMetric(string name, Func<Sample,double> select)
        {
            for (int i=0;i<_count;i++) values[i]=select(_samples[i]);
            Array.Sort(values);
            Debug.Log(FormattableString.Invariant($"BENGINE_NATIVE_FRAME_PHASE|phase={name}|samples={_count}|median={values[_count/2]:F3}|p95={values[Math.Min(_count-1,(int)(_count*.95))]:F3}|p99={values[Math.Min(_count-1,(int)(_count*.99))]:F3}|p99_9={values[Math.Min(_count-1,(int)(_count*.999))]:F3}|maximum={values[^1]:F3}"));
        }
        ReportMetric("waitMs",s=>s.Wait); ReportMetric("eventsMs",s=>s.Events);
        ReportMetric("updateMs",s=>s.Update); ReportMetric("beginMs",s=>s.Begin);
        ReportMetric("drawMs",s=>s.Draw); ReportMetric("presentMs",s=>s.Present);
        ReportMetric("totalMs",s=>s.Total); ReportMetric("allocatedBytes",s=>s.Allocated);
        // Keep correlated frame details for spikes; independent maxima cannot
        // establish whether decoding, GC and a long draw occurred together.
        // Sorting and logging happen only after the native loop has stopped.
        var indices = new int[_count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        Array.Sort(indices, (a,b) => _samples[b].Total.CompareTo(_samples[a].Total));
        for (int i = 0; i < Math.Min(20, indices.Length); i++) ReportSample("time", _samples[indices[i]]);
        Array.Sort(indices, (a,b) => _samples[b].Allocated.CompareTo(_samples[a].Allocated));
        for (int i = 0; i < Math.Min(10, indices.Length); i++) ReportSample("allocation", _samples[indices[i]]);
        if (_gpuCount > 0)
        {
            Array.Sort(_gpuSamples, 0, _gpuCount);
            Debug.Log(FormattableString.Invariant($"BENGINE_NATIVE_FRAME_PHASE|phase=gpuMs|samples={_gpuCount}|median={_gpuSamples[_gpuCount/2]:F3}|p95={_gpuSamples[Math.Min(_gpuCount-1,(int)(_gpuCount*.95))]:F3}|p99={_gpuSamples[Math.Min(_gpuCount-1,(int)(_gpuCount*.99))]:F3}|p99_9={_gpuSamples[Math.Min(_gpuCount-1,(int)(_gpuCount*.999))]:F3}|maximum={_gpuSamples[_gpuCount-1]:F3}"));
        }
        if (_gpuPassCount > 0)
        {
            var samples = _gpuPassSamples.AsSpan(0, _gpuPassCount).ToArray();
            foreach (var group in samples.GroupBy(sample => (sample.Backend, sample.Name)).OrderBy(group => group.Key.Name, StringComparer.Ordinal))
            {
                var durations = group.Select(sample => sample.Milliseconds).Order().ToArray();
                Debug.Log(FormattableString.Invariant($"BENGINE_NATIVE_GPU_PASS|backend={group.Key.Backend}|name={group.Key.Name}|samples={durations.Length}|median={durations[durations.Length/2]:F6}|p95={durations[Math.Min(durations.Length-1,(int)(durations.Length*.95))]:F6}|p99={durations[Math.Min(durations.Length-1,(int)(durations.Length*.99))]:F6}|maximum={durations[^1]:F6}"));
            }
            Debug.Log(FormattableString.Invariant($"BENGINE_NATIVE_GPU_PASS_SUMMARY|samples={_gpuPassCount}|frames={samples.Select(sample => (sample.Backend,sample.Frame)).Distinct().Count()}|dropped={_gpuPassDropped}|diagnosticOnly=True"));
        }
        // Emit every slow recorded frame after the loop, in capture order.
        // Startup spikes must not displace a later battle spike from the top-N
        // summaries. Raw monotonic boundaries also distinguish this loop's
        // Present from a render contributor's earlier EndFrame observation.
        for (var index = 0; index < _count; index++)
        {
            var sample = _samples[index];
            if (_reportAllDetails || sample.Total > 2.5)
            {
                ReportDetail(sample);
                if (CpuEnabled) ReportCpuDetail(sample, _cpuSamples[index], _nativeThreadId);
                if (_graphicsSamples.Length != 0 && _graphicsSamples[index].Frame != 0)
                    ReportGraphicsDetail(sample, _graphicsSamples[index]);
            }
        }
        _count=0;
        _gpuCount=0;
        _gpuPassCount=0;
        _gpuPassDropped=0;
    }

    private static void ReportSample(string rank, Sample s) => Debug.Log(FormattableString.Invariant(
        $"BENGINE_NATIVE_FRAME_SPIKE|rank={rank}|atSeconds={s.At:F6}|totalMs={s.Total:F3}|waitMs={s.Wait:F3}|eventsMs={s.Events:F3}|updateMs={s.Update:F3}|beginMs={s.Begin:F3}|drawMs={s.Draw:F3}|presentMs={s.Present:F3}|allocatedBytes={s.Allocated}|gen0={s.Gen0}|gen1={s.Gen1}|gen2={s.Gen2}"));

    private static void ReportDetail(Sample s) => Debug.Log(FormattableString.Invariant(
        $"BENGINE_NATIVE_FRAME_DETAIL|sequence={s.Sequence}|atSeconds={s.At:F6}|startTicks={s.StartTicks}|waitedTicks={s.WaitedTicks}|eventsTicks={s.EventsTicks}|updatedTicks={s.UpdatedTicks}|endTicks={s.EndTicks}|beginTicks={s.BeginTicks}|presentTicks={s.PresentTicks}|totalMs={s.Total:F3}|allocatedBytes={s.Allocated}|gen0={s.Gen0}|gen1={s.Gen1}|gen2={s.Gen2}"));

    private static void ReportCpuDetail(Sample s, CpuSample c, uint threadId)
    {
        var r = c.Statistics;
        Debug.Log(FormattableString.Invariant(
            $"BENGINE_NATIVE_CPU_FRAME|sequence={s.Sequence}|threadId={threadId}|cycleUnit=cpuCycles|eventsStartTicks={c.EventsStartTicks}|eventsEndTicks={c.EventsEndTicks}|eventsStartCycles={c.EventsStartCycles}|eventsEndCycles={c.EventsEndCycles}|updateStartTicks={c.UpdateStartTicks}|updateEndTicks={c.UpdateEndTicks}|renderStartTicks={c.RenderStartTicks}|beginEndTicks={c.BeginEndTicks}|drawEndTicks={c.DrawEndTicks}|presentEndTicks={c.PresentEndTicks}|updateStartCycles={c.UpdateStartCycles}|updateEndCycles={c.UpdateEndCycles}|renderStartCycles={c.RenderStartCycles}|beginEndCycles={c.BeginEndCycles}|drawEndCycles={c.DrawEndCycles}|presentEndCycles={c.PresentEndCycles}|collectMs={r.CollectMilliseconds:F6}|coreCollectMs={r.CoreCollectMilliseconds:F6}|extensionCollectMs={r.ExtensionCollectMilliseconds:F6}|sortMs={r.SortMilliseconds:F6}|geometryMs={r.GeometryMilliseconds:F6}|submitMs={r.SubmitMilliseconds:F6}|cameraCount={r.CameraCount}|submissionCount={r.VisibleSubmissionCount}|batchCount={r.BatchCount}|drawCalls={r.DrawCallCount}|geometryUploadBytes={r.CoreGeometryUploadBytes}"));
    }

    private static void ReportGraphicsDetail(Sample s, GraphicsCpuFrameTiming t)
    {
        static double Ms(long start, long end) => start == 0 || end == 0 || end < start
            ? 0 : (end - start) * 1000d / Stopwatch.Frequency;
        Debug.Log(FormattableString.Invariant(
            $"BENGINE_NATIVE_RHI_FRAME|sequence={s.Sequence}|frame={t.Frame}|clock=stopwatch|acquireStartTicks={t.AcquireStartTicks}|acquireEndTicks={t.AcquireEndTicks}|frameFenceEndTicks={t.FrameFenceEndTicks}|retireEndTicks={t.RetireEndTicks}|resizeEndTicks={t.ResizeEndTicks}|recordingEndTicks={t.RecordingEndTicks}|presentStartTicks={t.PresentStartTicks}|submitStartTicks={t.SubmitStartTicks}|submitEndTicks={t.SubmitEndTicks}|queriesEndTicks={t.QueriesEndTicks}|swapStartTicks={t.SwapStartTicks}|swapEndTicks={t.SwapEndTicks}|acquireMs={Ms(t.AcquireStartTicks, t.AcquireEndTicks):F3}|frameFenceWaitMs={Ms(t.AcquireEndTicks, t.FrameFenceEndTicks):F3}|retireMs={Ms(t.FrameFenceEndTicks, t.RetireEndTicks):F3}|resizeMs={Ms(t.RetireEndTicks, t.ResizeEndTicks):F3}|recordMs={Ms(t.ResizeEndTicks, t.RecordingEndTicks):F3}|submitMs={Ms(t.SubmitStartTicks, t.SubmitEndTicks):F3}|queriesMs={Ms(t.SubmitEndTicks, t.QueriesEndTicks):F3}|swapMs={Ms(t.SwapStartTicks, t.SwapEndTicks):F3}|retiredResources={t.RetiredResources}|pendingRetirements={t.PendingRetirements}"));
    }
}
