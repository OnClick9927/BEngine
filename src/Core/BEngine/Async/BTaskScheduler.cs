using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// Frame-driven scheduler for BEngine asynchronous work. The owning frame loop
/// is responsible for posting work and advancing this scheduler.
/// </summary>
public sealed class BTaskScheduler
{
    private const int MaximumCallbacksPerTick = 100_000;
    private const int MaximumReferenceGraphNodes = 512;
    private static readonly TimeSpan InfiniteDelay = TimeSpan.FromMilliseconds(-1);
    private static readonly Action EmptyCallback = static () => { };
    private readonly Queue<Action> _callbacks = new();
    private readonly PriorityQueue<ScheduledDelay, (long DueTicks, long Sequence)> _delays = new();
    private TimeSpan _elapsed;
    private long _frameCount;
    private long _sequence;
    private int _canceledDelayCount;
    private bool _isTicking;
    private int _maximumCallbacksPerTick = MaximumCallbacksPerTick;
    private double _callbackBudgetMilliseconds = 2;

    public static BTaskScheduler Main { get; } = new();

    public TimeSpan Elapsed => _elapsed;
    public int PendingCallbackCount => _callbacks.Count;
    public int PendingDelayCount => _delays.Count;
    public long FrameCount => _frameCount;
    public int CallbackLimitPerTick
    {
        get => _maximumCallbacksPerTick;
        set { if (value is < 1 or > MaximumCallbacksPerTick) throw new ArgumentOutOfRangeException(nameof(value)); _maximumCallbacksPerTick = value; }
    }
    /// <summary>Soft callback budget. A running callback is never interrupted. Zero disables the time limit.</summary>
    public double CallbackBudgetMilliseconds
    {
        get => _callbackBudgetMilliseconds;
        set { if (!double.IsFinite(value) || value < 0 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value)); _callbackBudgetMilliseconds = value; }
    }

    public event Action<Exception>? UnhandledException;
    public event Action<BFrameTick>? FrameAdvanced;

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Enqueue(callback);
    }

    /// <summary>Bounded admission for producers that can defer work. Post remains available for required continuations.</summary>
    public bool TryPost(Action callback, int maximumPendingCallbacks = MaximumCallbacksPerTick)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingCallbacks);
        if (_callbacks.Count >= maximumPendingCallbacks) return false;
        _callbacks.Enqueue(callback); return true;
    }

    /// <summary>
    /// Advances the scheduler from the owning Editor or Player Update callback.
    /// Custom hosts must pass their current frame delta explicitly.
    /// </summary>
    public int Tick(TimeSpan deltaTime)
    {
        if (deltaTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (_isTicking)
            throw new InvalidOperationException(
                "BTaskScheduler.Tick cannot be re-entered from a scheduled callback.");
        _isTicking = true;
        if (ReferenceEquals(this, Main)) FrameWorkBudget.Begin(deltaTime);
        try { return TickCore(deltaTime); }
        finally { if (ReferenceEquals(this, Main)) FrameWorkBudget.End(); _isTicking = false; }
    }

    internal BTask ScheduleDelay(TimeSpan delay, BCancellationToken cancellationToken)
    {
        if (delay < TimeSpan.Zero && delay != InfiniteDelay)
            throw new ArgumentOutOfRangeException(nameof(delay));
        if (cancellationToken.IsCancellationRequested)
            return BTask.FromCanceled(cancellationToken);

        var source = new BTaskCompletionSource(this);
        var due = delay == InfiniteDelay ? TimeSpan.MaxValue : AddClamped(_elapsed, delay);
        var sequence = _sequence++;
        var scheduled = new ScheduledDelay(due, sequence, source);
        scheduled.CancellationRegistration = cancellationToken.Register(() =>
        {
            if (scheduled.IsCanceled) return;
            scheduled.IsCanceled = true;
            _canceledDelayCount++;
            source.TrySetCanceled(cancellationToken);
        });
        _delays.Enqueue(scheduled, (due.Ticks, sequence));
        return source.Task;
    }

    internal BTask Schedule(Action action, BCancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var source = new BTaskCompletionSource(this);
        Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                source.TrySetCanceled(cancellationToken);
                return;
            }
            try
            {
                action();
                source.TrySetResult();
            }
            catch (BOperationCanceledException exception)
            {
                source.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                source.TrySetException(exception);
            }
        });
        return source.Task;
    }

    internal BTask<TResult> Schedule<TResult>(Func<TResult> function, BCancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        var source = new BTaskCompletionSource<TResult>(this);
        Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                source.TrySetCanceled(cancellationToken);
                return;
            }
            try
            {
                source.TrySetResult(function());
            }
            catch (BOperationCanceledException exception)
            {
                source.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                source.TrySetException(exception);
            }
        });
        return source.Task;
    }

    internal void RemoveWorkReferencing(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var owners = assemblies.Distinct().ToHashSet();
        if (owners.Count == 0) return;

        var queued = _callbacks.Count;
        for (var index = 0; index < queued; index++)
        {
            var callback = _callbacks.Dequeue();
            _callbacks.Enqueue(CallbackReferencesAnyAssembly(callback, owners)
                ? EmptyCallback
                : callback);
        }

        if (_delays.Count == 0) return;
        var retained = new List<ScheduledDelay>(_delays.Count);
        while (_delays.TryDequeue(out var delay, out _))
        {
            if (delay.IsCanceled)
            {
                delay.CancellationRegistration.Dispose();
                continue;
            }
            var source = delay.Source;
            if (!source.RemoveContinuationsReferencing(owners) || source.HasContinuations)
                retained.Add(delay);
            else
                delay.CancellationRegistration.Dispose();
        }
        _canceledDelayCount = 0;
        foreach (var delay in retained)
            _delays.Enqueue(delay, (delay.Due.Ticks, delay.Sequence));
    }

    internal static bool CallbackReferencesAnyAssembly(
        Delegate callback,
        IReadOnlySet<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(assemblies);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var remaining = MaximumReferenceGraphNodes;
        return ReferencesAnyAssembly(callback, assemblies, visited, ref remaining, 0);
    }

    private int TickCore(TimeSpan deltaTime)
    {
        _elapsed = AddClamped(_elapsed, deltaTime);
        _frameCount++;
        CompleteDueDelays();
        if (FrameAdvanced is { } frameAdvanced)
        {
            var tick = new BFrameTick(_frameCount, _elapsed, deltaTime);
            try { frameAdvanced(tick); }
            catch (Exception exception)
            {
                if (UnhandledException is { } handler) handler(exception);
                else RuntimeServiceAccess.PlatformService.WriteDiagnostic(
                    $"Unhandled frame callback exception: {exception}");
            }
        }

        // Loading may advance newly queued completion continuations in this
        // tick, avoiding a frame of latency for each layer of an async loader.
        // Explicit Yield/Delay still complete only at the next tick's delay
        // phase. The callback count and shared elapsed-time guards still apply.
        var originalCallbacks = Math.Min(_callbacks.Count, _maximumCallbacksPerTick);
        var callbacksToRun = FrameWorkBudget.isLoading ? _maximumCallbacksPerTick : originalCallbacks;
        var callbackStart = System.Diagnostics.Stopwatch.GetTimestamp();

        var processed = 0;
        for (; processed < callbacksToRun && _callbacks.Count > 0; processed++)
        {
            // The final scope can end inside a callback. Finish callbacks from
            // the original snapshot, but defer any loading-only extension.
            if (!FrameWorkBudget.isLoading && processed >= originalCallbacks) break;
            var callbackBudget = _callbackBudgetMilliseconds;
            if (FrameWorkBudget.IsActive && callbackBudget > 0)
                callbackBudget = FrameWorkBudget.CooperativeBudget(callbackBudget);
            if (processed > 0 && callbackBudget > 0 &&
                System.Diagnostics.Stopwatch.GetElapsedTime(callbackStart).TotalMilliseconds >= callbackBudget) break;
            if (processed > 0 && FrameWorkBudget.remainingMilliseconds <= 0) break;
            var callback = _callbacks.Dequeue();
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                if (UnhandledException is { } handler) handler(exception);
                else RuntimeServiceAccess.PlatformService.WriteDiagnostic(
                    $"Unhandled BTask callback exception: {exception}");
            }
        }
        return processed;
    }

    private void CompleteDueDelays()
    {
        RemoveCanceledDelays();
        while (_delays.TryPeek(out var delay, out _))
        {
            if (delay.Due > _elapsed) break;
            _delays.Dequeue();
            delay.CancellationRegistration.Dispose();
            delay.Source.TrySetResult();
        }
    }

    private void RemoveCanceledDelays()
    {
        if (_canceledDelayCount == 0) return;
        var retained = new List<ScheduledDelay>(Math.Max(0, _delays.Count - _canceledDelayCount));
        while (_delays.TryDequeue(out var delay, out _))
        {
            if (delay.IsCanceled) delay.CancellationRegistration.Dispose();
            else retained.Add(delay);
        }
        foreach (var delay in retained)
            _delays.Enqueue(delay, (delay.Due.Ticks, delay.Sequence));
        _canceledDelayCount = 0;
    }

    private static TimeSpan AddClamped(TimeSpan left, TimeSpan right)
    {
        if (right > TimeSpan.Zero && left > TimeSpan.MaxValue - right) return TimeSpan.MaxValue;
        return left + right;
    }

    private static bool ReferencesAnyAssembly(
        object? value,
        IReadOnlySet<Assembly> assemblies,
        ISet<object> visited,
        ref int remaining,
        int depth)
    {
        if (value is null || remaining-- <= 0 || depth > 8) return false;
        var type = value.GetType();
        if (assemblies.Contains(type.Assembly)) return true;
        if (value is Assembly assembly) return assemblies.Contains(assembly);
        if (value is Type reflectedType) return assemblies.Contains(reflectedType.Assembly);
        if (!type.IsValueType && !visited.Add(value)) return false;

        if (value is Delegate callback)
        {
            foreach (var handler in Delegate.EnumerateInvocationList(callback))
            {
                if (handler.Method.DeclaringType is { } declaringType &&
                    assemblies.Contains(declaringType.Assembly)) return true;
                if (ReferencesAnyAssembly(handler.Target, assemblies, visited, ref remaining, depth + 1))
                    return true;
            }
            return false;
        }

        if (value is Array array)
        {
            foreach (var item in array)
                if (ReferencesAnyAssembly(item, assemblies, visited, ref remaining, depth + 1)) return true;
            return false;
        }

        var inspectFields = typeof(BTask).IsAssignableFrom(type) ||
                            type.Assembly == typeof(BTaskScheduler).Assembly ||
                            type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
        if (!inspectFields) return false;
        for (var current = type; current is not null; current = current.BaseType)
        {
            FieldInfo[] fields;
            try
            {
                fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            }
            catch
            {
                continue;
            }
            foreach (var field in fields)
            {
                if (field.FieldType.IsPointer || field.FieldType.IsPrimitive || field.FieldType.IsEnum ||
                    field.FieldType == typeof(string)) continue;
                object? fieldValue;
                try { fieldValue = field.GetValue(value); }
                catch { continue; }
                if (ReferencesAnyAssembly(fieldValue, assemblies, visited, ref remaining, depth + 1)) return true;
            }
        }
        return false;
    }

    private sealed class ScheduledDelay(
        TimeSpan due,
        long sequence,
        BTaskCompletionSource source)
    {
        internal TimeSpan Due { get; } = due;
        internal long Sequence { get; } = sequence;
        internal BTaskCompletionSource Source { get; } = source;
        internal BCancellationRegistration CancellationRegistration { get; set; }
        internal bool IsCanceled { get; set; }
    }
}
