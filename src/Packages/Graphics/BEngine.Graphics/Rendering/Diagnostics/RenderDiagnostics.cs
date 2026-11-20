using System.Diagnostics;

namespace BEngine.Rendering;

/// <summary>Opt-in CPU breakdown. A capture covers one Render/RenderViewport/RenderCameras call.
/// Disabled entry reads no clock/allocation counter/type name and creates no buffer.</summary>
[RuntimeTypeAlias("BEngine.Rendering.SceneRenderDiagnostics, BEngine.Graphics")]
public static class RenderDiagnostics
{
    public const int MaximumRendererTypes = 64;
    private const int MaximumScopeDepth = 64, MaximumRenderDepth = 8;
    public const int MaximumPhases = 64;
    private const int OtherPhaseSlot = MaximumPhases;
    private const int TypeStart = OtherPhaseSlot + 1;
    private const int OtherTypeSlot = TypeStart + MaximumRendererTypes;
    [ThreadStatic] private static Frame? _current;
    [ThreadStatic] private static int _suppressed;
    [ThreadStatic] private static long _suppressionSequence, _suppressionTop;
    [ThreadStatic] private static SceneRenderDiagnosticSnapshot? _lastIncomplete;
    private static long _clockReads, _allocationReads, _typeNameReads;

    public static bool Enabled => SceneRuntimeDiagnostics.Enabled;
    public static long ClockReadCount => _clockReads;
    public static long AllocationReadCount => _allocationReads;
    public static long TypeNameReadCount => _typeNameReads;
    public static SceneRenderDiagnosticSnapshot? LastIncompleteCapture => _lastIncomplete;

    // The holder is created lazily by the caller only when diagnostics are enabled.
    public sealed class CaptureContext { internal Frame? Buffer; }
    internal sealed class Frame
    {
        internal readonly Row[] Rows = new Row[OtherTypeSlot + 1];
        internal readonly Dictionary<string, int> PhaseSlots = new(16, StringComparer.Ordinal);
        internal readonly ActiveScope[] Stack = new ActiveScope[MaximumScopeDepth];
        internal Frame? Parent;
        internal long Started, Allocated, NestedTicks, NestedBytes, Generation, ScopeSequence;
        internal int RenderDepth, ScopeDepth, TypeCount, OverflowTypes, OverflowPhases, DroppedScopes;
        internal bool Active, Snapshotted, CleanupCompleted;
    }
    internal struct Row
    {
        internal string? Name;
        internal long Calls, Ticks, SelfTicks, Bytes, SelfBytes;
    }
    internal struct ActiveScope
    {
        internal int Slot;
        internal long Started, Allocated, ChildTicks, ChildBytes, Token;
    }
    private static long Clock() { _clockReads++; return Stopwatch.GetTimestamp(); }
    private static long Allocation() { _allocationReads++; return GC.GetAllocatedBytesForCurrentThread(); }

    public static CaptureScope BeginCapture(ref CaptureContext? context)
    {
        if (!Enabled) return default;
        if (_suppressed != 0 || (_current?.RenderDepth ?? 0) >= MaximumRenderDepth)
        { if (_current is { } limited) limited.DroppedScopes++; return SuppressCapture(); }
        context ??= new CaptureContext();
        var frame = context.Buffer ??= new Frame();
        // PortableSceneRenderer already rejects reentrancy. Keep diagnostics fail-closed
        // for other callers instead of corrupting the active parent capture.
        if (frame.Active) { frame.DroppedScopes++; return SuppressCapture(); }
        Array.Clear(frame.Rows); Array.Clear(frame.Stack); frame.PhaseSlots.Clear();
        frame.Parent = _current;
        frame.RenderDepth = (_current?.RenderDepth ?? 0) + 1;
        frame.Generation = unchecked(frame.Generation + 1);
        frame.ScopeDepth = frame.TypeCount = frame.OverflowTypes = frame.OverflowPhases = frame.DroppedScopes = 0;
        frame.NestedTicks = frame.NestedBytes = 0;
        frame.Snapshotted = frame.CleanupCompleted = false;
        frame.Active = true;
        frame.Allocated = Allocation(); frame.Started = Clock();
        _current = frame;
        return new(frame, false);
    }

