using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Rendering;

namespace BEngine.Editor;

/// <summary>
/// Thread-safe, fixed-capacity editor profiler. Recording scopes are allocation-free after startup;
/// snapshots intentionally copy the ring so consumers cannot mutate live history.
/// </summary>
public static class EditorProfiler
{
    public const int DefaultHistoryCapacity = 300;
    public const int MaximumHistoryCapacity = 10_000;

    private static readonly Lock Gate = new();
    private static EditorProfilerFrame[] _frames = new EditorProfilerFrame[DefaultHistoryCapacity];
    private static int _historyStart;
    private static int _historyCount;
    private static int _recording;
    private static long _version;
    private static long _tokenSequence;
    private static long _activeFrameToken;
    private static long _nextFrameIndex;
    private static ActiveFrame _activeFrame;
    private static Process? _currentProcess;
    [ThreadStatic] private static Stack<ActiveMethod>? s_methodStack;

    /// <summary>Captures full managed call stacks for method markers and allocations.</summary>
    public static bool CaptureCallStacks { get; set; } = true;

    public static bool Recording
    {
        get => Volatile.Read(ref _recording) != 0;
        set
        {
            var requested = value ? 1 : 0;
            lock (Gate)
            {
                if (_recording == requested) return;
                Volatile.Write(ref _recording, requested);
                CancelActiveFrame();
                _version++;
            }
        }
    }

