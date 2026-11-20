using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>Optional runtime-thread sampling. Unregistered sampling performs no clock reads or allocations.</summary>
public static class SceneRuntimeDiagnostics
{
    private static Registration? _active;
    private static bool _requested;
    public static bool Enabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _requested && _active?.Recorder is not null;
        set { _requested = value; if (_active?.Recorder is { } recorder) recorder.Enabled = value; }
    }
    public static SceneRuntimeFrameStatistics LastFrame => _active?.Recorder?.LastFrame ?? default;
    public static IDisposable RegisterRecorder(ISceneRuntimeRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var registration = new Registration(recorder, _active);
        recorder.Enabled = _requested;
        _active = registration;
        return registration;
    }
    internal static void BeginFrame(long sequence) { if (Enabled) _active?.Recorder?.BeginFrame(sequence); }
    internal static void EndFrame(long sequence) { if (Enabled) _active?.Recorder?.EndFrame(sequence); }
    internal static TickSample BeginTick(int systems)
    {
        var registration = _requested ? _active : null;
        return registration?.Recorder is { } recorder ? new(registration, recorder.BeginTick(systems)) : default;
    }
    internal static void RecordDispatchCount(int count) => _active?.Recorder?.RecordDispatchCount(count);
    internal static void RecordFixedStep() => _active?.Recorder?.RecordFixedStep();
    internal static void RecordFixedCallback() => _active?.Recorder?.RecordFixedCallback();
    internal static void RecordUpdateCallback() => _active?.Recorder?.RecordUpdateCallback();
    internal static void RecordLateCallback() => _active?.Recorder?.RecordLateCallback();
    internal static long StartFrameSystems() => Enabled ? _active?.Recorder?.StartFrameSystems() ?? 0 : 0;
    internal static long StartCallback() => Enabled ? _active?.Recorder?.StartCallback() ?? 0 : 0;
    internal static void EndCallback(long started, object target, string callback)
    { if (Enabled) _active?.Recorder?.EndCallback(started, target, callback); }
    internal static void EndFrameSystems(long started, bool begin)
    { if (Enabled) _active?.Recorder?.EndFrameSystems(started, begin); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KernelMeasurement MeasureKernel(string name)
    {
        var registration = _requested ? _active : null;
        return registration?.Recorder is { } recorder ? new(registration, recorder.BeginKernel(name)) : default;
    }
    public readonly struct KernelMeasurement : IDisposable
    {
        private readonly Registration? _registration;
        private readonly SceneRuntimeKernelToken _token;
        internal KernelMeasurement(Registration registration, SceneRuntimeKernelToken token)
        { _registration = registration; _token = token; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _registration?.Recorder?.EndKernel(_token);
    }
    internal struct TickSample(Registration registration, SceneRuntimeTickToken token)
    {
        private SceneRuntimeTickToken _token = token;
        internal void MoveTo(SceneRuntimePhase phase) => registration?.Recorder?.MoveTick(ref _token, phase);
        internal void Complete() => registration?.Recorder?.CompleteTick(ref _token);
    }
    internal sealed class Registration(ISceneRuntimeRecorder recorder, Registration? previous) : IDisposable
    {
        internal ISceneRuntimeRecorder? Recorder = recorder;
        private readonly Registration? _previous = previous;
        public void Dispose()
        {
            var recorder = Recorder;
            Recorder = null;
            if (recorder is not null) recorder.Enabled = false;
            var active = _active;
            while (active is not null && active.Recorder is null) active = active._previous;
            _active = active;
            if (active?.Recorder is { } restored) restored.Enabled = _requested;
        }
    }
}

/// <summary>Implemented by an optional recorder; all methods run on the runtime thread.</summary>
public interface ISceneRuntimeRecorder
{
    bool Enabled { get; set; }
    SceneRuntimeFrameStatistics LastFrame { get; }
    void BeginFrame(long sequence);
    void EndFrame(long sequence);
    SceneRuntimeTickToken BeginTick(int systems);
    void MoveTick(ref SceneRuntimeTickToken token, SceneRuntimePhase phase);
    void CompleteTick(ref SceneRuntimeTickToken token);
    void RecordDispatchCount(int count);
    void RecordFixedStep();
    void RecordFixedCallback();
    void RecordUpdateCallback();
    void RecordLateCallback();
    long StartFrameSystems();
    long StartCallback();
    void EndCallback(long started, object target, string callback);
    void EndFrameSystems(long started, bool begin);
    SceneRuntimeKernelToken BeginKernel(string name);
    void EndKernel(SceneRuntimeKernelToken token);
}
public struct SceneRuntimeTickToken
{
    public long Sequence, Started;
    public SceneRuntimePhase Phase;
    public int PreviousKernelPhase;
}
/// <summary>The recorder owns Cookie's meaning; Core only transports this allocation-free token.</summary>
public readonly record struct SceneRuntimeKernelToken(long Cookie, long Started, long Allocated);

/// <summary>CPU costs for one sampled SceneRuntime host frame, summed across its scene runtimes.</summary>
public readonly record struct SceneRuntimeFrameStatistics
{
    public long FrameCount { get; init; }
    public int SceneCount { get; init; }
    public int SceneSystemCount { get; init; }
    public int EnabledBehaviourCount { get; init; }
    public int FixedStepCount { get; init; }
    public int BehaviourUpdateCalls { get; init; }
    public int FixedUpdateCalls { get; init; }
    public int LateUpdateCalls { get; init; }
    public long AllocatedBytes { get; init; }
    public double TotalMilliseconds { get; init; }
    public double FrameBeginSystemsMilliseconds { get; init; }
    public double FrameEndSystemsMilliseconds { get; init; }
    public double LifecycleBeginMilliseconds { get; init; }
    public double BehaviourStartMilliseconds { get; init; }
    public double BeforeUpdateSystemsMilliseconds { get; init; }
    public double FixedUpdateMilliseconds { get; init; }
    public double BehaviourUpdateMilliseconds { get; init; }
    public double UpdateSystemsMilliseconds { get; init; }
    public double LateUpdateMilliseconds { get; init; }
    public double LifecycleEndMilliseconds { get; init; }
    public SceneRuntimeCallbackStatistics[]? Callbacks { get; init; }
    public SceneRuntimeKernelStatistics[]? Kernels { get; init; }
}

/// <summary>Inclusive entry costs for an enabled runtime capture, grouped by FixedUpdate, Update or Other.
/// Instrumentation overhead is included; nested kernel costs must not be summed as independent phases.</summary>
public readonly record struct SceneRuntimeKernelStatistics
{
    public string Kernel { get; init; }
    public string Phase { get; init; }
    public long Calls { get; init; }
    public long Ticks { get; init; }
    public double Milliseconds { get; init; }
    public long AllocatedBytes { get; init; }
}

/// <summary>Aggregated opt-in callback CPU samples, without retaining runtime objects or their assemblies.</summary>
public readonly record struct SceneRuntimeCallbackStatistics
{
    public string Type { get; init; }
    public string Callback { get; init; }
    public int Calls { get; init; }
    public double Milliseconds { get; init; }
}

public enum SceneRuntimePhase
{
    FrameBeginSystems, FrameEndSystems, LifecycleBegin, BehaviourStart,
    BeforeUpdateSystems, FixedUpdate, BehaviourUpdate, UpdateSystems, LateUpdate, LifecycleEnd, Count
}