    private static CaptureScope SuppressCapture()
    {
        var previous = _suppressionTop;
        _suppressionTop = unchecked(++_suppressionSequence);
        _suppressed++;
        return new(null, true, _suppressionTop, previous);
    }

    /// <summary>Measures a caller-owned phase. Default phases are ignored; names use ordinal identity.
    /// At most MaximumPhases names are retained by a reusable capture buffer.</summary>
    public static Measurement Measure(RenderDiagnosticPhase phase)
    {
        var frame = _current;
        if (frame is null || _suppressed != 0 || phase.Name is not { } name) return default;
        if (!frame.PhaseSlots.TryGetValue(name, out var slot))
        {
            if (frame.PhaseSlots.Count < MaximumPhases)
            {
                slot = frame.PhaseSlots.Count;
                frame.PhaseSlots.Add(name, slot);
                frame.Rows[slot].Name = name;
            }
            else
            {
                slot = OtherPhaseSlot;
                frame.Rows[slot].Name = "<other-render-phases>";
                frame.OverflowPhases++;
            }
        }
        return Start(frame, slot);
    }

    public static Measurement MeasureRenderer(object renderer)
    {
        var frame = _current;
        if (frame is null || _suppressed != 0) return default;
        // This GetType/name read occurs only inside an enabled render capture.
        _typeNameReads++;
        var type = renderer.GetType();
        var name = type.FullName ?? type.Name;
        var slot = -1;
        for (var index = 0; index < frame.TypeCount; index++)
            if (string.Equals(frame.Rows[TypeStart + index].Name, name, StringComparison.Ordinal))
            { slot = TypeStart + index; break; }
        if (slot < 0)
        {
            if (frame.TypeCount < MaximumRendererTypes)
            { slot = TypeStart + frame.TypeCount++; frame.Rows[slot].Name = name; }
            else { slot = OtherTypeSlot; frame.Rows[slot].Name = "<other-renderer-types>"; frame.OverflowTypes++; }
        }
        return Start(frame, slot);
    }

    private static Measurement Start(Frame frame, int slot)
    {
        if (frame.ScopeDepth == MaximumScopeDepth) { frame.DroppedScopes++; return default; }
        var depth = frame.ScopeDepth++;
        var token = unchecked(++frame.ScopeSequence);
        frame.Stack[depth] = new ActiveScope { Slot = slot, Allocated = Allocation(), Started = Clock(), Token = token };
        return new(frame, frame.Generation, depth, token);
    }

    public readonly struct Measurement : IDisposable
    {
        private readonly Frame? _frame;
        private readonly long _generation;
        private readonly int _depth;
        private readonly long _token;
        internal Measurement(Frame frame, long generation, int depth, long token)
        { _frame = frame; _generation = generation; _depth = depth; _token = token; }
        public void Dispose()
        {
            var frame = _frame;
            if (frame is null || !frame.Active || frame.Generation != _generation) return;
            if (_depth >= frame.ScopeDepth || frame.Stack[_depth].Token != _token) return;
            if (!ReferenceEquals(_current, frame) || _suppressed != 0) { frame.DroppedScopes++; return; }
            if (frame.ScopeDepth != _depth + 1) { frame.DroppedScopes++; return; }
            var ended = Clock(); var allocated = Allocation();
            ref var current = ref frame.Stack[_depth];
            var ticks = Math.Max(0, ended - current.Started);
            var bytes = Math.Max(0, allocated - current.Allocated);
            ref var row = ref frame.Rows[current.Slot];
            row.Calls++; row.Ticks += ticks; row.Bytes += bytes;
            row.SelfTicks += Math.Max(0, ticks - current.ChildTicks);
            row.SelfBytes += Math.Max(0, bytes - current.ChildBytes);
            current = default;
            frame.ScopeDepth--;
            if (frame.ScopeDepth != 0)
            {
                ref var parent = ref frame.Stack[frame.ScopeDepth - 1];
                parent.ChildTicks += ticks; parent.ChildBytes += bytes;
            }
        }
    }