    public static int HistoryCapacity
    {
        get
        {
            lock (Gate) return _frames.Length;
        }
        set
        {
            if (value is < 1 or > MaximumHistoryCapacity)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"Profiler history capacity must be between 1 and {MaximumHistoryCapacity}.");
            lock (Gate)
            {
                if (_frames.Length == value) return;
                _frames = new EditorProfilerFrame[value];
                _historyStart = 0;
                _historyCount = 0;
                _nextFrameIndex = 0;
                CancelActiveFrame();
                _version++;
            }
        }
    }

    public static long Version => Volatile.Read(ref _version);

    /// <summary>Starts one host frame. Dispose the returned scope after presentation.</summary>
    public static EditorProfilerFrameScope BeginFrame()
    {
        if (Volatile.Read(ref _recording) == 0) return default;
        lock (Gate)
        {
            if (_recording == 0) return default;
            CancelActiveFrame();
            var token = ++_tokenSequence;
            _activeFrameToken = token;
            _activeFrame = new ActiveFrame
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                StartedTimestamp = Stopwatch.GetTimestamp(),
                AllocatedBytesAtStart = GC.GetTotalAllocatedBytes(false),
                Gen0CollectionsAtStart = GC.CollectionCount(0),
                Gen1CollectionsAtStart = GC.CollectionCount(1),
                Gen2CollectionsAtStart = GC.CollectionCount(2)
            };
            return new EditorProfilerFrameScope(token);
        }
    }

    /// <summary>Measures one top-level phase in the active frame.</summary>
    public static EditorProfilerSampleScope BeginSample(EditorProfilerArea area)
    {
        if (Volatile.Read(ref _recording) == 0) return default;
        if ((uint)area > (uint)EditorProfilerArea.Present)
            throw new ArgumentOutOfRangeException(nameof(area));
        lock (Gate)
        {
            if (_recording == 0 || _activeFrameToken == 0) return default;
            return new EditorProfilerSampleScope(
                _activeFrameToken, area, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>
    /// Profiles the calling method. Project and package code can use this without depending on
    /// internal editor types; Runtime is the default domain for user code.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static EditorProfilerMethodScope BeginMethodSample(
        EditorProfilerDomain domain = EditorProfilerDomain.Runtime)
    {
        if (Volatile.Read(ref _recording) == 0) return default;
        var trace = new StackTrace(1, false);
        var method = trace.GetFrame(0)?.GetMethod();
        return BeginMethodSampleCore(method?.DeclaringType, method?.Name ?? "Unknown", domain, trace);
    }

    /// <summary>Profiles an explicitly named method and records its nested parent marker.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static EditorProfilerMethodScope BeginMethodSample(
        Type declaringType,
        string methodName,
        EditorProfilerDomain domain)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (Volatile.Read(ref _recording) == 0) return default;
        var trace = CaptureCallStacks ? new StackTrace(1, false) : null;
        return BeginMethodSampleCore(declaringType, methodName, domain, trace);
    }

    /// <summary>Reports the current value of a counter registered by a profiler module.</summary>
    public static void ReportCounter(string moduleId, string counterName, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(counterName);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (Volatile.Read(ref _recording) == 0) return;
        lock (Gate)
        {
            if (_recording == 0 || _activeFrameToken == 0) return;
            _activeFrame.Counters ??= new Dictionary<CounterKey, double>();
            _activeFrame.Counters[new CounterKey(moduleId, counterName)] = value;
        }
    }

    internal static void ReportRuntimeMethodSample(BEngine.Profiling.ProfilerSample sample)
    {
        if (Volatile.Read(ref _recording) == 0 || sample.token <= 0) return;
        var ticks = sample.elapsedMilliseconds <= 0
            ? 0
            : Math.Max(1, (long)Math.Round(sample.elapsedMilliseconds * Stopwatch.Frequency / 1000d));
        lock (Gate)
        {
            if (_recording == 0 || _activeFrameToken == 0) return;
            _activeFrame.ExternalMethodIds ??= [];
            var id = ExternalMethodId(sample.token);
            var parentId = sample.parentToken > 0 ? ExternalMethodId(sample.parentToken) : 0;
            _activeFrame.MethodSamples ??= [];
            _activeFrame.MethodSamples.Add(new PendingMethodSample(
                id, parentId, EditorProfilerDomain.Runtime, sample.category, sample.name, ticks,
                Math.Max(0, sample.allocatedBytes), sample.threadId, sample.threadName,
                sample.callStack ?? []));
        }

        int ExternalMethodId(long token)
        {
            if (_activeFrame.ExternalMethodIds!.TryGetValue(token, out var existing)) return existing;
            var created = ++_activeFrame.NextMethodId;
            _activeFrame.ExternalMethodIds[token] = created;
            return created;
        }
    }

    private static EditorProfilerMethodScope BeginMethodSampleCore(
        Type? declaringType,
        string methodName,
        EditorProfilerDomain domain,
        StackTrace? trace)
    {
        var callStack = CaptureCallStacks && trace is not null
            ? FormatCallStack(trace)
            : [];
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        var startedTimestamp = Stopwatch.GetTimestamp();
        lock (Gate)
        {
            if (_recording == 0 || _activeFrameToken == 0) return default;
            var stack = s_methodStack ??= new Stack<ActiveMethod>();
            while (stack.Count > 0 && stack.Peek().FrameToken != _activeFrameToken) stack.Pop();
            var parentId = stack.Count > 0 ? stack.Peek().Id : 0;
            var id = ++_activeFrame.NextMethodId;
            var active = new ActiveMethod(_activeFrameToken, id);
            stack.Push(active);
            return new EditorProfilerMethodScope(
                _activeFrameToken,
                id,
                parentId,
                domain,
                declaringType?.FullName ?? declaringType?.Name ?? string.Empty,
                methodName,
                startedTimestamp,
                allocatedBytes,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name ?? $"Thread {Environment.CurrentManagedThreadId}",
                callStack);
        }
    }

    /// <summary>Adds a duration measured by an external host or native frame pump.</summary>
    public static void ReportSample(EditorProfilerArea area, double milliseconds)
    {
        if (Volatile.Read(ref _recording) == 0) return;
        if ((uint)area > (uint)EditorProfilerArea.Present)
            throw new ArgumentOutOfRangeException(nameof(area));
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var ticks = milliseconds <= 0
            ? 0
            : Math.Max(1, (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000d));
        lock (Gate)
        {
            if (_recording == 0 || _activeFrameToken == 0) return;
            AddSampleTicks(area, ticks);
        }
    }

    /// <summary>
    /// Adds one scene-render result to the active frame. Multiple viewports are accumulated; target
    /// dimensions are those of the most recently reported viewport.
    /// </summary>
    public static void ReportRenderStatistics(SceneRenderStatistics statistics)
    {
        if (Volatile.Read(ref _recording) == 0) return;
        lock (Gate)
        {
            if (_recording == 0 || _activeFrameToken == 0) return;
            if (!_activeFrame.HasRenderStatistics)
            {
                _activeFrame.RenderStatistics = statistics;
                _activeFrame.HasRenderStatistics = true;
                return;
            }

            var current = _activeFrame.RenderStatistics;
            _activeFrame.RenderStatistics = new SceneRenderStatistics(
                current.CameraCount + statistics.CameraCount,
                current.VisibleSubmissionCount + statistics.VisibleSubmissionCount,
                current.BatchCount + statistics.BatchCount,
                current.DrawCallCount + statistics.DrawCallCount,
                current.VertexCount + statistics.VertexCount,
                current.TriangleCount + statistics.TriangleCount,
                current.LineCount + statistics.LineCount,
                statistics.TargetWidth,
                statistics.TargetHeight,
                current.HasCompleteDrawStatistics && statistics.HasCompleteDrawStatistics);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Array.Clear(_frames);
            _historyStart = 0;
            _historyCount = 0;
            _nextFrameIndex = 0;
            CancelActiveFrame();
            _version++;
        }
    }

    public static EditorProfilerSnapshot GetSnapshot()
    {
        lock (Gate)
        {
            var frames = new EditorProfilerFrame[_historyCount];
            for (var index = 0; index < _historyCount; index++)
                frames[index] = _frames[(_historyStart + index) % _frames.Length];
            return new EditorProfilerSnapshot(
                _version, _recording != 0, _frames.Length, frames);
        }
    }

    /// <summary>Reads the profiler state atomically without copying history or allocating.</summary>
    public static EditorProfilerMetadata GetMetadata()
    {
        lock (Gate)
            return new EditorProfilerMetadata(
                _version, _recording != 0, _frames.Length, _historyCount);
    }

    /// <summary>
    /// Copies the newest frames into caller-owned storage without allocating. Frames are returned
    /// oldest-to-newest; when the destination is shorter than the history, the oldest frames are
    /// omitted.
    /// </summary>
    public static int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out EditorProfilerMetadata metadata)
    {
        lock (Gate)
        {
            metadata = new EditorProfilerMetadata(
                _version, _recording != 0, _frames.Length, _historyCount);
            var copyCount = Math.Min(destination.Length, _historyCount);
            var sourceOffset = _historyCount - copyCount;
            for (var index = 0; index < copyCount; index++)
                destination[index] = _frames[
                    (_historyStart + sourceOffset + index) % _frames.Length];
            return copyCount;
        }
    }

    /// <summary>
    /// Copies the newest frames into caller-owned storage without allocating. Frames are returned
    /// oldest-to-newest; when the destination is shorter than the history, the oldest frames are
    /// omitted.
    /// </summary>
    public static int CopyFrames(
        Span<EditorProfilerFrame> destination,
        out long snapshotVersion,
        out bool recording,
        out int capacity)
    {
        var copyCount = CopyFrames(destination, out EditorProfilerMetadata metadata);
        snapshotVersion = metadata.Version;
        recording = metadata.Recording;
        capacity = metadata.Capacity;
        return copyCount;
    }

    internal static void EndFrame(long token)
    {
        if (token == 0 || Volatile.Read(ref _recording) == 0) return;
        lock (Gate)
        {
            if (_recording == 0 || token != _activeFrameToken) return;
            var completedTimestamp = Stopwatch.GetTimestamp();
            var allocatedBytes = Math.Max(0,
                GC.GetTotalAllocatedBytes(false) - _activeFrame.AllocatedBytesAtStart);
            var memory = CaptureMemory();
            var frame = new EditorProfilerFrame(
                ++_nextFrameIndex,
                _activeFrame.TimestampUtc,
                TicksToMilliseconds(completedTimestamp - _activeFrame.StartedTimestamp),
                TicksToMilliseconds(_activeFrame.UpdateTicks),
                TicksToMilliseconds(_activeFrame.RuntimeTicks),
                TicksToMilliseconds(_activeFrame.RenderTicks),
                TicksToMilliseconds(_activeFrame.ImGuiTicks),
                TicksToMilliseconds(_activeFrame.PresentTicks),
                allocatedBytes,
                memory.ManagedHeapBytes,
                memory.ManagedFragmentedBytes,
                memory.WorkingSetBytes,
                memory.PrivateBytes,
                Math.Max(0, GC.CollectionCount(0) - _activeFrame.Gen0CollectionsAtStart),
                Math.Max(0, GC.CollectionCount(1) - _activeFrame.Gen1CollectionsAtStart),
                Math.Max(0, GC.CollectionCount(2) - _activeFrame.Gen2CollectionsAtStart),
                _activeFrame.HasRenderStatistics ? _activeFrame.RenderStatistics : default)
            {
                MethodSamples = CompleteMethodSamples(_activeFrame.MethodSamples),
                CounterSamples = CompleteCounterSamples(_activeFrame.Counters)
            };
            Append(frame);
            _activeFrameToken = 0;
            _activeFrame = default;
            _version++;
        }
    }

    internal static void EndSample(long token, EditorProfilerArea area, long startedTimestamp)
    {
        if (token == 0 || Volatile.Read(ref _recording) == 0) return;
        var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startedTimestamp);
        lock (Gate)
        {
            if (_recording == 0 || token != _activeFrameToken) return;
            AddSampleTicks(area, elapsedTicks);
        }
    }

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
        string[] callStack)
    {
        if (token == 0 || id == 0 || Volatile.Read(ref _recording) == 0) return;
        var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startedTimestamp);
        var allocatedBytes = Math.Max(0,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBytesAtStart);
        RemoveActiveMethod(token, id);
        lock (Gate)
        {
            if (_recording == 0 || token != _activeFrameToken) return;
            _activeFrame.MethodSamples ??= [];
            _activeFrame.MethodSamples.Add(new PendingMethodSample(
                id, parentId, domain, typeName, methodName, elapsedTicks,
                allocatedBytes, threadId, threadName, callStack));
        }
    }

    private static void RemoveActiveMethod(long token, int id)
    {
        var stack = s_methodStack;
        if (stack is null || stack.Count == 0) return;
        if (stack.Peek() is { FrameToken: var frameToken, Id: var topId } &&
            frameToken == token && topId == id)
        {
            stack.Pop();
            return;
        }

        var retained = stack.Where(item => item.FrameToken != token || item.Id != id)
            .Reverse().ToArray();
        stack.Clear();
        foreach (var item in retained) stack.Push(item);
    }

    private static EditorProfilerMethodSample[] CompleteMethodSamples(
        List<PendingMethodSample>? pending)
    {
        if (pending is not { Count: > 0 }) return [];
        var childTicks = new Dictionary<int, long>();
        var childAllocatedBytes = new Dictionary<int, long>();
        foreach (var sample in pending)
        {
            if (sample.ParentId <= 0) continue;
            childTicks[sample.ParentId] = childTicks.GetValueOrDefault(sample.ParentId) +
                                          sample.ElapsedTicks;
            childAllocatedBytes[sample.ParentId] =
                childAllocatedBytes.GetValueOrDefault(sample.ParentId) + sample.AllocatedBytes;
        }

        return pending.OrderBy(sample => sample.Id).Select(sample =>
            new EditorProfilerMethodSample(
                sample.Id,
                sample.ParentId,
                sample.Domain,
                sample.TypeName,
                sample.MethodName,
                TicksToMilliseconds(sample.ElapsedTicks),
                TicksToMilliseconds(Math.Max(0,
                    sample.ElapsedTicks - childTicks.GetValueOrDefault(sample.Id))),
                1,
                sample.AllocatedBytes,
                Math.Max(0, sample.AllocatedBytes -
                            childAllocatedBytes.GetValueOrDefault(sample.Id)),
                sample.ThreadId,
                sample.ThreadName,
                sample.CallStack)).ToArray();
    }

    private static EditorProfilerCounterSample[] CompleteCounterSamples(
        Dictionary<CounterKey, double>? counters) => counters is not { Count: > 0 }
        ? []
        : counters.OrderBy(pair => pair.Key.ModuleId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.CounterName, StringComparer.Ordinal)
            .Select(pair => new EditorProfilerCounterSample(
                pair.Key.ModuleId, pair.Key.CounterName, pair.Value)).ToArray();

    private static string[] FormatCallStack(StackTrace trace) => trace.GetFrames()
        .Select(frame => frame.GetMethod())
        .Where(method => method is not null && method.DeclaringType != typeof(EditorProfiler))
        .Take(32)
        .Select(method => $"{method!.DeclaringType?.FullName ?? "<global>"}.{method.Name}")
        .ToArray();

    private static void AddSampleTicks(EditorProfilerArea area, long ticks)
    {
        switch (area)
        {
            case EditorProfilerArea.Update:
                _activeFrame.UpdateTicks += ticks;
                break;
            case EditorProfilerArea.Runtime:
                _activeFrame.RuntimeTicks += ticks;
                break;
            case EditorProfilerArea.Render:
                _activeFrame.RenderTicks += ticks;
                break;
            case EditorProfilerArea.IMGUI:
                _activeFrame.ImGuiTicks += ticks;
                break;
            case EditorProfilerArea.Present:
                _activeFrame.PresentTicks += ticks;
                break;
        }
    }

    private static void Append(EditorProfilerFrame frame)
    {
        if (_historyCount < _frames.Length)
        {
            _frames[(_historyStart + _historyCount) % _frames.Length] = frame;
            _historyCount++;
            return;
        }
        _frames[_historyStart] = frame;
        _historyStart = (_historyStart + 1) % _frames.Length;
    }

    private static MemorySnapshot CaptureMemory()
    {
        var gc = GC.GetGCMemoryInfo();
        long workingSet = 0;
        long privateBytes = 0;
        try
        {
            _currentProcess ??= Process.GetCurrentProcess();
            _currentProcess.Refresh();
            workingSet = Math.Max(0, _currentProcess.WorkingSet64);
            privateBytes = Math.Max(0, _currentProcess.PrivateMemorySize64);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return new MemorySnapshot(
            Math.Max(0, gc.HeapSizeBytes),
            Math.Max(0, gc.FragmentedBytes),
            workingSet,
            privateBytes);
    }

    private static double TicksToMilliseconds(long ticks) =>
        ticks <= 0 ? 0 : ticks * 1000d / Stopwatch.Frequency;

    private static void CancelActiveFrame()
    {
        _activeFrameToken = 0;
        _activeFrame = default;
    }

    private struct ActiveFrame
    {
        internal DateTimeOffset TimestampUtc;
        internal long StartedTimestamp;
        internal long AllocatedBytesAtStart;
        internal int Gen0CollectionsAtStart;
        internal int Gen1CollectionsAtStart;
        internal int Gen2CollectionsAtStart;
        internal long UpdateTicks;
        internal long RuntimeTicks;
        internal long RenderTicks;
        internal long ImGuiTicks;
        internal long PresentTicks;
        internal bool HasRenderStatistics;
        internal SceneRenderStatistics RenderStatistics;
        internal int NextMethodId;
        internal List<PendingMethodSample>? MethodSamples;
        internal Dictionary<long, int>? ExternalMethodIds;
        internal Dictionary<CounterKey, double>? Counters;
    }

    private readonly record struct ActiveMethod(long FrameToken, int Id);

    private readonly record struct PendingMethodSample(
        int Id,
        int ParentId,
        EditorProfilerDomain Domain,
        string TypeName,
        string MethodName,
        long ElapsedTicks,
        long AllocatedBytes,
        int ThreadId,
        string ThreadName,
        string[] CallStack);

    private readonly record struct CounterKey(string ModuleId, string CounterName);

    private readonly record struct MemorySnapshot(
        long ManagedHeapBytes,
        long ManagedFragmentedBytes,
        long WorkingSetBytes,
        long PrivateBytes);
}
