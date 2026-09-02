using System.Diagnostics;

namespace BEngine.Editor;

/// <summary>
/// Keeps manually pumped native windows responsive without repainting every unfocused frame.
/// </summary>
internal sealed class NativeWindowFrameScheduler
{
    internal const int UnfocusedFramesPerSecond = 8;
    internal static long UnfocusedFrameIntervalTicks { get; } =
        Math.Max(1, Stopwatch.Frequency / UnfocusedFramesPerSecond);

    private long _requestVersion = 1;
    private long _renderedRequestVersion;
    private long _lastRenderTimestamp;
    private bool _hasRendered;
    private bool _hasObservedState;
    private bool _wasFocused;
    private bool _wasMinimized;

    internal void RequestRender() => Interlocked.Increment(ref _requestVersion);

    internal NativeWindowFrameDecision Evaluate(bool focused, bool minimized, long timestamp)
    {
        if (_hasObservedState)
        {
            if (focused && !_wasFocused || !minimized && _wasMinimized) RequestRender();
        }
        else
            _hasObservedState = true;

        _wasFocused = focused;
        _wasMinimized = minimized;
        var requestVersion = Volatile.Read(ref _requestVersion);
        if (minimized) return new NativeWindowFrameDecision(false, timestamp, requestVersion);

        var requestPending = requestVersion > Volatile.Read(ref _renderedRequestVersion);
        var inactiveIntervalElapsed = _hasRendered && timestamp - _lastRenderTimestamp >=
            UnfocusedFrameIntervalTicks;
        return new NativeWindowFrameDecision(
            focused || !_hasRendered || requestPending || inactiveIntervalElapsed,
            timestamp,
            requestVersion);
    }

    internal void NotifyRendered(NativeWindowFrameDecision decision)
    {
        if (!decision.ShouldRender) return;
        _hasRendered = true;
        _lastRenderTimestamp = decision.Timestamp;
        Volatile.Write(ref _renderedRequestVersion, decision.RequestVersion);
    }
}
