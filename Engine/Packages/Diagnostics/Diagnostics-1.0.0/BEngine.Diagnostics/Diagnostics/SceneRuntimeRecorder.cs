using System.Diagnostics;

namespace BEngine.Diagnostics;

/// <summary>Opt-in CPU sampling for the runtime thread. Disabled sampling reads no clock or allocation counter.</summary>
internal sealed class SceneRuntimeRecorder : ISceneRuntimeRecorder
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        var registration = SceneRuntimeDiagnostics.RegisterRecorder(new SceneRuntimeRecorder());
        RuntimeAssemblyCleanup.Register(assembly =>
        {
            if (assembly == typeof(SceneRuntimeRecorder).Assembly) registration.Dispose();
        });
    }

    private bool _enabled, _capturing;
    private long _sequence, _started, _allocated;
    private SceneRuntimeFrameStatistics _current;
    private readonly long[] PhaseTicks = new long[(int)SceneRuntimePhase.Count];
    private readonly Dictionary<(string Type, string Callback), int> CallbackIndices = new();
    private readonly List<SceneRuntimeCallbackStatistics> CallbackSamples = [];
    private KernelRow[]? _kernelRows;
    private long _kernelGeneration, _kernelClockReads, _kernelAllocationReads;
    // The bounded recorder has 256 names in three phases. The remaining bits
    // identify the capture, keeping the generic token at three machine words.
    private const int KernelSlotBits = 10;
    private const long KernelSlotMask = (1L << KernelSlotBits) - 1;
    private KernelPhase _kernelPhase;
    public long KernelClockReadCount => _kernelClockReads;
    public long KernelAllocationReadCount => _kernelAllocationReads;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value; _capturing = false;
            _kernelGeneration = unchecked(_kernelGeneration + (1L << KernelSlotBits));
            if (value) LastFrame = default;
        }
    }

    /// <summary>The last completed sampled host frame; timings include user callbacks and runtime systems.</summary>
    public SceneRuntimeFrameStatistics LastFrame { get; private set; }

    public void BeginFrame(long sequence)
    {
        if (!_enabled) return;
        _sequence = sequence; _capturing = true;
        _current = new() { FrameCount = Time.frameCount };
        Array.Clear(PhaseTicks);
        CallbackIndices.Clear();
        CallbackSamples.Clear();
        _kernelRows ??= new KernelRow[256 * (int)KernelPhase.Count];
        Array.Clear(_kernelRows);
        _kernelGeneration = unchecked(_kernelGeneration + (1L << KernelSlotBits));
        _kernelPhase = KernelPhase.Other;
        _allocated = GC.GetAllocatedBytesForCurrentThread();
        _started = Stopwatch.GetTimestamp();
    }

    public void EndFrame(long sequence)
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

    private enum KernelPhase { Other, FixedUpdate, Update, Count }
    private struct KernelRow { internal long Calls, Ticks, AllocatedBytes; }
    private readonly Dictionary<string,int> _kernelIndices = new(StringComparer.Ordinal);
    private readonly List<string> _kernelNames = [];
    public SceneRuntimeKernelToken BeginKernel(string name)
    {
        if (!_capturing) return default;
        if (!_kernelIndices.TryGetValue(name, out var index))
        {
            if (_kernelNames.Count >= 256) return default;
            _kernelIndices.Add(name, index = _kernelNames.Count);
            _kernelNames.Add(name);
        }
        var slot = index * (int)KernelPhase.Count + (int)_kernelPhase;
        _kernelAllocationReads++;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        _kernelClockReads++;
        return new(_kernelGeneration | (uint)slot, Stopwatch.GetTimestamp(), allocated);
    }
    public void EndKernel(SceneRuntimeKernelToken token)
    {
        if (token.Started == 0 || !_capturing || (token.Cookie & ~KernelSlotMask) != _kernelGeneration) return;
        var slot = (int)(token.Cookie & KernelSlotMask);
        if ((uint)slot >= (uint)_kernelRows!.Length) return;
        _kernelClockReads++;
        var ended = Stopwatch.GetTimestamp();
        _kernelAllocationReads++;
        ref var row = ref _kernelRows[slot];
        row.Calls++;
        row.Ticks += ended - token.Started;
        row.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - token.Allocated;
    }

    private SceneRuntimeKernelStatistics[] SnapshotKernels()
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
                Kernel = _kernelNames[slot / (int)KernelPhase.Count],
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

    public SceneRuntimeTickToken BeginTick(int systems)
    {
        if (!_capturing) return default;
        _current = _current with { SceneCount = _current.SceneCount + 1, SceneSystemCount = _current.SceneSystemCount + systems };
        var sample = new SceneRuntimeTickToken { Sequence = _sequence, Started = Stopwatch.GetTimestamp(), Phase = SceneRuntimePhase.LifecycleBegin, PreviousKernelPhase = (int)_kernelPhase };
        _kernelPhase = KernelPhase.Other;
        return sample;
    }

    public void RecordDispatchCount(int count) =>
        _current = _current with { EnabledBehaviourCount = _current.EnabledBehaviourCount + count };
    public void RecordFixedStep() => _current = _current with { FixedStepCount = _current.FixedStepCount + 1 };
    public void RecordFixedCallback() => _current = _current with { FixedUpdateCalls = _current.FixedUpdateCalls + 1 };
    public void RecordUpdateCallback() => _current = _current with { BehaviourUpdateCalls = _current.BehaviourUpdateCalls + 1 };
    public void RecordLateCallback() => _current = _current with { LateUpdateCalls = _current.LateUpdateCalls + 1 };

    public long StartFrameSystems() => _capturing ? Stopwatch.GetTimestamp() : 0;
    public long StartCallback() => _capturing ? Stopwatch.GetTimestamp() : 0;
    public void EndCallback(long started, object target, string callback)
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
    public void EndFrameSystems(long started, bool begin)
    {
        if (started != 0 && _capturing)
            PhaseTicks[(int)(begin ? SceneRuntimePhase.FrameBeginSystems : SceneRuntimePhase.FrameEndSystems)] += Stopwatch.GetTimestamp() - started;
    }

    public void MoveTick(ref SceneRuntimeTickToken token, SceneRuntimePhase next)
    {
        if (!_capturing || token.Sequence != _sequence) return;
        var ended = Stopwatch.GetTimestamp();
        PhaseTicks[(int)token.Phase] += ended - token.Started;
        token.Phase = next; token.Started = ended;
        _kernelPhase = next switch
        {
            SceneRuntimePhase.FixedUpdate => KernelPhase.FixedUpdate,
            SceneRuntimePhase.BehaviourUpdate or SceneRuntimePhase.UpdateSystems => KernelPhase.Update,
            _ => KernelPhase.Other
        };
    }
    public void CompleteTick(ref SceneRuntimeTickToken token)
    {
        if (token.Started == 0 || !_capturing || token.Sequence != _sequence) return;
        PhaseTicks[(int)token.Phase] += Stopwatch.GetTimestamp() - token.Started;
        _kernelPhase = (KernelPhase)token.PreviousKernelPhase;
        token.Started = 0;
    }
}
