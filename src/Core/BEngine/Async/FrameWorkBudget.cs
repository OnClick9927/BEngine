using System.Diagnostics;

namespace BEngine;

/// <summary>Shared soft budget for optional main-thread work, not gameplay simulation.</summary>
public static class FrameWorkBudget
{
    private static long _started;
    private static double _milliseconds;
    private static double _periodMilliseconds;
    private static double _frameFraction = .15;
    public static bool enabled { get; set; } = true;
    public static double frameFraction
    {
        get => _frameFraction;
        set { if (!double.IsFinite(value) || value is <= 0 or > .5) throw new ArgumentOutOfRangeException(nameof(value)); _frameFraction = value; }
    }
    /// <summary>Target frame period used to derive the current optional-work budget.</summary>
    public static double framePeriodMilliseconds => _periodMilliseconds;
    /// <summary>Optional-work budget allocated for the current frame.</summary>
    public static double budgetMilliseconds => _milliseconds;
    internal static bool IsActive { get; private set; }
    public static double remainingMilliseconds => !IsActive || !enabled ? double.PositiveInfinity
        : Math.Max(0, _milliseconds - Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    internal static void Begin(TimeSpan delta)
    {
        var rate = Application.targetFrameRate;
        var period = rate > 0 ? 1000d / rate : Math.Clamp(delta.TotalMilliseconds, 1, 1000d / 60);
        _periodMilliseconds = period;
        _milliseconds = Math.Min(2, period * _frameFraction);
        _started = Stopwatch.GetTimestamp(); IsActive = true;
    }
    internal static void End() => IsActive = false;
}
