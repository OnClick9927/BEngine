using System.Diagnostics;

namespace BEngine;

public enum CooperativeWorkStatus { Continue, Blocked, Complete }
public enum CooperativeWorkPriority { Background, Normal, Realtime }

public readonly record struct CooperativeWorkSnapshot(int Pending, int Steps, double Milliseconds,
    double LongestStepMilliseconds, long OverBudgetFrames);

/// <summary>Shares a soft time budget between resumable work on the main frame thread.</summary>
public sealed class CooperativeWorkScheduler
{
    private readonly LinkedList<Registration>[] _queues = [new(), new(), new()];
    private static readonly int[] PriorityCycle = [2, 1, 2, 0, 2, 1, 2];
    private int _cycle;
    private readonly HashSet<Registration> _blocked = [];
    private Registration? _current;
    private int _ownerThread;
    private bool _subscribed, _advancing, _canceling;
    private double _budgetMilliseconds = 2;
    private int _maximumStepsPerFrame = 16384;
    private double _maximumStepMilliseconds = .5;
    private long _overBudgetFrames;
    private long _pumpFrame = -1;
    private int _frameSteps;
    private double _frameMilliseconds, _frameLongest;
    private bool _frameStopped, _frameOverBudgetCounted;
    public static CooperativeWorkScheduler Main { get; } = new();
    static CooperativeWorkScheduler()
    {
        Application.quitting += Main.CancelPending;
        Diagnostics.RuntimeDiagnosticRegistry.Register("budget.async", "Cooperative work",
            new("pending", "Pending work", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.PendingCount),
            new("budget", "Shared frame budget", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => Main.BudgetMilliseconds),
            new("period", "Target frame period", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => FrameWorkBudget.framePeriodMilliseconds),
            new("effective", "Effective frame budget", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => FrameWorkBudget.budgetMilliseconds),
            new("elapsed", "Last frame work", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => Main.Snapshot.Milliseconds),
            new("longest", "Longest step", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => Main.Snapshot.LongestStepMilliseconds),
            new("overruns", "Budget overruns", Diagnostics.RuntimeDiagnosticUnit.Number, () => Main.Snapshot.OverBudgetFrames));
    }
    public CooperativeWorkSnapshot Snapshot { get; private set; }
    public int PendingCount => _queues[0].Count + _queues[1].Count + _queues[2].Count;
    public double BudgetMilliseconds
    {
        get => _budgetMilliseconds;
        set { if (!double.IsFinite(value) || value <= 0 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value)); _budgetMilliseconds = value; }
    }
    public int MaximumStepsPerFrame
    {
        get => _maximumStepsPerFrame;
        set { if (value is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(value)); _maximumStepsPerFrame = value; }
    }
    /// <summary>
    /// Soft guard for one indivisible step. A step is never interrupted, but a
    /// step that crosses the guard stops this scheduler for the current frame.
    /// </summary>
    public double MaximumStepMilliseconds
    {
        get => _maximumStepMilliseconds;
        set { if (!double.IsFinite(value) || value <= 0 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value)); _maximumStepMilliseconds = value; }
    }

    public IDisposable Schedule(Func<CooperativeWorkStatus> step, Action<Exception> failure,
        CooperativeWorkPriority priority = CooperativeWorkPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(step); ArgumentNullException.ThrowIfNull(failure);
        CheckOwner();
        if (_canceling) throw new InvalidOperationException("Cooperative work is shutting down.");
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        if (PendingCount >= 8192) throw new InvalidOperationException("The cooperative work queue is full (8192 requests).");
        var registration = new Registration(this, step, failure) { Priority = (int)priority };
        registration.Node = _queues[registration.Priority].AddLast(registration);
        if (!_subscribed) { _subscribed = true; BTaskScheduler.Main.FrameAdvanced += Advance; }
        return registration;
    }
    public void CancelPending()
    {
        CheckOwner(); _canceling = true;
        try
        {
            var pending = _queues.SelectMany(queue => queue).ToList(); if (_current is { } current) pending.Add(current);
            foreach (var registration in pending)
            {
                var failure = registration.Failure; registration.Dispose();
                try { failure?.Invoke(new BOperationCanceledException(default)); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
        finally { _canceling = false; }
    }
    public void SetPriority(IDisposable handle, CooperativeWorkPriority priority)
    {
        CheckOwner();
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        if (handle is not Registration registration || !ReferenceEquals(registration.Owner, this))
            throw new ArgumentException("The work handle belongs to a different scheduler.", nameof(handle));
        if (registration.Disposed || registration.Priority == (int)priority) return;
        if (registration.Node is { } node) _queues[registration.Priority].Remove(node);
        registration.Priority = (int)priority;
        if (registration.Node is not null) registration.Node = _queues[registration.Priority].AddLast(registration);
    }
    private Registration? TakeNext()
    {
        for (var attempt = 0; attempt < PriorityCycle.Length; attempt++)
        {
            var queue = _queues[PriorityCycle[_cycle]]; _cycle = (_cycle + 1) % PriorityCycle.Length;
            if (queue.First is not { } node) continue;
            queue.RemoveFirst(); node.Value.Node = null; return node.Value;
        }
        return null;
    }

    private void CheckOwner()
    {
        var current = Environment.CurrentManagedThreadId;
        if (_ownerThread == 0) _ownerThread = current;
        else if (_ownerThread != current) throw new InvalidOperationException("Cooperative work must run on its owning engine thread.");
    }

    private void Advance(BFrameTick tick)
    {
        CheckOwner();
        if (_advancing) throw new InvalidOperationException("Cooperative work cannot be re-entered.");
        BeginPumpFrame(tick.Frame);
        Pump(allowFirstStep: true);
    }

    // Only the main BTask callback phase may use this additional loading pass.
    // The original frame event and delay phase are never raised again.
    internal bool PumpLoadingWork()
    {
        CheckOwner();
        if (_advancing) throw new InvalidOperationException("Cooperative work cannot be re-entered.");
        if (!FrameWorkBudget.IsActive || !FrameWorkBudget.isLoading ||
            FrameWorkBudget.remainingMilliseconds <= 0 || PendingCount == 0) return false;
        BeginPumpFrame(BTaskScheduler.Main.FrameCount);
        if (_frameStopped || _frameSteps >= _maximumStepsPerFrame || _blocked.Count >= PendingCount) return false;
        return Pump(allowFirstStep: false);
    }

    // After the owner attaches cancellation/cleanup, use the existing weighted
    // FIFO policy until the target receives its first step. The admission never
    // gives a target priority over older work, drains callbacks or renews a frame.
    internal bool TryRunLoadingFirstStep(IDisposable handle)
    {
        CheckOwner();
        if (handle is not Registration registration || !ReferenceEquals(registration.Owner, this))
            throw new ArgumentException("The work handle belongs to a different scheduler.", nameof(handle));
        if (!ReferenceEquals(this, Main) || registration.Disposed || registration.Node is null ||
            _advancing || _canceling || !FrameWorkBudget.IsActive || !FrameWorkBudget.isLoading ||
            FrameWorkBudget.remainingMilliseconds <= 0)
        {
            if (CooperativeLoadingDiagnostics.Enabled)
                CooperativeLoadingDiagnostics.Admission(handle,
                    !ReferenceEquals(this, Main) ? "not-main" : registration.Disposed || registration.Node is null ? "not-live" :
                    _advancing ? "advancing" : _canceling ? "canceling" : !FrameWorkBudget.IsActive ? "no-frame" :
                    !FrameWorkBudget.isLoading ? "not-loading" : "budget",
                    BTaskScheduler.Main.FrameCount, PendingCount, FrameWorkBudget.remainingMilliseconds);
            return false;
        }
        BeginPumpFrame(BTaskScheduler.Main.FrameCount);
        if (_frameStopped || _frameSteps >= _maximumStepsPerFrame || _blocked.Count >= PendingCount)
        {
            if (CooperativeLoadingDiagnostics.Enabled)
                CooperativeLoadingDiagnostics.Admission(handle, _frameStopped ? "soft-stop" : _frameSteps >= _maximumStepsPerFrame ? "step-limit" : "blocked",
                    BTaskScheduler.Main.FrameCount, PendingCount, FrameWorkBudget.remainingMilliseconds);
            return false;
        }
        if (CooperativeLoadingDiagnostics.Enabled)
            CooperativeLoadingDiagnostics.Admission(handle, "admitted", BTaskScheduler.Main.FrameCount, PendingCount, FrameWorkBudget.remainingMilliseconds);
        return Pump(allowFirstStep: false, firstStepTarget: registration);
    }

    private void BeginPumpFrame(long frame)
    {
        if (_pumpFrame == frame) return;
        _pumpFrame = frame; _frameSteps = 0; _frameMilliseconds = _frameLongest = 0;
        _frameStopped = _frameOverBudgetCounted = false; _blocked.Clear();
    }

    private bool Pump(bool allowFirstStep, Registration? firstStepTarget = null)
    {
        _advancing = true;
        var start = Stopwatch.GetTimestamp(); var madeProgress = false;
        var firstStepStart = _frameSteps;
        var effectiveBudget = FrameWorkBudget.CooperativeBudget(_budgetMilliseconds);
        try
        {
            while (!_frameStopped && (allowFirstStep || FrameWorkBudget.isLoading) &&
                   (firstStepTarget is null || (!firstStepTarget.Disposed && _frameSteps - firstStepStart < 32)) &&
                   PendingCount > 0 && _frameSteps < _maximumStepsPerFrame &&
                   _frameMilliseconds + Stopwatch.GetElapsedTime(start).TotalMilliseconds < effectiveBudget &&
                   ((allowFirstStep && _frameSteps == 0) || FrameWorkBudget.remainingMilliseconds > 0))
            {
                var registration = TakeNext()!;
                _current = registration;
                var stepStart = Stopwatch.GetTimestamp(); var status = CooperativeWorkStatus.Complete;
                var stopAfterStep = false;
                try
                {
                    if (CooperativeLoadingDiagnostics.Enabled) CooperativeLoadingDiagnostics.FirstStep(registration, BTaskScheduler.Main.FrameCount);
                    status = registration.Step!();
                    if (CooperativeLoadingDiagnostics.Enabled)
                        CooperativeLoadingDiagnostics.StepReturned(registration, BTaskScheduler.Main.FrameCount, status);
                }
                catch (Exception error)
                {
                    if (CooperativeLoadingDiagnostics.Enabled) CooperativeLoadingDiagnostics.End(registration, BTaskScheduler.Main.FrameCount, success: false);
                    try { registration.Failure?.Invoke(error); }
                    catch (Exception reportingError) { Debug.LogException(reportingError); }
                }
                finally
                {
                    var stepMilliseconds = Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
                    _frameLongest = Math.Max(_frameLongest, stepMilliseconds); _frameSteps++;
                    // An indivisible operation can still run past the soft
                    // budget. Do not immediately start another one in that frame.
                    stopAfterStep = stepMilliseconds >= FrameWorkBudget.CooperativeMaximumStep(_maximumStepMilliseconds) ||
                        (FrameWorkBudget.IsActive && FrameWorkBudget.remainingMilliseconds <= 0);
                }
                if (stopAfterStep) _frameStopped = true;
                madeProgress |= status != CooperativeWorkStatus.Blocked || registration.Disposed;
                if (!registration.Disposed && status != CooperativeWorkStatus.Complete)
                    registration.Node = _queues[registration.Priority].AddLast(registration);
                else registration.Clear();
                if (status == CooperativeWorkStatus.Blocked)
                { if (!registration.Disposed) _blocked.Add(registration); if (_blocked.Count >= PendingCount) break; }
                else _blocked.Clear();
                if (stopAfterStep || ReferenceEquals(registration, firstStepTarget)) break;
            }
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _frameMilliseconds += elapsed;
            if (!_frameOverBudgetCounted && _frameMilliseconds > effectiveBudget)
            { _overBudgetFrames++; _frameOverBudgetCounted = true; }
            Snapshot = new(PendingCount, _frameSteps, _frameMilliseconds, _frameLongest, _overBudgetFrames);
            _current = null; _advancing = false; UnsubscribeIfEmpty();
        }
        return madeProgress;
    }

    private void UnsubscribeIfEmpty()
    {
        if (PendingCount != 0 || !_subscribed) return;
        _subscribed = false; BTaskScheduler.Main.FrameAdvanced -= Advance;
    }

    private sealed class Registration(CooperativeWorkScheduler owner, Func<CooperativeWorkStatus> step,
        Action<Exception> failure) : IDisposable
    {
        internal LinkedListNode<Registration>? Node;
        internal Func<CooperativeWorkStatus>? Step = step;
        internal Action<Exception>? Failure = failure;
        internal bool Disposed;
        internal int Priority;
        internal CooperativeWorkScheduler Owner => owner;
        internal void Clear() { Disposed = true; Step = null; Failure = null; owner._blocked.Remove(this); }
        public void Dispose()
        {
            owner.CheckOwner();
            if (Disposed) return;
            if (CooperativeLoadingDiagnostics.Enabled) CooperativeLoadingDiagnostics.Disposed(this, BTaskScheduler.Main.FrameCount);
            if (Node is { } node) { owner._queues[Priority].Remove(node); Node = null; }
            Clear(); owner.UnsubscribeIfEmpty();
        }
    }
}
