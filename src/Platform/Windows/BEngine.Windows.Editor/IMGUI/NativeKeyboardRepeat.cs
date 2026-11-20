using System.Diagnostics;
using System.Windows.Forms;

namespace BEngine.Editor;

/// <summary>Supplies repeat events for editing keys that do not produce character callbacks.</summary>
internal sealed class NativeKeyboardRepeat
{
    private const int FallbackDelayMilliseconds = 500;
    private const int FallbackIntervalMilliseconds = 33;

    private readonly long _delayTicks;
    private readonly long _intervalTicks;
    private KeyCode _key;
    private long _nextRepeat;
    private bool _nativeRepeatObserved;

    internal NativeKeyboardRepeat(long delayTicks, long intervalTicks)
    {
        _delayTicks = Math.Max(1, delayTicks);
        _intervalTicks = Math.Max(1, intervalTicks);
    }

    internal static NativeKeyboardRepeat CreateSystemDefault()
    {
        try
        {
            var delayMilliseconds = (SystemInformation.KeyboardDelay + 1) * 250;
            var repeatsPerSecond = 2.5 + SystemInformation.KeyboardSpeed * (27.5 / 31.0);
            return new NativeKeyboardRepeat(
                MillisecondsToStopwatchTicks(delayMilliseconds),
                Math.Max(1, (long)Math.Round(Stopwatch.Frequency / repeatsPerSecond)));
        }
        catch
        {
            return new NativeKeyboardRepeat(
                MillisecondsToStopwatchTicks(FallbackDelayMilliseconds),
                MillisecondsToStopwatchTicks(FallbackIntervalMilliseconds));
        }
    }

    internal void KeyDown(KeyCode key, long timestamp)
    {
        if (!IsRepeatable(key)) return;
        if (_key == key)
        {
            // Some backends expose native repeat KeyDown callbacks. Once observed,
            // let that backend remain the only repeat source for this key hold.
            _nativeRepeatObserved = true;
            return;
        }

        _key = key;
        _nextRepeat = timestamp + _delayTicks;
        _nativeRepeatObserved = false;
    }

    internal void KeyUp(KeyCode key)
    {
        if (_key == key) Clear();
    }

    internal bool TryGetRepeat(long timestamp, out KeyCode key)
    {
        key = KeyCode.None;
        if (_key == KeyCode.None || _nativeRepeatObserved || timestamp < _nextRepeat) return false;

        key = _key;
        _nextRepeat = timestamp + _intervalTicks;
        return true;
    }

    internal void Clear()
    {
        _key = KeyCode.None;
        _nextRepeat = 0;
        _nativeRepeatObserved = false;
    }

    internal static bool IsRepeatable(KeyCode key) => key is
        KeyCode.Backspace or KeyCode.Delete or
        KeyCode.LeftArrow or KeyCode.RightArrow or KeyCode.Home or KeyCode.End;

    private static long MillisecondsToStopwatchTicks(int milliseconds) =>
        Math.Max(1, (long)Math.Round(milliseconds / 1000.0 * Stopwatch.Frequency));
}
