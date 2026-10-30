using System.Diagnostics;

namespace BEngine.Rendering;

public enum SceneRenderDiagnosticPhase
{
    SimpleSprites, NonSpriteRenderers, Sort, BuildContiguous,
    UiRender, UiBuildBatches, UiWebSdfClip, UiWebSolidClip, UiUpload, UiDraw,
    UiImageResolve, UiImageUpload, UiTextRaster, UiTextUpload,
    UiLayoutAndCommands, UiSubmit,
    Count
}

/// <summary>Opt-in CPU breakdown. A capture covers one Render/RenderViewport/RenderCameras call.
/// Disabled entry reads no clock/allocation counter/type name and creates no buffer.</summary>
public static class SceneRenderDiagnostics
{
    public const int MaximumRendererTypes = 64;
    private const int MaximumScopeDepth = 64, MaximumRenderDepth = 8;
    private const int PhaseCount = (int)SceneRenderDiagnosticPhase.Count;
    private const int OtherTypeSlot = PhaseCount + MaximumRendererTypes;
    [ThreadStatic] private static Frame? _current;
    [ThreadStatic] private static int _suppressed;
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
        internal readonly ActiveScope[] Stack = new ActiveScope[MaximumScopeDepth];
        internal Frame? Parent;
        internal long Started, Allocated, NestedTicks, NestedBytes, Generation;
        internal int RenderDepth, ScopeDepth, TypeCount, OverflowTypes, DroppedScopes;
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
        internal long Started, Allocated, ChildTicks, ChildBytes;
    }
    private static long Clock() { _clockReads++; return Stopwatch.GetTimestamp(); }
    private static long Allocation() { _allocationReads++; return GC.GetAllocatedBytesForCurrentThread(); }

    public static CaptureScope BeginCapture(ref CaptureContext? context)
    {
        if (!Enabled) return default;
        if (_suppressed != 0 || (_current?.RenderDepth ?? 0) >= MaximumRenderDepth)
        { _suppressed++; if (_current is { } limited) limited.DroppedScopes++; return new(null, true); }
        context ??= new CaptureContext();
        var frame = context.Buffer ??= new Frame();
        // PortableSceneRenderer already rejects reentrancy. Keep diagnostics fail-closed
        // for other callers instead of corrupting the active parent capture.
        if (frame.Active) { _suppressed++; frame.DroppedScopes++; return new(null, true); }
        Array.Clear(frame.Rows); Array.Clear(frame.Stack);
        frame.Parent = _current;
        frame.RenderDepth = (_current?.RenderDepth ?? 0) + 1;
        frame.Generation = unchecked(frame.Generation + 1);
        frame.ScopeDepth = frame.TypeCount = frame.OverflowTypes = frame.DroppedScopes = 0;
        frame.NestedTicks = frame.NestedBytes = 0;
        frame.Snapshotted = frame.CleanupCompleted = false;
        frame.Active = true;
        frame.Allocated = Allocation(); frame.Started = Clock();
        _current = frame;
        return new(frame, false);
    }

    public static Measurement Measure(SceneRenderDiagnosticPhase phase)
    {
        var frame = _current;
        if (frame is null || _suppressed != 0 || (uint)phase >= PhaseCount) return default;
        var slot = (int)phase;
        frame.Rows[slot].Name ??= phase switch
        {
            SceneRenderDiagnosticPhase.SimpleSprites => "Core.SimpleSprites",
            SceneRenderDiagnosticPhase.NonSpriteRenderers => "Core.NonSpriteRenderers",
            SceneRenderDiagnosticPhase.Sort => "Core.Sort",
            SceneRenderDiagnosticPhase.BuildContiguous => "Core.BuildContiguous",
            SceneRenderDiagnosticPhase.UiRender => "UI.Render",
            SceneRenderDiagnosticPhase.UiBuildBatches => "UI.BuildBatches",
            SceneRenderDiagnosticPhase.UiWebSdfClip => "UI.WebSdfClip",
            SceneRenderDiagnosticPhase.UiWebSolidClip => "UI.WebSolidClip",
            SceneRenderDiagnosticPhase.UiUpload => "UI.Upload",
            SceneRenderDiagnosticPhase.UiDraw => "UI.Draw",
            SceneRenderDiagnosticPhase.UiImageResolve => "UI.ImageResolve",
            SceneRenderDiagnosticPhase.UiImageUpload => "UI.ImageUpload",
            SceneRenderDiagnosticPhase.UiTextRaster => "UI.TextRaster",
            SceneRenderDiagnosticPhase.UiTextUpload => "UI.TextUpload",
            SceneRenderDiagnosticPhase.UiLayoutAndCommands => "UI.LayoutAndCommands",
            SceneRenderDiagnosticPhase.UiSubmit => "UI.Submit",
            _ => "Unknown"
        };
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
            if (string.Equals(frame.Rows[PhaseCount + index].Name, name, StringComparison.Ordinal))
            { slot = PhaseCount + index; break; }
        if (slot < 0)
        {
            if (frame.TypeCount < MaximumRendererTypes)
            { slot = PhaseCount + frame.TypeCount++; frame.Rows[slot].Name = name; }
            else { slot = OtherTypeSlot; frame.Rows[slot].Name = "<other-renderer-types>"; frame.OverflowTypes++; }
        }
        return Start(frame, slot);
    }

    private static Measurement Start(Frame frame, int slot)
    {
        if (frame.ScopeDepth == MaximumScopeDepth) { frame.DroppedScopes++; return default; }
        var depth = frame.ScopeDepth++;
        frame.Stack[depth] = new ActiveScope { Slot = slot, Allocated = Allocation(), Started = Clock() };
        return new(frame, frame.Generation, depth);
    }

    public readonly struct Measurement : IDisposable
    {
        private readonly Frame? _frame;
        private readonly long _generation;
        private readonly int _depth;
        internal Measurement(Frame frame, long generation, int depth)
        { _frame = frame; _generation = generation; _depth = depth; }
        public void Dispose()
        {
            var frame = _frame;
            if (frame is null || !frame.Active || frame.Generation != _generation) return;
            if (frame.ScopeDepth != _depth + 1) { frame.DroppedScopes++; return; }
            var ended = Clock(); var allocated = Allocation();
            ref var current = ref frame.Stack[_depth];
            var ticks = Math.Max(0, ended - current.Started);
            var bytes = Math.Max(0, allocated - current.Allocated);
            ref var row = ref frame.Rows[current.Slot];
            row.Calls++; row.Ticks += ticks; row.Bytes += bytes;
            row.SelfTicks += Math.Max(0, ticks - current.ChildTicks);
            row.SelfBytes += Math.Max(0, bytes - current.ChildBytes);
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
        internal CaptureScope(Frame? frame, bool suppressed)
        { _frame = frame; _suppressedCapture = suppressed; }
        public void CleanupCompleted() { if (_frame is { } frame) frame.CleanupCompleted = true; }
        public void Dispose()
        {
            if (_suppressedCapture) { if (_suppressed > 0) _suppressed--; return; }
            var frame = _frame;
            if (frame is null || !frame.Active) return;
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
            rows[next++] = new(row.Name!, index >= PhaseCount, row.Calls,
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
            OverflowRendererTypeOccurrences = frame.OverflowTypes, DroppedScopes = frame.DroppedScopes,
            OpenScopes = frame.ScopeDepth, Samples = rows
        };
    }
}

/// <summary>Inclusive rows overlap their nested rows. Sum SelfTicks, NestedRenderTicks and
/// UnattributedTicks for the capture ledger. Diagnostic snapshot creation/serialization is excluded.</summary>
public sealed record SceneRenderDiagnosticSnapshot
{
    public bool Completed { get; init; }
    public bool BeforeCleanup { get; init; }
    public int CameraCount { get; init; }
    public int RenderDepth { get; init; }
    public long TotalTicks { get; init; }
    public double TotalMilliseconds { get; init; }
    public long TotalAllocatedBytes { get; init; }
    public long NestedRenderTicks { get; init; }
    public double NestedRenderMilliseconds { get; init; }
    public long NestedRenderAllocatedBytes { get; init; }
    public long UnattributedTicks { get; init; }
    public long UnattributedAllocatedBytes { get; init; }
    public int OverflowRendererTypeOccurrences { get; init; }
    public int DroppedScopes { get; init; }
    public int OpenScopes { get; init; }
    public SceneRenderDiagnosticSample[] Samples { get; init; } = [];
}
public readonly record struct SceneRenderDiagnosticSample(
    string Name, bool RendererType, long Calls, long InclusiveTicks, long SelfTicks,
    double InclusiveMilliseconds, double SelfMilliseconds, long AllocatedBytes, long SelfAllocatedBytes);