    public readonly struct CaptureScope : IDisposable
    {
        private readonly Frame? _frame;
        private readonly bool _suppressedCapture;
        private readonly long _generation;
        private readonly long _suppressionToken, _previousSuppression;
        internal CaptureScope(Frame? frame, bool suppressed, long suppressionToken = 0, long previousSuppression = 0)
        {
            _frame = frame; _suppressedCapture = suppressed; _generation = frame?.Generation ?? 0;
            _suppressionToken = suppressionToken; _previousSuppression = previousSuppression;
        }
        public void CleanupCompleted() { if (_frame is { Active: true } frame && frame.Generation == _generation) frame.CleanupCompleted = true; }
        public void Dispose()
        {
            if (_suppressedCapture)
            {
                if (_suppressed != 0 && _suppressionTop == _suppressionToken)
                { _suppressionTop = _previousSuppression; _suppressed--; }
                return;
            }
            var frame = _frame;
            if (frame is null || !frame.Active || frame.Generation != _generation) return;
            if (!ReferenceEquals(_current, frame) || _suppressed != 0) { frame.DroppedScopes++; return; }
            var ended = Clock(); var allocated = Allocation();
            try
            {
                if (!frame.Snapshotted || !frame.CleanupCompleted)
                    _lastIncomplete = Snapshot(frame, 0, ended, allocated, false);
            }
            catch (Exception) { frame.DroppedScopes++; } // Never replace the original render exception.
            finally
            {
                frame.Active = false;
                _current = frame.Parent;
                if (frame.Parent is { } parent)
                {
                    var ticks = Math.Max(0, ended - frame.Started);
                    var bytes = Math.Max(0, allocated - frame.Allocated);
                    parent.NestedTicks += ticks; parent.NestedBytes += bytes;
                    if (parent.ScopeDepth != 0)
                    {
                        ref var active = ref parent.Stack[parent.ScopeDepth - 1];
                        active.ChildTicks += ticks; active.ChildBytes += bytes;
                    }
                }
                frame.Parent = null;
            }
        }
    }

    public static SceneRenderDiagnosticSnapshot? CaptureStatistics(int cameraCount)
    {
        var frame = _current;
        if (frame is null || _suppressed != 0) return null;
        var ended = Clock(); var allocated = Allocation();
        var result = Snapshot(frame, cameraCount, ended, allocated, true);
        frame.Snapshotted = true;
        return result;
    }

    private static SceneRenderDiagnosticSnapshot Snapshot(Frame frame, int cameras, long ended, long allocated, bool completed)
    {
        var count = 0; long selfTicks = 0, selfBytes = 0;
        foreach (ref readonly var row in frame.Rows.AsSpan()) if (row.Calls != 0) count++;
        var rows = new SceneRenderDiagnosticSample[count]; var next = 0;
        var milliseconds = 1000d / Stopwatch.Frequency;
        for (var index = 0; index < frame.Rows.Length; index++)
        {
            ref readonly var row = ref frame.Rows[index];
            if (row.Calls == 0) continue;
            selfTicks += row.SelfTicks; selfBytes += row.SelfBytes;
            rows[next++] = new(row.Name!, index >= TypeStart, row.Calls,
                row.Ticks, row.SelfTicks, row.Ticks * milliseconds, row.SelfTicks * milliseconds,
                row.Bytes, row.SelfBytes);
        }
        var totalTicks = Math.Max(0, ended - frame.Started);
        var totalBytes = Math.Max(0, allocated - frame.Allocated);
        return new SceneRenderDiagnosticSnapshot
        {
            Completed = completed, BeforeCleanup = completed, CameraCount = cameras,
            RenderDepth = frame.RenderDepth, TotalTicks = totalTicks, TotalMilliseconds = totalTicks * milliseconds,
            TotalAllocatedBytes = totalBytes, NestedRenderTicks = frame.NestedTicks,
            NestedRenderMilliseconds = frame.NestedTicks * milliseconds, NestedRenderAllocatedBytes = frame.NestedBytes,
            UnattributedTicks = Math.Max(0, totalTicks - selfTicks - frame.NestedTicks),
            UnattributedAllocatedBytes = Math.Max(0, totalBytes - selfBytes - frame.NestedBytes),
            OverflowRendererTypeOccurrences = frame.OverflowTypes, OverflowPhaseOccurrences = frame.OverflowPhases, DroppedScopes = frame.DroppedScopes,
            OpenScopes = frame.ScopeDepth, Samples = rows
        };
    }
}

