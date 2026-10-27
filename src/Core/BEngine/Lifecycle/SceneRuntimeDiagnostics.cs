using System.Diagnostics;

namespace BEngine;

/// <summary>Opt-in CPU sampling for the runtime thread. Disabled sampling reads no clock or allocation counter.</summary>
public static class SceneRuntimeDiagnostics
{
    private static bool _enabled, _capturing;
    private static long _sequence, _started, _allocated;
    private static SceneRuntimeFrameStatistics _current;
    private static readonly long[] PhaseTicks = new long[(int)SceneRuntimePhase.Count];
    private static readonly Dictionary<(string Type, string Callback), int> CallbackIndices = new();
    private static readonly List<SceneRuntimeCallbackStatistics> CallbackSamples = [];
    private static KernelRow[]? _kernelRows;
    private static long _kernelGeneration, _kernelClockReads, _kernelAllocationReads;
    private static KernelPhase _kernelPhase;
    internal static long KernelClockReadCount => _kernelClockReads;
    internal static long KernelAllocationReadCount => _kernelAllocationReads;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value; _capturing = false;
            _kernelGeneration++;
            if (value) LastFrame = default;
        }
    }

    /// <summary>The last completed sampled host frame; timings include user callbacks and runtime systems.</summary>
    public static SceneRuntimeFrameStatistics LastFrame { get; private set; }

    internal static void BeginFrame(long sequence)
    {
        if (!_enabled) return;
        _sequence = sequence; _capturing = true;
        _current = new() { FrameCount = Time.frameCount };
        Array.Clear(PhaseTicks);
        CallbackIndices.Clear();
        CallbackSamples.Clear();
        _kernelRows ??= new KernelRow[(int)KernelKind.Count * (int)KernelPhase.Count];
        Array.Clear(_kernelRows);
        _kernelGeneration++;
        _kernelPhase = KernelPhase.Other;
        _allocated = GC.GetAllocatedBytesForCurrentThread();
        _started = Stopwatch.GetTimestamp();
    }

    internal static void EndFrame(long sequence)
    {
        if (!_capturing || sequence != _sequence) return;
        var ended = Stopwatch.GetTimestamp();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - _allocated;
        _capturing = false;
        var milliseconds = 1000d / Stopwatch.Frequency;
        LastFrame = _current with
        {
            TotalMilliseconds = (ended - _started) * milliseconds,
            AllocatedBytes = bytes,
            FrameBeginSystemsMilliseconds = PhaseTicks[(int)SceneRuntimePhase.FrameBeginSystems] * milliseconds,
            FrameEndSystemsMilliseconds = PhaseTicks[(int)SceneRuntimePhase.FrameEndSystems] * milliseconds,
            LifecycleBeginMilliseconds = PhaseTicks[(int)SceneRuntimePhase.LifecycleBegin] * milliseconds,
            BehaviourStartMilliseconds = PhaseTicks[(int)SceneRuntimePhase.BehaviourStart] * milliseconds,
            BeforeUpdateSystemsMilliseconds = PhaseTicks[(int)SceneRuntimePhase.BeforeUpdateSystems] * milliseconds,
            FixedUpdateMilliseconds = PhaseTicks[(int)SceneRuntimePhase.FixedUpdate] * milliseconds,
            BehaviourUpdateMilliseconds = PhaseTicks[(int)SceneRuntimePhase.BehaviourUpdate] * milliseconds,
            UpdateSystemsMilliseconds = PhaseTicks[(int)SceneRuntimePhase.UpdateSystems] * milliseconds,
            LateUpdateMilliseconds = PhaseTicks[(int)SceneRuntimePhase.LateUpdate] * milliseconds,
            LifecycleEndMilliseconds = PhaseTicks[(int)SceneRuntimePhase.LifecycleEnd] * milliseconds,
            Callbacks = CallbackSamples.ToArray(),
            Kernels = SnapshotKernels()
        };
    }

    // Fixed slots retain no caller objects. Inclusive samples also include any
    // nested kernel, and are diagnostic costs rather than additive CPU phases.
    internal enum KernelKind { CircleSeparationSolve, CircleMovementMove, PolylineCapsuleHit, RectangleSegmentClear, CircleSeparationPairBuild, CircleSeparationPairResolution, CircleSeparationApplyCorrections, Count }
    private enum KernelPhase { Other, FixedUpdate, Update, Count }
    private struct KernelRow { internal long Calls, Ticks, AllocatedBytes; }

    internal static KernelMeasurement MeasureKernel(KernelKind kernel)
    {
        if (!_capturing) return default;
        var slot = (int)kernel * (int)KernelPhase.Count + (int)_kernelPhase;
        _kernelAllocationReads++;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        _kernelClockReads++;
        return new(_kernelGeneration, slot, Stopwatch.GetTimestamp(), allocated);
    }

    internal readonly struct KernelMeasurement(long generation, int slot, long started, long allocated) : IDisposable
    {
        public void Dispose()
        {
            if (started == 0 || !_capturing || generation != _kernelGeneration) return;
            _kernelClockReads++;
            var ended = Stopwatch.GetTimestamp();
            _kernelAllocationReads++;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            ref var row = ref _kernelRows![slot];
            row.Calls++;
            row.Ticks += ended - started;
            row.AllocatedBytes += bytes;
        }
    }

    private static SceneRuntimeKernelStatistics[] SnapshotKernels()
    {
        var count = 0;
        foreach (var row in _kernelRows!) if (row.Calls != 0) count++;
        if (count == 0) return [];
        var samples = new SceneRuntimeKernelStatistics[count];
        var next = 0;
        for (var slot = 0; slot < _kernelRows!.Length; slot++)
        {
            var row = _kernelRows[slot];
            if (row.Calls == 0) continue;
            samples[next++] = new()
            {
                Kernel = (KernelKind)(slot / (int)KernelPhase.Count) switch
                {
                    KernelKind.CircleSeparationSolve => "FixedCircleSeparationWorkspace2D.Solve",
                    KernelKind.CircleMovementMove => "FixedCircleMovement2D.Move",
                    KernelKind.PolylineCapsuleHit => "FixedPolylineQueries2D.TryCapsuleHit",
                    KernelKind.RectangleSegmentClear => "FixedRectangleQueries2D.IsSegmentClearWithinBounds",
                    KernelKind.CircleSeparationPairBuild => "FixedCircleSeparationWorkspace2D.PairBuild",
                    KernelKind.CircleSeparationPairResolution => "FixedCircleSeparationWorkspace2D.PairResolution",
                    _ => "FixedCircleSeparationWorkspace2D.ApplyCorrections"
                },
                Phase = (KernelPhase)(slot % (int)KernelPhase.Count) switch
                {
                    KernelPhase.FixedUpdate => "FixedUpdate",
                    KernelPhase.Update => "Update",
                    _ => "Other"
                },
                Calls = row.Calls, Ticks = row.Ticks,
                Milliseconds = row.Ticks * (1000d / Stopwatch.Frequency),
                AllocatedBytes = row.AllocatedBytes
            };
        }
        return samples;
    }

    internal static TickSample BeginTick(int systems)
    {
        if (!_capturing) return default;
        _current = _current with { SceneCount = _current.SceneCount + 1, SceneSystemCount = _current.SceneSystemCount + systems };
        var sample = new TickSample(_sequence);
        _kernelPhase = KernelPhase.Other;
        return sample;
    }

    internal static void RecordDispatchCount(int count) =>
        _current = _current with { EnabledBehaviourCount = _current.EnabledBehaviourCount + count };
    internal static void RecordFixedStep() => _current = _current with { FixedStepCount = _current.FixedStepCount + 1 };
    internal static void RecordFixedCallback() => _current = _current with { FixedUpdateCalls = _current.FixedUpdateCalls + 1 };
    internal static void RecordUpdateCallback() => _current = _current with { BehaviourUpdateCalls = _current.BehaviourUpdateCalls + 1 };
    internal static void RecordLateCallback() => _current = _current with { LateUpdateCalls = _current.LateUpdateCalls + 1 };

    internal static long StartFrameSystems() => _capturing ? Stopwatch.GetTimestamp() : 0;
    internal static long StartCallback() => _capturing ? Stopwatch.GetTimestamp() : 0;
    internal static void EndCallback(long started, object target, string callback)
    {
        if (started == 0 || !_capturing) return;
        var ended = Stopwatch.GetTimestamp();
        var type = target.GetType().FullName ?? target.GetType().Name;
        var key = (type, callback);
        if (!CallbackIndices.TryGetValue(key, out var index))
        {
            // Diagnostic storage remains bounded even for generated behaviour types.
            if (CallbackSamples.Count >= 256) return;
            index = CallbackSamples.Count;
            CallbackIndices.Add(key, index);
            CallbackSamples.Add(new() { Type = type, Callback = callback });
        }
        var previous = CallbackSamples[index];
        CallbackSamples[index] = previous with
        {
            Calls = previous.Calls + 1,
            Milliseconds = previous.Milliseconds + (ended - started) * (1000d / Stopwatch.Frequency)
        };
    }
    internal static void EndFrameSystems(long started, bool begin)
    {
        if (started != 0 && _capturing)
            PhaseTicks[(int)(begin ? SceneRuntimePhase.FrameBeginSystems : SceneRuntimePhase.FrameEndSystems)] += Stopwatch.GetTimestamp() - started;
    }

    internal struct TickSample(long sequence)
    {
        private readonly long _sequence = sequence;
        private long _started = Stopwatch.GetTimestamp();
        private SceneRuntimePhase _phase = SceneRuntimePhase.LifecycleBegin;
        private readonly KernelPhase _previousKernelPhase = _kernelPhase;

        internal void MoveTo(SceneRuntimePhase next)
        {
            if (!_capturing || _sequence != SceneRuntimeDiagnostics._sequence) return;
            var ended = Stopwatch.GetTimestamp();
            PhaseTicks[(int)_phase] += ended - _started;
            _phase = next; _started = ended;
            _kernelPhase = next switch
            {
                SceneRuntimePhase.FixedUpdate => KernelPhase.FixedUpdate,
                SceneRuntimePhase.BehaviourUpdate or SceneRuntimePhase.UpdateSystems => KernelPhase.Update,
                _ => KernelPhase.Other
            };
        }

        internal void Complete()
        {
            if (_capturing && _sequence == SceneRuntimeDiagnostics._sequence)
            {
                PhaseTicks[(int)_phase] += Stopwatch.GetTimestamp() - _started;
                _kernelPhase = _previousKernelPhase;
            }
        }
    }
}

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

internal enum SceneRuntimePhase
{
    FrameBeginSystems, FrameEndSystems, LifecycleBegin, BehaviourStart,
    BeforeUpdateSystems, FixedUpdate, BehaviourUpdate, UpdateSystems, LateUpdate, LifecycleEnd, Count
}
