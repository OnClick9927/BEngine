using System.Diagnostics;

namespace BEngine;

/// <summary>Shared soft budget for optional main-thread work, not gameplay simulation.</summary>
public static class FrameWorkBudget
{
    private static long _started;
    private static double _milliseconds;
    private static double _periodMilliseconds;
    private static double _frameFraction = .15;
    private static double _targetPeriodMilliseconds = 1000d / 60;
    private static double _observedPeriodMilliseconds = 1000d / 60;
    private static readonly object LoadingGate = new();
    private static readonly HashSet<LoadingScope> LoadingScopes = [];
    private static int _loadingScopeCount;
    private static double _loadingFrameFraction = .5;
    private static double _loadingMaximumBudget = 8;
    private static double _loadingMaximumStep = 2;
    public static bool enabled { get; set; } = true;
    public static double frameFraction
    {
        get => _frameFraction;
        set { if (!double.IsFinite(value) || value is <= 0 or > .5) throw new ArgumentOutOfRangeException(nameof(value)); _frameFraction = value; }
    }
    /// <summary>Frame period used to derive the current optional-work budget.</summary>
    public static double framePeriodMilliseconds => _periodMilliseconds;
    /// <summary>Optional-work budget allocated for the current frame.</summary>
    public static double budgetMilliseconds => _milliseconds;
    /// <summary>Whether an explicit loading operation is using the bounded loading budget.</summary>
    public static bool isLoading => Volatile.Read(ref _loadingScopeCount) != 0;
    internal static bool IsActive { get; private set; }
    public static double remainingMilliseconds => !IsActive || !enabled ? double.PositiveInfinity
        : Math.Max(0, _milliseconds - Stopwatch.GetElapsedTime(_started).TotalMilliseconds);

    /// <summary>
    /// Temporarily gives cooperative preparation work more of each loading frame.
    /// The budget follows the slower of the target and observed frame periods,
    /// capped by <paramref name="maximumBudgetMilliseconds"/>. Nested scopes use
    /// the strictest active limits. Cancellation or disposal releases only this
    /// scope; disposal is idempotent and never cancels the underlying operation.
    /// </summary>
    /// <remarks>
    /// This is a soft budget: an indivisible step cannot be interrupted. Keep
    /// individual steps bounded. Gameplay time guards are restored as soon as
    /// the last loading scope ends. The scheduler's step-count limit always applies.
    /// </remarks>
    public static IDisposable EnterLoading(BCancellationToken cancellationToken = default,
        double frameFraction = .5, double maximumBudgetMilliseconds = 8,
        double maximumStepMilliseconds = 2)
    {
        if (!double.IsFinite(frameFraction) || frameFraction is <= 0 or > .5)
            throw new ArgumentOutOfRangeException(nameof(frameFraction));
        if (!double.IsFinite(maximumBudgetMilliseconds) || maximumBudgetMilliseconds is <= 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maximumBudgetMilliseconds));
        if (!double.IsFinite(maximumStepMilliseconds) || maximumStepMilliseconds is <= 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maximumStepMilliseconds));
        var scope = new LoadingScope(frameFraction, maximumBudgetMilliseconds, maximumStepMilliseconds);
        lock (LoadingGate)
        {
            if (LoadingScopes.Count >= 1024)
                throw new InvalidOperationException("The loading work budget already has 1024 active scopes.");
            LoadingScopes.Add(scope);
            RefreshLoadingLimits();
        }
        try { scope.Attach(cancellationToken.Register(scope.Dispose)); }
        catch { scope.Dispose(); throw; }
        return scope;
    }

    internal static double CooperativeBudget(double configuredBudget) => IsActive
        ? isLoading ? _milliseconds : Math.Min(configuredBudget, _milliseconds)
        : configuredBudget;
    internal static double CooperativeMaximumStep(double configuredMaximum) => isLoading
        ? Math.Min(_loadingMaximumStep, _loadingMaximumBudget) : configuredMaximum;

    internal static void Begin(TimeSpan delta)
    {
        var rate = Application.targetFrameRate;
        var period = rate > 0 ? 1000d / rate : Math.Clamp(delta.TotalMilliseconds, 1, 1000d / 60);
        lock (LoadingGate)
        {
            _targetPeriodMilliseconds = period;
            _observedPeriodMilliseconds = double.IsFinite(delta.TotalMilliseconds) && delta.TotalMilliseconds > 0
                ? Math.Clamp(delta.TotalMilliseconds, 1, 100) : period;
            RefreshFrameBudget();
        }
        _started = Stopwatch.GetTimestamp(); IsActive = true;
    }
    internal static void End() => IsActive = false;

    private static void RefreshLoadingLimits()
    {
        var fraction = .5; var budget = 16d; var step = 4d;
        foreach (var scope in LoadingScopes)
        {
            fraction = Math.Min(fraction, scope.Fraction);
            budget = Math.Min(budget, scope.MaximumBudget);
            step = Math.Min(step, scope.MaximumStep);
        }
        _loadingFrameFraction = fraction; _loadingMaximumBudget = budget; _loadingMaximumStep = step;
        Volatile.Write(ref _loadingScopeCount, LoadingScopes.Count);
        // Do not reset the frame clock: entering or leaving a scope must not
        // grant another budget to work already performed in the current frame.
        RefreshFrameBudget();
    }

    private static void RefreshFrameBudget()
    {
        _periodMilliseconds = isLoading
            ? Math.Max(_targetPeriodMilliseconds, _observedPeriodMilliseconds) : _targetPeriodMilliseconds;
        _milliseconds = isLoading ? Math.Min(_loadingMaximumBudget, _periodMilliseconds * _loadingFrameFraction)
            : Math.Min(2, _periodMilliseconds * _frameFraction);
    }

    private sealed class LoadingScope(double fraction, double maximumBudget, double maximumStep) : IDisposable
    {
        internal readonly double Fraction = fraction, MaximumBudget = maximumBudget, MaximumStep = maximumStep;
        private BCancellationRegistration _cancellation;
        private bool _disposed;

        internal void Attach(BCancellationRegistration cancellation)
        {
            lock (LoadingGate)
            {
                if (!_disposed) { _cancellation = cancellation; return; }
            }
            // Register invokes inline when its token is already canceled.
            cancellation.Dispose();
        }

        public void Dispose()
        {
            BCancellationRegistration cancellation;
            lock (LoadingGate)
            {
                if (_disposed) return;
                _disposed = true; cancellation = _cancellation; _cancellation = default;
                LoadingScopes.Remove(this); RefreshLoadingLimits();
            }
            cancellation.Dispose();
        }
    }
}
